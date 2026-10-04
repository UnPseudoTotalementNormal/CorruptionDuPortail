#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Autoplay
{
    [Serializable]
    public sealed class AutoplaySessionConfig
    {
        public string scenario = "solohost-8p-randomvalid";
        public int bots = 7;
        public int seed;
        public float timeScale = 10f;
        public ushort port = 7850;
        [Tooltip("Ports tried from `port` when it is busy (autoplay range 7850-7899).")]
        public int portRangeSize = 50;
        public string outputDirectory;
        public float gameTimeoutRealSeconds = 600f;
        public float stateStallRealSeconds = 150f;
        public AutoplayOptions options = new();
    }

    /// <summary>What a session left behind: the verdict, plus the live objects the caller must tear down.</summary>
    public sealed class AutoplaySessionResult
    {
        public string failure;
        public AutoplayJournal.Report report;
        public AutoplayDriver driver;
        public NetworkManager networkManager;
        public AutoplayJournal journal;
    }

    /// <summary>
    /// One complete autoplay game, shared by the PlayMode test (editor) and the dev-build bootstrap (windowed player):
    /// boot → host on a direct loopback UnityTransport (no Lobby / Relay / Steam) → GameScene → N simulated bots →
    /// the designer's classic preset for that player count → force start → play to GameEndingState → report.
    /// Never throws for a game-level failure: it is written to <see cref="AutoplaySessionResult.failure"/> and the report.
    /// </summary>
    public static class AutoplaySession
    {
        private const int BootSceneIndex = 0;
        private const int MainMenuSceneIndex = 1;

        public static IEnumerator RunSoloHost(AutoplaySessionConfig _config, AutoplaySessionResult _result)
        {
            var _journal = new AutoplayJournal(_config.outputDirectory);
            _result.journal = _journal;
            _journal.Record("run.begin", $"scenario={_config.scenario} seed={_config.seed} out={_config.outputDirectory}");
            Application.logMessageReceived += _journal.OnLog; // errors before the driver starts count too

            string _failure = null;
            try
            {
                // --- Boot: BootScene owns the NetworkManager (DontDestroyOnLoad) and then switches to the main menu.
                if (NetworkManager.Singleton == null && SceneManager.GetActiveScene().buildIndex != BootSceneIndex)
                {
                    SceneManager.LoadScene(BootSceneIndex, LoadSceneMode.Single);
                }
            }
            catch (Exception _exception)
            {
                _failure = $"boot: {_exception.Message}";
            }

            bool _ok = true;
            if (_failure == null)
            {
                yield return WaitFor(() => NetworkManager.Singleton != null, 30f, _r => _ok = _r);
                if (!_ok) _failure = "BootScene did not create a NetworkManager";
            }
            if (_failure == null)
            {
                yield return WaitFor(() => SceneManager.GetActiveScene().buildIndex == MainMenuSceneIndex, 30f, _r => _ok = _r);
                if (!_ok) _failure = $"never reached the main menu (active: {SceneManager.GetActiveScene().name})";
            }

            NetworkManager _networkManager = NetworkManager.Singleton;
            _result.networkManager = _networkManager;
            if (_failure == null)
            {
                UnityTransport _transport = _networkManager.GetComponent<UnityTransport>();
                if (_transport == null)
                {
                    _transport = _networkManager.gameObject.AddComponent<UnityTransport>();
                }
                _networkManager.NetworkConfig.NetworkTransport = _transport;
                // A previous run killed mid-game can leave its socket bound inside this process: take the first free
                // port of the autoplay range instead of failing the whole run on a stale bind.
                ushort _port = FindFreeUdpPort(_config.port, _config.portRangeSize);
                if (_port != _config.port)
                {
                    _journal.Record("port", $"UDP {_config.port} busy, using {_port}");
                }
                _transport.SetConnectionData("127.0.0.1", _port, "127.0.0.1");
                ConnectionApprovalGate.Enable(_networkManager);
                if (!_networkManager.StartHost())
                {
                    _failure = "StartHost failed (no free UDP port in the autoplay range?)";
                }
                else
                {
                    _networkManager.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);
                }
            }

            GameManager _gameManager = null;
            CharacterManager _characterManager = null;
            if (_failure == null)
            {
                yield return WaitFor(() =>
                {
                    _gameManager = CompositionRoot.For(_networkManager).GameManager;
                    _characterManager = CompositionRoot.For(_networkManager).CharacterManager;
                    return _gameManager != null && _gameManager.IsSpawned && _characterManager != null &&
                           _gameManager.GetGameState(_gameManager.currentGameStateIndex.Value) is LobbyState &&
                           _characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == _networkManager.LocalClientId);
                }, 60f, _r => _ok = _r);
                if (!_ok) _failure = "GameScene never reached LobbyState with the host's character spawned";
            }

            // --- Simulated players (ids 100..), one per frame so each spawn settles.
            if (_failure == null)
            {
                for (int _i = 0; _i < _config.bots; _i++)
                {
                    _characterManager.SpawnSimulatedPlayer();
                    yield return null;
                }

                int _expected = _config.bots + 1;
                yield return WaitFor(() => CountPlayers(_characterManager) == _expected, 10f, _r => _ok = _r);
                if (!_ok) _failure = $"expected {_expected} players, got {CountPlayers(_characterManager)}";
            }

            if (_failure == null)
            {
                ulong[] _controlled = _characterManager.GetCharacters(false)
                    .Where(_c => _c && !_c.isFake)
                    .Select(_c => _c.ownerClientId.Value)
                    .ToArray();

                var _driverGo = new GameObject("AutoplayDriver");
                UnityEngine.Object.DontDestroyOnLoad(_driverGo);
                AutoplayDriver _driver = _driverGo.AddComponent<AutoplayDriver>();
                _result.driver = _driver;
                _driver.Begin(_networkManager, _controlled, new RandomValidPolicy(_config.seed), _config.options, _journal);
                yield return null;

                // --- Composition: the designer's classic preset for this player count, applied like the lobby tablet.
                RolePresetDatabase _presets = Resources.FindObjectsOfTypeAll<RolePresetDatabase>().FirstOrDefault();
                RolePreset _preset = _presets != null ? _presets.Classic(_controlled.Length) : null;
                if (_preset == null)
                {
                    _failure = $"no classic RolePreset for {_controlled.Length} players (database loaded: {_presets != null})";
                }
                else
                {
                    var _rolePool = (RoleAttributionState)_gameManager.GetGameStates(typeof(RoleAttributionState)).First();
                    AutoplayComposition.ApplyPreset(CompositionRoot.For(_networkManager).GameSettingsManager, _rolePool, _preset);
                    _journal.Record("composition", $"preset {_preset.name} ({_preset.displayName})");
                    yield return null;

                    // --- Start: skip the ready census (bots have no ready button); the composition gate still applies.
                    ((LobbyState)_gameManager.GetGameState(_gameManager.currentGameStateIndex.Value)).ForceStart();
                    yield return null;
                    if (_gameManager.GetGameState(_gameManager.currentGameStateIndex.Value) is LobbyState)
                    {
                        _failure = "ForceStart refused: invalid composition (see the console warning)";
                    }
                }

                // --- Play until GameEndingState, a stalled state, or the global timeout.
                if (_failure == null)
                {
                    Time.timeScale = _config.timeScale;
                    float _startedAt = Time.realtimeSinceStartup;
                    while (!(_driver.CurrentState is GameEndingState))
                    {
                        if (Time.realtimeSinceStartup - _startedAt > _config.gameTimeoutRealSeconds)
                        {
                            _failure = $"timeout after {_config.gameTimeoutRealSeconds}s real in {_driver.CurrentState?.GetType().Name}";
                            break;
                        }

                        if (_driver.CurrentState != null &&
                            Time.realtimeSinceStartup - _driver.CurrentStateRealSince > _config.stateStallRealSeconds)
                        {
                            _failure = $"stuck {_config.stateStallRealSeconds}s real in {_driver.CurrentState.GetType().Name}";
                            break;
                        }

                        if (!_networkManager.IsListening)
                        {
                            _failure = "the host stopped listening mid-game";
                            break;
                        }

                        yield return null;
                    }

                    yield return new WaitForSecondsRealtime(_failure == null ? 1.5f : 0.2f);
                    _driver.RequestCapture(_failure == null ? "final-GameEnding" : "failure");
                    yield return new WaitForSecondsRealtime(0.8f);
                }
            }

            Application.logMessageReceived -= _journal.OnLog;
            Time.timeScale = 1f;
            _result.failure = _failure;
            _result.report = _journal.BuildReport(_config.scenario, _config.seed, _failure == null ? "GameEnding" : "Failed",
                _failure, _gameManager != null ? _gameManager.currentDay : 0,
                _result.driver != null ? _result.driver.DescribeRoster() : Array.Empty<string>());
            _journal.Write(_result.report);
            Debug.Log($"{AutoplayJournal.LogTag} report {Path.Combine(_config.outputDirectory, "report.json")} " +
                      $"outcome={_result.report.outcome} days={_result.report.days} powers={_result.report.powersUsed} " +
                      $"votes={_result.report.votesCast} errors={_result.report.errorCount}");
        }

        /// <summary>A fresh run folder: &lt;root&gt;/&lt;stamp&gt;-&lt;scenario&gt;-seed&lt;seed&gt;.</summary>
        public static string NewRunDirectory(string _root, string _scenario, int _seed)
            => Path.GetFullPath(Path.Combine(_root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{_scenario}-seed{_seed}"));

        private static ushort FindFreeUdpPort(ushort _first, int _count)
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

        private static int CountPlayers(CharacterManager _characterManager)
            => _characterManager.GetCharacters(false).Count(_c => _c && !_c.isFake);

        private static IEnumerator WaitFor(Func<bool> _condition, float _timeoutRealSeconds, Action<bool> _done)
        {
            float _start = Time.realtimeSinceStartup;
            while (!_condition())
            {
                if (Time.realtimeSinceStartup - _start > _timeoutRealSeconds)
                {
                    _done(false);
                    yield break;
                }
                yield return null;
            }
            _done(true);
        }
    }
}
#endif
