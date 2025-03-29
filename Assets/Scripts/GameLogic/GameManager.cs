using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AYellowpaper.SerializedCollections;
using GameLogic;
using GameLogic.GameStates;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

public class GameManager : NetworkBehaviour
{
    public static GameManager instance { get; private set; }
    
    
    public List<Character> characters = new();
    
    public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

    public NetworkVariable<int> currentGameStateIndex { get; private set; } = new();

    [HideInInspector] public bool c = false;

    private void Awake()
    {
        instance = this;
        SetupGameStates();
    }
    
    private void Start()
    {
        if (IsServer)
        {
            currentGameStateIndex.Value = 0;
            GetGameState(currentGameStateIndex.Value).OnStartStateServer();
        }
        
        GetGameState(currentGameStateIndex.Value).OnStartStateClient();
    }

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

    private void Update()
    {
        if (IsServer)
        {
            GetGameState(currentGameStateIndex.Value).StateUpdate();
        }
    }

    public void NextGameState()
    {
        int newGameStateIndex = currentGameStateIndex.Value + 1;
        if (newGameStateIndex >= gameStates.Count)
        {
            newGameStateIndex = 0;
        }
        SwitchGameState(newGameStateIndex);
    }

    private void SwitchGameState(int newGameStateIndex)
    {
        Assert.IsTrue(IsServer, "SwitchGameState can only be called on the server");
        Assert.IsTrue(newGameStateIndex >= 0 && newGameStateIndex < gameStates.Count, "Invalid game state index");

        var _oldGameState = GetGameState(currentGameStateIndex.Value);
        _oldGameState.OnEndStateServer();
        DoStateMethodRpc(_oldGameState.GetType().FullName, nameof(_oldGameState.OnEndStateClient), new StateRpcParams(StateRpcParams.RpcTargetType.clients));
        
        currentGameStateIndex.Value = newGameStateIndex;
        var _newGameState = GetGameState(currentGameStateIndex.Value);
        _newGameState.OnStartStateServer();
        DoStateMethodRpc(_newGameState.GetType().FullName, nameof(_newGameState.OnStartStateClient), new StateRpcParams(StateRpcParams.RpcTargetType.clients));
    }

    public GameState GetGameState(int index)
    {
        return gameStates.Keys.ElementAt(index);
    }

    public void DoStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName, NetworkSerializableObject[] arguments, StateRpcParams stateRpcParams)
    {
        RpcParams _rpcParams;
        switch (stateRpcParams.targetType)
        {
            case StateRpcParams.RpcTargetType.single:
                _rpcParams = RpcTarget.Single(stateRpcParams.clientId[0], RpcTargetUse.Temp);
                break;
            case StateRpcParams.RpcTargetType.server:
                _rpcParams = RpcTarget.Server;
                break;
            case StateRpcParams.RpcTargetType.host:
                _rpcParams = RpcTarget.Single(NetworkManager.ServerClientId, RpcTargetUse.Temp);
                break;
            case StateRpcParams.RpcTargetType.clients:
                _rpcParams = RpcTarget.ClientsAndHost;
                break;
            case StateRpcParams.RpcTargetType.all:
                _rpcParams = RpcTarget.Everyone;
                break;
            default:
                return;
        }
        CallStateMethodRpc(stateTypeName, methodName, arguments, _rpcParams);
    }

    public void DoStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName, StateRpcParams stateRpcParams)
    {
        DoStateMethodRpc(stateTypeName, methodName, null, stateRpcParams);
    }


    [Rpc(SendTo.SpecifiedInParams)]
    private void CallStateMethodRpc(FixedString64Bytes stateTypeName, FixedString64Bytes methodName,
        NetworkSerializableObject[] arguments, RpcParams rpcParams)
    {
        GameState gameState = gameStates.Keys.FirstOrDefault(state => state.GetType().FullName == stateTypeName.ToString());
        Assert.IsNotNull(gameState, $"GameState {stateTypeName} not found");
        
        MethodInfo method = gameState.GetType().GetMethod(methodName.ToString(), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, $"Method {methodName} not found in GameState {stateTypeName}");

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

        method.Invoke(gameState, parameters);
    }
}

public class StateRpcParams
{
    public ulong[] clientId;
    public RpcTargetType targetType;
    
    public StateRpcParams(RpcTargetType targetType, ulong[] clientId = null)
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

