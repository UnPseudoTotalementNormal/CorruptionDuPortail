using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AYellowpaper.SerializedCollections;
using Board.UI;
using Characters;
using GameLogic;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using Object = System.Object;

public class GameManager : NetworkBehaviour
{
    public static GameManager instance { get; private set; }

    public CharactersBar charactersBar;
    
    [field: SerializeField] private List<Character> _characters = new();

    public List<Character> characters
    {
        get
        {
            StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            return _characters;
        }
        set
        {
            _characters = value;
        }
    }
    
    public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

    public NetworkVariable<int> currentGameStateIndex { get; private set; } = new();

    [HideInInspector] public bool ignoreGameLoop = false;

    public event Action<List<Character>> onCharactersListUpdated;
    public IEnumerator TriggerOnCharactersListUpdatedAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        onCharactersListUpdated?.Invoke(_characters);
    }

    private void Awake()
    {
        instance = this;
        
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
        
        UpdateAllCharactersRpc(characters.ToArray());
    }
    
    [Rpc(SendTo.NotServer)]
    private void UpdateAllCharactersRpc(Character[] _characters)
    {
        characters.Clear();
        characters = _characters.ToList();
        onCharactersListUpdated?.Invoke(this._characters);
    }

    #region GameState Methods

    private void SetupGameStates()
    {
        var oldGameStates = gameStates.ToDictionary(key => key.Key, value => value.Value);
        gameStates.Clear();
        
        foreach (var gameState in oldGameStates)
        {
            var clonedGameState = ScriptableObject.Instantiate(gameState.Key);
            gameStates.Add(clonedGameState, gameState.Value);
            
            clonedGameState.gameManager = this;
            clonedGameState.OnStateCreated();
        }
    }
    
    public void NextGameState()
    {
        Assert.IsTrue(IsServer, "NextGameState can only be called on the server");
        
        bool _wasInGameLoop = gameStates[GetGameState(currentGameStateIndex.Value)].isInGameLoop;
        int _newGameStateIndex = currentGameStateIndex.Value + 1;
        if (_newGameStateIndex >= gameStates.Count)
        {
            _newGameStateIndex = 0;
        }

        if (_wasInGameLoop && !ignoreGameLoop && !gameStates[GetGameState(_newGameStateIndex)].isInGameLoop)
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
        Role _role = characters.FirstOrDefault(character => character.ownerClientId == characterOwnerClientId)?.role;
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
        all,
    }
}

