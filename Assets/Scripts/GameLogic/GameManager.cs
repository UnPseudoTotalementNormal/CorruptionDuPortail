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
    
        [field: SerializeField] private List<Character> _characters = new();
    
        public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

        public NetworkVariable<int> currentGameStateIndex { get; private set; } = new();

        [HideInInspector] public bool ignoreGameLoop = false;
    
        public Character GetLocalCharacter(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters.FirstOrDefault(_character => _character.ownerClientId == NetworkManager.LocalClientId);
        }

        public Character GetCharacter(ulong _characterId, bool _triggerUpdate = true)
        {
            return GetCharacters(_triggerUpdate).FirstOrDefault(_c => _c.ownerClientId == _characterId);
        }

        public List<Character> GetCharacters(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters;
        }

        public event Action<List<Character>> onCharactersListUpdated;
        public IEnumerator TriggerOnCharactersListUpdatedAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            onCharactersListUpdated?.Invoke(_characters);
        }

        private void Awake()
        {
            instance = this;
            onCharactersListUpdated += (_characters) =>
                powersBar.RefreshCharacterPowerBar(_characters.FirstOrDefault(_c =>
                    _c.ownerClientId == NetworkManager.LocalClientId));
        }
    
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
        
            SetupGameStates();
        
            if (IsServer)
            {
                currentGameStateIndex.Value = 0;
                GetGameState(currentGameStateIndex.Value).OnStartStateServer();
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
    
        [Rpc(SendTo.Server)]
        public void AskForUpdateAllCharactersRpc()
        {
            if (!IsServer)
            {
                return;
            }
     
            UpdateAllCharactersRpc(GetCharacters().ToArray());
        }
    
        [Rpc(SendTo.NotServer)]
        private void UpdateAllCharactersRpc(Character[] _characters)
        {
            foreach (var _character in _characters)
            {
                var _sameCharacter = GetCharacters().FirstOrDefault(_c => _c.ownerClientId == _character.ownerClientId);
                if (_sameCharacter != null)
                {
                    _sameCharacter.UpdateCharacter(_character);
                }
                else
                {
                    this._characters.Add(_character);
                }
            }
            this._characters.Clear();
            this._characters = _characters.ToList();
            onCharactersListUpdated?.Invoke(this._characters);
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
    
        #region PowerStaticMethodRpc

        public void DoPowerMethodRpc(ulong _powerOwner, Power _power, FixedString64Bytes _methodName, NetworkSerializableObject[] _arguments, CustomRpcParams _customRpcParams)
        {
                if (_power == null)
                {
                    Debug.LogError("Power is null in DoPowerMethodRpc");
                    return;
                }
    
                RpcParams _rpcParams;
                if (!GetTargetFromCustomRpcParams(_customRpcParams, out _rpcParams))
                {
                    return;
                }
    
                CallPowerMethodRpc(_powerOwner, _power, _methodName, _arguments, _rpcParams);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CallPowerMethodRpc(ulong _powerOwner, Power _power, FixedString64Bytes _methodName, NetworkSerializableObject[] _arguments, RpcParams _rpcParams)
        {
            // Recherche du personnage possédant ce pouvoir
            var character = GetCharacters().FirstOrDefault(c => c.ownerClientId == _powerOwner);
            if (character == null)
            {
                Debug.LogError($"Aucun personnage trouvé avec ownerClientId {_powerOwner}");
                return;
            }
    
            // Recherche du pouvoir correspondant sur ce personnage
            var power = character.role.powers.FirstOrDefault(p => p.IsTheSamePower(_power));
            if (power == null)
            {
                Debug.LogError("Aucun pouvoir correspondant trouvé sur le personnage");
                return;
            }
    
            // Appel de la méthode sur le pouvoir trouvé
            CallMethodAfterRpc(power, _methodName, _arguments);
        }
        
        public void DoPowerStaticMethodRpc(FixedString64Bytes typeName, FixedString64Bytes staticMethodName, NetworkSerializableObject[] arguments, CustomRpcParams customRpcParams)
        {
            RpcParams _rpcParams;
            if (!GetTargetFromCustomRpcParams(customRpcParams, out _rpcParams))
            {
                return;
            }
            CallPowerStaticMethodRpc(typeName, staticMethodName, arguments, _rpcParams);
        }

        public void DoPowerStaticMethodRpc(FixedString64Bytes typeName, FixedString64Bytes staticMethodName, CustomRpcParams customRpcParams)
        {
            DoPowerStaticMethodRpc(typeName, staticMethodName, null, customRpcParams);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CallPowerStaticMethodRpc(FixedString64Bytes typeName, FixedString64Bytes staticMethodName,
            NetworkSerializableObject[] arguments, RpcParams rpcParams)
        {
            Type staticType = Type.GetType(typeName.ToString());
            Assert.IsNotNull(staticType, $"Static type {typeName} not found");

            MethodInfo method = staticType.GetMethod(staticMethodName.ToString(), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, $"Static method {staticMethodName} not found in type {typeName}");

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

            method.Invoke(null, parameters);
        }

        #endregion

        #region CharacterMethodRpc

        public void DoRoleMethodRpc(ulong roleOwnerClientId, FixedString64Bytes methodName, NetworkSerializableObject[] arguments, CustomRpcParams customRpcParams)
        {
            RpcParams _rpcParams;
            if (!GetTargetFromCustomRpcParams(customRpcParams, out _rpcParams))
            {
                return;
            }
            CallRoleMethodRpc(roleOwnerClientId, methodName, arguments, _rpcParams);
        }

        public void DoRoleMethodRpc(ulong roleOwnerClientId, FixedString64Bytes methodName, CustomRpcParams customRpcParams)
        {
            DoRoleMethodRpc(roleOwnerClientId, methodName, null, customRpcParams);
        }


        [Rpc(SendTo.SpecifiedInParams)]
        private void CallRoleMethodRpc(ulong characterOwnerClientId, FixedString64Bytes methodName,
            NetworkSerializableObject[] arguments, RpcParams rpcParams)
        {
            Role _role = GetCharacters().FirstOrDefault(character => character.ownerClientId == characterOwnerClientId)?.role;
            Assert.IsNotNull(_role, $"character from client {characterOwnerClientId} not found");
        
            CallMethodAfterRpc(_role, methodName, arguments);
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

        [Rpc(SendTo.Server)]
        public void CorruptPlayerRpc(ulong _ownerClientId)
        {
            if (!IsServer)
            {
                return;
            }
        
            var _character = GetCharacters().First(_c => _c.ownerClientId == _ownerClientId);
            _character.isCorrupted = true;
        
            AskForUpdateAllCharactersRpc();
        }
    
        [Rpc(SendTo.Server)]
        public void HealPlayerRpc(ulong _ownerClientId)
        {
            if (!IsServer)
            {
                return;
            }
        
            var _character = GetCharacters().First(_c => _c.ownerClientId == _ownerClientId);
            _character.isCorrupted = false;
        
            AskForUpdateAllCharactersRpc();
        }
    
        [Rpc(SendTo.Everyone)]
        public void AwakeCharacterRpc(ulong _characterClientId)
        {
            Character _character = GetCharacters().FirstOrDefault(_c => _c.ownerClientId == _characterClientId);

            _character?.AwakenCharacter();
            if (IsServer)
            {
                AskForUpdateAllCharactersRpc();
            }
        }
        
        [Rpc(SendTo.Everyone)]
        public void SleepCharacterRpc(ulong _characterClientId)
        {
            Character _character = GetCharacters().FirstOrDefault(_c => _c.ownerClientId == _characterClientId);

            _character?.SleepCharacter();
            if (IsServer)
            {
                AskForUpdateAllCharactersRpc();
            }
        }
        
        [Rpc(SendTo.Everyone)]
        public void ShutOffGameRpc()
        {
            _ = ShutOffGame();
        }

        private async UniTaskVoid ShutOffGame()
        {
            await UniTask.WaitForSeconds(1);
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }

        public Character CreateNewFakeCharacter()
        {
            Character _character = new Character();
            _character.ownerClientId = GameValues.FAKE_CLIENT_ID - (ulong)instance.GetCharacters().Count(_c => _c.isFake);
            _characters.Add(_character);
            return _character;
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