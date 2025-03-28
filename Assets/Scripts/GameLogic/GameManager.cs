using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager instance { get; private set; }
    
    
    public List<Character> characters = new();
    
    public SerializedDictionary<GameState, GameStateSettings> gameStates = new();

    public int currentGameStateIndex { get; private set; }

    private void Awake()
    {
        instance = this;
        SetupGameStates();
    }
    
    private void Start()
    {
        currentGameStateIndex = 0;
        GetGameState(currentGameStateIndex).OnStartState();
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
            GetGameState(currentGameStateIndex).StateUpdate();
        }
    }

    public void NextGameState()
    {
        int newGameStateIndex = currentGameStateIndex + 1;
        if (newGameStateIndex >= gameStates.Count)
        {
            newGameStateIndex = 0;
        }
        SwitchGameState(newGameStateIndex);
    }

    private void SwitchGameState(int newGameState)
    {
        GetGameState(currentGameStateIndex).OnEndState();
        
        currentGameStateIndex = newGameState;
        GetGameState(currentGameStateIndex).OnStartState();
    }

    public GameState GetGameState(int index)
    {
        return gameStates.Keys.ElementAt(index);
    }
}