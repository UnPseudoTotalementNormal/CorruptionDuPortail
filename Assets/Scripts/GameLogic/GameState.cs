using System;
using UnityEngine;

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
                stateUI = Instantiate(stateUIPrefab).GetComponentInChildren<StateUI>();
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
                stateUI.gameObject.SetActive(true);
            }
        }
        
        public virtual void OnEndStateClient()
        {
            if (stateUI != null)
            {
                stateUI.gameObject.SetActive(false);
            }
        }

        public virtual void StateUpdate()
        {
            
        }

        public event Action OnStateStartEvent;
        public event Action OnStateEndEvent;
    }
}