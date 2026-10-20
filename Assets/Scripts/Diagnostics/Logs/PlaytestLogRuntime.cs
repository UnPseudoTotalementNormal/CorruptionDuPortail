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
    /// <c>enabled</c>. Switch off = no upload within a minute and the waiting lines are dropped (the local file stays).
    /// Never runs in the Editor; an autoplay run logs locally and uploads only with <c>-playtest-logs-upload</c>.
    /// A session that did not end cleanly (crash, killed) is uploaded at the next launch, marked crashed.
    /// Read the logs with <c>tools/logs/logs.py</c>.
    /// </summary>
    internal sealed class PlaytestLogRuntime : MonoBehaviour
    {
        public const string UploadInAutoplaySwitch = "-playtest-logs-upload";
        // Same switch as Unpseudo.Autoplay.AutoplayCommandLine, whose code is compiled out of non-development builds.
        private const string AutoplaySwitch = "-autoplay";
        private const int KeptSessions = 10;
        private const int ExpireDays = 60;
        private const float ConfigPollSeconds = 60f;
        private const float PerfSampleSeconds = 30f;
        private const int MaxChunksPerPass = 6;

        [Serializable]
        private sealed class SessionMeta
        {
            public string sid;
            public long startMs;
            public long cursor;
            public int seq;
            public bool ended;
            public bool crashed;
            public bool autoplay;
            public string player;
            public string playerFull;
            public string steamId;
            public string version;
            public string flavor;
            public bool host;
            public string[] gameIds = Array.Empty<string>();
            public int errors;
            public int warnings;
            public long lines;
        }

        private sealed class Session
        {
            public SessionMeta meta;
            public string logPath;
            public string metaPath;
            public bool current;
            public int failures;
        }

        private const int MaxRecoveryFailures = 3;

        private static PlaytestLogRuntime s_running;

        private string _directory;
        private Session _session;
        private FileStream _file;
        private readonly LogBuffer _buffer = new();
        private FirestoreLogClient _client;
        private RemoteLogConfig _config;
        private float _nextConfigPoll;
        private bool _uploadAllowed;
        private bool _quitUploadDone;
        private readonly List<Session> _recovered = new();
        private readonly HashSet<string> _gameIds = new();
        private CancellationTokenSource _cts;

        private int _frames;
        private float _frameTime;
        private float _worstFrame;
        private float _nextPerfSample;

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
                FlushToFile();
            }
        }

        private void FlushToFile()
        {
            if (_file == null) return;
            List<string> _lines = _buffer.Drain(NowMs());
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
            _meta.host = GameLog.IsHost;
            if (!string.IsNullOrEmpty(GameLog.GameId) && _gameIds.Add(GameLog.GameId))
            {
                _meta.gameIds = _gameIds.ToArray();
            }
            _meta.errors = _buffer.Errors;
            _meta.warnings = _buffer.Warnings;
            _meta.lines = _buffer.Lines;
        }

        // ---- upload ----

        private async UniTaskVoid UploadLoop(CancellationToken _ct)
        {
            await UniTask.Delay(3000, DelayType.Realtime, PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
            while (!_ct.IsCancellationRequested)
            {
                try
                {
                    await UploadPass(_ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception _e)
                {
                    GameLog.Warn("log", "log.upload.error", "error", _e.Message);
                }
                int _seconds = _config?.flushSeconds ?? 20;
                await UniTask.Delay(_seconds * 1000, DelayType.Realtime, PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
            }
        }

        private async UniTask UploadPass(CancellationToken _ct)
        {
            if (Time.realtimeSinceStartup >= _nextConfigPoll)
            {
                _nextConfigPoll = Time.realtimeSinceStartup + ConfigPollSeconds;
                if (await _client.EnsureAuth(_ct))
                {
                    string _json = await _client.ReadConfig(_ct);
                    RemoteLogConfig _fresh = RemoteLogConfig.Parse(_json);
                    if (_fresh != null)
                    {
                        if (_config == null || _config.enabled != _fresh.enabled)
                        {
                            GameLog.Info("log", "log.switch", "enabled", _fresh.enabled);
                        }
                        _config = _fresh;
                        _buffer.CapPerMinute = _fresh.capPerMinute;
                    }
                }
            }

            FlushToFile();
            RefreshCurrentMeta();
            SaveMeta(_session);
            if (_config == null)
            {
                return; // switch unknown: keep everything local, the cursor waits
            }
            if (!_config.enabled)
            {
                DropPending(_session);
                foreach (Session _old in _recovered) DropPending(_old);
                _recovered.Clear();
                return;
            }
            if (!await _client.EnsureAuth(_ct))
            {
                return;
            }

            await UploadSession(_session, _ct);
            for (int _i = _recovered.Count - 1; _i >= 0; _i--)
            {
                if (await UploadSession(_recovered[_i], _ct))
                {
                    _recovered.RemoveAt(_i);
                }
            }
        }

        /// <summary>Sends the index then every complete line after the cursor. True when the file is fully sent.</summary>
        private async UniTask<bool> UploadSession(Session _target, CancellationToken _ct)
        {
            if (!await _client.UpsertSession(_target.meta.sid, IndexFields(_target, _client.Uid), _ct))
            {
                // An old session written under another anonymous id (auth file lost) can never be updated: give up.
                return !_target.current && ++_target.failures >= MaxRecoveryFailures;
            }
            bool _verbose = _config.IsVerbose(_target.meta.player, _target.meta.steamId, _target.meta.sid);
            for (int _pass = 0; _pass < MaxChunksPerPass; _pass++)
            {
                byte[] _bytes;
                int _count;
                try
                {
                    using var _read = new FileStream(_target.logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (_target.meta.cursor >= _read.Length) return true;
                    _read.Seek(_target.meta.cursor, SeekOrigin.Begin);
                    _bytes = new byte[(int)Math.Min(_read.Length - _target.meta.cursor, LogChunk.MaxChunkBytes * 2L)];
                    _count = _read.Read(_bytes, 0, _bytes.Length);
                }
                catch (Exception)
                {
                    return !_target.current; // an unreadable old file is given up
                }
                int _length = LogChunk.CompleteLinesLength(_bytes, _count);
                if (_length == 0) return !_target.current;

                LogChunk _chunk = LogChunk.Build(Encoding.UTF8.GetString(_bytes, 0, _length),
                    (_lv, _cat) => _config.Uploads(_lv, _cat, _verbose));
                if (_chunk.lineCount > 0)
                {
                    if (!await _client.CreateChunk(_target.meta.sid, _target.meta.seq, ChunkFields(_target, _chunk), _ct))
                    {
                        return false;
                    }
                    _target.meta.seq++;
                }
                _target.meta.cursor += _length;
                SaveMeta(_target);
            }
            return false;
        }

        private static void DropPending(Session _target)
        {
            try
            {
                _target.meta.cursor = new FileInfo(_target.logPath).Length;
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
            long _now = NowMs();
            var _fields = new FirestoreFields()
                .String("uid", _uid)
                .String("sid", _m.sid)
                .String("player", _m.player)
                .String("playerFull", _m.playerFull)
                .String("steamId", _m.steamId)
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
                _fields.Timestamp("lastSeen", _now)
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

        // ---- previous sessions ----

        private void FindRecoverableSessions()
        {
            foreach (string _metaPath in Directory.GetFiles(_directory, "*.meta.json"))
            {
                try
                {
                    var _meta = JsonUtility.FromJson<SessionMeta>(File.ReadAllText(_metaPath));
                    if (_meta == null || _meta.sid == _session.meta.sid) continue;
                    string _logPath = Path.Combine(_directory, _meta.sid + ".ndjson");
                    if (!File.Exists(_logPath) || IsWrittenByAnotherProcess(_logPath)) continue;
                    if (_meta.cursor >= new FileInfo(_logPath).Length && _meta.ended) continue;
                    if (!_meta.ended && !_meta.crashed)
                    {
                        _meta.crashed = true;
                        GameLog.Warn("session", "session.previous.crashed", "sid", _meta.sid);
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

        private void PruneOldSessions()
        {
            try
            {
                string[] _logs = Directory.GetFiles(_directory, "*.ndjson").OrderByDescending(_p => _p, StringComparer.Ordinal).ToArray();
                for (int _i = KeptSessions; _i < _logs.Length; _i++)
                {
                    if (IsWrittenByAnotherProcess(_logs[_i])) continue;
                    string _sid = Path.GetFileNameWithoutExtension(_logs[_i]);
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

        private static void SaveMeta(Session _target)
        {
            try
            {
                File.WriteAllText(_target.metaPath, JsonUtility.ToJson(_target.meta));
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
            FlushToFile();
            RefreshCurrentMeta();
            _session.meta.ended = true;
            SaveMeta(_session);
            if (!_uploadAllowed || _config == null || !_config.enabled)
            {
                return true;
            }
            FinalUploadThenQuit().Forget();
            return false;
        }

        private async UniTaskVoid FinalUploadThenQuit()
        {
            using var _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await UploadSession(_session, _timeout.Token);
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
            FlushToFile();
            if (_session != null)
            {
                RefreshCurrentMeta();
                _session.meta.ended = true;
                SaveMeta(_session);
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
