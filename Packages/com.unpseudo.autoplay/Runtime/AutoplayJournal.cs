#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Everything an autoplay run observed: ordered events, errors/exceptions the game logged, capture files written.
    /// Every event is mirrored to the console with the filterable <c>[AUTOPLAY]</c> tag and appended live to
    /// <c>events.ndjson</c> (one flushed line per event), so a run that hangs or crashes still leaves its full trace.
    /// <c>report.json</c> is the compact summary meant to be read first.
    /// </summary>
    public sealed class AutoplayJournal : IDisposable
    {
        public const string LogTag = "[AUTOPLAY]";

        /// <summary>Event kind the runner records on every phase change; feeds <see cref="Report.stateTrace"/>.</summary>
        public const string PhaseEvent = "state.enter";
        public const string CaptureEvent = "capture";
        /// <summary>Recorded once the game's Host step succeeded (multi-process launchers wait for the host's).</summary>
        public const string SessionReadyEvent = "session.ready";

        [Serializable]
        public sealed class Entry
        {
            public float realTime;
            public float gameTime;
            public int frame;
            public string kind;
            public string detail;
        }

        [Serializable]
        public sealed class Report
        {
            public string game;
            public string scenario;
            public int seed;
            public string outcome;
            public string failureReason;
            public float realSeconds;
            public int errorCount;
            public string[] stateTrace;
            /// <summary>Game-specific facts ("days=3"), from <see cref="IAutoplayGame.DescribeOutcome"/>.</summary>
            public string[] facts;
            /// <summary>How many times each event kind was recorded ("power.start=19").</summary>
            public string[] counters;
            public string[] finalRoster;
            public string[] errors;
            public string[] captures;
        }

        private readonly List<Entry> entries = new();
        private readonly List<string> errors = new();
        private readonly List<string> captures = new();
        private readonly List<string> stateTrace = new();
        private readonly Dictionary<string, int> counters = new();
        private readonly float startRealTime = Time.realtimeSinceStartup;
        private readonly StreamWriter eventStream;

        public string OutputDirectory { get; }

        /// <summary>Raised for every recorded event (e.g. the animation recorder starts on matching events).</summary>
        public event Action<Entry> Recorded;
        public IReadOnlyList<Entry> Entries => entries;
        public IReadOnlyList<string> Errors => errors;

        public AutoplayJournal(string _outputDirectory)
        {
            OutputDirectory = _outputDirectory;
            Directory.CreateDirectory(_outputDirectory);
            eventStream = new StreamWriter(Path.Combine(_outputDirectory, "events.ndjson"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public int Count(string _kind) => counters.TryGetValue(_kind, out int _n) ? _n : 0;

        /// <summary>
        /// Real time (since startup) of the last event that shows the game moving: every kind except the passive
        /// ones (captures, periodic samples, the watchdog's own alerts). Read by <see cref="AutoplayWatchdog"/>.
        /// </summary>
        public float LastProgressRealTime { get; private set; } = Time.realtimeSinceStartup;

        private static readonly string[] PassiveKindPrefixes =
            { "capture", "net.rtt", "state.hash", "state.repl", "state.view", "watchdog", "record", "video" };

        public static bool IsProgressKind(string _kind)
        {
            foreach (string _prefix in PassiveKindPrefixes)
            {
                if (_kind.StartsWith(_prefix, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        public void Record(string _kind, string _detail)
        {
            if (IsProgressKind(_kind))
            {
                LastProgressRealTime = Time.realtimeSinceStartup;
            }
            var _entry = new Entry
            {
                realTime = Time.realtimeSinceStartup - startRealTime,
                gameTime = Time.time,
                frame = Time.frameCount,
                kind = _kind,
                detail = _detail,
            };
            entries.Add(_entry);
            counters[_kind] = Count(_kind) + 1;
            if (_kind == PhaseEvent) stateTrace.Add(_detail);
            if (_kind == CaptureEvent) captures.Add(_detail);

            try
            {
                eventStream.WriteLine(JsonUtility.ToJson(_entry));
            }
            catch (ObjectDisposedException)
            {
                // journal already closed (late event after the report) — kept in memory only
            }

            Debug.Log($"{LogTag} {_kind} {_detail}");

            try
            {
                Recorded?.Invoke(_entry);
            }
            catch (Exception _exception)
            {
                Debug.LogWarning($"{LogTag} journal listener failed: {_exception.Message}");
            }
        }

        /// <summary>Hook for <see cref="Application.logMessageReceived"/>: keeps every error the game logs.</summary>
        public void OnLog(string _condition, string _stackTrace, LogType _type)
        {
            if (_type != LogType.Error && _type != LogType.Exception && _type != LogType.Assert)
            {
                return;
            }

            if (_condition.StartsWith(LogTag, StringComparison.Ordinal))
            {
                return;
            }

            string _firstFrame = string.IsNullOrEmpty(_stackTrace) ? string.Empty : _stackTrace.Split('\n')[0];
            errors.Add($"{_type}: {_condition} @ {_firstFrame}".Trim());
        }

        public Report BuildReport(string _game, string _scenario, int _seed, string _failureReason,
            IEnumerable<string> _facts, IEnumerable<string> _finalRoster)
        {
            return new Report
            {
                game = _game,
                scenario = _scenario,
                seed = _seed,
                outcome = _failureReason == null ? "Completed" : "Failed",
                failureReason = _failureReason,
                realSeconds = Time.realtimeSinceStartup - startRealTime,
                errorCount = errors.Count,
                stateTrace = stateTrace.ToArray(),
                facts = (_facts ?? Array.Empty<string>()).ToArray(),
                counters = counters.OrderBy(_c => _c.Key, StringComparer.Ordinal).Select(_c => $"{_c.Key}={_c.Value}").ToArray(),
                finalRoster = (_finalRoster ?? Array.Empty<string>()).ToArray(),
                errors = errors.ToArray(),
                captures = captures.ToArray(),
            };
        }

        public void Write(Report _report)
        {
            File.WriteAllText(Path.Combine(OutputDirectory, "report.json"), JsonUtility.ToJson(_report, true));
        }

        public void Dispose() => eventStream.Dispose();
    }
}
#endif
