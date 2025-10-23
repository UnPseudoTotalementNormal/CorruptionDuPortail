#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AYellowpaper.SerializedCollections;
using Board.UI.CharacterBar;
using Board.UI.PowerBar;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using Network;
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

        public GameInfoRevealer gameInfoRevealer;
        public CharactersBar charactersBar;
        public PowersBar powersBar;
        public ChainingManager chainingManager;
        public CharacterManager characterManager;
    
        public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

        public NetworkVariable<int> currentGameStateIndex { get; private set; } = new();

        [HideInInspector] public bool ignoreGameLoop = false;
        private bool gameHasStartedFirstLoop = false;
        public int gameLoopCount { get; private set; } = 0;
        public int currentDay => gameLoopCount + 1;

        public bool hasGameStarted => gameHasStartedFirstLoop;
        public event Action onGameStarted;
        public event Action onNewDayPassed;
        

        private void Awake()
        {
            instance = this;
            onNewDayPassed += () => gameLoopCount++;
        }
    
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
        
            SetupGameStates();
        
            if (IsServer)
            {
                currentGameStateIndex.Value = 0;
                GetGameState(currentGameStateIndex.Value).OnStartStateServer();
                NetworkManager.OnClientDisconnectCallback += OnPlayerDisconnectedServer;
            }
        
            GetGameState(currentGameStateIndex.Value).OnStartStateClient();
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
        
        #region Events Rpc
        
        [Rpc(SendTo.Everyone)]
        private void OnGameStartedRpc()
        {
            onGameStarted?.Invoke();
        }
        
        [Rpc(SendTo.Everyone)]
        private void OnNewDayPassedRpc()
        {
            onNewDayPassed?.Invoke();
        }
        
        #endregion

        

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
        
            bool _wasInGameLoop = gameStates[GetGameState(currentGameStateIndex.Value)].isInGameLoop;
            int _newGameStateIndex = currentGameStateIndex.Value + 1;
            if (_newGameStateIndex >= gameStates.Count)
            {
                _newGameStateIndex = 0;
            }

            if (!_ignoreGameLoop && _wasInGameLoop && !ignoreGameLoop && !gameStates[GetGameState(_newGameStateIndex)].isInGameLoop)
            {
                _newGameStateIndex = gameStates.ToList().FindIndex(pair => pair.Value.isInGameLoop);
                OnNewDayPassedRpc();
            }

            if (gameStates[GetGameState(_newGameStateIndex)].isInGameLoop && !gameHasStartedFirstLoop)
            {
                gameHasStartedFirstLoop = true;
                OnGameStartedRpc();
            }
            
            SwitchGameState(_newGameStateIndex);
        }

        public void PreviousGameState()
        {
            Assert.IsTrue(IsServer, "PreviousGameState can only be called on the server");
    
            bool _wasInGameLoop = gameStates[GetGameState(currentGameStateIndex.Value)].isInGameLoop;
            int _newGameStateIndex = currentGameStateIndex.Value - 1;
            if (_newGameStateIndex < 0)
            {
                _newGameStateIndex = gameStates.Count - 1;
            }
    
            if (_wasInGameLoop && !ignoreGameLoop && !gameStates[GetGameState(_newGameStateIndex)].isInGameLoop)
            {
                _newGameStateIndex = gameStates.ToList().FindLastIndex(pair => pair.Value.isInGameLoop);
            }
            SwitchGameState(_newGameStateIndex);
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
                        rpcParams = RpcTarget.Single(customRpcParams.clientId[0], RpcTargetUse.Temp);
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
            NetworkManager.Singleton.Shutdown();
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