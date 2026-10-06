#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>What a run left behind: the verdict, the report, and the host object the caller destroys.</summary>
    public sealed class AutoplayResult
    {
        public string failure;
        public AutoplayJournal.Report report;
        /// <summary>DontDestroyOnLoad object carrying the capture component; destroy it once the run is over.</summary>
        public GameObject host;
        public bool Succeeded => failure == null;
    }

    /// <summary>
    /// Plays one game through an <see cref="IAutoplayGame"/>: boot → host on the first free loopback UDP port →
    /// set up → start → watch phases until the game is over, failing on a stalled phase, a global timeout or a dead
    /// session → report. Shared by PlayMode tests (editor) and the dev-build bootstrap (<c>-autoplay</c>).
    /// Never throws for a game-level failure: it ends up in <see cref="AutoplayResult.failure"/> and the report.
    /// </summary>
    public static class AutoplayRunner
    {
        public static IEnumerator Run(IAutoplayGame _game, AutoplayConfig _config, AutoplayResult _result)
        {
            var _journal = new AutoplayJournal(_config.outputDirectory);
            var _host = new GameObject("Autoplay");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _result.host = _host;
            var _capture = _host.AddComponent<AutoplayCapture>();
            var _context = new AutoplayContext(_config, _journal, _capture);

            // "png" wins over "no-png" (launchers pass no-png to every client; a scenario may need one client's screenshots).
            _capture.Begin(_journal, () => _game.Phase, _game.ExportStateJson, !_config.Flag("no-png") || _config.Flag("png"));

            // Optional animation recorder: "-autoplay-record kindRegex:seconds[,…]" (+ record-fps, record-width).
            string _recordSpec = _config.Option("record");
            if (!string.IsNullOrEmpty(_recordSpec))
            {
                var _recorder = _host.AddComponent<AutoplayRecorder>();
                _recorder.Begin(_journal, _recordSpec, _config.OptionInt("record-fps", 20), _config.OptionInt("record-width", 480),
                    _game is IAutoplayAnimationSource _source ? _source.TracksFor : null);
            }
            // Optional film of the whole run: "-autoplay-video" (+ video-fps); Tools~/make_videos.py makes the mp4.
            AutoplayVideo _video = null;
            if (_config.Flag("video"))
            {
                _video = _host.AddComponent<AutoplayVideo>();
                _video.Begin(_journal, _config.OptionInt("video-fps", 10));
            }
            // "-autoplay-fps N": cap this process's frame rate (the net launcher caps the small client windows nobody
            // watches, which frees CPU / GPU for the host and for a second run). Game time is unaffected.
            int _fps = _config.OptionInt("fps", 0);
            if (_fps > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _fps;
            }
            Application.logMessageReceived += _journal.OnLog;
            _journal.Record("run.begin", $"game={_game.Name} scenario={_config.scenario} seed={_config.seed} out={_config.outputDirectory}");

            // Every step and phase runs under the watchdog: a stall is reported (alerts, then a failed run with a
            // report) instead of hanging until the global timeout or the launcher's kill.
            var _watchdog = new AutoplayWatchdog(_context, _game);
            _capture.StartCoroutine(_watchdog.Run());

            yield return RunStep(_capture, _watchdog, "boot", _game.Boot(_context), _context);

            if (!_context.Failed)
            {
                // "port-strict": the port was agreed with other processes (multi-process runs) — never move it.
                ushort _port = _config.Flag("port-strict") ? _config.port : UdpPorts.FindFree(_config.port, _config.portRangeSize);
                if (_port != _config.port)
                {
                    _journal.Record("port", $"UDP {_config.port} busy, using {_port}");
                }
                yield return RunStep(_capture, _watchdog, "host", _game.Host(_context, _port), _context);
                if (!_context.Failed)
                {
                    // The session is up (host: listening with the game scene loaded; client: joined or refused). The
                    // multi-process launcher starts the clients only after the host's one: a client joining while the
                    // host still loads its scene gets a broken scene synchronization.
                    _journal.Record(AutoplayJournal.SessionReadyEvent, _game.Phase);
                }
            }

            if (!_context.Failed)
            {
                yield return RunStep(_capture, _watchdog, "setup", _game.SetUp(_context), _context);
            }

            if (!_context.Failed)
            {
                yield return RunStep(_capture, _watchdog, "start", _game.StartGame(_context), _context);
            }

            if (!_context.Failed)
            {
                Time.timeScale = _config.timeScale;
                yield return Watch(_game, _context, _watchdog);

                yield return new WaitForSecondsRealtime(_context.Failed ? 0.2f : 1.5f);
                _capture.Request(_context.Failed ? "failure" : "final");
                yield return new WaitForSecondsRealtime(0.8f);
            }

            _watchdog.Stop();
            try
            {
                _game.TearDown();
            }
            catch (Exception _exception)
            {
                _journal.Record("teardown.error", _exception.Message);
            }

            Time.timeScale = 1f;
            Application.logMessageReceived -= _journal.OnLog;
            _capture.End();
            if (_video != null)
            {
                _video.End();
            }

            _result.failure = _context.FailureReason;
            _result.report = _journal.BuildReport(_game.Name, _config.scenario, _config.seed, _context.FailureReason,
                SafeLines(_game.DescribeOutcome), SafeLines(_game.DescribeRoster));
            _journal.Write(_result.report);
            Debug.Log($"{AutoplayJournal.LogTag} report {Path.Combine(_config.outputDirectory, "report.json")} " +
                      $"outcome={_result.report.outcome} errors={_result.report.errorCount} {_context.FailureReason}");
            _journal.Dispose();
        }

        /// <summary>A fresh run folder: &lt;root&gt;/&lt;stamp&gt;-&lt;scenario&gt;-seed&lt;seed&gt;.</summary>
        public static string NewRunDirectory(string _root, string _scenario, int _seed)
            => Path.GetFullPath(Path.Combine(_root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{_scenario}-seed{_seed}"));

        // Runs one game step as its own coroutine so the watchdog can end it: a failed run (watchdog, operator abort,
        // or the step itself) stops waiting on it at once.
        private static IEnumerator RunStep(MonoBehaviour _owner, AutoplayWatchdog _watchdog, string _name, IEnumerator _step,
            AutoplayContext _context)
        {
            _watchdog.Enter(_name);
            bool _done = false;
            Coroutine _running = _owner.StartCoroutine(Track(_step, () => _done = true));
            while (!_done && !_context.Failed)
            {
                yield return null;
            }
            if (!_done && _running != null)
            {
                _owner.StopCoroutine(_running);
            }
        }

        private static IEnumerator Track(IEnumerator _step, Action _onDone)
        {
            yield return _step;
            _onDone();
        }

        private static IEnumerator Watch(IAutoplayGame _game, AutoplayContext _context, AutoplayWatchdog _watchdog)
        {
            AutoplayConfig _config = _context.Config;
            float _startedAt = Time.realtimeSinceStartup;
            float _phaseSince = _startedAt;
            string _lastPhase = null;

            // Optional per-phase speed-up (test only): phases matching "fast-phases" (e.g. pure animation / recap
            // phases where no bot acts) run at "fast-timescale"; the others keep the run's time scale.
            string _fastPattern = _config.Option("fast-phases");
            var _fastPhases = string.IsNullOrEmpty(_fastPattern) ? null
                : new System.Text.RegularExpressions.Regex(_fastPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            float _fastScale = float.TryParse(_config.Option("fast-timescale"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float _fs) ? _fs : _config.timeScale * 3f;

            while (true)
            {
                string _phase = _game.Phase;
                if (_fastPhases != null && !AutoplayRecorder.IsRecording)
                {
                    Time.timeScale = _fastPhases.IsMatch(_phase ?? string.Empty) ? _fastScale : _config.timeScale;
                }
                if (_context.Failed)
                {
                    yield break; // the watchdog (or an operator abort) failed the run
                }
                if (_phase != _lastPhase)
                {
                    _lastPhase = _phase;
                    _phaseSince = Time.realtimeSinceStartup;
                    _watchdog.Enter("phase " + _phase);
                    _context.Journal.Record(AutoplayJournal.PhaseEvent, _phase);
                    if (_config.captureOnPhaseChange)
                    {
                        _context.Capture.Request(_phase, _config.phaseCaptureDelay);
                    }
                }

                if (_game.IsOver)
                {
                    yield break;
                }

                float _now = Time.realtimeSinceStartup;
                if (_now - _startedAt > _config.gameTimeoutRealSeconds)
                {
                    _context.Fail($"timeout after {_config.gameTimeoutRealSeconds}s real in {_phase}");
                    yield break;
                }

                if (_now - _phaseSince > _config.stallRealSeconds)
                {
                    _context.Fail($"stuck {_config.stallRealSeconds}s real in {_phase}");
                    yield break;
                }

                if (!_game.IsAlive)
                {
                    _context.Fail($"session died in {_phase}");
                    yield break;
                }

                yield return null;
            }
        }

        private static string[] SafeLines(Func<System.Collections.Generic.IEnumerable<string>> _read)
        {
            try
            {
                return new System.Collections.Generic.List<string>(_read() ?? Array.Empty<string>()).ToArray();
            }
            catch (Exception _exception)
            {
                return new[] { $"<{_exception.GetType().Name}: {_exception.Message}>" };
            }
        }
    }

    /// <summary>Loopback UDP port probing: a run killed mid-game can leave its socket bound inside the process.</summary>
    public static class UdpPorts
    {
        public static ushort FindFree(ushort _first, int _count)
        {
            for (int _i = 0; _i < _count; _i++)
            {
                ushort _candidate = (ushort)(_first + _i);
                try
                {
                    using var _probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _candidate));
                    return _candidate;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // busy — try the next one
                }
            }
            return _first;
        }
    }
}
#endif
