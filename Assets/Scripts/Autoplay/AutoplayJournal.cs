#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Autoplay
{
    /// <summary>
    /// Everything an autoplay run observed: ordered events, errors/exceptions logged by the game, screenshots taken.
    /// Every event is also mirrored to the console with the filterable <c>[AUTOPLAY]</c> tag. Written to disk as
    /// <c>events.ndjson</c> (full trace) + <c>report.json</c> (compact summary meant to be read first).
    /// </summary>
    public sealed class AutoplayJournal
    {
        public const string LogTag = "[AUTOPLAY]";

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
            public string scenario;
            public int seed;
            public string outcome;
            public string failureReason;
            public float realSeconds;
            public int days;
            public int powersUsed;
            public int votesCast;
            public int errorCount;
            public string[] stateTrace;
            public string[] finalRoster;
            public string[] errors;
            public string[] captures;
        }

        private readonly List<Entry> entries = new();
        private readonly List<string> errors = new();
        private readonly List<string> captures = new();
        private readonly List<string> stateTrace = new();
        private readonly float startRealTime = Time.realtimeSinceStartup;

        public string OutputDirectory { get; }
        public IReadOnlyList<Entry> Entries => entries;
        public IReadOnlyList<string> Errors => errors;
        public IReadOnlyList<string> Captures => captures;
        public IReadOnlyList<string> StateTrace => stateTrace;
        public int PowersUsed { get; private set; }
        public int VotesCast { get; private set; }

        // events.ndjson is appended live (one flushed line per event), so a run that hangs or crashes still leaves
        // its full trace on disk and can be analysed while it plays.
        private readonly StreamWriter eventStream;

        public AutoplayJournal(string _outputDirectory)
        {
            OutputDirectory = _outputDirectory;
            Directory.CreateDirectory(_outputDirectory);
            eventStream = new StreamWriter(Path.Combine(_outputDirectory, "events.ndjson"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void Record(string _kind, string _detail)
        {
            var _entry = new Entry
            {
                realTime = Time.realtimeSinceStartup - startRealTime,
                gameTime = Time.time,
                frame = Time.frameCount,
                kind = _kind,
                detail = _detail,
            };
            entries.Add(_entry);
            try
            {
                eventStream.WriteLine(JsonUtility.ToJson(_entry));
            }
            catch (ObjectDisposedException)
            {
                // journal already closed (late event after the report) — kept in memory only
            }

            switch (_kind)
            {
                case "state.enter":
                    stateTrace.Add(_detail);
                    break;
                case "power.start":
                    PowersUsed++;
                    break;
                case "vote":
                    VotesCast++;
                    break;
                case "capture":
                    captures.Add(_detail);
                    break;
            }

            Debug.Log($"{LogTag} {_kind} {_detail}");
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

        public Report BuildReport(string _scenario, int _seed, string _outcome, string _failureReason, int _days,
            IEnumerable<string> _finalRoster)
        {
            return new Report
            {
                scenario = _scenario,
                seed = _seed,
                outcome = _outcome,
                failureReason = _failureReason,
                realSeconds = Time.realtimeSinceStartup - startRealTime,
                days = _days,
                powersUsed = PowersUsed,
                votesCast = VotesCast,
                errorCount = errors.Count,
                stateTrace = stateTrace.ToArray(),
                finalRoster = new List<string>(_finalRoster).ToArray(),
                errors = errors.ToArray(),
                captures = captures.ToArray(),
            };
        }

        public void Write(Report _report)
        {
            File.WriteAllText(Path.Combine(OutputDirectory, "report.json"), JsonUtility.ToJson(_report, true));
            eventStream.Dispose();
        }
    }
}
#endif
