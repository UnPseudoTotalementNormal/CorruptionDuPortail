using System;
using UI;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public abstract class GameState : ScriptableObject
    {
        public GameManager gameManager { get; set; }
        
        public GameObject stateUIPrefab;
        public StateUI stateUI { get; protected set; }

        public virtual void OnStateCreated()
        {
            if (stateUIPrefab != null)
            {
                stateUI = Instantiate(stateUIPrefab, StatesCanvas.Instance.transform).GetComponentInChildren<StateUI>();
                Assert.IsNotNull(stateUI, "There is no StateUI component in the prefab");
                stateUI.SetupStateUI(gameManager, this);
                stateUI.gameObject.SetActive(false);
            }
        }
        
        public virtual void OnStartStateServer()
        {
            
        }

        public virtual void OnEndStateServer()
        {
            
        }
        
        public virtual void OnStartStateClient()
        {
            if (stateUI != null)
            {
                stateUI.ShowStateUI();
            }
        }
        
        public virtual void OnEndStateClient()
        {
            if (stateUI != null)
            {
                stateUI.HideStateUI();
            }
        }

        public virtual void StateUpdateServer()
        {
            
        }
        
        public virtual void StateUpdateClient()
        {
            
        }

        public event Action OnStateStartEvent;
        public event Action OnStateEndEvent;
    }
}