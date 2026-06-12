#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.BoardCameraSystem;
using Board.UI.CharacterBar;
using Characters;
using UI;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    public abstract class GameState : ScriptableObject
    {
        public GameManager gameManager { get; set; }
        // Story 7.2 lane B: CharacterManager pushed directly by SetupGameStates alongside gameManager,
        // so states stop hub-hopping through gameManager.characterManager (deleted in 7.5).
        public CharacterManager characterManager { get; set; }
        // Story 7.3 lane B: GameInfoRevealer pushed the same way.
        public GameInfoRevealer gameInfoRevealer { get; set; }
        // Story 7.4 lane B: ChainingManager + CharactersBar pushed the same way, so states stop
        // hub-hopping through gameManager.chainingManager / gameManager.charactersBar (deleted in 7.5).
        public ChainingManager chainingManager { get; set; }
        public CharactersBar charactersBar { get; set; }

        public GameObject stateUIPrefab;
        public StateUI stateUI { get; protected set; }
        
        public List<GameState> gameStateDependencies = new();
        
        public BoardCameraIdEnum forceBoardCamera = BoardCameraIdEnum.None;
        
        public bool useLightColorOverride = false;
        public Color lightColorOverride = Color.white;
        
        public event Action onStateStartServer;
        public event Action onStateEndServer;
        public event Action onStateStartClient;
        public event Action onStateEndClient;

        public virtual void OnStateCreated()
        {
            if (stateUIPrefab != null)
            {
                stateUI = Instantiate(stateUIPrefab, StatesCanvas.Instance.transform).GetComponentInChildren<StateUI>();
                Assert.IsNotNull(stateUI, "There is no StateUI component in the prefab");
                stateUI.SetupStateUI(gameManager, this);
                stateUI.characterManager = characterManager;
                stateUI.HideStateUI(true);
            }
        }
        
        public virtual void OnStartStateServer()
        {
            Assert.IsTrue(gameManager.IsServer, "OnStartStateServer can only be called on server");
            onStateStartServer?.Invoke();
        }

        public virtual void OnEndStateServer()
        {
            Assert.IsTrue(gameManager.IsServer, "OnEndStateServer can only be called on server");
            onStateEndServer?.Invoke();
        }
        
        public virtual void OnStartStateClient()
        {
            if (stateUI != null)
            {
                stateUI.ShowStateUI(stateUI.instantShowOnStateStart);
            }
            
            onStateStartClient?.Invoke();
        }
        
        public virtual void OnEndStateClient()
        {
            if (stateUI != null)
            {
                stateUI.HideStateUI();
            }
            
            onStateEndClient?.Invoke();
        }

        public virtual void StateUpdateServer()
        {
            Assert.IsTrue(gameManager.IsServer, "StateUpdateServer can only be called on server");
        }
        
        public virtual void StateUpdateClient()
        {
            
        }
        
        public bool IsStateActive()
        {
            return gameManager.currentGameStateIndex.Value == gameManager.gameStates.Keys.ToList().IndexOf(this);
        }
    }
}