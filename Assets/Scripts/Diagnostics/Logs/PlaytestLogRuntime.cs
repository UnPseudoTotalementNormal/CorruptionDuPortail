#if CDP_PLAYTEST_LOGS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Network;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Playtest builds only (<c>CDP_PLAYTEST_LOGS</c>, added by CdpBuild). Collects <see cref="GameLog"/> events and
    /// every Unity log, writes them to <c>persistentDataPath/logs/&lt;session&gt;.ndjson</c>, and streams the file to
    /// Firestore (project corruption-du-portail) in chunks while the remote switch <c>config/logging</c> says
    /// <c>enabled</c>. Switch off, missing or unreadable for long = no upload within a minute, and what was waiting is
    /// dropped when it says off (the local file stays). Never runs in the Editor; an autoplay run logs locally and
    /// uploads only with <c>-playtest-logs-upload</c>. A session that did not end cleanly (crash, killed) is uploaded at
    /// the next launch, marked crashed. Read the logs with <c>tools/logs/logs.py</c>.
    /// Upload invariants: one pass at a time (lock shared with the quit upload); the byte range of a chunk is saved
    /// before it is sent and sent again unchanged after a lost answer, so the cursor only moves over bytes that are
    /// online; a range that can never be accepted is skipped and logged instead of blocking the session.
    /// </summary>
    internal sealed class PlaytestLogRuntime : MonoBehaviour
    {
        public const string UploadInAutoplaySwitch = "-playtest-logs-upload";
        // Same switch as Unpseudo.Autoplay.AutoplayCommandLine, whose code is compiled out of non-development builds.
        private const string AutoplaySwitch = "-autoplay";
        private const int KeptSessions = 10;
        private const int MaxKeptUnsentSessions = 30;
        private const int ExpireDays = 60;
        private const float ConfigPollSeconds = 50f;
        private const float ConfigStaleSeconds = 180f;
        private const float PerfSampleSeconds = 30f;
        private const int MaxChunksPerPass = 6;
        private const int MaxBackoffSeconds = 300;
        private const int MaxRecoveryFailures = 5;
        private const int MaxReportAttempts = 10;
        private const int ReportMaxAgeDays = 3;

        [Serializable]
        private sealed class SessionMeta
        {
            public string sid;
            public string uid;
            public long startMs;
            public long cursor;
            public int seq;
            /// <summary>Byte length of the chunk being sent (0 = none): sent again unchanged if its answer was lost.</summary>
            public long pendingLength;
            public bool ended;
            public bool crashed;
            public bool autoplay;
            public bool uploadAllowed;
            public string player;
            public string playerFull;
            public string steamId;
            public string ugsId;
            public string version;
            public string flavor;
            public bool host;
            public string[] gameIds = Array.Empty<string>();
            public int errors;
            public int warnings;
            public long lines;
        }

        private enum Outcome
        {
            /// <summary>Everything sent (or nothing more can ever be sent).</summary>
            Done,
            /// <summary>Chunks went up, more remain for the next pass.</summary>
            Progress,
            /// <summary>Stopped by a passing failure (network, server, auth).</summary>
            Failed,
        }

        private sealed class Session
        {
            public SessionMeta meta;
            public string logPath;
            public string metaPath;
            public bool current;
            public int failures;
            public bool dead;
        }

        private static PlaytestLogRuntime s_running;

        private string _directory;
        private Session _session;
        private FileStream _file;
        private readonly LogBuffer _buffer = new();
        private FirestoreLogClient _client;
        private RemoteLogConfig _config;
        private float _nextConfigPoll;
        private float _lastConfigOk = -9999f;
        private bool _uploadAllowed;
        private bool _quitUploadDone;
        private readonly SemaphoreSlim _uploadLock = new(1, 1);
        private readonly List<Session> _recovered = new();
        private readonly HashSet<string> _gameIds = new();
        private CancellationTokenSource _cts;
        private int _failedPasses;
        private string _reportsDir;
        private long _lastReportMs;

        private int _frames;
        private float _frameTime;
        private float _worstFrame;
        private float _nextPerfSample;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_running = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor || s_running != null)
            {
                return;
            }
            var _go = new GameObject("[PlaytestLogs]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(_go);
            _go.AddComponent<PlaytestLogRuntime>();
        }

        private void Awake()
        {
            s_running = this;
            _cts = new CancellationTokenSource();
            string[] _args = Environment.GetCommandLineArgs();
            bool _autoplay = Array.IndexOf(_args, AutoplaySwitch) >= 0;
            _uploadAllowed = !_autoplay || Array.IndexOf(_args, UploadInAutoplaySwitch) >= 0;

            try
            {
                _directory = Path.Combine(Application.persistentDataPath, "logs");
                Directory.CreateDirectory(_directory);
                string _sid = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                _session = new Session
                {
                    current = true,
                    logPath = Path.Combine(_directory, _sid + ".ndjson"),
                    metaPath = Path.Combine(_directory, _sid + ".meta.json"),
                    meta = new SessionMeta
                    {
                        sid = _sid,
                        startMs = NowMs(),
                        autoplay = _autoplay,
                        uploadAllowed = _uploadAllowed,
                        version = Application.version,
                        flavor = GameFlavor.IsSteam ? "steam" : "relay",
                    },
                };
                _file = new FileStream(_session.logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                SaveMeta(_session);
            }
            catch (Exception _e)
            {
                Debug.LogWarning($"[PLAYTEST-LOGS] Local log unavailable, logging disabled: {_e.Message}");
                enabled = false;
                return;
            }

            GameLog.Sink = _buffer;
            Application.logMessageReceivedThreaded += OnUnityLog;
            Application.wantsToQuit += OnWantsToQuit;

            GameLog.Info("session", "session.start",
                "sid", _session.meta.sid, "version", Application.version, "flavor", _session.meta.flavor,
                "autoplay", _autoplay, "upload", _uploadAllowed,
                "os", SystemInfo.operatingSystem, "cpu", SystemInfo.processorType, "cores", SystemInfo.processorCount,
                "ramMb", SystemInfo.systemMemorySize, "gpu", SystemInfo.graphicsDeviceName,
                "gpuMb", SystemInfo.graphicsMemorySize, "api", SystemInfo.graphicsDeviceType,
                "screen", $"{Screen.width}x{Screen.height}@{Screen.currentResolution.refreshRateRatio.value:0}",
                "fullscreen", Screen.fullScreenMode, "quality", QualitySettings.GetQualityLevel(),
                "lang", Application.systemLanguage);

            _nextPerfSample = Time.unscaledTime + PerfSampleSeconds; // the loading frame would read as 0.3 fps
            _client = new FirestoreLogClient(_directory);
            _reportsDir = Path.Combine(_directory, "reports");
            try { Directory.CreateDirectory(_reportsDir); } catch (Exception) { /* reports then stay unsent */ }
            BugReportWindow.Create(this, _args);
            FindRecoverableSessions();
            PruneOldSessions();
            FlushLoop(_cts.Token).Forget();
            if (_uploadAllowed)
            {
                UploadLoop(_cts.Token).Forget();
            }
        }

        private void Update()
        {
            float _dt = Time.unscaledDeltaTime;
            _frames++;
            _frameTime += _dt;
            if (_dt > _worstFrame) _worstFrame = _dt;
            if (Time.unscaledTime < _nextPerfSample) return;
            _nextPerfSample = Time.unscaledTime + PerfSampleSeconds;
            if (_frames > 0 && _frameTime > 0f)
            {
                GameLog.Info("perf", "perf.sample",
                    "fps", _frames / _frameTime, "fpsMin", _worstFrame > 0f ? 1f / _worstFrame : 0f,
                    "monoMb", GC.GetTotalMemory(false) / (1024 * 1024),
                    "rttMs", CurrentRttMs());
            }
            _frames = 0;
            _frameTime = 0f;
            _worstFrame = 0f;
        }

        private static long CurrentRttMs()
        {
            NetworkManager _nm = NetworkManager.Singleton;
            if (_nm == null || !_nm.IsClient || _nm.IsServer || _nm.NetworkConfig?.NetworkTransport == null) return -1;
            try { return (long)_nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId); }
            catch (Exception) { return -1; }
        }

        private static void OnUnityLog(string _condition, string _stack, LogType _type)
        {
            char _level;
            switch (_type)
            {
                case LogType.Warning: _level = 'W'; break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception: _level = 'E'; break;
                default: _level = 'I'; break;
            }
            string _tag = null;
            if (!string.IsNullOrEmpty(_condition) && _condition[0] == '[')
            {
                int _close = _condition.IndexOf(']');
                if (_close > 1 && _close < 40) _tag = _condition.Substring(1, _close - 1);
            }
            if (_type == LogType.Exception && _tag == null)
            {
                int _colon = _condition?.IndexOf(':') ?? -1;
                _tag = _colon > 0 && _colon < 80 ? _condition.Substring(0, _colon) : "Exception";
            }
            GameLog.WriteUnityLog(_level, _tag, _condition, _level == 'E' ? _stack : null);
        }

        // ---- local file ----

        private async UniTaskVoid FlushLoop(CancellationToken _ct)
        {
            while (!_ct.IsCancellationRequested)
            {
                await UniTask.Delay(1000, DelayType.Realtime, PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
                FlushToFile(false);
            }
        }

        private void FlushToFile(bool _all)
        {
            if (_file == null) return;
            List<string> _lines = _buffer.Drain(NowMs(), _all);
            if (_lines.Count == 0) return;
            try
            {
                var _sb = new StringBuilder();
                foreach (string _line in _lines) _sb.Append(_line).Append('\n');
                byte[] _bytes = Encoding.UTF8.GetBytes(_sb.ToString());
                _file.Write(_bytes, 0, _bytes.Length);
                _file.Flush();
            }
            catch (Exception)
            {
                // Disk full / file gone: lines are lost, the game goes on.
            }
        }

        private void RefreshCurrentMeta()
        {
            SessionMeta _meta = _session.meta;
            PlayerInfo _info = LocalPlayerInfoHolder.playerInfo;
            _meta.player = _info.playerName.ToString();
            _meta.playerFull = _info.playerFullName.ToString();
            _meta.steamId = _info.playerSteamId != 0 ? _info.playerSteamId.ToString() : null;
            _meta.ugsId = UgsPlayerId() ?? _meta.ugsId;
            _meta.host = GameLog.IsHost;
            if (!string.IsNullOrEmpty(GameLog.GameId) && _gameIds.Add(GameLog.GameId))
            {
                _meta.gameIds = _gameIds.ToArray();
            }
            _meta.errors = _buffer.Errors;
            _meta.warnings = _buffer.Warnings;
            _meta.lines = _buffer.Lines;
        }

        private static string UgsPlayerId()
        {
            try
            {
                if (Unity.Services.Core.UnityServices.State != Unity.Services.Core.ServicesInitializationState.Initialized) return null;
                var _auth = Unity.Services.Authentication.AuthenticationService.Instance;
                return _auth.IsSignedIn ? _auth.PlayerId : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- upload ----

        private async UniTaskVoid UploadLoop(CancellationToken _ct)
        {
            await UniTask.Delay(3000, DelayType.Realtime, PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
            while (!_ct.IsCancellationRequested)
            {
                bool _ok = false;
                try
                {
                    await _uploadLock.WaitAsync(_ct);
                    try
                    {
                        _ok = await UploadPass(_ct);
                    }
                    finally
                    {
                        _uploadLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception _e)
                {
                    GameLog.Warn("log", "log.upload.error", "error", _e.Message);
                }
                _failedPasses = _ok ? 0 : Math.Min(_failedPasses + 1, 8);
                int _seconds = _config?.flushSeconds ?? 20;
                if (_failedPasses > 0)
                {
                    _seconds = Math.Min(MaxBackoffSeconds, _seconds << Math.Min(_failedPasses, 4)); // offline: back off
                }
                await UniTask.Delay(_seconds * 1000, DelayType.Realtime, PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
            }
        }

        /// <summary>True when the pass did what it could (nothing failed for a passing reason).</summary>
        private async UniTask<bool> UploadPass(CancellationToken _ct)
        {
            FlushToFile(false);
            RefreshCurrentMeta();
            SaveMeta(_session);
            await PollConfigIfDue(_ct);
            if (!UploadEnabled())
            {
                return true;
            }
            if (!await _client.EnsureAuth(_ct))
            {
                return false;
            }

            bool _ok = await UploadSession(_session, _ct) != Outcome.Failed;
            for (int _i = _recovered.Count - 1; _i >= 0; _i--)
            {
                Session _old = _recovered[_i];
                Outcome _outcome = await UploadSession(_old, _ct);
                if (_outcome == Outcome.Done || (_outcome == Outcome.Failed && ++_old.failures >= MaxRecoveryFailures))
                {
                    _recovered.RemoveAt(_i);
                }
            }
            _ok &= await SendQueuedReports(_ct, null);
            return _ok;
        }

        /// <summary>Unknown (never read, or unreachable for minutes) and off both mean: nothing goes online.</summary>
        private bool UploadEnabled() => _config != null && _config.enabled;

        private async UniTask PollConfigIfDue(CancellationToken _ct)
        {
            if (Time.realtimeSinceStartup < _nextConfigPoll) return;
            _nextConfigPoll = Time.realtimeSinceStartup + ConfigPollSeconds;
            FirestoreLogClient.ConfigStatus _status = FirestoreLogClient.ConfigStatus.Unreachable;
            string _json = null;
            if (await _client.EnsureAuth(_ct))
            {
                (_status, _json) = await _client.ReadConfig(_ct);
            }
            RemoteLogConfig _fresh;
            switch (_status)
            {
                case FirestoreLogClient.ConfigStatus.Ok:
                    _fresh = RemoteLogConfig.Parse(_json) ?? new RemoteLogConfig { enabled = false }; // garbage = off
                    _lastConfigOk = Time.realtimeSinceStartup;
                    break;
                case FirestoreLogClient.ConfigStatus.Missing:
                    _fresh = new RemoteLogConfig { enabled = false };
                    _lastConfigOk = Time.realtimeSinceStartup;
                    break;
                default:
                    if (Time.realtimeSinceStartup - _lastConfigOk > ConfigStaleSeconds && _config != null)
                    {
                        GameLog.Warn("log", "log.switch.unknown");
                        _config = null; // unreadable for too long: back to local only
                    }
                    return;
            }
            if (_config == null || _config.enabled != _fresh.enabled)
            {
                GameLog.Info("log", "log.switch", "enabled", _fresh.enabled);
            }
            _config = _fresh;
            _buffer.CapPerMinute = _fresh.capPerMinute;
            if (!_fresh.enabled)
            {
                DropPending(_session);
                foreach (Session _old in _recovered) DropPending(_old);
                _recovered.Clear();
            }
        }

        /// <summary>Sends the index then the complete lines after the cursor (a few chunks per pass).</summary>
        private async UniTask<Outcome> UploadSession(Session _target, CancellationToken _ct)
        {
            if (_target.dead) return Outcome.Done;
            SessionMeta _meta = _target.meta;
            if (!string.IsNullOrEmpty(_meta.uid) && _meta.uid != _client.Uid)
            {
                // Written under another anonymous account (lost auth file): the rules will never let us update it.
                _target.dead = true;
                GameLog.Warn("log", "log.session.orphan", "sid", _meta.sid);
                return Outcome.Done;
            }
            FirestoreLogClient.Result _index = await _client.UpsertSession(_meta.sid, IndexFields(_target, _client.Uid), _ct);
            if (_index == FirestoreLogClient.Result.Rejected)
            {
                _target.dead = true;
                GameLog.Warn("log", "log.session.rejected", "sid", _meta.sid);
                return Outcome.Done;
            }
            if (_index != FirestoreLogClient.Result.Ok) return Outcome.Failed;
            if (_meta.uid != _client.Uid)
            {
                _meta.uid = _client.Uid;
                SaveMeta(_target);
            }

            bool _verbose = _config.IsVerbose(_meta.player, _meta.steamId, _meta.ugsId, _meta.sid);
            for (int _pass = 0; _pass < MaxChunksPerPass; _pass++)
            {
                await PollConfigIfDue(_ct);
                if (!UploadEnabled()) return Outcome.Done; // switched off mid-pass: stop now

                byte[] _bytes;
                int _count;
                bool _windowFull;
                try
                {
                    using var _read = new FileStream(_target.logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (_meta.cursor >= _read.Length)
                    {
                        _meta.pendingLength = 0;
                        return Outcome.Done;
                    }
                    _read.Seek(_meta.cursor, SeekOrigin.Begin);
                    _bytes = new byte[(int)Math.Min(_read.Length - _meta.cursor, LogChunk.MaxChunkBytes * 2L)];
                    _count = _read.Read(_bytes, 0, _bytes.Length);
                    _windowFull = _count == LogChunk.MaxChunkBytes * 2;
                }
                catch (Exception)
                {
                    if (!_target.current) _target.dead = true; // an unreadable old file is given up
                    return _target.current ? Outcome.Failed : Outcome.Done;
                }

                LogChunk.RangeKind _kind = LogChunk.NextRange(_bytes, _count, _windowFull, _meta.pendingLength, out int _length);
                if (_kind == LogChunk.RangeKind.None)
                {
                    if (!_target.current) _target.dead = true; // an unfinished last line of a dead session: done
                    return Outcome.Done;
                }
                if (_kind == LogChunk.RangeKind.Skip)
                {
                    GameLog.Warn("log", "log.range.skipped", "sid", _meta.sid, "bytes", _length);
                    Advance(_target, _length, false);
                    continue;
                }

                LogChunk _chunk = LogChunk.Build(Encoding.UTF8.GetString(_bytes, 0, _length),
                    (_lv, _cat) => _config.Uploads(_lv, _cat, _verbose));
                if (_chunk.lineCount == 0)
                {
                    Advance(_target, _length, false);
                    continue;
                }
                _meta.pendingLength = _length;
                SaveMeta(_target); // the range is fixed before the request leaves
                FirestoreLogClient.Result _sent = await _client.CreateChunk(_meta.sid, _meta.seq, ChunkFields(_target, _chunk), _ct);
                switch (_sent)
                {
                    case FirestoreLogClient.Result.Ok:
                    case FirestoreLogClient.Result.AlreadyExists:
                        Advance(_target, _length, true);
                        break;
                    case FirestoreLogClient.Result.Rejected:
                        GameLog.Warn("log", "log.chunk.rejected", "sid", _meta.sid, "seq", _meta.seq, "bytes", _length);
                        Advance(_target, _length, true); // never accepted as is: step over, keep the session going
                        break;
                    default:
                        return Outcome.Failed; // pendingLength kept: the same range goes again next time
                }
            }
            return Outcome.Progress;
        }

        private static void Advance(Session _target, int _length, bool _usedSeq)
        {
            _target.meta.cursor += _length;
            _target.meta.pendingLength = 0;
            if (_usedSeq) _target.meta.seq++;
            SaveMeta(_target);
        }

        private static void DropPending(Session _target)
        {
            try
            {
                _target.meta.cursor = new FileInfo(_target.logPath).Length;
                _target.meta.pendingLength = 0;
                SaveMeta(_target);
            }
            catch (Exception)
            {
                // Nothing to drop.
            }
        }

        private static FirestoreFields IndexFields(Session _target, string _uid)
        {
            SessionMeta _m = _target.meta;
            var _fields = new FirestoreFields()
                .String("uid", _uid)
                .String("sid", _m.sid)
                .String("player", _m.player)
                .String("playerFull", _m.playerFull)
                .String("steamId", _m.steamId)
                .String("ugsId", _m.ugsId)
                .String("version", _m.version)
                .String("flavor", _m.flavor)
                .Bool("autoplay", _m.autoplay)
                .Bool("host", _m.host)
                .StringArray("gameIds", _m.gameIds ?? Array.Empty<string>())
                .Int("errors", _m.errors)
                .Int("warnings", _m.warnings)
                .Int("lines", _m.lines)
                .Bool("ended", _m.ended)
                .Bool("crashed", _m.crashed)
                .Timestamp("startedAt", _m.startMs)
                .Timestamp("expireAt", _m.startMs + ExpireDays * 86400000L);
            if (_target.current)
            {
                // A recovered session keeps what its own run wrote (last seen, machine).
                _fields.Timestamp("lastSeen", NowMs())
                    .String("os", SystemInfo.operatingSystem)
                    .String("gpu", SystemInfo.graphicsDeviceName);
            }
            return _fields;
        }

        private static FirestoreFields ChunkFields(Session _target, LogChunk _chunk)
        {
            return new FirestoreFields()
                .String("sid", _target.meta.sid)
                .String("player", _target.meta.player)
                .Int("seq", _target.meta.seq)
                .Timestamp("t0", _chunk.t0 > 0 ? _chunk.t0 : NowMs())
                .Timestamp("t1", _chunk.t1 > 0 ? _chunk.t1 : NowMs())
                .StringArray("gameIds", _chunk.gameIds)
                .Int("lineCount", _chunk.lineCount)
                .Int("errors", _chunk.errors)
                .Int("warnings", _chunk.warnings)
                .String("lines", _chunk.lines)
                .Timestamp("expireAt", _target.meta.startMs + ExpireDays * 86400000L);
        }

        // ---- bug reports (F8 window) and crash alerts ----

        [Serializable]
        private sealed class LastLine
        {
            public long t;
            public string ph;
            public int d;
            public string g;
            public string ro;
        }

        /// <summary>Milliseconds before the next report may be sent (anti-spam).</summary>
        internal long ReportWaitMs() => BugReport.WaitBeforeNext(_lastReportMs, NowMs());

        /// <summary>
        /// The F8 window's Envoyer, step 1 (instant): marks the moment in the log and stores the report until it is
        /// online. Null = it will never go online (local-only run, or it could not be saved).
        /// </summary>
        internal string QueueReport(string _text, byte[] _jpg)
        {
            var _report = new BugReport
            {
                id = BugReport.NewId(DateTime.UtcNow),
                kind = BugReport.KindReport,
                sid = _session.meta.sid,
                gameId = GameLog.GameId,
                player = LocalPlayerInfoHolder.playerInfo.playerName.ToString(),
                seat = GameLog.Seat?.ToString(),
                role = GameLog.Role,
                phase = GameLog.Phase,
                day = GameLog.Day,
                text = BugReport.CleanText(_text),
                t = NowMs(),
                version = Application.version,
                flavor = _session.meta.flavor,
                hasCapture = _jpg != null && _jpg.Length > 0,
            };
            GameLog.Mark("bug.report", "id", _report.id, "text", _report.text);
            _lastReportMs = _report.t;
            if (!_uploadAllowed || !SaveReport(_report, _jpg))
            {
                return null; // a local-only run (autoplay) keeps only the mark in its local log
            }
            return _report.id;
        }

        /// <summary>
        /// Step 2: sends the waiting log lines and the report now, giving up after <paramref name="_seconds"/> (the
        /// report then stays queued for the regular passes). Never blocks the game: the window is already closed.
        /// </summary>
        internal async UniTask<bool> TrySendNow(string _reportId, float _seconds)
        {
            if (string.IsNullOrEmpty(_reportId)) return false;
            using var _limit = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _limit.CancelAfter(TimeSpan.FromSeconds(_seconds));
            try
            {
                await _uploadLock.WaitAsync(_limit.Token);
                try
                {
                    _nextConfigPoll = 0f; // the switch may have changed since the last poll
                    await PollConfigIfDue(_limit.Token);
                    if (!UploadEnabled() || !await _client.EnsureAuth(_limit.Token))
                    {
                        return false;
                    }
                    FlushToFile(true); // the marked moment and what led to it go online first
                    await UploadSession(_session, _limit.Token);
                    var _sent = new HashSet<string>();
                    await SendQueuedReports(_limit.Token, _sent, _reportId);
                    return _sent.Contains(_reportId);
                }
                finally
                {
                    _uploadLock.Release();
                }
            }
            catch (Exception)
            {
                return false; // timeout or offline: queued, the next pass sends it
            }
        }

        private void QueueCrashReport(SessionMeta _meta, string _logPath)
        {
            if (_meta.autoplay) return; // killed autoplay processes are not crashes worth an alert
            LastLine _last = ReadLastLine(_logPath);
            SaveReport(new BugReport
            {
                // The crashed session's id: copies launched together all spot the same crash, the rules keep one.
                id = _meta.sid,
                kind = BugReport.KindCrash,
                sid = _meta.sid,
                gameId = _last?.g ?? (_meta.gameIds != null && _meta.gameIds.Length > 0 ? _meta.gameIds[_meta.gameIds.Length - 1] : null),
                player = _meta.player,
                role = _last?.ro,
                phase = _last?.ph,
                day = _last?.d ?? 0,
                text = string.Empty,
                t = _last != null && _last.t > 0 ? _last.t : _meta.startMs,
                version = _meta.version,
                flavor = _meta.flavor,
            }, null);
        }

        private static LastLine ReadLastLine(string _logPath)
        {
            try
            {
                using var _read = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long _start = Math.Max(0, _read.Length - 16384);
                _read.Seek(_start, SeekOrigin.Begin);
                var _bytes = new byte[_read.Length - _start];
                int _count = _read.Read(_bytes, 0, _bytes.Length);
                string[] _lines = Encoding.UTF8.GetString(_bytes, 0, _count).Split('\n');
                for (int _i = _lines.Length - 1; _i >= 0; _i--)
                {
                    if (_lines[_i].StartsWith("{") && _lines[_i].EndsWith("}"))
                    {
                        return JsonUtility.FromJson<LastLine>(_lines[_i]);
                    }
                }
            }
            catch (Exception)
            {
                // No context: the alert still says who crashed.
            }
            return null;
        }

        private bool SaveReport(BugReport _report, byte[] _jpg)
        {
            try
            {
                if (File.Exists(Path.Combine(_reportsDir, _report.id + ".json"))) return true; // already queued
                _report.queuedAt = NowMs();
                if (_report.hasCapture) File.WriteAllBytes(Path.Combine(_reportsDir, _report.id + ".jpg"), _jpg);
                string _path = Path.Combine(_reportsDir, _report.id + ".json");
                File.WriteAllText(_path + ".tmp", JsonUtility.ToJson(_report));
                File.Move(_path + ".tmp", _path);
                return true;
            }
            catch (Exception _e)
            {
                GameLog.Warn("log", "log.report.unsaved", "error", _e.Message);
                return false;
            }
        }

        /// <summary>
        /// Sends the queued reports (capture first, then the document that triggers the Discord alert), the given one
        /// first. Each report is on its own: one that keeps failing never blocks the others, and is dropped after
        /// <see cref="MaxReportAttempts"/> passes or <see cref="ReportMaxAgeDays"/> days (its moment's logs would be gone).
        /// False = stopped by passing failures.
        /// </summary>
        private async UniTask<bool> SendQueuedReports(CancellationToken _ct, HashSet<string> _sent, string _first = null)
        {
            string[] _files;
            try { _files = Directory.GetFiles(_reportsDir, "*.json"); }
            catch (Exception) { return true; }
            Array.Sort(_files, StringComparer.Ordinal);
            if (_first != null)
            {
                int _index = Array.FindIndex(_files, _f => Path.GetFileNameWithoutExtension(_f) == _first);
                if (_index > 0) (_files[0], _files[_index]) = (_files[_index], _files[0]);
            }
            int _failuresInARow = 0;
            foreach (string _file in _files)
            {
                _ct.ThrowIfCancellationRequested();
                if (_failuresInARow >= 3) return false; // offline: do not hammer, the next pass retries
                bool? _ok;
                try
                {
                    _ok = await SendReport(_file, _ct, _sent);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception _e)
                {
                    GameLog.Warn("log", "log.report.error", "file", Path.GetFileName(_file), "error", _e.Message);
                    _ok = false;
                }
                _failuresInARow = _ok == false ? _failuresInARow + 1 : 0;
            }
            return _failuresInARow == 0;
        }

        /// <summary>True = sent (or dropped for good), false = passing failure (kept for later), null = nothing to do.</summary>
        private async UniTask<bool?> SendReport(string _file, CancellationToken _ct, HashSet<string> _sent)
        {
            BugReport _report;
            try { _report = JsonUtility.FromJson<BugReport>(File.ReadAllText(_file)); }
            catch (Exception) { _report = null; }
            string _jpgPath = Path.ChangeExtension(_file, ".jpg");
            if (_report == null || string.IsNullOrEmpty(_report.id))
            {
                TryDelete(_file);
                TryDelete(_jpgPath);
                return null;
            }
            if (_report.attempts >= MaxReportAttempts
                || (_report.queuedAt > 0 && NowMs() - _report.queuedAt > ReportMaxAgeDays * 86400000L))
            {
                GameLog.Warn("log", "log.report.dropped", "id", _report.id, "attempts", _report.attempts);
                TryDelete(_file);
                TryDelete(_jpgPath);
                return true;
            }
            _report.attempts++;
            File.WriteAllText(_file, JsonUtility.ToJson(_report));

            if (_report.hasCapture && !_report.captureSent)
            {
                bool _triedBefore = _report.captureTried;
                _report.captureTried = true;
                File.WriteAllText(_file, JsonUtility.ToJson(_report));
                FirestoreLogClient.Result _capture = File.Exists(_jpgPath)
                    ? await _client.UploadCapture(_report.id, File.ReadAllBytes(_jpgPath), _ct)
                    : FirestoreLogClient.Result.Rejected;
                if (_capture == FirestoreLogClient.Result.Failed) return false;
                if (_capture == FirestoreLogClient.Result.Rejected && !_triedBefore)
                {
                    _report.hasCapture = false; // never accepted: the text still goes
                }
                else
                {
                    // Ok, or refused on a retry: the rules allow create only, so the first try did get it online.
                    _report.captureSent = true;
                }
                File.WriteAllText(_file, JsonUtility.ToJson(_report));
            }
            FirestoreLogClient.Result _doc = await _client.CreateReport(_report.id,
                _report.ToFields(_client.Uid, _report.t + ExpireDays * 86400000L), _ct);
            if (_doc == FirestoreLogClient.Result.Failed) return false;
            if (_doc == FirestoreLogClient.Result.Rejected)
            {
                GameLog.Warn("log", "log.report.rejected", "id", _report.id);
            }
            else
            {
                _sent?.Add(_report.id);
                GameLog.Info("log", "log.report.sent", "id", _report.id, "kind", _report.kind);
            }
            TryDelete(_file);
            TryDelete(_jpgPath);
            return true;
        }

        private static void TryDelete(string _path)
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception) { /* next pass */ }
        }

        // ---- previous sessions ----

        private void FindRecoverableSessions()
        {
            foreach (string _metaPath in Directory.GetFiles(_directory, "*.meta.json"))
            {
                try
                {
                    var _meta = JsonUtility.FromJson<SessionMeta>(File.ReadAllText(_metaPath));
                    if (_meta == null || string.IsNullOrEmpty(_meta.sid) || _meta.sid == _session.meta.sid) continue;
                    if (!_meta.uploadAllowed) continue; // a local-only run (autoplay) never goes online, even later
                    string _logPath = Path.Combine(_directory, _meta.sid + ".ndjson");
                    if (!File.Exists(_logPath) || IsWrittenByAnotherProcess(_logPath)) continue;
                    if (_meta.cursor >= new FileInfo(_logPath).Length && _meta.ended) continue;
                    if (!_meta.ended && !_meta.crashed)
                    {
                        _meta.crashed = true;
                        GameLog.Warn("session", "session.previous.crashed", "sid", _meta.sid);
                        QueueCrashReport(_meta, _logPath);
                    }
                    _meta.ended = true;
                    var _old = new Session { meta = _meta, logPath = _logPath, metaPath = _metaPath };
                    SaveMeta(_old);
                    _recovered.Add(_old);
                }
                catch (Exception)
                {
                    // A broken meta file is skipped.
                }
            }
        }

        // Keeps the newest sessions, and older ones still waiting to go online (up to a hard cap, so an offline PC
        // does not fill its disk).
        private void PruneOldSessions()
        {
            try
            {
                string[] _logs = Directory.GetFiles(_directory, "*.ndjson").OrderByDescending(_p => _p, StringComparer.Ordinal).ToArray();
                for (int _i = KeptSessions; _i < _logs.Length; _i++)
                {
                    if (IsWrittenByAnotherProcess(_logs[_i])) continue;
                    string _sid = Path.GetFileNameWithoutExtension(_logs[_i]);
                    if (_i < MaxKeptUnsentSessions && _recovered.Any(_s => _s.meta.sid == _sid)) continue;
                    _recovered.RemoveAll(_s => _s.meta.sid == _sid);
                    File.Delete(_logs[_i]);
                    File.Delete(Path.Combine(_directory, _sid + ".meta.json"));
                }
            }
            catch (Exception)
            {
                // Pruning is best effort.
            }
        }

        // Another copy of the game (several Relay builds on one PC) keeps its current log open for writing.
        private static bool IsWrittenByAnotherProcess(string _path)
        {
            try
            {
                using var _probe = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        // Atomic: a crash while saving never leaves a truncated meta (the crashed session is the one that matters).
        private static void SaveMeta(Session _target)
        {
            try
            {
                string _tmp = _target.metaPath + ".tmp";
                File.WriteAllText(_tmp, JsonUtility.ToJson(_target.meta));
                if (File.Exists(_target.metaPath)) File.Replace(_tmp, _target.metaPath, null);
                else File.Move(_tmp, _target.metaPath);
            }
            catch (Exception)
            {
                // The next save retries.
            }
        }

        // ---- quit ----

        private bool OnWantsToQuit()
        {
            if (_quitUploadDone || _session == null)
            {
                return true;
            }
            _quitUploadDone = true;
            GameLog.Info("session", "session.end");
            FlushToFile(true);
            RefreshCurrentMeta();
            _session.meta.ended = true;
            SaveMeta(_session);
            if (_config != null && !_config.enabled)
            {
                DropPending(_session); // logged while off: never online, not even at the next launch
            }
            if (!_uploadAllowed || !UploadEnabled())
            {
                return true;
            }
            FinalUploadThenQuit().Forget();
            return false;
        }

        private async UniTaskVoid FinalUploadThenQuit()
        {
            _cts.Cancel(); // the regular pass stops at its next await and releases the lock (its range stays pending)
            using var _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _uploadLock.WaitAsync(_timeout.Token);
                try
                {
                    if (await _client.EnsureAuth(_timeout.Token))
                    {
                        await UploadSession(_session, _timeout.Token);
                    }
                }
                finally
                {
                    _uploadLock.Release();
                }
            }
            catch (Exception)
            {
                // Whatever is left goes up at the next launch.
            }
            Application.Quit();
        }

        private void OnDestroy()
        {
            if (s_running != this) return;
            _cts?.Cancel();
            Application.logMessageReceivedThreaded -= OnUnityLog;
            Application.wantsToQuit -= OnWantsToQuit;
            FlushToFile(true);
            if (_session != null)
            {
                RefreshCurrentMeta();
                _session.meta.ended = true;
                SaveMeta(_session);
                if (_config != null && !_config.enabled)
                {
                    DropPending(_session); // the shutdown lines were logged while off: never online, even next launch
                }
            }
            GameLog.Sink = null;
            _file?.Dispose();
            _file = null;
            s_running = null;
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
#endif
