using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

public class StateUI : NetworkBehaviour
{
    [HideInInspector] public GameManager gameManager;
    [HideInInspector] public GameState owningGameState;

    [SerializeField] private CanvasGroup canvasGroup;
    
    public void SetupStateUI(GameManager gameManager, GameState gameState)
    {
        this.gameManager = gameManager;
        this.owningGameState = gameState;
    }
    
    public virtual void ShowStateUI(bool instant = false)
    {
        if (instant)
        {
            canvasGroup.alpha = 1;
        }
        else
        {
            canvasGroup.DOFade(1, 0.5f);
        }

        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

    }
    
    public virtual void HideStateUI(bool instant = false)
    {
        if (instant)
        {
            canvasGroup.alpha = 0;
        }
        else
        {
            canvasGroup.DOFade(0, 0.5f);
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
