#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Characters;
using Cysharp.Threading.Tasks;
using CorruptionDuPortail.Domain;
using GameLogic;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using Network;
using Network.Services;
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
    /// <c>connect</c>, <c>visual-picker</c>, <c>vote-focus &lt;role text&gt;</c> (all bots vote that role: forces a situation).
    /// <para>Scenario levers: <c>force-roles A,B</c> (role-name fragments guaranteed in the composition),
    /// <c>fast-fakes</c> (fake roles go back to sleep after ~1 s — test speed only), <c>max-days N</c> (stop after day N), <c>role-holder host|client|bot</c> (who must hold the forced roles — otherwise the run fails fast with
    /// "composition mismatch" so the launcher retries another seed), <c>netsim delay,jitter,loss</c> (Multiplayer Tools
    /// Network Simulator on this process), <c>quit-at &lt;phase text&gt;</c> (a client leaves mid-game on purpose),
    /// <c>target-focus</c> / <c>target-focus-power</c> (deterministic power targets), <c>chat</c> (bots write in their
    /// private channels), <c>build-version &lt;v&gt;</c> and <c>stall-load &lt;seconds&gt;</c> (a client joins with
    /// another version / a load that does not finish: the join is expected to be refused, the run then ends with a
    /// <c>rejected=</c> fact), <c>expect-clients N</c> (host: real clients that must join, when some are expected to be
    /// refused). Every host run writes <c>roles.json</c> (role pool + powers) for coverage tools.</para>
    /// </summary>
    public sealed class CdpAutoplayGame : IAutoplayGame, IAutoplayAnimationSource
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
        private string leftAt;
        // Server reason that ended this client's join on purpose (version gate, stuck-load kick): an expected outcome.
        private string rejected;
        // Real-input mode (-autoplay-real-input): virtual mouse / keyboard, real devices disabled while installed.
        private AutoplayVirtualInput realInput;
        private AutoplayInputMask inputMask;
        private AutoplayJournal journal;

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

        public bool IsOver => rejected != null || leftAt != null || stoppedAtDay || (gameManager != null && gameManager.IsSpawned &&
                              gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is GameEndingState);

        // Scenario lever "max-days N": end the run (completed) once day N+1 starts — enough for coverage runs.
        private bool stoppedAtDay => maxDays > 0 && gameManager != null && gameManager.IsSpawned && gameManager.currentDay > maxDays;
        private int maxDays;

        public bool IsAlive => rejected != null || leftAt != null ||
                               (networkManager != null && (isClient ? networkManager.IsConnectedClient : networkManager.IsListening));

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

            // Virtual devices from the main menu on (a refused join is seen there), and the menu tour when asked.
            InstallRealInput(_context);
            if (realInput == null && !string.IsNullOrEmpty(_context.Config.Option("real-input-mask")))
            {
                _context.Fail("real-input-mask needs -autoplay-real-input (nothing would click the masked object)");
                yield break;
            }
            if (realInput != null && _context.Config.Flag("menu-ui"))
            {
                yield return AutoplayMenuTour.Run(realInput, _context.Journal, _context.Capture);
            }
        }

        public IEnumerator Host(AutoplayContext _context, ushort _port)
        {
            isClient = string.Equals(_context.Config.Option("role", "host"), "client", StringComparison.OrdinalIgnoreCase);
            maxDays = _context.Config.OptionInt("max-days", 0);
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
            ApplyNetworkSimulator(_context);
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
            InstallRealInput(_context);
            if (isClient)
            {
                if (rejected != null)
                {
                    yield break;
                }

                // A real client plays its own seat only, through the client code paths (no possession, no bots).
                ulong _self = networkManager.LocalClientId;
                var _clientOptions = new AutoplayOptions
                {
                    visualPicker = _context.Config.Flag("visual-picker") || _context.Config.Flag("real-input"),
                    possessActor = false,
                    voteFocusRole = _context.Config.Option("vote-focus"),
                    targetFocus = _context.Config.Option("target-focus"),
                    targetFocusPower = _context.Config.Option("target-focus-power"),
                    chat = _context.Config.Flag("chat"),
                    realInput = GameRealInput(_context),
                    lobbyInput = LobbyUi(_context) ? realInput : null,
                    powerUseProbability = ParseProbability(_context.Config.Option("power-use-probability"), 1.0),
                    voteProbability = ParseProbability(_context.Config.Option("vote-probability"), 1.0),
                    realInputTour = _context.Config.Flag("real-input-tour"),
                    tourAudioSlider = false, // PlayerPrefs are shared by every process: only the host moves a slider
                };
                driver = _context.Capture.gameObject.AddComponent<AutoplayDriver>();
                driver.Begin(networkManager, new[] { _self }, new RandomValidPolicy(_context.Config.seed + (int)_self),
                    _clientOptions, _context.Journal, _context.Capture);
                _context.Capture.StartCoroutine(RecordRtt(_context));
                string _quitAt = _context.Config.Option("quit-at");
                if (!string.IsNullOrEmpty(_quitAt))
                {
                    _context.Capture.StartCoroutine(LeaveAt(_context, _quitAt));
                }
                yield break;
            }

            // Real clients first (multi-process run): the lobby adds their characters as they connect. A scenario where
            // some clients are meant to be refused (version gate, stuck load) waits for the others only.
            int _clients = _context.Config.OptionInt("expect-clients", _context.Config.OptionInt("clients", 0));
            float _spawnDuringLoad = ParseSeconds(_context.Config.Option("spawn-during-load"));
            if (_spawnDuringLoad > 0f)
            {
                _context.Capture.StartCoroutine(SpawnDuringLoad(_context, _spawnDuringLoad));
            }
            if (_clients > 0)
            {
                yield return _context.WaitFor(() => networkManager.ConnectedClientsIds.Count >= _clients + 1 && CountPlayers() >= _clients + 1,
                    180f, $"only {networkManager.ConnectedClientsIds.Count - 1}/{_clients} clients joined");
                if (_context.Failed) yield break;
                _context.Journal.Record("clients.joined", string.Join(",", networkManager.ConnectedClientsIds));
            }

            // Simulated players (ids 100..) fill the table, one per frame so each spawn settles.
            int _bots = Math.Max(0, _context.Config.OptionInt("bots", _context.Config.OptionInt("players", 8) - 1 - _clients) - earlyBots);
            for (int _i = 0; _i < _bots; _i++)
            {
                characterManager.SpawnSimulatedPlayer();
                yield return null;
            }
            int _expected = _bots + earlyBots + _clients + 1;
            yield return _context.WaitFor(() => CountPlayers() == _expected, 10f,
                $"expected {_expected} players, got {CountPlayers()}");
            if (_context.Failed) yield break;

            // The host plays its own seat and the simulated bots; real clients play themselves.
            ulong _hostId = networkManager.LocalClientId;
            ulong[] _controlled = characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake && (_c.ownerClientId.Value == _hostId || _c.ownerClientId.Value >= 100))
                .Select(_c => _c.ownerClientId.Value)
                .ToArray();

            var _options = new AutoplayOptions
            {
                visualPicker = _context.Config.Flag("visual-picker") || _context.Config.Flag("real-input"),
                voteFocusRole = _context.Config.Option("vote-focus"),
                fastFakes = _context.Config.Flag("fast-fakes"),
                targetFocus = _context.Config.Option("target-focus"),
                targetFocusPower = _context.Config.Option("target-focus-power"),
                chat = _context.Config.Flag("chat"),
                realInput = GameRealInput(_context),
                lobbyInput = LobbyUi(_context) ? realInput : null,
                powerUseProbability = ParseProbability(_context.Config.Option("power-use-probability"), 1.0),
                voteProbability = ParseProbability(_context.Config.Option("vote-probability"), 1.0),
                realInputTour = _context.Config.Flag("real-input-tour"),
                tourAudioSlider = true,
            };
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
            bool _presetClicked = false;
            if (LobbyUi(_context))
            {
                yield return driver.LobbyApplyClassicPreset().ToCoroutine(_ok => _presetClicked = _ok, _e => InputError(_context, "lobby-preset", _e));
            }
            if (!_presetClicked)
            {
                AutoplayComposition.ApplyPreset(CompositionRoot.For(networkManager).GameSettingsManager, _rolePool, _preset);
            }
            _context.Journal.Record("composition", $"preset {_preset.name} ({_preset.displayName}) via={(_presetClicked ? "click" : "direct")}");
            if (_presetClicked)
            {
                // A role card face on the tablet opens the role detail overlay (screen-space UI Toolkit), then close it.
                yield return driver.LobbyPeekRoleCard().ToCoroutine(null, _e => InputError(_context, "lobby-role-card", _e));
            }
            AutoplayComposition.WriteRolePool(_rolePool, System.IO.Path.Combine(_context.Journal.OutputDirectory, "roles.json"));

            string _forced = _context.Config.Option("force-roles");
            if (!string.IsNullOrEmpty(_forced))
            {
                foreach (string _fragment in _forced.Split(',').Select(_f => _f.Trim()).Where(_f => _f.Length > 0))
                {
                    bool _forcedClicked = false;
                    if (LobbyUi(_context))
                    {
                        yield return driver.LobbyForceRole(_fragment).ToCoroutine(_ok => _forcedClicked = _ok, _e => InputError(_context, $"lobby-force {_fragment}", _e));
                    }
                    if (_forcedClicked)
                    {
                        _context.Journal.Record("composition.force", $"{_fragment} via=click");
                        continue;
                    }
                    foreach (string _missing in AutoplayComposition.ForceRoles(CompositionRoot.For(networkManager).GameSettingsManager, _rolePool,
                                 new[] { _fragment }, _context.Journal))
                    {
                        _context.Fail($"force-roles: no role matches '{_missing}'");
                    }
                }
            }
            yield return null;
        }

        // Bots already seated by the spawn-during-load lever (counted in the table fill).
        private int earlyBots;

        // Scenario lever "spawn-during-load S": S real seconds after a joiner starts synchronizing (and while it still
        // is), the host seats a simulated player: a Character is spawned, then gets its owner / parent / roster entry,
        // while that joiner is still loading (late-joiner Characters desync, investigation late-joiner-character-desync).
        private IEnumerator SpawnDuringLoad(AutoplayContext _context, float _seconds)
        {
            yield return _context.WaitFor(() => ConnectionApprovalGate.HasSynchronizingClients, 120f, "spawn-during-load: no joiner ever synchronized");
            if (_context.Failed) yield break;
            yield return new WaitForSecondsRealtime(_seconds);
            if (!ConnectionApprovalGate.HasSynchronizingClients)
            {
                _context.Journal.Record("join.spawn-during-load", "skipped: the joiner finished loading first");
                yield break;
            }
            characterManager.SpawnSimulatedPlayer();
            earlyBots++;
            _context.Journal.Record("join.spawn-during-load", string.Format(CultureInfo.InvariantCulture,
                "bot seated {0:0.0}s after a joiner started loading, joiner still loading", _seconds));
        }

        private static void InputError(AutoplayContext _context, string _action, Exception _exception)
        {
            _context.Journal.Record("input.error", $"{_action} {_exception.GetType().Name}: {_exception.Message}");
            Debug.LogException(_exception);
        }

        // Lobby through the tablet with the mouse: its own lever, implied by real-input.
        private static bool LobbyUi(AutoplayContext _context) => _context.Config.Flag("lobby-ui") || _context.Config.Flag("real-input");

        // Game actions by real input only with -autoplay-real-input (lobby-ui alone keeps them direct).
        private AutoplayVirtualInput GameRealInput(AutoplayContext _context) => _context.Config.Flag("real-input") ? realInput : null;

        public IEnumerator StartGame(AutoplayContext _context)
        {
            if (isClient)
            {
                if (rejected != null)
                {
                    yield break;
                }

                // The host starts the game; a client waits for the loop to leave the lobby (clicking its ready button
                // first when the lobby is played through the tablet).
                if (LobbyUi(_context))
                {
                    yield return driver.LobbyReady().ToCoroutine(null, _e => InputError(_context, "lobby-ready", _e));
                }
                yield return _context.WaitFor(() => !(gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState),
                    240f, "the host never started the game");
                yield break;
            }

            // Role distribution draws from UnityEngine.Random: seed it so a scenario seed reproduces the same seats.
            UnityEngine.Random.InitState(_context.Config.seed);

            // A joiner still loading blocks any start (NET-05), forced or not: wait for it to finish or be kicked.
            if (ConnectionApprovalGate.HasSynchronizingClients)
            {
                _context.Journal.Record("lobby.wait-loaders", "a joiner is still loading");
                yield return _context.WaitFor(() => !ConnectionApprovalGate.HasSynchronizingClients,
                    JoinHandshake.SyncTotalTimeoutSeconds + 60f, "a joiner was still loading long after the join cap");
                if (_context.Failed) yield break;
                _context.Journal.Record("lobby.loaders-done", string.Join(",", networkManager.ConnectedClientsIds));
            }

            if (LobbyUi(_context))
            {
                // As players do: the host clicks its ready button last (bots are ready by themselves, real clients
                // clicked theirs), then LobbyState.TryAutoStart starts the game once all are ready and the
                // composition is valid.
                yield return driver.LobbyReady().ToCoroutine(null, _e => InputError(_context, "lobby-ready", _e));
                yield return _context.WaitFor(() => !(gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState),
                    60f, "TryAutoStart never started the game");
                if (_context.Failed)
                {
                    _context.Journal.Record("lobby.status", driver.LobbyStatus());
                    yield break;
                }
                _context.Journal.Record("lobby.autostart", driver.LobbyStatus());
            }
            else
            {
                // Bots have no ready button: skip the ready census; the composition gate still applies.
                ((LobbyState)gameManager.GetGameState(gameManager.currentGameStateIndex.Value)).ForceStart();
                yield return null;
                if (gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState)
                {
                    _context.Fail("ForceStart refused: invalid composition (see the console warning)");
                    yield break;
                }
            }

            yield return _context.WaitFor(() => characterManager.GetCharacters(false).Where(_c => _c && !_c.isFake).All(_c => _c.role != null),
                30f, "roles were never assigned");
            if (_context.Failed) yield break;
            _context.Journal.Record("roles.assigned", string.Join(", ", characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake).OrderBy(_c => _c.ownerClientId.Value)
                .Select(_c => $"{_c.ownerClientId.Value}:{HolderKind(_c.ownerClientId.Value)}:{_c.role.roleName}")));

            string _forced = _context.Config.Option("force-roles");
            string _holder = _context.Config.Option("role-holder");
            if (!string.IsNullOrEmpty(_forced) && !string.IsNullOrEmpty(_holder))
            {
                foreach (string _fragment in _forced.Split(',').Select(_f => _f.Trim()).Where(_f => _f.Length > 0))
                {
                    Character _owner = characterManager.GetCharacters(false).FirstOrDefault(_c => _c && !_c.isFake &&
                        _c.role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0);
                    string _kind = _owner == null ? "nobody" : HolderKind(_owner.ownerClientId.Value);
                    if (!string.Equals(_kind, _holder, StringComparison.OrdinalIgnoreCase))
                    {
                        _context.Fail($"composition mismatch: '{_fragment}' held by {_kind} {(_owner != null ? _owner.ownerClientId.Value.ToString() : "-")}, wanted {_holder}");
                        yield break;
                    }
                }
            }
        }

        public IEnumerable<string> DescribeRoster() => driver != null ? driver.DescribeRoster() : Array.Empty<string>();

        public IEnumerable<string> DescribeOutcome()
        {
            if (rejected != null) yield return $"rejected={rejected}";
            if (leftAt != null) yield return $"left={leftAt}";
            if (stoppedAtDay) yield return $"stopped-after-day={maxDays}";
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
            if (inputMask != null)
            {
                UnityEngine.Object.Destroy(inputMask.gameObject);
                inputMask = null;
            }
            if (realInput != null)
            {
                journal?.Record("input.uninstall", realInput.Describe());
                realInput.Dispose();
                realInput = null;
            }
        }

        private static double ParseProbability(string _text, double _fallback)
            => double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out double _value) ? Math.Clamp(_value, 0.0, 1.0) : _fallback;

        // Without the lever nothing is installed: no virtual device, no input.* event (real-input spec, AC 1).
        private void InstallRealInput(AutoplayContext _context)
        {
            bool _control = _context.Config.Flag("real-input-control");
            if (!(_context.Config.Flag("real-input") || _control || _context.Config.Flag("lobby-ui") || _context.Config.Flag("menu-ui")) || realInput != null)
            {
                return;
            }

            journal = _context.Journal;
            realInput = AutoplayVirtualInput.Install(_control);
            _context.Journal.Record("input.install", realInput.Describe());

            // Breakage test: cover that object with a click-eating overlay; its clicks must end in input.miss.
            string _mask = _context.Config.Option("real-input-mask");
            if (!string.IsNullOrEmpty(_mask))
            {
                inputMask = AutoplayInputMask.Create(_mask);
                _context.Journal.Record("input.mask", _mask);
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
            ApplyNetworkSimulator(_context);

            // Scenario lever "stall-load S": this client's scene load does not finish for S seconds once the host has
            // started synchronizing it (the stuck-load kick). Unity queues scene loads: a load parked with
            // allowSceneActivation = false holds every later one, NGO's GameScene load included, while the transport
            // and the main thread keep running (a frozen or suspended process would be dropped by the transport
            // timeout instead, another path).
            float _stall = ParseSeconds(_context.Config.Option("stall-load"));
            AsyncOperation _blocker = null;
            if (_stall > 0f)
            {
                _blocker = SceneManager.LoadSceneAsync(MainMenuSceneIndex, LoadSceneMode.Additive);
                _blocker.allowSceneActivation = false;
                _context.Journal.Record("join.stall", string.Format(CultureInfo.InvariantCulture, "scene loads held for {0:0}s after synchronization starts", _stall));
            }

            // Scenario lever "connect-delay S": this client waits S real seconds before its first connect attempt, so it
            // joins while another client is still synchronizing (e.g. one held by stall-load): the host then spawns
            // this client's Character during the other client's load.
            float _connectDelay = ParseSeconds(_context.Config.Option("connect-delay"));
            if (_connectDelay > 0f)
            {
                _context.Journal.Record("join.delay", string.Format(CultureInfo.InvariantCulture, "{0:0.0}s", _connectDelay));
                yield return new WaitForSecondsRealtime(_connectDelay);
            }

            // The host may not be listening yet: retry for a while (each attempt bounded).
            float _deadline = Time.realtimeSinceStartup + 120f;
            int _attempt = 0;
            bool _synchronizing = false;
            float _syncStart = 0f;
            while (!networkManager.IsConnectedClient)
            {
                _attempt++;
                _transport.SetConnectionData(_address, _port);
                // Same start path as a real client: profile + build version in the connection request (the host's
                // join gate rejects a request without them).
                ClientConnectionPayload.Apply(networkManager);
                ApplyForcedBuildVersion(_context);
                // Like the menu's join: the join wait owns a refusal (its reason is shown), not the "host lost" popup.
                ClientDisconnectHandler.SetJoinHandshakeInProgress(true);
                if (!networkManager.StartClient())
                {
                    ClientDisconnectHandler.SetJoinHandshakeInProgress(false);
                    _context.Fail("StartClient refused");
                    yield break;
                }

                networkManager.SceneManager.OnSynchronize += _ =>
                {
                    if (_synchronizing) return;
                    _synchronizing = true;
                    _syncStart = Time.realtimeSinceStartup;
                    _context.Journal.Record("join.synchronizing", $"attempt {_attempt}");
                };

                // Before synchronization: 10 s per attempt (host not up yet). Once the host synchronizes us, wait for
                // the load itself, however long: a stuck load is the host's to end (join cap).
                float _until = Time.realtimeSinceStartup + 10f;
                while ((Time.realtimeSinceStartup < _until || _synchronizing) && !networkManager.IsConnectedClient && networkManager.IsListening)
                {
                    if (_blocker != null && _synchronizing && Time.realtimeSinceStartup - _syncStart >= _stall)
                    {
                        _blocker.allowSceneActivation = true; // an honest slow load: let it finish
                        _context.Journal.Record("join.stall.released", string.Format(CultureInfo.InvariantCulture, "after {0:0.0}s", Time.realtimeSinceStartup - _syncStart));
                        _blocker = null;
                    }
                    yield return null;
                }

                if (networkManager.IsConnectedClient)
                {
                    break;
                }

                // Read BEFORE any shutdown: the server's own reason, if it refused or kicked us on purpose.
                string _reason = networkManager.DisconnectReason;
                if (RelayFallbackPolicy.HasServerReason(_reason))
                {
                    yield return Rejected(_context, _reason, _synchronizing ? Time.realtimeSinceStartup - _syncStart : 0f);
                    yield break;
                }
                ClientDisconnectHandler.SetJoinHandshakeInProgress(false);

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
            ClientDisconnectHandler.SetJoinHandshakeInProgress(false);
            _context.Journal.Record("connected", string.Format(CultureInfo.InvariantCulture, "as client {0} to {1}:{2}{3}",
                networkManager.LocalClientId, _address, _port, _synchronizing
                    ? string.Format(CultureInfo.InvariantCulture, " load={0:0.0}s", Time.realtimeSinceStartup - _syncStart)
                    : string.Empty));

            // NGO scene sync loads GameScene on the client: wait for the replicated managers and our own character.
            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            }, 90f, "the client never received GameScene with its own character");
        }

        // The host refused or kicked this client on purpose: show the reason the way the join menu does (same wording
        // builder, same error report the notification panel listens to), capture it, and end the run as completed.
        private IEnumerator Rejected(AutoplayContext _context, string _reason, float _afterSyncSeconds)
        {
            string _message = JoinHandshake.BuildFailureMessage(ConnectFailReason.SessionEnded);
            _context.Journal.Record("connect.rejected", string.Format(CultureInfo.InvariantCulture,
                "reason={0} | after-sync={1:0.0}s", _reason, _afterSyncSeconds));
            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.ReportError(_message);
            }
            ClientDisconnectHandler.SetJoinHandshakeInProgress(false);
            _context.Capture.Request("join-rejected", 0.5f);
            yield return new WaitForSecondsRealtime(1.5f);
            if (realInput != null)
            {
                // Real input: the notification must be on top (above the login screen) and dismissable by a click.
                yield return AutoplayMenuTour.CheckRejectedNotification(realInput, _context.Journal);
            }
            rejected = _reason;
        }

        // Scenario lever "build-version <v>": the connection request carries another build version (version gate).
        private void ApplyForcedBuildVersion(AutoplayContext _context)
        {
            string _version = _context.Config.Option("build-version");
            if (string.IsNullOrEmpty(_version) ||
                !ConnectionPayload.TryParse(networkManager.NetworkConfig.ConnectionData, out ConnectionPayload _payload))
            {
                return;
            }

            _payload.BuildVersion = _version;
            networkManager.NetworkConfig.ConnectionData = _payload.ToBytes();
            _context.Journal.Record("join.version", $"sending {_version} (this build: {Application.version})");
        }

        private static float ParseSeconds(string _text)
            => float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out float _value) ? _value : 0f;

        private string HolderKind(ulong _id)
            => _id == networkManager.LocalClientId ? "host" : _id >= 100 ? "bot" : "client";

        // Multiplayer Tools Network Simulator (the supported way since UnityTransport's DebugSimulator became a no-op),
        // reached by reflection so the Game assembly does not depend on the tools package.
        private void ApplyNetworkSimulator(AutoplayContext _context)
        {
            string _spec = _context.Config.Option("netsim");
            if (string.IsNullOrEmpty(_spec))
            {
                return;
            }

            int[] _v = _spec.Split(',').Select(_x => int.TryParse(_x, out int _n) ? _n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
            Type _simulatorType = Type.GetType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator, Unity.Multiplayer.Tools.NetworkSimulator.Runtime");
            Type _presetType = Type.GetType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulatorPreset, Unity.Multiplayer.Tools.NetworkSimulator.Runtime");
            if (_simulatorType == null || _presetType == null)
            {
                _context.Fail("netsim: Multiplayer Tools Network Simulator not available in this build");
                return;
            }

            Component _simulator = networkManager.GetComponent(_simulatorType) ?? networkManager.gameObject.AddComponent(_simulatorType);
            object _preset = _presetType.GetMethod("Create").Invoke(null, new object[] { "autoplay", "autoplay netsim", _v[0], _v[1], 0, _v[2] });
            _simulatorType.GetProperty("ConnectionPreset").SetValue(_simulator, _preset);
            _context.Journal.Record("netsim", $"delay={_v[0]}ms jitter={_v[1]}ms loss={_v[2]}%");
        }

        private IEnumerator RecordRtt(AutoplayContext _context)
        {
            var _transport = networkManager.NetworkConfig.NetworkTransport;
            while (leftAt == null && networkManager != null && networkManager.IsConnectedClient)
            {
                _context.Journal.Record("net.rtt", $"{_transport.GetCurrentRtt(NetworkManager.ServerClientId)}ms");
                yield return new WaitForSecondsRealtime(5f);
            }
        }

        // A client that leaves on purpose mid-game (disconnect policy tests): the run then ends "completed" with a
        // left=<phase> fact instead of failing on the dead session.
        private IEnumerator LeaveAt(AutoplayContext _context, string _phaseText)
        {
            while (Phase.IndexOf(_phaseText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1f);
            string _phase = Phase;
            ulong _self = networkManager.LocalClientId; // read before leaving: a shut-down NetworkManager reports 0
            // Real input: leave as a player does (pause button, then Leave); direct shutdown if that path fails.
            bool _viaUi = false;
            if (GameRealInput(_context) != null && driver != null)
            {
                yield return driver.LeaveThroughPauseMenu().ToCoroutine(_left => _viaUi = _left, _e => InputError(_context, "leave", _e));
            }
            leftAt = _phase;
            _context.Journal.Record("leave", $"client {_self} leaves at {leftAt} via={(_viaUi ? "click" : "direct")}");
            driver.End();
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
        }

        /// <summary>
        /// What to measure frame by frame while a recorded animation plays. Picker openings: the blur veil alpha and the
        /// world position / scale of every card the picker can be clicked on (index = stable order by owner id), for any
        /// trigger (blank while no picker is open).
        /// Any trigger: the current phase index and day, so a recording can be lined up with the game flow.
        /// </summary>
        public IEnumerable<AutoplayTrack> TracksFor(string _eventKind, string _detail)
        {
            yield return AutoplayTrack.Value("stateIndex", () => gameManager != null ? gameManager.currentGameStateIndex.Value : float.NaN);
            // Picker tracks for every trigger (blank while no picker is open), so a recording started BEFORE the
            // opening (e.g. on power.start) captures the whole animation from the first frame.

            yield return AutoplayTrack.Value("frost", () => UI.BoardUI.CardPickerManager.instance != null ? UI.BoardUI.CardPickerManager.instance.FrostAlpha : float.NaN);
            yield return AutoplayTrack.Value("lifted", () => UI.BoardUI.CardPickerManager.instance != null ? UI.BoardUI.CardPickerManager.instance.LiftedCharacterCards.Count : float.NaN);
            for (int _i = 0; _i < 10; _i++)
            {
                int _slot = _i;
                foreach (AutoplayTrack _track in AutoplayTrack.TransformOf($"card{_slot}", () => PickableCard(_slot)))
                {
                    yield return _track;
                }
            }
        }

        private static Transform PickableCard(int _slot)
        {
            var _picker = UI.BoardUI.CardPickerManager.instance;
            if (_picker == null || !_picker.IsPickerActive) return null;
            Board.Card _card = _picker.PickableCards.Where(_c => _c)
                .OrderBy(_c => _c.characterInfo != null ? _c.characterInfo.ownerClientId.Value : ulong.MaxValue)
                .ElementAtOrDefault(_slot);
            return _card ? _card.transform : null;
        }

        private int CountPlayers() => characterManager.GetCharacters(false).Count(_c => _c && !_c.isFake);
    }
}
#endif
