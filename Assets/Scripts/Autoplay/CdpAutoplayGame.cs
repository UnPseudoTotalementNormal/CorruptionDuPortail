#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
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
using Unpseudo.Autoplay;

namespace Autoplay
{
    /// <summary>
    /// Corruption du Portail adapter for the autoplay package: boot → host on a direct loopback UnityTransport (no
    /// Lobby / Relay / Steam) → GameScene → N simulated bots → the designer's classic preset for that player count →
    /// force start, then <see cref="AutoplayDriver"/> plays every seat. Phase = current GameState + day; over at
    /// GameEndingState.
    /// <para>
    /// Multi-process runs (real network clients, <c>-autoplay-role</c>): the <b>host</b> waits for
    /// <c>-autoplay-clients N</c> real clients, fills the table with simulated bots up to <c>-autoplay-players</c> (8)
    /// and plays its own seat + the bots; each <b>client</b> connects for real (<c>-autoplay-connect</c>, retried while the
    /// host is not up yet) and plays only its own seat through the client code paths.
    /// </para>
    /// Game options (<c>-autoplay-&lt;key&gt;</c>): <c>role</c> (host|client), <c>clients</c>, <c>players</c>, <c>bots</c>,
    /// <c>connect</c>, <c>visual-picker</c>.
    /// </summary>
    public sealed class CdpAutoplayGame : IAutoplayGame
    {
        private const int BootSceneIndex = 0;
        private const int MainMenuSceneIndex = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => AutoplayRegistry.Register("cdp", () => new CdpAutoplayGame());

        private NetworkManager networkManager;
        private GameManager gameManager;
        private CharacterManager characterManager;
        private AutoplayDriver driver;
        private bool isClient;

        public string Name => "cdp";

        public NetworkManager NetworkManager => networkManager;
        public AutoplayDriver Driver => driver;

        public string Phase
        {
            get
            {
                if (gameManager == null || !gameManager.IsSpawned) return "booting";
                GameState _state = gameManager.GetGameState(gameManager.currentGameStateIndex.Value);
                return $"{gameManager.currentGameStateIndex.Value}:{(_state != null ? _state.GetType().Name : "null")} day={gameManager.currentDay}";
            }
        }

        public bool IsOver => gameManager != null && gameManager.IsSpawned &&
                              gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is GameEndingState;

        public bool IsAlive => networkManager != null && (isClient ? networkManager.IsConnectedClient : networkManager.IsListening);

        public IEnumerator Boot(AutoplayContext _context)
        {
            // BootScene owns the NetworkManager (DontDestroyOnLoad) and then switches to the main menu.
            if (NetworkManager.Singleton == null && SceneManager.GetActiveScene().buildIndex != BootSceneIndex)
            {
                SceneManager.LoadScene(BootSceneIndex, LoadSceneMode.Single);
            }

            yield return _context.WaitFor(() => NetworkManager.Singleton != null, 30f, "BootScene did not create a NetworkManager");
            if (_context.Failed) yield break;
            yield return _context.WaitFor(() => SceneManager.GetActiveScene().buildIndex == MainMenuSceneIndex, 30f,
                "never reached the main menu");
            networkManager = NetworkManager.Singleton;
        }

        public IEnumerator Host(AutoplayContext _context, ushort _port)
        {
            isClient = string.Equals(_context.Config.Option("role", "host"), "client", StringComparison.OrdinalIgnoreCase);
            if (isClient)
            {
                yield return Connect(_context, _port);
                yield break;
            }

            UnityTransport _transport = networkManager.GetComponent<UnityTransport>();
            if (_transport == null)
            {
                _transport = networkManager.gameObject.AddComponent<UnityTransport>();
            }
            networkManager.NetworkConfig.NetworkTransport = _transport;
            _transport.SetConnectionData("127.0.0.1", _port, "127.0.0.1");
            ConnectionApprovalGate.Enable(networkManager);
            if (!networkManager.StartHost())
            {
                _context.Fail($"StartHost failed on UDP {_port}");
                yield break;
            }
            networkManager.SceneManager.LoadScene("GameScene", LoadSceneMode.Single);

            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState &&
                       characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            }, 60f, "GameScene never reached LobbyState with the host's character spawned");
        }

        public IEnumerator SetUp(AutoplayContext _context)
        {
            if (isClient)
            {
                // A real client plays its own seat only, through the client code paths (no possession, no bots).
                ulong _self = networkManager.LocalClientId;
                var _clientOptions = new AutoplayOptions { visualPicker = _context.Config.Flag("visual-picker"), possessActor = false };
                driver = _context.Capture.gameObject.AddComponent<AutoplayDriver>();
                driver.Begin(networkManager, new[] { _self }, new RandomValidPolicy(_context.Config.seed + (int)_self),
                    _clientOptions, _context.Journal, _context.Capture);
                yield break;
            }

            // Real clients first (multi-process run): the lobby adds their characters as they connect.
            int _clients = _context.Config.OptionInt("clients", 0);
            if (_clients > 0)
            {
                yield return _context.WaitFor(() => networkManager.ConnectedClientsIds.Count >= _clients + 1 && CountPlayers() >= _clients + 1,
                    180f, $"only {networkManager.ConnectedClientsIds.Count - 1}/{_clients} clients joined");
                if (_context.Failed) yield break;
                _context.Journal.Record("clients.joined", string.Join(",", networkManager.ConnectedClientsIds));
            }

            // Simulated players (ids 100..) fill the table, one per frame so each spawn settles.
            int _bots = _context.Config.OptionInt("bots", _context.Config.OptionInt("players", 8) - 1 - _clients);
            for (int _i = 0; _i < _bots; _i++)
            {
                characterManager.SpawnSimulatedPlayer();
                yield return null;
            }
            yield return _context.WaitFor(() => CountPlayers() == _bots + _clients + 1, 10f,
                $"expected {_bots + _clients + 1} players, got {CountPlayers()}");
            if (_context.Failed) yield break;

            // The host plays its own seat and the simulated bots; real clients play themselves.
            ulong _hostId = networkManager.LocalClientId;
            ulong[] _controlled = characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake && (_c.ownerClientId.Value == _hostId || _c.ownerClientId.Value >= 100))
                .Select(_c => _c.ownerClientId.Value)
                .ToArray();

            var _options = new AutoplayOptions { visualPicker = _context.Config.Flag("visual-picker") };
            driver = _context.Capture.gameObject.AddComponent<AutoplayDriver>();
            driver.Begin(networkManager, _controlled, new RandomValidPolicy(_context.Config.seed), _options, _context.Journal, _context.Capture);
            yield return null;

            // The designer's classic preset for this player count, applied like the lobby tablet does.
            RolePresetDatabase _presets = Resources.FindObjectsOfTypeAll<RolePresetDatabase>().FirstOrDefault();
            int _players = CountPlayers();
            RolePreset _preset = _presets != null ? _presets.Classic(_players) : null;
            if (_preset == null)
            {
                _context.Fail($"no classic RolePreset for {_players} players (database loaded: {_presets != null})");
                yield break;
            }

            var _rolePool = (RoleAttributionState)gameManager.GetGameStates(typeof(RoleAttributionState)).First();
            AutoplayComposition.ApplyPreset(CompositionRoot.For(networkManager).GameSettingsManager, _rolePool, _preset);
            _context.Journal.Record("composition", $"preset {_preset.name} ({_preset.displayName})");
            yield return null;
        }

        public IEnumerator StartGame(AutoplayContext _context)
        {
            if (isClient)
            {
                // The host starts the game; a client just waits for the loop to leave the lobby.
                yield return _context.WaitFor(() => !(gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState),
                    240f, "the host never started the game");
                yield break;
            }

            // Bots have no ready button: skip the ready census; the composition gate still applies.
            ((LobbyState)gameManager.GetGameState(gameManager.currentGameStateIndex.Value)).ForceStart();
            yield return null;
            if (gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState)
            {
                _context.Fail("ForceStart refused: invalid composition (see the console warning)");
            }
        }

        public IEnumerable<string> DescribeRoster() => driver != null ? driver.DescribeRoster() : Array.Empty<string>();

        public IEnumerable<string> DescribeOutcome()
        {
            if (gameManager == null) yield break;
            yield return $"days={gameManager.currentDay}";
        }

        public string ExportStateJson() => driver != null ? driver.ExportStateJson() : "{}";

        public void SetAudioMuted(bool _muted)
        {
            if (FMODUnity.RuntimeManager.IsInitialized)
            {
                FMODUnity.RuntimeManager.MuteAllEvents(_muted);
            }
        }

        public void TearDown()
        {
            if (driver != null)
            {
                driver.End();
            }
        }

        private IEnumerator Connect(AutoplayContext _context, ushort _port)
        {
            string _address = _context.Config.Option("connect", "127.0.0.1");
            UnityTransport _transport = networkManager.GetComponent<UnityTransport>();
            if (_transport == null)
            {
                _transport = networkManager.gameObject.AddComponent<UnityTransport>();
            }
            networkManager.NetworkConfig.NetworkTransport = _transport;

            // The host may not be listening yet: retry for a while (each attempt bounded).
            float _deadline = Time.realtimeSinceStartup + 120f;
            int _attempt = 0;
            while (!networkManager.IsConnectedClient)
            {
                _attempt++;
                _transport.SetConnectionData(_address, _port);
                if (!networkManager.StartClient())
                {
                    _context.Fail("StartClient refused");
                    yield break;
                }

                float _until = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < _until && !networkManager.IsConnectedClient && networkManager.IsListening)
                {
                    yield return null;
                }

                if (networkManager.IsConnectedClient)
                {
                    break;
                }

                _context.Journal.Record("connect.retry", $"attempt {_attempt} to {_address}:{_port}");
                networkManager.Shutdown();
                while (networkManager.ShutdownInProgress)
                {
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1f);
                if (Time.realtimeSinceStartup > _deadline)
                {
                    _context.Fail($"could not connect to {_address}:{_port} after {_attempt} attempts");
                    yield break;
                }
            }
            _context.Journal.Record("connected", $"as client {networkManager.LocalClientId} to {_address}:{_port}");

            // NGO scene sync loads GameScene on the client: wait for the replicated managers and our own character.
            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            }, 90f, "the client never received GameScene with its own character");
        }

        private int CountPlayers() => characterManager.GetCharacters(false).Count(_c => _c && !_c.isFake);
    }
}
#endif
