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
using UnityEngine.UI;
using Extensions;
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
    /// refused), <c>rejoin-after S</c> / <c>rejoin-via menu</c> / <c>crash-at &lt;phase&gt;</c> / <c>relaunched</c> (a client
    /// drops or crashes, then takes its seat back; see the methods). Every host run writes <c>roles.json</c> (role pool +
    /// powers) for coverage tools.</para>
    /// </summary>
    public sealed class CdpAutoplayGame : IAutoplayGame, IAutoplayAnimationSource, IAutoplayWatchdogSource, IAutoplayRounds
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
        // Rejoin lever: the client is between its drop and its seat coming back (the session is not dead).
        private bool rejoinInProgress;
        // Lever "relaunched": this process is the game a player relaunched after a crash (launch-net RelaunchArgs).
        private bool relaunched;

        private static bool isClientRole(AutoplayContext _context) =>
            string.Equals(_context.Config.Option("role", "host"), "client", StringComparison.OrdinalIgnoreCase);

        private static bool RejoinViaMenu(AutoplayContext _context) =>
            string.Equals(_context.Config.Option("rejoin-via"), "menu", StringComparison.OrdinalIgnoreCase) ||
            _context.Config.Flag("relaunched");
        private ushort connectPort;
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

        public bool IsOver => rejected != null || leftAt != null || stoppedAtDay || EndingOver || hostLossDone || HostLossSeen();

        // Scenario lever "linger-end <seconds>": stay that long on the ending screen (winners' cards, ending animation)
        // instead of ending the run as soon as GameEndingState starts; captures "ending" on the way.
        private bool EndingOver
        {
            get
            {
                if (gameManager == null || !gameManager.IsSpawned ||
                    gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is not GameEndingState)
                {
                    return false;
                }
                if (lingerEndSeconds <= 0f || lingerContext == null)
                {
                    return true;
                }
                if (endingSeenAt < 0f)
                {
                    endingSeenAt = Time.realtimeSinceStartup;
                    lingerContext.Journal.Record("ending.linger", $"{lingerEndSeconds:0.#}s");
                    lingerContext.Capture.RequestBurst("ending", new[] { 0.5f, 2f, Mathf.Max(2.5f, lingerEndSeconds - 0.5f) });
                }
                return Time.realtimeSinceStartup - endingSeenAt >= lingerEndSeconds;
            }
        }
        private float lingerEndSeconds;
        private float endingSeenAt = -1f;
        private AutoplayContext lingerContext;

        // Client lever "expect-host-loss": the host is meant to vanish (host-quit-at / host-crash-at). Once this client
        // lost its session it stops its bot, journals host.lost, waits for the main menu (the game's own return path),
        // journals whether the "host lost" notification is shown (host.lost.menu notification=shown|hidden), captures
        // "host-lost" and ends the run completed. Never true without the lever.
        private bool HostLossSeen()
        {
            if (!expectHostLoss || !isClient || hostLostAt != null || !everConnected || lingerContext == null ||
                networkManager == null || networkManager.IsConnectedClient || rejoinInProgress || leftAt != null)
            {
                return false;
            }
            hostLostAt = gameManager != null && gameManager ? Phase : "unknown";
            lingerContext.Journal.Record("host.lost", $"at {hostLostAt}");
            if (driver != null)
            {
                driver.End();
            }
            lingerContext.Capture.StartCoroutine(HostLossToMenu(lingerContext));
            return false;
        }

        private IEnumerator HostLossToMenu(AutoplayContext _context)
        {
            float _deadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().buildIndex != MainMenuSceneIndex && Time.realtimeSinceStartup < _deadline)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            GameObject _handler = GameObject.Find("ClientDisconnectHandler");
            Transform _notification = _handler != null ? _handler.transform.Find("ClientDisconnectCanvas/Notification") : null;
            bool _shown = _notification != null && _notification.gameObject.activeInHierarchy;
            string _text = _shown ? _notification.GetComponentInChildren<UnityEngine.UI.Text>(true)?.text : null;
            _context.Journal.Record("host.lost.menu", string.Format(CultureInfo.InvariantCulture, "menu={0} notification={1}{2}",
                SceneManager.GetActiveScene().buildIndex == MainMenuSceneIndex ? "reached" : "missed",
                _shown ? "shown" : "hidden", _text != null ? $" text='{_text}'" : string.Empty));
            _context.Capture.Request("host-lost", 0f);
            yield return new WaitForSecondsRealtime(0.5f);
            hostLossDone = true;
        }

        private bool expectHostLoss;
        private bool everConnected;
        private string hostLostAt;
        private bool hostLossDone;

        // Host levers "host-quit-at <phase text>" (the host leaves through its pause menu's Leave: ShutOffGameRpc, a
        // graceful end for everyone) and "host-crash-at <phase text>" (the host process is killed: the clients lose it).
        // Given to every process (scenario "args"): only the host acts on them.
        private IEnumerator HostQuitAt(AutoplayContext _context, string _phaseText, bool _crash)
        {
            while (Phase.IndexOf(_phaseText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            string _phase = Phase;
            if (_crash)
            {
                if (Application.isEditor)
                {
                    _context.Fail("host-crash-at kills the process: player builds only");
                    yield break;
                }
                _context.Capture.Request("host-crash", 0f);
                yield return new WaitForSecondsRealtime(0.8f);
                _context.Journal.Record("crash", $"host at {_phase}");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                yield break;
            }
            bool _viaUi = false;
            if (GameRealInput(_context) != null && driver != null)
            {
                yield return driver.LeaveThroughPauseMenu().ToCoroutine(_left => _viaUi = _left, _e => InputError(_context, "host-leave", _e));
            }
            if (!_viaUi && gameManager != null && gameManager.IsSpawned)
            {
                gameManager.ShutOffGameRpc(); // what the host's Leave button does
            }
            leftAt = _phase;
            _context.Journal.Record("leave", $"host leaves at {_phase} via={(_viaUi ? "click" : "direct")}");
            if (driver != null)
            {
                driver.End();
            }
        }

        // Scenario lever "max-days N": end the run (completed) once day N+1 starts — enough for coverage runs.
        private bool stoppedAtDay => maxDays > 0 && gameManager != null && gameManager.IsSpawned && gameManager.currentDay > maxDays;
        private int maxDays;

        public bool IsAlive => rejected != null || leftAt != null || rejoinInProgress || hostLostAt != null || HostLossSeen() ||
                               (networkManager != null && (isClient ? networkManager.IsConnectedClient : networkManager.IsListening));

        private AutoplayConfig config;
        private int expectedClients;

        public IEnumerator Boot(AutoplayContext _context)
        {
            config = _context.Config;
            expectedClients = _context.Config.OptionInt("expect-clients", _context.Config.OptionInt("clients", 0));
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
            // Scenario lever "net-log": NGO's own developer log (approvals, disconnect events and their side) in this
            // process's player log.
            if (_context.Config.Flag("net-log"))
            {
                networkManager.LogLevel = LogLevel.Developer;
            }
            // Several player processes share PlayerPrefs on one machine: each seat keeps its rejoin session under its own
            // key (scenario name + agreed port), the same for a relaunched process, which must find it as a player's
            // relaunched game does. Any other process starts with no session.
            relaunched = isClientRole(_context) && _context.Config.Flag("relaunched");
            RejoinSessionStore.KeySuffix = $"-autoplay-{_context.Config.scenario}-{_context.Config.port}";
            if (!relaunched)
            {
                RejoinSessionStore.Clear();
            }
            // The menu read the session before this key was set: let it look again (a player's game has one key only).
            UnityEngine.Object.FindAnyObjectByType<global::UI.MainMenu>()?.RefreshRejoinButton();

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
            lingerEndSeconds = ParseSeconds(_context.Config.Option("linger-end"));
            lingerContext = _context;
            expectHostLoss = _context.Config.Flag("expect-host-loss");
            rejoinGraceSeconds = ParseSeconds(_context.Config.Option("rejoin-grace"));
            if (Relay(_context))
            {
                yield return LoginThroughMenu(_context);
                if (_context.Failed) yield break;
            }
            if (isClient)
            {
                connectPort = _port;
                if (relaunched)
                {
                    _context.Journal.Record("rejoin.relaunch", "token " + (string.IsNullOrEmpty(RejoinSessionStore.TokenForConnection()) ? "missing" : "present"));
                    yield return RejoinThroughMenu(_context);
                    yield break;
                }
                if (Relay(_context))
                {
                    yield return JoinThroughMenuByCode(_context);
                    yield break;
                }
                yield return Connect(_context, _port);
                yield break;
            }

            if (Relay(_context))
            {
                yield return HostThroughMenu(_context);
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
            if (rejoinGraceSeconds > 0f)
            {
                _context.Journal.Record("seat.grace", string.Format(CultureInfo.InvariantCulture, "{0:0.0}s", rejoinGraceSeconds));
            }

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

                // A real client plays its own seat only, through the client code paths (no possession, no bots). Its seat is
                // its clientId, or its original one when it rejoined after a relaunch.
                ulong _seat = characterManager.GetLocalClientId();
                StartClientDriver(_context, _seat, _joinedMidPhase: relaunched);
                _context.Capture.StartCoroutine(RecordRtt(_context));
                if (relaunched)
                {
                    _context.Journal.Record("rejoin.seat", $"seat {_seat} connection {networkManager.LocalClientId}");
                    _context.Capture.RequestBurst("rejoin-after", new[] { 1f, 3f, 6f });
                    yield break; // the relaunched game plays on: no second leave / crash
                }
                string _quitAt = _context.Config.Option("quit-at");
                if (!string.IsNullOrEmpty(_quitAt))
                {
                    _context.Capture.StartCoroutine(LeaveAt(_context, _quitAt));
                }
                string _crashAt = _context.Config.Option("crash-at");
                if (!string.IsNullOrEmpty(_crashAt))
                {
                    _context.Capture.StartCoroutine(CrashAt(_context, _crashAt));
                }
                yield break;
            }

            // Real clients first (multi-process run): the lobby adds their characters as they connect. A scenario where
            // some clients are meant to be refused (version gate, stuck load) waits for the others only.
            if (rejoinGraceSeconds > 0f)
            {
                gameManager.RejoinGraceSeconds = rejoinGraceSeconds;
            }
            int _clients = _context.Config.OptionInt("expect-clients", _context.Config.OptionInt("clients", 0));
            float _spawnDuringLoad = ParseSeconds(_context.Config.Option("spawn-during-load"));
            if (_spawnDuringLoad > 0f)
            {
                _context.Capture.StartCoroutine(SpawnDuringLoad(_context, _spawnDuringLoad));
            }
            if (_clients > 0)
            {
                // Bots already seated by spawn-during-load are characters too: without them in the count, a joiner whose
                // load is held looked arrived (host + early bot + the others) and the table filled before its Character.
                yield return _context.WaitFor(() => networkManager.ConnectedClientsIds.Count >= _clients + 1 && CountPlayers() >= _clients + 1 + earlyBots,
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
                targetMap = _context.Config.Option("target-map"),
                holdPowers = _context.Config.Option("hold-power"),
                chat = _context.Config.Flag("chat"),
                realInput = GameRealInput(_context),
                lobbyInput = LobbyUi(_context) ? realInput : null,
                powerUseProbability = ParseProbability(_context.Config.Option("power-use-probability"), 1.0),
                voteProbability = ParseProbability(_context.Config.Option("vote-probability"), 1.0),
                voteSkipIds = ParseIds(_context.Config.Option("vote-skip")),
                voteSkipCast = _context.Config.Flag("vote-skip-cast"),
                realInputTour = _context.Config.Flag("real-input-tour"),
                tourAudioSlider = true,
            };
            driver = _context.Capture.gameObject.AddComponent<AutoplayDriver>();
            driver.Begin(networkManager, _controlled, new RandomValidPolicy(_context.Config.seed), _options, _context.Journal, _context.Capture);
            string _hostQuitAt = _context.Config.Option("host-quit-at");
            string _hostCrashAt = _context.Config.Option("host-crash-at");
            if (!string.IsNullOrEmpty(_hostQuitAt) || !string.IsNullOrEmpty(_hostCrashAt))
            {
                _context.Capture.StartCoroutine(HostQuitAt(_context, string.IsNullOrEmpty(_hostCrashAt) ? _hostQuitAt : _hostCrashAt,
                    !string.IsNullOrEmpty(_hostCrashAt)));
            }
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
            InstallSeatOrder(_context, _forced, _context.Config.Option("role-holder"));
            InstallCopyLevers(_context);
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

        // Scenario lever "rejoin-grace S" (host): a mid-game leaver's seat stays reserved S real seconds instead of
        // GameValues.REJOIN_GRACE_SECONDS, so a test run sees the expiry (chain) without waiting two minutes.
        private float rejoinGraceSeconds;

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
            _context.Journal.Record("roles.fakes", string.Join(", ", characterManager.GetCharacters(false)
                .Where(_c => _c && _c.isFake && _c.role != null).Select(_c => _c.role.roleName.ToString())));
            string _forcedFakes = _context.Config.Option("force-fakes");
            if (!string.IsNullOrEmpty(_forcedFakes))
            {
                foreach (string _fragment in SplitList(_forcedFakes))
                {
                    if (!characterManager.GetCharacters(false).Any(_c => _c && _c.isFake && _c.role != null &&
                            _c.role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        _context.Fail($"composition mismatch: '{_fragment}' is not a fake role (force-fakes)");
                        yield break;
                    }
                }
            }

            string _forced = _context.Config.Option("force-roles");
            string _holder = _context.Config.Option("role-holder");
            if (!string.IsNullOrEmpty(_forced) && !string.IsNullOrEmpty(_holder))
            {
                foreach (string _fragment in _forced.Split(',').Select(_f => _f.Trim()).Where(_f => _f.Length > 0))
                {
                    Character _owner = characterManager.GetCharacters(false).FirstOrDefault(_c => _c && !_c.isFake &&
                        _c.role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0);
                    string _kind = _owner == null ? "nobody" : HolderKind(_owner.ownerClientId.Value);
                    bool _held = _holder.StartsWith("seat:", StringComparison.OrdinalIgnoreCase)
                        ? _owner != null && _owner.ownerClientId.Value.ToString(CultureInfo.InvariantCulture) == _holder.Substring(5).Trim()
                        : string.Equals(_kind, _holder, StringComparison.OrdinalIgnoreCase);
                    if (!_held)
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
            if (hostLostAt != null) yield return $"host-lost={hostLostAt}";
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

        private static string[] SplitList(string _list)
            => _list.Split(',').Select(_f => _f.Trim()).Where(_f => _f.Length > 0).ToArray();

        private static void ClearCopyLevers()
        {
            RoleAttributionState.DevFakeRoles = null;
            Characters.Powers.PMarqueHurluberluges.DevStealPreference = null;
        }

        // Copied-power levers (host). "steal A,B": Ugës's Marque d'Hurluberluges steals the powers whose name contains A,
        // then B, first when they are present (the rest of its draw is kept). "force-fakes R,S": those roles are drawn as
        // factices (swapped with a real draw, same faction first, else they replace a fake), so Luma can pick an absent
        // role and copy it. Checked after the attribution (composition mismatch -> next seed).
        private static void InstallCopyLevers(AutoplayContext _context)
        {
            ClearCopyLevers();
            string _steal = _context.Config.Option("steal");
            if (!string.IsNullOrEmpty(_steal))
            {
                Characters.Powers.PMarqueHurluberluges.DevStealPreference = SplitList(_steal);
                _context.Journal.Record("steal.prefer", _steal);
            }

            string _fakes = _context.Config.Option("force-fakes");
            if (string.IsNullOrEmpty(_fakes))
            {
                return;
            }
            string[] _fragments = SplitList(_fakes);
            RoleAttributionState.DevFakeRoles = (_pool, _fake, _real) =>
            {
                bool Matches(int _index, string _fragment) =>
                    _pool[_index].role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0;
                bool Protected(int _index) => _fragments.Any(_f => Matches(_index, _f));

                foreach (string _fragment in _fragments)
                {
                    int _role = -1;
                    for (int _i = 0; _i < _pool.Count && _role < 0; _i++)
                    {
                        if (Matches(_i, _fragment)) _role = _i;
                    }
                    if (_role < 0)
                    {
                        _context.Journal.Record("composition.fake", $"{_fragment} no such role");
                        continue;
                    }
                    if (_fake.Contains(_role))
                    {
                        _context.Journal.Record("composition.fake", $"{_pool[_role].role.roleName} via=already");
                        continue;
                    }
                    FactionType _faction = _pool[_role].role.factionType;
                    int _slot = _fake.FindIndex(_f => !Protected(_f) && _pool[_f].role.factionType == _faction);
                    if (_slot < 0)
                    {
                        _slot = _fake.FindIndex(_f => !Protected(_f));
                    }
                    if (_slot < 0)
                    {
                        _context.Journal.Record("composition.fake", $"{_pool[_role].role.roleName} no fake slot");
                        continue;
                    }
                    int _drawn = _real.IndexOf(_role);
                    string _replaced = _pool[_fake[_slot]].role.roleName.ToString();
                    if (_drawn >= 0)
                    {
                        _real[_drawn] = _fake[_slot];
                    }
                    _fake[_slot] = _role;
                    _context.Journal.Record("composition.fake",
                        $"{_pool[_role].role.roleName} via={(_drawn >= 0 ? "swap" : "replace")} with={_replaced}");
                }
            };
        }

        // Lever "role-holder host|client|bot" with "force-roles": the forced roles are SEATED on that kind of player
        // (RoleAttributionState.DevSeatOrder reorders who receives the drawn roles; the draw itself is untouched), so a
        // run never has to be thrown away and re-rolled with another seed because the role landed elsewhere. The
        // post-attribution check (composition mismatch) stays as the proof.
        private static void InstallSeatOrder(AutoplayContext _context, string _forced, string _holder)
        {
            RoleAttributionState.DevSeatOrder = null;
            if (string.IsNullOrEmpty(_forced) || string.IsNullOrEmpty(_holder))
            {
                return;
            }
            string[] _fragments = _forced.Split(',').Select(_f => _f.Trim()).Where(_f => _f.Length > 0).ToArray();
            RoleAttributionState.DevSeatOrder = (_roles, _characters) =>
            {
                var _order = new List<Character>(_characters);
                var _placed = new HashSet<int>();
                foreach (string _fragment in _fragments)
                {
                    int _roleIndex = IndexWhere(Math.Min(_roles.Count, _order.Count), _i =>
                        _roles[_i].role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (_roleIndex < 0)
                    {
                        continue; // not drawn: the composition check reports it
                    }
                    if (Holds(_order[_roleIndex], _holder))
                    {
                        _placed.Add(_roleIndex);
                        continue;
                    }
                    int _seat = IndexWhere(_order.Count, _i => !_placed.Contains(_i) && _i != _roleIndex &&
                        Holds(_order[_i], _holder));
                    if (_seat < 0)
                    {
                        continue;
                    }
                    (_order[_roleIndex], _order[_seat]) = (_order[_seat], _order[_roleIndex]);
                    _placed.Add(_roleIndex);
                    _context.Journal.Record("composition.seat", $"{_fragment} -> {_holder} {_order[_roleIndex].ownerClientId.Value}");
                }
                return _order;
            };

            static int IndexWhere(int _count, Func<int, bool> _match)
            {
                for (int _i = 0; _i < _count; _i++)
                {
                    if (_match(_i)) return _i;
                }
                return -1;
            }

            // "seat:<id>" = that exact seat (e.g. seat:1, the first client to connect), else a kind (host / client / bot).
            static bool Holds(Character _c, string _holderSpec)
                => _holderSpec.StartsWith("seat:", StringComparison.OrdinalIgnoreCase)
                    ? _c.ownerClientId.Value.ToString(CultureInfo.InvariantCulture) == _holderSpec.Substring(5).Trim()
                    : KindOf(_c) == _holderSpec.ToLowerInvariant();

            static string KindOf(Character _c)
            {
                ulong _id = _c.ownerClientId.Value;
                return _id == NetworkManager.ServerClientId ? "host" : _id >= 100 ? "bot" : "client";
            }
        }

        // Lever "replay N" (IAutoplayRounds): the host ends the finished game with "Terminer la partie" (a real click with
        // real-input, else the button's own RPC), every process goes back to the main menu (GameManager.ShutOffGame),
        // then the runner hosts / joins, seats and starts the next game in the same processes.
        public IEnumerator EndRound(AutoplayContext _context, int _nextRound)
        {
            bool _atEnding = gameManager != null && gameManager.IsSpawned &&
                             gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is GameEndingState;
            if (!isClient)
            {
                bool _clicked = false;
                if (driver != null && GameRealInput(_context) != null && _atEnding)
                {
                    yield return driver.ClickEndGame().ToCoroutine(_ok => _clicked = _ok, _e => InputError(_context, "end-game", _e));
                }
                if (!_clicked && gameManager != null && gameManager.IsSpawned)
                {
                    gameManager.ShutOffGameRpc();
                }
                _context.Journal.Record("round.shutoff", $"at={(_atEnding ? "ending" : Phase)} via={(_clicked ? "click" : "direct")}");
            }
            if (driver != null)
            {
                driver.End();
                UnityEngine.Object.Destroy(driver);
                driver = null;
            }

            // Everyone (the RPC goes to every peer) shuts NGO down and loads the main menu.
            yield return _context.WaitFor(() => networkManager != null && !networkManager.IsListening && !networkManager.ShutdownInProgress &&
                                                SceneManager.GetActiveScene().buildIndex == MainMenuSceneIndex, 60f,
                "the end-of-game button never brought this process back to the main menu");
            if (_context.Failed) yield break;
            _context.Journal.Record("round.menu", $"back to the main menu, round {_nextRound} next");

            RoleAttributionState.DevSeatOrder = null;
            ClearCopyLevers();
            gameManager = null;
            characterManager = null;
            endingSeenAt = -1f;
            earlyBots = 0;
            // Let the menu settle (its own Start / login checks) before hosting or joining again.
            yield return new WaitForSecondsRealtime(isClient ? 3f : 1f);
            if (isClient)
            {
                // A client joining while the host still loads GameScene gets a broken scene sync ("Server Scene Handle
                // already exist", stuck): wait for the host's session.ready of this round, as the launcher does for the
                // first one (the host's journal is a sibling folder of this process's).
                yield return WaitForHostSessionReady(_context, _nextRound);
            }
        }

        private static IEnumerator WaitForHostSessionReady(AutoplayContext _context, int _count)
        {
            string _parent = System.IO.Path.GetDirectoryName(_context.Journal.OutputDirectory.TrimEnd('/', '\\'));
            string _hostDir = _parent != null && System.IO.Directory.Exists(_parent)
                ? System.IO.Directory.GetDirectories(_parent, "*-host-*").FirstOrDefault()
                : null;
            if (_hostDir == null)
            {
                _context.Journal.Record("round.wait-host", "host journal not found: waiting 20 s");
                yield return new WaitForSecondsRealtime(20f);
                yield break;
            }
            string _events = System.IO.Path.Combine(_hostDir, "events.ndjson");
            float _deadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < _deadline)
            {
                int _ready = 0;
                try
                {
                    using var _stream = new System.IO.FileStream(_events, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                        System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                    using var _reader = new System.IO.StreamReader(_stream);
                    string _line;
                    while ((_line = _reader.ReadLine()) != null)
                    {
                        if (_line.Contains("\"kind\":\"session.ready\""))
                        {
                            _ready++;
                        }
                    }
                }
                catch (System.IO.IOException)
                {
                    // being written: read again next time
                }
                if (_ready >= _count)
                {
                    _context.Journal.Record("round.wait-host", $"host session ready (round {_count})");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            _context.Fail($"round {_count}: the host never journaled its session.ready");
        }

        public void TearDown()
        {
            RoleAttributionState.DevSeatOrder = null;
            ClearCopyLevers();
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

        // "0,101" -> {0, 101}; unparsable entries are ignored.
        private static ulong[] ParseIds(string _text)
            => string.IsNullOrEmpty(_text)
                ? Array.Empty<ulong>()
                : _text.Split(',').Select(_s => ulong.TryParse(_s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong _v) ? (ulong?)_v : null)
                    .Where(_v => _v.HasValue).Select(_v => _v.Value).ToArray();

        private static double ParseProbability(string _text, double _fallback)
            => double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out double _value) ? Math.Clamp(_value, 0.0, 1.0) : _fallback;

        // Without the lever nothing is installed: no virtual device, no input.* event (real-input spec, AC 1).
        private void InstallRealInput(AutoplayContext _context)
        {
            bool _control = _context.Config.Flag("real-input-control");
            if (!(_context.Config.Flag("real-input") || _control || _context.Config.Flag("lobby-ui") || _context.Config.Flag("menu-ui") ||
                  RejoinViaMenu(_context) || Relay(_context)) || realInput != null)
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
            RejoinSessionStore.SetConnectionTarget("direct", $"{_address}:{_port}", string.Empty);
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

                _context.Journal.Record("connect.retry", $"attempt {_attempt} to {_address}:{_port} reason='{_reason}' synchronizing={_synchronizing}");
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
            everConnected = true;
            _context.Journal.Record("connected", string.Format(CultureInfo.InvariantCulture, "as client {0} to {1}:{2}{3}",
                networkManager.LocalClientId, _address, _port, _synchronizing
                    ? string.Format(CultureInfo.InvariantCulture, " load={0:0.0}s", Time.realtimeSinceStartup - _syncStart)
                    : string.Empty));

            // NGO scene sync loads GameScene on the client: wait for the replicated managers and our own character.
            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                // Own character = the seat this peer plays (its clientId, or its original seat once a rejoin is done).
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       (characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId) ||
                        rejoinInProgress);
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
        // Scenario lever "rejoin-after S" (with quit-at, client): this client drops at that phase (as a crash or a network
        // loss would, no Leave button), lands back in the main menu like a real player, waits S real seconds, reconnects
        // with its session token (directly, or with "rejoin-via menu" through the menu's rejoin button) and must get its
        // seat back, then plays on. Captures "rejoin-before" (just before the drop) and "rejoin-after" (bursts after the
        // rejoin) export what this player sees, for the before/after comparison (tools/autoplay/analyze_rejoin.py).
        private IEnumerator DropAndRejoin(AutoplayContext _context, float _after, string _phase)
        {
            ulong _seat = driver != null ? driver.LocalSeat : networkManager.LocalClientId;
            _context.Capture.Request("rejoin-before", 0f);
            yield return new WaitForSecondsRealtime(0.8f);

            rejoinInProgress = true;
            _context.Journal.Record("rejoin.drop", $"seat {_seat} connection {networkManager.LocalClientId} at {_phase}");
            if (driver != null)
            {
                driver.End();
                UnityEngine.Object.Destroy(driver);
                driver = null;
            }
            // An unannounced drop, as a crash of the connection: the real client path runs (ClientDisconnectHandler: "connection
            // lost" notification, back to the main menu, session statics reset). The player then rejoins from the menu
            // (a relaunch after a crash also starts from the menu): never reconnect inside the old game scene.
            networkManager.Shutdown();
            while (networkManager.ShutdownInProgress)
            {
                yield return null;
            }
            yield return _context.WaitFor(() => SceneManager.GetActiveScene().buildIndex == MainMenuSceneIndex, 30f,
                "rejoin: the dropped client never got back to the main menu");
            if (_context.Failed)
            {
                rejoinInProgress = false;
                yield break;
            }
            _context.Journal.Record("rejoin.menu", "back in the main menu after the drop");
            _context.Capture.Request("rejoin-menu", 0.5f);

            yield return new WaitForSecondsRealtime(_after);
            _context.Journal.Record("rejoin.reconnect", string.Format(CultureInfo.InvariantCulture, "after {0:0.0}s, token {1}",
                _after, string.IsNullOrEmpty(RejoinSessionStore.TokenForConnection()) ? "missing" : "present"));
            networkManager.LogLevel = LogLevel.Developer; // [REJOIN] NGO's own account of the reconnect, in the player log
            if (RejoinViaMenu(_context))
            {
                yield return RejoinThroughMenu(_context);
            }
            else
            {
                yield return Connect(_context, connectPort);
            }
            if (!_context.Config.Flag("net-log"))
            {
                networkManager.LogLevel = LogLevel.Normal;
            }
            if (_context.Failed || rejected != null)
            {
                rejoinInProgress = false;
                yield break;
            }

            yield return _context.WaitFor(() => characterManager != null && characterManager.GetLocalClientId() == _seat, 15f,
                $"rejoin: the host never gave seat {_seat} back");
            if (_context.Failed)
            {
                rejoinInProgress = false;
                yield break;
            }
            _context.Journal.Record("rejoin.seat", $"seat {_seat} connection {networkManager.LocalClientId}");
            StartClientDriver(_context, _seat, _joinedMidPhase: true);
            rejoinInProgress = false;
            _context.Capture.RequestBurst("rejoin-after", new[] { 1f, 3f, 6f });
        }

        // Scenario lever "crash-at <phase>" (client): the game process is killed at that phase, as in a crash: no Leave, no
        // disconnect message, nothing run on the way out. The launcher relaunches the game for that player
        // (scenario "client1Relaunch", launch-net -RelaunchArgs) with "relaunched": it finds its session on the PC and
        // rejoins through the main menu (RejoinThroughMenu). "rejoin-before" captures what he saw before the crash.
        private IEnumerator CrashAt(AutoplayContext _context, string _phaseText)
        {
            while (Phase.IndexOf(_phaseText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            if (Application.isEditor)
            {
                _context.Fail("crash-at kills the process: player builds only");
                yield break;
            }

            ulong _seat = driver != null ? driver.LocalSeat : networkManager.LocalClientId;
            _context.Capture.Request("rejoin-before", 0f);
            yield return new WaitForSecondsRealtime(0.8f);
            _context.Journal.Record("crash", $"seat {_seat} connection {networkManager.LocalClientId} at {Phase} token " +
                                             (string.IsNullOrEmpty(RejoinSessionStore.TokenForConnection()) ? "missing" : "present"));
            // A real crash: the process dies on the spot (no Leave, no disconnect message, nothing run on the way out).
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        // The rejoin a player does: in the main menu, the "Rejoindre la partie en cours" button, clicked with the virtual
        // pointer (the disconnect notification dismissed first when it covers the menu). The login screen only signs in
        // to Unity Gaming Services, which the autoplay never contacts: it is lifted as a signed-in player's would be. A
        // failed attempt (the menu keeps the session) is retried like a player would, up to 3 clicks. Ends with this peer
        // connected and its own character known (the host told it its seat).
        private IEnumerator RejoinThroughMenu(AutoplayContext _context)
        {
            for (int _attempt = 1; _attempt <= 3 && !networkManager.IsConnectedClient; _attempt++)
            {
                Button _button = null;
                yield return _context.WaitFor(() => (_button = FindRejoinButton()) != null, 30f,
                    "rejoin: the main menu never showed the \"Rejoindre la partie en cours\" button");
                if (_context.Failed) yield break;
                if (!Relay(_context))
                {
                    LiftLoginScreen(_context);
                }
                yield return new WaitForSecondsRealtime(0.6f); // menu fades
                if (realInput != null && IsDisconnectNotificationShown())
                {
                    yield return AutoplayMenuTour.CheckRejectedNotification(realInput, _context.Journal);
                }
                _context.Capture.Request("rejoin-menu-button", 0f);
                yield return new WaitForSecondsRealtime(0.4f);

                float _clickAt = Time.realtimeSinceStartup;
                bool _clicked = false;
                if (realInput != null)
                {
                    yield return AutoplayMenuTour.Click(realInput, _context.Journal, "menu-rejoin", _button.gameObject,
                        () => networkManager.IsListening || networkManager.IsConnectedClient, _ok => _clicked = _ok);
                }
                else
                {
                    _button.onClick.Invoke();
                    _clicked = true;
                    _context.Journal.Record("input.click", "menu-rejoin mode=invoke");
                }
                if (!_clicked)
                {
                    _context.Fail("rejoin: the rejoin button could not be clicked (see input.miss)");
                    yield break;
                }
                _context.Journal.Record("rejoin.click", $"attempt {_attempt}");

                bool _started = false;
                float _deadline = Time.realtimeSinceStartup + JoinHandshake.SyncTotalTimeoutSeconds + 30f;
                while (!networkManager.IsConnectedClient && Time.realtimeSinceStartup < _deadline)
                {
                    if (networkManager.IsListening)
                    {
                        _started = true;
                    }
                    else if ((_started || Time.realtimeSinceStartup - _clickAt > 5f) && !networkManager.ShutdownInProgress)
                    {
                        break; // this attempt failed, the menu is back
                    }
                    yield return null;
                }
                if (!networkManager.IsConnectedClient)
                {
                    string _reason = networkManager.DisconnectReason;
                    _context.Journal.Record("rejoin.retry", $"attempt {_attempt} failed reason='{_reason}'");
                    yield return new WaitForSecondsRealtime(1f);
                    if (RelayFallbackPolicy.HasServerReason(_reason))
                    {
                        // Refused by the host (seat no longer reserved): the menu drops the saved session, so the
                        // button must be gone and the notification shown; the run ends completed with rejected=.
                        yield return new WaitForSecondsRealtime(1f);
                        _context.Journal.Record("rejoin.refused", $"reason='{_reason}' button={(FindRejoinButton() != null ? "shown" : "hidden")}");
                        _context.Journal.Record("connect.rejected", $"reason={_reason} | after-sync=0.0s");
                        _context.Capture.Request("join-rejected", 0f);
                        yield return new WaitForSecondsRealtime(0.5f);
                        if (realInput != null && IsDisconnectNotificationShown())
                        {
                            yield return AutoplayMenuTour.CheckRejectedNotification(realInput, _context.Journal);
                        }
                        rejected = _reason;
                        yield break;
                    }
                }
            }
            if (!networkManager.IsConnectedClient)
            {
                _context.Fail("rejoin: never reconnected through the menu");
                yield break;
            }
            _context.Journal.Record("connected", $"as client {networkManager.LocalClientId} through the rejoin button");

            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       characterManager.GetLocalCharacter(false) != null;
            }, 30f, "rejoin: the host never gave this player his seat back");
        }

        // Scenario lever "relay": the session goes through Unity Gaming Services as players do: real login (anonymous
        // UGS sign-in, a distinct profile per process), the host clicks Host (Relay allocation + UGS lobby), the clients
        // join by the lobby code the launcher reads from the host's journal ("relay.lobby", passed as join-code), the
        // rejoin button reconnects through Relay. Everything is clicked / typed with the virtual devices. Needs internet
        // and the project's UGS services; creates anonymous UGS players and a short-lived public lobby.
        private static bool Relay(AutoplayContext _context) => _context.Config.Flag("relay");

        private IEnumerator LoginThroughMenu(AutoplayContext _context)
        {
            UnityTransport _utp = networkManager.GetComponent<UnityTransport>();
            if (_utp == null)
            {
                _context.Fail("relay: the NetworkManager has no UnityTransport");
                yield break;
            }
            networkManager.NetworkConfig.NetworkTransport = _utp; // the menu picks Relay when the transport is UTP

            LoginMenu _login = null;
            CanvasGroup _overlay = null;
            yield return _context.WaitFor(() =>
            {
                _login = UnityEngine.Object.FindAnyObjectByType<LoginMenu>();
                _overlay = _login != null ? PrivateField<CanvasGroup>(_login, "menuCanvasGroup") : null;
                return _overlay != null;
            }, 30f, "relay: no login screen in the main menu");
            if (_context.Failed) yield break;
            if (!_overlay.blocksRaycasts)
            {
                _context.Journal.Record("login.ok", "already signed in");
                yield break;
            }

            CanvasGroup _loginGroup = PrivateField<CanvasGroup>(_login, "loginCanvasGroup");
            yield return _context.WaitFor(() => _loginGroup != null && _loginGroup.alpha > 0.9f && _loginGroup.interactable, 60f,
                "relay: the login form never showed (Unity Services initialisation)");
            if (_context.Failed) yield break;

            string _name = $"Autoplay{_context.Config.scenario}".Replace("-", string.Empty);
            yield return TypeInto(_context, PrivateField<TMPro.TMP_InputField>(_login, "usernameInputField"), _name, "login-name");
            bool _clicked = false;
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, "login", PrivateField<Button>(_login, "loginButton").gameObject,
                () => true, _ok => _clicked = _ok);
            if (!_clicked)
            {
                _context.Fail("relay: the login button could not be clicked (see input.miss)");
                yield break;
            }
            yield return _context.WaitFor(() => !_overlay.blocksRaycasts, 60f, "relay: the UGS sign-in never completed");
            if (_context.Failed) yield break;
            _context.Journal.Record("login.ok", $"signed in to Unity Gaming Services as {_name}");
            yield return new WaitForSecondsRealtime(0.6f);
        }

        private IEnumerator HostThroughMenu(AutoplayContext _context)
        {
            global::UI.MainMenu _menu = UnityEngine.Object.FindAnyObjectByType<global::UI.MainMenu>();
            if (_menu == null)
            {
                _context.Fail("relay: no main menu");
                yield break;
            }
            CanvasGroup _hostPanel = UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(_g => _g.name == "HostPanel");
            bool _ok = false;
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, "menu-host-open", PrivateField<Button>(_menu, "openHostMenu").gameObject,
                () => _hostPanel == null || (_hostPanel.blocksRaycasts && _hostPanel.alpha > 0.5f), _r => _ok = _r);
            if (!_ok) { _context.Fail("relay: the Host panel did not open"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return TypeInto(_context, PrivateField<TMPro.TMP_InputField>(_menu, "lobbyNameInputField"), $"Autoplay {_context.Config.seed}", "lobby-name");
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, "menu-host", PrivateField<Button>(_menu, "_hostButton").gameObject,
                () => networkManager.IsListening, _r => _ok = _r);
            if (!_ok) { _context.Fail("relay: Host did not start the session"); yield break; }

            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is LobbyState &&
                       characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            }, 120f, "relay: GameScene never reached LobbyState with the host's character spawned");
            if (_context.Failed) yield break;
            _context.Journal.Record("relay.lobby", GameCode.gameCode);
            if (rejoinGraceSeconds > 0f)
            {
                _context.Journal.Record("seat.grace", string.Format(CultureInfo.InvariantCulture, "{0:0.0}s", rejoinGraceSeconds));
            }
        }

        private IEnumerator JoinThroughMenuByCode(AutoplayContext _context)
        {
            string _code = _context.Config.Option("join-code");
            global::UI.MainMenu _menu = UnityEngine.Object.FindAnyObjectByType<global::UI.MainMenu>();
            if (string.IsNullOrEmpty(_code) || _menu == null)
            {
                _context.Fail($"relay: no lobby code to join (join-code='{_code}') or no main menu");
                yield break;
            }
            CanvasGroup _joinPanel = UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(_g => _g.name == "JoinPanel");
            bool _ok = false;
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, "menu-join-open", PrivateField<Button>(_menu, "openJoinMenu").gameObject,
                () => _joinPanel == null || (_joinPanel.blocksRaycasts && _joinPanel.alpha > 0.5f), _r => _ok = _r);
            if (!_ok) { _context.Fail("relay: the Join panel did not open"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return TypeInto(_context, PrivateField<TMPro.TMP_InputField>(_menu, "joinCodeInputField"), _code, "join-code");
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, "menu-join", PrivateField<Button>(_menu, "_joinWithCodeButton").gameObject,
                () => true, _r => _ok = _r);
            if (!_ok) { _context.Fail("relay: the Join button could not be clicked"); yield break; }
            _context.Journal.Record("relay.join", _code);

            yield return _context.WaitFor(() =>
            {
                gameManager = CompositionRoot.For(networkManager).GameManager;
                characterManager = CompositionRoot.For(networkManager).CharacterManager;
                return networkManager.IsConnectedClient && gameManager != null && gameManager.IsSpawned && characterManager != null &&
                       characterManager.GetCharacters(false).Any(_c => _c && _c.ownerClientId.Value == networkManager.LocalClientId);
            }, 150f, "relay: the client never joined the lobby's game through Relay");
            if (_context.Failed) yield break;
            _context.Journal.Record("connected", $"as client {networkManager.LocalClientId} through Relay (lobby {_code})");
        }

        // Clicks the field, types the text with the virtual keyboard; a TMP field (IMGUI key events) gets the same key
        // events through its ProcessEvent (input.type mode=events). Only if that fails too is the text set directly,
        // journaled as such (mode=set), never hidden.
        private IEnumerator TypeInto(AutoplayContext _context, TMPro.TMP_InputField _field, string _text, string _action)
        {
            if (_field == null)
            {
                _context.Journal.Record("input.miss", $"{_action} reason=no-field");
                yield break;
            }
            yield return AutoplayMenuTour.Click(realInput, _context.Journal, _action, _field.gameObject, () => _field.isFocused, _ => { });
            _field.text = string.Empty;
            yield return realInput.TypeText(_text);
            yield return null;
            if (_field.text == _text)
            {
                _context.Journal.Record("input.type", $"{_action} mode=keyboard");
                yield break;
            }
            // TMP fields read IMGUI key events (fed by the OS, not by Input System devices): hand them the same
            // key events, one per character, through the field's own handling (validation, limits, onValueChanged).
            _field.text = string.Empty;
            foreach (char _character in _text)
            {
                _field.ProcessEvent(new Event { type = EventType.KeyDown, character = _character });
                yield return null;
            }
            _field.ForceLabelUpdate();
            if (_field.text == _text)
            {
                _context.Journal.Record("input.type", $"{_action} mode=events");
            }
            else
            {
                _context.Journal.Record("input.type", $"{_action} mode=set (key events not taken: '{_field.text}')");
                _field.text = _text;
            }
        }

        private static T PrivateField<T>(object _owner, string _name) where T : class
        {
            var _field = _owner?.GetType().GetField(_name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return _field?.GetValue(_owner) as T;
        }

        private static Button FindRejoinButton()
        {
            GameObject _go = GameObject.Find("RejoinButton");
            Button _button = _go != null ? _go.GetComponent<Button>() : null;
            return _button != null && _button.isActiveAndEnabled && _button.interactable ? _button : null;
        }

        private static bool IsDisconnectNotificationShown()
        {
            GameObject _handler = GameObject.Find("ClientDisconnectHandler");
            Transform _notification = _handler != null ? _handler.transform.Find("ClientDisconnectCanvas/Notification") : null;
            return _notification != null && _notification.gameObject.activeInHierarchy;
        }

        // LoginMenu's overlay group (login / loading screens over the menu), hidden the way a successful sign-in hides it.
        private static void LiftLoginScreen(AutoplayContext _context)
        {
            LoginMenu _login = UnityEngine.Object.FindAnyObjectByType<LoginMenu>();
            var _field = typeof(LoginMenu).GetField("menuCanvasGroup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (_login == null || _field == null || !(_field.GetValue(_login) is CanvasGroup _group) || !_group.blocksRaycasts)
            {
                return;
            }
            _group.DoHideGroup(0.2f);
            _context.Journal.Record("login.skip", "UGS sign-in screen lifted (the autoplay never signs in to the cloud)");
        }

        // A real client's bot brain, playing seat _self (its own clientId, or its original seat after a rejoin).
        private void StartClientDriver(AutoplayContext _context, ulong _self, bool _joinedMidPhase = false)
        {
            var _clientOptions = new AutoplayOptions
            {
                visualPicker = _context.Config.Flag("visual-picker") || _context.Config.Flag("real-input"),
                possessActor = false,
                voteFocusRole = _context.Config.Option("vote-focus"),
                targetFocus = _context.Config.Option("target-focus"),
                targetFocusPower = _context.Config.Option("target-focus-power"),
                targetMap = _context.Config.Option("target-map"),
                holdPowers = _context.Config.Option("hold-power"),
                chat = _context.Config.Flag("chat"),
                realInput = GameRealInput(_context),
                lobbyInput = LobbyUi(_context) ? realInput : null,
                powerUseProbability = ParseProbability(_context.Config.Option("power-use-probability"), 1.0),
                voteProbability = ParseProbability(_context.Config.Option("vote-probability"), 1.0),
                voteSkipIds = ParseIds(_context.Config.Option("vote-skip")),
                voteSkipCast = _context.Config.Flag("vote-skip-cast"),
                realInputTour = _context.Config.Flag("real-input-tour"),
                tourAudioSlider = false, // PlayerPrefs are shared by every process: only the host moves a slider
                joinedMidPhase = _joinedMidPhase,
            };
            driver = _context.Capture.gameObject.AddComponent<AutoplayDriver>();
            driver.Begin(networkManager, new[] { _self }, new RandomValidPolicy(_context.Config.seed + (int)_self),
                _clientOptions, _context.Journal, _context.Capture);
        }

        private IEnumerator LeaveAt(AutoplayContext _context, string _phaseText)
        {
            while (Phase.IndexOf(_phaseText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1f);
            string _phase = Phase;
            ulong _self = networkManager.LocalClientId; // read before leaving: a shut-down NetworkManager reports 0
            float _rejoinAfter = ParseSeconds(_context.Config.Option("rejoin-after"));
            if (_rejoinAfter > 0f)
            {
                yield return DropAndRejoin(_context, _rejoinAfter, _phase);
                yield break;
            }
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

        // ---- Watchdog (package AutoplayWatchdog): budgets in REAL seconds, "it never takes longer when all is well" ----

        public float BudgetFor(string _step)
        {
            int _clients = Math.Max(0, expectedClients);
            switch (_step)
            {
                case "boot": return 120f;
                case "host": return isClient ? 150f + RejoinBudget() : 150f;      // a client's "host" = join + scene load
                case "setup": return isClient ? 120f : 120f + 30f * _clients;   // the host waits for every client to load
                case "start": return 180f;                                        // lobby: ready flags, start gate
            }
            // Phases: the game's own timers (game seconds) at the current time scale, plus a margin for the network.
            float _scale = Mathf.Max(0.1f, Time.timeScale);
            if (_step.Contains("VoteState"))
            {
                float _vote = gameManager != null ? gameManager.GetGameStates(typeof(VoteState)).OfType<VoteState>().Select(_v => _v.voteDuration).DefaultIfEmpty(300f).Max() : 300f;
                return _vote / _scale + 30f;
            }
            if (_step.Contains("AwakeningState"))
            {
                return 360f / _scale + 30f; // every role layer at its longest timer
            }
            if (_step.Contains("LobbyState"))
            {
                return 240f;
            }
            return 120f / _scale + 30f;     // recaps, chaining, portal, intro: animations
        }

        private float RejoinBudget() => ParseSeconds(config?.Option("rejoin-after")) + ParseSeconds(config?.Option("stall-load"));

        public string DescribeWait()
        {
            string _game = driver != null ? driver.DescribeWait() : "no driver";
            return $"{Phase} {_game}{(rejoinInProgress ? " rejoin-in-progress" : string.Empty)}";
        }
    }
}
#endif
