using GameLogic;
using GameLogic.GameStates;
using UnityEngine;

public class StateUI : MonoBehaviour
{
    [HideInInspector] public GameManager gameManager;
    [HideInInspector] public GameState owningGameState;
    
    public void SetupStateUI(GameManager gameManager, GameState gameState)
    {
        this.gameManager = gameManager;
        this.owningGameState = gameState;
    }
}
