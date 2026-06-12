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
    public class GameManager : NetworkBehaviour
    {
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

#if UNITY_EDITOR
        // Play-restart backstop ONLY. Domain reload is disabled in this project, so
        // statics survive across Play Mode sessions; this fires once at Play entry
        // (SubsystemRegistration) to drop any manager/registry left over from a prior
        // session. It does NOT run on scene loads and is NOT a subscription cleanup:
        // the OnClientDisconnectCallback unsubscribe and the registry/instance
        // teardown for normal scene exit live in OnNetworkDespawn and OnDestroy, which
        // fire because GameManager is scene-placed in GameScene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
            instance = null;
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
                NetworkManager.OnClientDisconnectCallback += OnPlayerDisconnectedServer;
            }
        
            GetGameState(currentGameStateIndex.Value).OnStartStateClient();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback -= OnPlayerDisconnectedServer;
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
            await UniTask.WaitForEndOfFrame();
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

            var _oldGameState = GetGameState(currentGameStateIndex.Value);
            _oldGameState.OnEndStateServer();
            DoStateMethodRpc(_oldGameState.GetType().FullName, nameof(_oldGameState.OnEndStateClient), new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
        
            currentGameStateIndex.Value = newGameStateIndex;
            var _newGameState = GetGameState(currentGameStateIndex.Value);
            _newGameState.OnStartStateServer();
            DoStateMethodRpc(_newGameState.GetType().FullName, nameof(_newGameState.OnStartStateClient), new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
        }

        public GameState GetGameState(int index)
        {
            return gameStates.Keys.ElementAt(index);
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
        
            CallMethodAfterRpc(_gameState, methodName, arguments);
        }

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
            await UniTask.WaitForSeconds(1);
            NetworkManager.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene(1); // Loading menu
        }

        
        
        private void OnPlayerDisconnectedServer(ulong _clientId)
        {
            if (characterManager.GetCharacters().Any(_c => _c.ownerClientId.Value == _clientId))
            {
                characterManager.GetCharacter(_clientId).ownerClientId.Value = GameValues.FAKE_CLIENT_ID;
                characterManager.AskForUpdateAllCharactersRpc();
            }

            OnPlayerDisconnectedRpc();
        }

        [Rpc(SendTo.Everyone)]
        private void OnPlayerDisconnectedRpc()
        {
            BoardManager.instance.DestroyCard(BoardManager.instance.visibleCards.Find(_c => _c.characterInfo.isFake));
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