using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager instance { get; private set; }
    
    
    public List<Character> characters = new();
    
    public List<GameState> gameStates = new();

    public GameState gameEndingState;

    public int currentGameStateIndex { get; private set; }

    private void Awake()
    {
        instance = this;
        SetupGameStates();
    }
    
    private void Start()
    {
        currentGameStateIndex = 0;
        gameStates[currentGameStateIndex].OnStartState();
    }

    private void SetupGameStates()
    {
        List<GameState> oldGameStates = gameStates.ToList();
        gameStates.Clear();
        
        foreach (GameState gameState in oldGameStates)
        {
            var clonedGameState = ScriptableObject.Instantiate(gameState);
            gameStates.Add(clonedGameState);
            
            clonedGameState.gameManager = this;
            clonedGameState.OnStateCreated();
        }
        gameEndingState = ScriptableObject.Instantiate(gameEndingState);
    }

    private void Update()
    {
        if (IsServer)
        {
            gameStates[currentGameStateIndex].StateUpdate();
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
        gameStates[currentGameStateIndex].OnEndState();
        
        currentGameStateIndex = newGameState;
        gameStates[currentGameStateIndex].OnStartState();
    }
}