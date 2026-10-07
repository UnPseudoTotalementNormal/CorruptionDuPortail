#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AYellowpaper.SerializedCollections;
using Board.UI.CharacterBar;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using GameLogic.GameStates;
using Network;
using Network.Action;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using Object = System.Object;

#endregion

namespace GameLogic
{
    public class GameManager : NetworkBehaviour, IGameLoop, IGameStateQuery
    {
        // Story 12.3 (strategy B): recorded §4 survivor, NOT deleted — read only by context-less static
        // machinery (W* winning-condition POCOs / TargetUtils) + the network test
        // fixtures, which have no injection seam. CompositionRoot.For(nm) is the sanctioned indirection the
        // rest of the codebase uses. Whitelisted in StaticSingletonCensusGuardTests.
        public static GameManager instance { get; private set; }

        // Per-NetworkManager registry: lets a second in-process client's replica
        // coexist (resolved via For(NetworkManager)) instead of clobbering or
        // destroying the primary's static instance. The primary manager keeps the
        // historical `instance` façade so the Awake->spawn window is unchanged.
        private static readonly Dictionary<NetworkManager, GameManager> s_byNetworkManager = new();

        /// <summary>
        /// Resolves the GameManager owned by the given NetworkManager. For the
        /// primary (Singleton) manager this falls back to the Awake-claimed instance
        /// so the pre-spawn window behaves exactly as the historical static access.
        /// </summary>
        public static GameManager For(NetworkManager _networkManager)
        {
            if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _manager) && _manager != null)
            {
                return _manager;
            }
            return _networkManager == NetworkManager.Singleton ? instance : null;
        }

        // [LEAVE][PHASE 4] Explicit per-session static reset, invoked from the return-to-menu paths
        // (ShutOffGame + Network.ClientDisconnectHandler leave / host-loss). Domain reload is disabled in
        // this project, so statics survive across sessions; relying SOLELY on the OnDestroy /
        // OnNetworkDespawn value-scans + the editor-only SubsystemRegistration backstop can strand a stale
        // `instance` or per-NM registry entry into the NEXT host/join when a teardown is abrupt and skips
        // OnDestroy. Clearing both eagerly here guarantees a clean start. Idempotent + null-safe: clearing an
        // already-empty registry and nulling an already-null instance are no-ops, so a double-invocation
        // during teardown cannot double-free or NRE.
        public static void ResetSessionStatics()
        {
            s_byNetworkManager.Clear();
            instance = null;
        }

#if UNITY_EDITOR
        // Play-restart backstop ONLY. Domain reload is disabled in this project, so
        // statics survive across Play Mode sessions; this fires once at Play entry
        // (SubsystemRegistration) to drop any manager/registry left over from a prior
        // session. It does NOT run on scene loads and is NOT a subscription cleanup:
        // the OnClientDisconnectCallback unsubscribe and the registry/instance
        // teardown for normal scene exit live in OnNetworkDespawn and OnDestroy, which
        // fire because GameManager is scene-placed in GameScene. Reuses the same explicit
        // reset the leave paths call ([LEAVE][PHASE 4]).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            ResetSessionStatics();
        }
#endif

        // Story 7.5 (Epic 7 close, D-NFR4): these were public pass-through accessors that turned
        // GameManager into a Service Locator hub. The public exposure is DELETED so the hub cannot
        // quietly come back. What survives is GameManager's OWN dependency on the four it genuinely
        // uses internally (SetupGameStates push, RPC target resolution, disconnect handling) — kept
        // as [SerializeField] private, SAME field name so the name-based scene binding is untouched.
        // powersBar had no internal use and was removed entirely (its scene reference is now orphaned,
        // by design). Consumers inject these directly (lanes A/B/C); none read them off GameManager.
        [SerializeField] private GameInfoRevealer gameInfoRevealer;
        [SerializeField] private CharactersBar charactersBar;
        [SerializeField] private ChainingManager chainingManager;
        [SerializeField] private CharacterManager characterManager;
    
        public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

        public NetworkVariable<int> currentGameStateIndex { get; private set; } = new();

        [HideInInspector] public bool ignoreGameLoop = false;
        private readonly GameLoopMachine _gameLoopMachine = new();
        private bool gameHasStartedFirstLoop = false;
        public int gameLoopCount { get; private set; } = 0;
        public int currentDay => gameLoopCount + 1;

        public bool hasGameStarted => gameHasStartedFirstLoop;
        public NetworkAction onGameStarted = new("onGameStarted", false);
        public NetworkAction onNewDayPassed = new("onNewDayPassed", false);

        // Story 8.1 (Epic 8 / D2): IGameLoop exposes onGameStarted/onNewDayPassed as get-only
        // properties, but they are public NetworkAction FIELDS here (5.0b). A field cannot implicitly
        // satisfy a same-named interface property, so wrap them with explicit interface implementations.
        // The fields are untouched — concrete callers keep using them directly; only the interface view
        // is added. Zero behaviour change. The rest of IGameLoop/IGameStateQuery is satisfied implicitly
        // by the existing public members.
        // get;set; maps to the underlying field both ways (Story 8.3): `+=`/`-=` through the interface
        // round-trips the same NetworkAction reference, identical to mutating the field directly.
        NetworkAction IGameLoop.onGameStarted { get => onGameStarted; set => onGameStarted = value; }
        NetworkAction IGameLoop.onNewDayPassed { get => onNewDayPassed; set => onNewDayPassed = value; }

        private void Awake()
        {
            // Design B (NGO probe in 5.0c: NetworkManagerOwner is assigned AFTER
            // Object.Instantiate returns, so it is not visible here). Awake cannot
            // tell a foreign-NM replica from a true duplicate, so it only claims the
            // façade if free; same-NM duplicate destruction and foreign-replica
            // reconciliation happen in OnNetworkSpawn where NetworkManager is
            // authoritative. Production has exactly one scene-placed GameManager, so
            // this is behaviour-identical there.
            if (instance == null)
            {
                instance = this;
            }
            // Per-instance wiring: this mutates per-instance state (gameLoopCount),
            // not a static, so it must run on EVERY replica - keep it outside the
            // claim guard.
            onNewDayPassed += () => gameLoopCount++;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var _networkManager = NetworkManager;

            // Same-NM duplicate (today's semantics, one frame later than the
            // historical Awake destroy): a live manager is already registered for
            // this NetworkManager -> this is an extra instance, destroy it.
            if (s_byNetworkManager.TryGetValue(_networkManager, out var _existing) && _existing != null && _existing != this)
            {
                Destroy(gameObject);
                return;
            }

            // Façade reconciliation: the primary (Singleton) manager owns `instance`,
            // a foreign-NM replica must not. Because Awake claims-if-free, whichever
            // GameManager awoke first holds the claim - release/transfer it here now
            // that NetworkManager is authoritative.
            if (_networkManager != NetworkManager.Singleton)
            {
                if (instance == this)
                {
                    instance = null;
                }
            }
            else if (instance == null)
            {
                instance = this;
            }

            // Registry claim (the inherited NetworkManager property is valid here).
            s_byNetworkManager[_networkManager] = this;

            SetupGameStates();
        
            if (IsServer)
            {
                currentGameStateIndex.Value = 0;
                GetGameState(currentGameStateIndex.Value).OnStartStateServer();
                NetworkManager.OnClientDisconnectCallback += HandlePlayerLeft;
            }

            // Rejoin 02: mid-game, the host only lets a rejoiner in (this peer sent a session token). Until the host tells
            // this peer which seat it plays,
            // its local identity is the new connection id and the state's client start would find no local character:
            // the start waits for RejoinCatchUpRpc (sent right after the seat assignment).
            if (!IsServer && !(GetGameState(currentGameStateIndex.Value) is LobbyState) &&
                !string.IsNullOrEmpty(Network.RejoinSessionStore.TokenForConnection()))
            {
                _clientStartDeferredForRejoin = true;
                return;
            }
            GetGameState(currentGameStateIndex.Value).OnStartStateClient();
        }

        private bool _clientStartDeferredForRejoin;

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback -= HandlePlayerLeft;
            }

            UnregisterFromRegistry();

            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            // Safety net: a same-NM duplicate is destroyed in OnNetworkSpawn and may
            // never despawn cleanly; also covers teardown ordering where
            // OnNetworkDespawn was not invoked.
            UnregisterFromRegistry();

            if (instance == this)
            {
                instance = null;
            }
        }

        // Removes this manager from the per-NetworkManager registry by value, so
        // teardown ordering (NGO: NetworkManager.Singleton may be null in OnDestroy
        // during shutdown) can never strand a stale entry or throw on a stale key.
        // The registry holds at most a handful of entries, so the scan is trivial.
        private void UnregisterFromRegistry()
        {
            NetworkManager _key = null;
            foreach (var _pair in s_byNetworkManager)
            {
                if (_pair.Value == this)
                {
                    _key = _pair.Key;
                    break;
                }
            }
            if (_key != null)
            {
                s_byNetworkManager.Remove(_key);
            }
        }

        private void Update()
        {
            GetGameState(currentGameStateIndex.Value).StateUpdateClient();
        
            if (!IsServer)
            {
                return;
            }
        
            GetGameState(currentGameStateIndex.Value).StateUpdateServer();
            ExpireReservedSeats(Time.realtimeSinceStartupAsDouble);
        }

        #region GameState Methods

        private void SetupGameStates()
        {
            var oldGameStates = gameStates.ToDictionary(key => key.Key, value => value.Value);
            gameStates.Clear();
        
            foreach (var gameState in oldGameStates)
            {
                var clonedGameState = Instantiate(gameState.Key);
                gameStates.Add(clonedGameState, gameState.Value);
            
                clonedGameState.gameManager = this;
                clonedGameState.characterManager = characterManager;
                clonedGameState.gameInfoRevealer = gameInfoRevealer;
                clonedGameState.chainingManager = chainingManager;
                clonedGameState.charactersBar = charactersBar;
                // Story 10.3 lane B: BoardManager resolved from the composition root (the still-singleton
                // board) and pushed, so states stop reading the BoardManager.instance global.
                clonedGameState.boardManager = CompositionRoot.For(NetworkManager).BoardManager;
                // Story 10.4 lane B: StatesCanvas (UI host) pushed the same way.
                clonedGameState.statesCanvas = CompositionRoot.For(NetworkManager).StatesCanvas;
                // Quick-dev gamesettings-refonte (2026-06-20): the role-settings backbone pushed the same
                // lane-B way, so LobbyState/RoleAttributionState read the replicated counts instead of the
                // old per-client SO dictionary.
                clonedGameState.gameSettingsManager = CompositionRoot.For(NetworkManager).GameSettingsManager;
                // Story 10.5 lane B: SelectionFlowService + FocusManager pushed the same way (sole consumer
                // TakeDownThePortalState), so it stops reading those globals.
                clonedGameState.selectionFlowService = CompositionRoot.For(NetworkManager).SelectionFlowService;
                clonedGameState.focusManager = CompositionRoot.For(NetworkManager).FocusManager;
                clonedGameState.OnStateCreated();
            }
        }
        
        public void SetGameState(GameState _gameState)
        {
            Assert.IsTrue(IsServer, "SetGameState can only be called on the server");
            int _newGameStateIndex = gameStates.ToList().FindIndex(pair => pair.Key == _gameState);
            if (_newGameStateIndex < 0)
            {
                Debug.LogError($"Game state {_gameState} not found in game states list");
                return;
            }
        
            SwitchGameState(_newGameStateIndex);
        }
        
        public void SetGameState(Type _gameStateType)
        {
            Assert.IsTrue(IsServer, "SetGameState can only be called on the server");
            int _newGameStateIndex = gameStates.ToList().FindIndex(pair => pair.Key.GetType() == _gameStateType);
            if (_newGameStateIndex < 0)
            {
                Debug.LogError($"Game state {_gameStateType} not found in game states list");
                return;
            }
        
            SwitchGameState(_newGameStateIndex);
        }
        
        public async UniTask WaitAFrameAndNextGameState()
        {
            int _from = currentGameStateIndex.Value;
            await UniTask.WaitForEndOfFrame();
            // The state that asked may have been left during that frame (out-of-band victory after a leave): advancing
            // from the new state would skip it (from GameEndingState, wrap back to the lobby).
            if (currentGameStateIndex.Value != _from)
            {
                Debug.Log($"[LEAVE] WaitAFrameAndNextGameState: state {_from} was left meanwhile — not advancing.");
                return;
            }
            NextGameState();
        }
    
        public void NextGameState(bool _ignoreGameLoop = false)
        {
            Assert.IsTrue(IsServer, "NextGameState can only be called on the server");

            // Decision-only POCO (Story 2.11b): the arithmetic lives in Domain; the adapter keeps
            // the NetworkVariable ownership + event raising + SwitchGameState ordering (HELD to Epic 5).
            GameLoopTransition _transition = _gameLoopMachine.Advance(
                currentGameStateIndex.Value,
                BuildIsInGameLoopList(),
                gameHasStartedFirstLoop,
                ignoreGameLoop,
                _ignoreGameLoop);

            if (_transition.FireNewDayPassed)
            {
                onNewDayPassed?.Invoke();
            }

            gameHasStartedFirstLoop = _transition.GameHasStartedFirstLoop;
            if (_transition.FireGameStarted)
            {
                onGameStarted?.Invoke();
            }

            SwitchGameState(_transition.NewIndex);
        }

        public void PreviousGameState()
        {
            Assert.IsTrue(IsServer, "PreviousGameState can only be called on the server");

            int _newGameStateIndex = _gameLoopMachine.Rewind(
                currentGameStateIndex.Value,
                BuildIsInGameLoopList(),
                ignoreGameLoop);

            SwitchGameState(_newGameStateIndex);
        }

        // The per-state isInGameLoop flags in dictionary order — the same order as
        // GetGameState(index) (gameStates.Keys.ElementAt(index)) — so the POCO's positional
        // indices line up with the live state indices.
        private List<bool> BuildIsInGameLoopList()
        {
            var _flags = new List<bool>(gameStates.Count);
            foreach (var _pair in gameStates)
            {
                _flags.Add(_pair.Value.isInGameLoop);
            }
            return _flags;
        }
    
        private void SwitchGameState(int newGameStateIndex)
        {
            Assert.IsTrue(IsServer, "SwitchGameState can only be called on the server");
            Assert.IsTrue(newGameStateIndex >= 0 && newGameStateIndex < gameStates.Count, "Invalid game state index");

            // NET-06 (epic-network-sync-hardening): flush the NetworkVariable deltas written by the server callbacks
            // BEFORE each client lifecycle RPC. NGO sends RPCs immediately but deltas on the next tick, so without the
            // barrier OnEnd/OnStartStateClient ran on remote clients with stale NetworkVariables (including
            // currentGameStateIndex, which drives StateUpdateClient).
            var _oldGameState = GetGameState(currentGameStateIndex.Value);
            _oldGameState.OnEndStateServer();
            Network.NetworkVariableFlush.TryFlush(NetworkManager);
            DoStateMethodRpc(_oldGameState.GetType().FullName, nameof(_oldGameState.OnEndStateClient), new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));

            currentGameStateIndex.Value = newGameStateIndex;
            var _newGameState = GetGameState(currentGameStateIndex.Value);
            _newGameState.OnStartStateServer();
            Network.NetworkVariableFlush.TryFlush(NetworkManager);
            DoStateMethodRpc(_newGameState.GetType().FullName, nameof(_newGameState.OnStartStateClient), new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
        }

        public GameState GetGameState(int index)
        {
            return gameStates.Keys.ElementAt(index);
        }

        /// <summary>
        /// Server-side phase gate consumed by the NGO connection-approval callback
        /// (<see cref="Network.ConnectionApprovalGate"/>): true while the session is still forming in the
        /// lobby (index 0), false once the game has started. Reuses the exact LobbyState signal that
        /// HandlePlayerLeft keys off (the _inLobby check). Empty/out-of-range-safe so the approval callback
        /// can query it during the spawn/setup race without throwing — an un-progressed loop reads as joinable.
        /// </summary>
        public bool IsInLobbyPhase
        {
            get
            {
                int _index = currentGameStateIndex.Value;
                if (gameStates.Count == 0 || _index < 0 || _index >= gameStates.Count)
                {
                    return true;
                }
                return GetGameState(_index) is LobbyState;
            }
        }

        public GameState[] GetGameStates(Type _gameStateType)
        {
            return gameStates.Keys.Where(_state => _state.GetType() == _gameStateType).ToArray();
        }
        
        public int GetGameStateIndex(GameState _gameState)
        {
            int index = 0;
            foreach (var pair in gameStates)
            {
                if (ReferenceEquals(pair.Key, _gameState))
                {
                    return index;
                }
                index++;
            }
            
            Debug.LogWarning($"GameState {_gameState?.name ?? "null"} not found in gameStates dictionary. This might be because you're passing an uncloned GameState reference.");
            return -1;
        }

        /// <summary>
        /// Gets the closest previous game state of type T before the current game state.
        /// Handles circular game loop properly.
        /// </summary>
        /// <typeparam name="T">The type of GameState to find</typeparam>
        /// <returns>The closest previous state of type T, or null if not found</returns>
        public T GetClosestPreviousState<T>() where T : GameState
        {
            GameState[] statesOfType = GetGameStates(typeof(T));
            if (statesOfType.Length == 0)
            {
                return null;
            }

            int currentIndex = currentGameStateIndex.Value;
            T closestPreviousState = null;
            int closestDistance = int.MaxValue;

            foreach (GameState state in statesOfType)
            {
                int stateIndex = GetGameStateIndex(state);
                if (stateIndex < 0)
                {
                    continue;
                }

                // Calculate distance (handling circular loop)
                int distance;
                if (stateIndex < currentIndex)
                {
                    distance = currentIndex - stateIndex;
                }
                else
                {
                    distance = gameStates.Count - stateIndex + currentIndex;
                }

                if (distance > 0 && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPreviousState = state as T;
                }
            }

            return closestPreviousState;
        }

        #endregion

        #region StateMethodRpc

        public void DoStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName, NetworkSerializableObject[] arguments, CustomRpcParams customRpcParams)
        {
            RpcParams _rpcParams;
            if (!GetTargetFromCustomRpcParams(customRpcParams, out _rpcParams))
            {
                return;
            }
            CallStateMethodRpc(stateTypeName, methodName, arguments, _rpcParams);
        }

        public void DoStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName, CustomRpcParams customRpcParams)
        {
            DoStateMethodRpc(stateTypeName, methodName, null, customRpcParams);
        }


        [Rpc(SendTo.SpecifiedInParams)]
        private void CallStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName,
            NetworkSerializableObject[] arguments, RpcParams rpcParams)
        {
            GameState _gameState = gameStates.Keys.FirstOrDefault(state => state.GetType().FullName == stateTypeName.ToString());
            Assert.IsNotNull(_gameState, $"GameState {stateTypeName} not found");

            // Rejoin 02: a rejoining peer starts the state current at its catch-up (RejoinCatchUpRpc). State transitions
            // broadcast while it is still loading would run a state's client side before it has its seat and cards.
            if (_clientStartDeferredForRejoin &&
                (methodName == nameof(GameState.OnStartStateClient) || methodName == nameof(GameState.OnEndStateClient)))
            {
                return;
            }

            // NET-06: expose the REAL sender to server-side state methods (they used to trust ids in the payload).
            // Rejoin 02: a rejoined player's new connection acts for his original seat.
            CurrentStateRpcSenderId = characterManager != null
                ? characterManager.SeatOfTransport(rpcParams.Receive.SenderClientId)
                : rpcParams.Receive.SenderClientId;
            try
            {
                CallMethodAfterRpc(_gameState, methodName, arguments);
            }
            finally
            {
                CurrentStateRpcSenderId = NetworkManager.ServerClientId;
            }
        }

        /// <summary>
        /// NET-06: on the server, the clientId that sent the state-method RPC currently being executed
        /// (<see cref="NetworkManager.ServerClientId"/> outside such a call or for the host itself).
        /// </summary>
        public ulong CurrentStateRpcSenderId { get; private set; }

        #endregion

        #region CallMethodRpc

        // ReSharper disable Unity.PerformanceAnalysis
        private bool GetTargetFromCustomRpcParams(CustomRpcParams customRpcParams, out RpcParams rpcParams)
        {
            rpcParams = null;
            try
            {
                switch (customRpcParams.targetType)
                {
                    case CustomRpcParams.RpcTargetType.single:
                        rpcParams = characterManager.GetSafeRpcTarget(customRpcParams.clientId[0]);
                        break;
                    case CustomRpcParams.RpcTargetType.server:
                        rpcParams = RpcTarget.Server;
                        break;
                    case CustomRpcParams.RpcTargetType.host:
                        rpcParams = RpcTarget.Single(NetworkManager.ServerClientId, RpcTargetUse.Temp);
                        break;
                    case CustomRpcParams.RpcTargetType.clients:
                        rpcParams = RpcTarget.ClientsAndHost;
                        break;
                    case CustomRpcParams.RpcTargetType.all:
                        rpcParams = RpcTarget.Everyone;
                        break;
                    case CustomRpcParams.RpcTargetType.notHost:
                        rpcParams = RpcTarget.NotServer;
                        break;
                    default:
                        return false;
                }
            
                return true;
            }
            catch (Exception _exception)
            {
                Debug.LogError("Error while getting target from custom RPC params: " + _exception);
                return false;
            }
        }

        private void CallMethodAfterRpc(Object objectToCall, FixedString64Bytes methodName,
            NetworkSerializableObject[] arguments)
        {
            MethodInfo method = objectToCall.GetType().GetMethod(methodName.ToString(), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"Method {methodName} not found in object {objectToCall.ToString()}");

            object[] parameters = null;
            if (arguments != null)
            {
                ParameterInfo[] paramInfos = method.GetParameters();
                parameters = new object[arguments.Length];

                for (int i = 0; i < arguments.Length; i++)
                {
                    parameters[i] = arguments[i].DeserializeNonGeneric(paramInfos[i].ParameterType);
                }
            }

            method.Invoke(objectToCall, parameters);
        }

        #endregion
        
        
        [Rpc(SendTo.Everyone)]
        public void ShutOffGameRpc()
        {
            _ = ShutOffGame();
        }

        private async UniTaskVoid ShutOffGame()
        {
            // [LEAVE][PHASE 3] Graceful host end. Flag the imminent shutdown as EXPECTED so the client-side
            // ClientDisconnectHandler.OnClientStopped does NOT mistake this for an abrupt host loss and pop
            // the "host connection lost" notification. Runs on Everyone (host + clients); harmless on the host.
            Network.ClientDisconnectHandler.NotifyExpectedShutdown();

            // Clean up the cloud lobby on a graceful end: the host DELETES the whole lobby (so it does not
            // linger for the remaining members until Unity's ~30s no-heartbeat expiry), each client just
            // removes itself. Fire-and-forget — LobbyManager is DontDestroyOnLoad, so it outlives the scene load.
            if (Network.Services.LobbyManager.instance != null)
            {
                _ = Network.Services.LobbyManager.instance.LeaveOrDeleteLobby();
            }

            // [LEAVE][PHASE 4] Tie the grace delay to this object's lifetime. If the session is torn down
            // abruptly during the 1s wait (object destroyed / scene unloaded), the token cancels the delay so
            // a dangling continuation cannot resume and race a redundant Shutdown + scene load. UniTaskVoid:
            // SuppressCancellationThrow keeps a cancellation from surfacing to the unhandled-exception handler.
            bool _canceled = await UniTask.WaitForSeconds(1, cancellationToken: this.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();
            if (_canceled)
            {
                return;
            }

            if (NetworkManager != null)
            {
                NetworkManager.Shutdown();
            }

            // [LEAVE][PHASE 4] Explicit per-session static reset on the return-to-menu path, so an abrupt
            // teardown cannot strand a stale instance / registry entry into the next host or join (domain
            // reload is disabled). Idempotent + null-safe.
            ResetSessionStatics();
            CompositionRoot.ResetSessionStatics();

            UnityEngine.SceneManagement.SceneManager.LoadScene(1); // Loading menu
        }

        
        
        // [LEAVE][PHASE 2] Server-side set of REAL client ids that have left this session (populated by
        // HandlePlayerLeft). Read by VoteState.CanVote (through HasClientLeft) so a departed player is dropped from
        // the eligible-voter denominator — the behavior the old fakify-on-disconnect gave for free (isFake), lost
        // when Phase 1 switched to chaining. Keyed on the true "this real client has left" discriminator, NOT on
        // isChained (chained-but-present players stay eligible — owner ruling). A rejoin (feat/player-rejoin) will
        // clear its entry. Bots (id >= 100) are never added (they never fire the disconnect callback).
        private readonly HashSet<ulong> _departedClientIds = new();

        /// <summary>
        /// [LEAVE] True iff the given REAL client has left this session. Server-side bookkeeping populated by
        /// <see cref="HandlePlayerLeft"/>; consumed by VoteState.CanVote to exclude a departed voter without
        /// excluding chained-but-present players.
        /// </summary>
        public bool HasClientLeft(ulong _clientId) => _departedClientIds.Contains(_clientId);

        // Rejoin step 1 (feat/player-rejoin): a real player who disconnects MID-GAME keeps his seat for a grace delay
        // instead of being chained at once (owner decision 2026-10-05). While reserved he is "departed" (shown as
        // left, skipped at night, excluded from the vote denominator) but NOT chained; when the delay expires the
        // ratified leave rule applies (instant chain + victory re-check). Server-only.
        private readonly SeatReservations _reservedSeats = new();

        /// <summary>Real seconds a mid-game leaver's seat stays reserved (dev / test runs may shorten it).</summary>
        public double RejoinGraceSeconds { get; internal set; } = GameValues.REJOIN_GRACE_SECONDS;

        /// <summary>Server: true while this real client's seat is reserved after a mid-game disconnect.</summary>
        public bool IsSeatReserved(ulong _clientId) => _reservedSeats.IsReserved(_clientId);

        /// <summary>Server: seats currently reserved (read-only view, e.g. for tooling).</summary>
        public IEnumerable<ulong> ReservedSeatIds => _reservedSeats.ReservedIds;

        // Rejoin 02: approved reconnections waiting for their sync to complete (connection id -> seat).
        private readonly Dictionary<ulong, ulong> _rejoiningSeats = new();

        /// <summary>
        /// Server, connection approval (game already started): true when <paramref name="_token"/> proves ownership of
        /// a RESERVED seat. The new connection is bound to that seat at once so everything it sends during its sync
        /// already acts for the seat; <see cref="CompleteRejoin"/> finishes when the sync completes.
        /// </summary>
        public bool TryClaimReservedSeat(string _token, ulong _connectionId, out ulong _seat)
        {
            _seat = 0;
            if (!IsServer || characterManager == null || !characterManager.Seats.TryGetSeatOfToken(_token, out _seat))
            {
                return false;
            }
            // A player who relaunches quickly after a crash comes back before the host noticed he was gone (liveness
            // ~15 s, transport ~30 s): his old connection still holds the seat. The token proves it is him: that dead
            // connection is dropped now (the seat gets reserved by the leave pipeline) and the new one takes the seat.
            // Likewise when the seat is already reserved (liveness noticed the silence) but the dead connection has not
            // timed out at the transport level yet (~30 s): it is closed now, before the new connection is bound, so its
            // late disconnect can never pass for the player leaving again once he is back.
            ulong _holder = characterManager.TransportOfSeat(_seat);
            if (_holder != _connectionId && _holder != NetworkManager.ServerClientId && NetworkManager.ConnectedClientsIds.Contains(_holder))
            {
                Debug.Log($"[REJOIN] Seat {_seat} is still held by connection {_holder}: its game is gone, connection {_connectionId} takes over.");
                NetworkManager.DisconnectClient(_holder); // the leave pipeline runs from the disconnect callback
                if (!_reservedSeats.IsReserved(_seat) && !HasClientLeft(_seat))
                {
                    HandlePlayerLeft(_holder);
                }
            }
            if (!_reservedSeats.IsReserved(_seat))
            {
                Debug.Log($"[REJOIN] Connection {_connectionId} holds the token of seat {_seat}, which is not reserved (grace over or seat in use) — refused.");
                return false;
            }
            characterManager.Seats.Bind(_connectionId, _seat);
            _rejoiningSeats[_connectionId] = _seat;
            Debug.Log($"[REJOIN] Connection {_connectionId} claims reserved seat {_seat}.");
            return true;
        }

        /// <summary>Server: true when this connection was approved as a rejoin (its sync must not be refused).</summary>
        public bool IsRejoining(ulong _connectionId) => _rejoiningSeats.ContainsKey(_connectionId);

        /// <summary>
        /// Server, end of a rejoiner's sync: the seat is his again — reservation released, "left" cleared, his peer
        /// told which seat it plays, and what a fresh client misses re-sent (day, game start, chat channels).
        /// </summary>
        public void CompleteRejoin(ulong _connectionId)
        {
            if (!IsServer || !_rejoiningSeats.TryGetValue(_connectionId, out ulong _seat))
            {
                return;
            }
            _rejoiningSeats.Remove(_connectionId);
            _reservedSeats.Release(_seat);
            _departedClientIds.Remove(_seat);
            Debug.Log($"[REJOIN] Seat {_seat} taken back by connection {_connectionId}.");

            var _toNewConnection = new RpcParams
            {
                Send = new RpcSendParams { Target = RpcTarget.Single(_connectionId, RpcTargetUse.Temp) },
            };
            characterManager.AssignLocalSeatRpc(_seat, _toNewConnection);
            // The Mage id was broadcast once, when the Mage got chained: a player who was away missed it.
            foreach (TakeDownThePortalState _portal in GetGameStates(typeof(TakeDownThePortalState)).OfType<TakeDownThePortalState>())
            {
                if (_portal.shouldActivate)
                {
                    DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(TakeDownThePortalState.SetMageCharacterRpc),
                        new NetworkSerializableObject[] { new(_portal.mageCharacterOwnerId) },
                        new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new[] { _seat }));
                }
            }
            RejoinCatchUpRpc(gameLoopCount, _toNewConnection);
            onGameStarted.InvokeFor(_connectionId);
            onPlayerRejoinedServer?.Invoke(_seat, _connectionId);
        }

        /// <summary>Server: a seat was taken back (seat id, new connection id) — subsystems re-send their slice.</summary>
        public event Action<ulong, ulong> onPlayerRejoinedServer;

        // A rejoined peer missed every day-passed signal: set its day counter directly.
        [Rpc(SendTo.SpecifiedInParams)]
        private void RejoinCatchUpRpc(int _gameLoopCount, RpcParams _params = default)
        {
            gameLoopCount = _gameLoopCount;
            Debug.Log($"[REJOIN] Caught up: day {currentDay}.");
            if (_clientStartDeferredForRejoin)
            {
                _clientStartDeferredForRejoin = false;
                // Past the introduction, the table it dealt (cards, role shelf) is rebuilt for this peer.
                if (!(GetGameState(currentGameStateIndex.Value) is GameIntroductionState))
                {
                    foreach (GameIntroductionState _intro in GetGameStates(typeof(GameIntroductionState)).OfType<GameIntroductionState>())
                    {
                        _intro.DealBoard();
                    }
                }
                GetGameState(currentGameStateIndex.Value).OnStartStateClient();
            }
        }

        // Settles every reserved seat whose grace delay is over at _now: the leave rule applies to each, once.
        internal void ExpireReservedSeats(double _now)
        {
            if (!IsServer || _reservedSeats.Count == 0)
            {
                return;
            }

            foreach (ulong _clientId in _reservedSeats.TakeExpired(_now))
            {
                Character _leaver = characterManager.GetCharacters(false)
                    .FirstOrDefault(_c => _c && _c.ownerClientId.Value == _clientId);
                if (_leaver == null || _leaver.isChained.Value)
                {
                    Debug.Log($"[LEAVE] Reserved seat of {_clientId} expired — nothing to chain (no live seat or already chained).");
                    continue;
                }

                Debug.Log($"[LEAVE] Reserved seat of {_clientId} expired after {RejoinGraceSeconds:0} s — chaining instantly (no animation).");
                ChainLeaverInstant(_leaver);
                if (TryResolveVictoryAfterLeave())
                {
                    Debug.Log($"[LEAVE] Player {_clientId} was the last anomaly — victory resolved instantly, game ending.");
                    return;
                }
            }
        }

        // [LEAVE] Phase 1 (epic-player-leave-stability) — THE ONE authoritative server-side reaction to
        // a player disconnect. Replaces the four independent, order-undefined callback reactions (the old
        // fakify here + LobbyState.RemoveCharacter + the ancillary despawns) with one ordered pipeline.
        // Branches on game phase: in lobby -> remove the character (kept lobby behavior); mid-game -> chain
        // the leaver INSTANTLY (no ChainingState animation), NOT fakify. Wired as the single server-side
        // OnClientDisconnectCallback subscription (OnNetworkSpawn/OnNetworkDespawn).
        // [LIVENESS B2] Made public so the liveness layer can use this as a SECOND, faster ignition source
        // (arch-liveness-heartbeat §8.4): LivenessService routes serverTracker.PeerLost here, the SAME pipeline
        // the transport-driven OnClientDisconnectCallback drives. Both sources are reconciled by the idempotency
        // guard below.
        public void HandlePlayerLeft(ulong _clientId)
        {
            if (!IsServer)
            {
                return;
            }

            // Bots (clientId >= 100) never open a transport connection, so they never fire this callback.
            // Defensive early-return so a stray simulated-id call can never mutate real game state.
            if (_clientId >= 100)
            {
                return;
            }

            // Rejoin 02: the callback carries the CONNECTION id; a rejoined player's new connection stands for his
            // original seat, which is what every game system keys on.
            _rejoiningSeats.Remove(_clientId); // a rejoin that dropped during its sync is simply over
            if (characterManager != null && !characterManager.Seats.IsAlias(_clientId) &&
                characterManager.TransportOfSeat(_clientId) != _clientId)
            {
                // A dead connection of a seat whose player already plays again through another connection: stale.
                Debug.Log($"[REJOIN] Late disconnect of connection {_clientId}: seat {_clientId} is played through connection {characterManager.TransportOfSeat(_clientId)} now — ignored.");
                return;
            }
            if (characterManager != null && characterManager.Seats.Unbind(_clientId, out ulong _seat))
            {
                Debug.Log($"[LEAVE] Connection {_clientId} played seat {_seat} (rejoined) — handling the seat.");
                _clientId = _seat;
            }

            // [LEAVE][PHASE 2] Record this REAL client's departure. Phase 1 replaced the old fakify-on-disconnect
            // with chaining; the original vote excluded a disconnected seat for free (fakify -> isFake), so we
            // re-record the specific departed client here and re-exclude it in VoteState.CanVote via HasClientLeft —
            // WITHOUT excluding chained-but-present players (owner ruling). Never cleared (reconnection is out of scope).
            //
            // [LIVENESS B2] THIS IS ALSO THE IDEMPOTENCY LOCK (arch §8.7). Two ignition sources now reach here
            // (liveness ~5s + transport backstop ~12s), in EITHER order. HashSet.Add returns false when the id is
            // already recorded, so the FIRST call for a client processes and EVERY later call is a total no-op —
            // no double-chain, no double win-check, no replayed chain, no NRE. Departure record + dedup are one act.
            if (!_departedClientIds.Add(_clientId))
            {
                return;
            }

            // Phase signal: the current game state is a LobbyState iff we are still in the lobby (index 0,
            // pre-start); mid-game it is any other state. The LobbyState check is the precise phase signal —
            // hasGameStarted is a coarser proxy that the DummyGameState test substrate cannot set, so the
            // authoritative branch keys off the current state type.
            bool _inLobby = GetGameState(currentGameStateIndex.Value) is LobbyState;

            if (_inLobby)
            {
                // Lobby leave: remove the character. Conceptually the historical LobbyState.OnClientDisconnected
                // behavior, now owned HERE so exactly one code path reacts (LobbyState's own subscription is gone).
                Debug.Log($"[LEAVE] Player {_clientId} left in lobby — removing character.");
                characterManager.RemoveCharacter(_clientId);
            }
            else
            {
                // Mid-game leave (rejoin step 1): RESERVE the seat for the grace delay instead of chaining at once.
                // The player shows as left and is skipped; ExpireReservedSeats applies the ratified rule (instant
                // chain -> victory re-check) once the delay is over, unless he came back.
                Character _leaver = characterManager.GetCharacters()
                    .FirstOrDefault(_c => _c.ownerClientId.Value == _clientId);
                if (_leaver != null)
                {
                    // An already chained player gets their seat reserved too (owner decision 2026-10-07): chained but
                    // present, they still vote, so a drop must not cost them that for good. Their grace expiry has nothing
                    // left to chain (ExpireReservedSeats skips chained seats, no victory re-check).
                    _reservedSeats.Reserve(_clientId, Time.realtimeSinceStartupAsDouble, RejoinGraceSeconds);
                    Debug.Log(_leaver.isChained.Value
                        ? $"[LEAVE] Player {_clientId} left mid-game, already chained — seat reserved for {RejoinGraceSeconds:0} s (may rejoin)."
                        : $"[LEAVE] Player {_clientId} left mid-game — seat reserved for {RejoinGraceSeconds:0} s (not chained yet).");

                    // Unblock whatever state was waiting on this specific player so the night/vote/portal cannot
                    // hang on a seat that is away.
                    UnblockCurrentStateAfterLeave(_clientId);
                }
                else
                {
                    Debug.Log($"[LEAVE] Player {_clientId} left mid-game but owned no live character — nothing to reserve.");
                }
            }

            // Ancillary cleanup (Task 6 decision, documented): player-info removal
            // (LobbyPlayerInfoHolder.OnClientDisconnected) and avatar despawn (AvatarManager.OnClientDisconnected)
            // keep their OWN server-gated OnClientDisconnectCallback subscriptions — each already fires exactly
            // once and neither conflicts with this pipeline. The old board fake-card cleanup is intentionally
            // DROPPED: it only existed to destroy the fake seat this handler no longer creates (no more fakify).
        }

        // Instant-chain primitive. In production the injected ChainingManager runs the full ratified chain
        // (ChainCharacterServer -> isChained + role reveal + portal/Mage special case + AskForUpdate). Because
        // ChainCharacterRpc is [Rpc(SendTo.Server)] and we are already on the server, its body executes
        // immediately; the card ANIMATION lives ONLY in ChainingState.OnStartStateClient, so invoking the apply
        // here is instant with NO animation.
        private void ChainLeaverInstant(Character _leaver)
        {
            var _chaining = chainingManager != null
                ? chainingManager
                : CompositionRoot.For(NetworkManager).ChainingManager;
            if (_chaining != null)
            {
                // _showCardReveal: true — no ChainingState animation runs on the leave path, so the reveal
                // must drive the card flip itself (discover the leaver's role); the chained sprite follows
                // from Card's isChained.OnValueChanged.
                _chaining.ChainCharacterRpc(_leaver.ownerClientId.Value, true);
                return;
            }

            // Substrate fallback (no ChainingManager wired — e.g. the 2-NM loopback test fixture): apply the
            // chain state directly so the seat is still chained. Role-reveal needs the ChainingManager's
            // GameInfoRevealer surface, which is absent here, so it is skipped in this fallback path only.
            _leaver.ChainCharacterServer();
            characterManager.AskForUpdateAllCharactersRpc();
        }

        // [LEAVE][PHASE 2] Out-of-band victory re-check after a mid-game leave. Resolves the reusable
        // VictoryConditionCheckState (never SetGameState(VictoryConditionCheckState) blindly from an arbitrary
        // state — that would disrupt mid-state flow) and asks it to evaluate NOW. Returns true iff the game
        // ended (the resolver jumped to GameEndingState). Gracefully returns false when no such state is seeded
        // (e.g. the DummyGameState test substrate), so the caller simply proceeds to state-unblock.
        private bool TryResolveVictoryAfterLeave()
        {
            var _states = GetGameStates(typeof(VictoryConditionCheckState));
            if (_states.Length == 0)
            {
                return false;
            }

            return ((VictoryConditionCheckState)_states[0]).TryResolveVictoryNow();
        }

        // [LEAVE][PHASE 2] Route the leave to the ONE currently-active state that could be waiting on this
        // specific player, so it can drain/advance instead of hanging. Dispatching on the current state (not a
        // broadcast) is the "only act if this state is current" guarantee the phase spec calls for. States not
        // listed here (or a state not currently active) have no per-player wait to unblock. ChainingState needs
        // no server hook — its only leave hazard is the client-side animation NRE, guarded inside the state.
        private void UnblockCurrentStateAfterLeave(ulong _clientId)
        {
            var _current = GetGameState(currentGameStateIndex.Value);
            switch (_current)
            {
                case AwakeningState _awakening:
                    _awakening.OnPlayerLeftServer(_clientId);
                    break;
                case TakeDownThePortalState _portal:
                    _portal.OnPlayerLeftServer(_clientId);
                    break;
                case VoteState _vote:
                    _vote.OnPlayerLeftServer(_clientId);
                    break;
            }
        }
    }

    public class CustomRpcParams
    {
        public ulong[] clientId;
        public RpcTargetType targetType;
    
        public CustomRpcParams(RpcTargetType targetType, ulong[] clientId = null)
        {
            this.clientId = clientId;
            this.targetType = targetType;
        }
    
        public enum RpcTargetType
        {
            single,
            server,
            host,
            clients,
            notHost,
            all,
        }
    }
}