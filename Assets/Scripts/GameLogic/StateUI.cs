using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

public class StateUI : NetworkBehaviour
{
    [HideInInspector] public GameManager gameManager;
    [HideInInspector] public GameState owningGameState;
    
    public void SetupStateUI(GameManager gameManager, GameState gameState)
    {
        this.gameManager = gameManager;
        this.owningGameState = gameState;
    }
    
    public virtual void ShowStateUI()
    {
        gameObject.SetActive(true);
    }
    
    public virtual void HideStateUI()
    {
        gameObject.SetActive(false);
    }
}
