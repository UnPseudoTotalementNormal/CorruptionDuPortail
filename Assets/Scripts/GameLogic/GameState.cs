#region

using System;
using System.Collections.Generic;
using System.Linq;
using UI;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    public abstract class GameState : ScriptableObject
    {
        public GameManager gameManager { get; set; }
        
        public GameObject stateUIPrefab;
        public StateUI stateUI { get; protected set; }
        
        public List<GameState> gameStateDependencies = new();
        
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