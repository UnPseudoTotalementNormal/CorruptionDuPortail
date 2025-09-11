#region

using DG.Tweening;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

public class StateUI : NetworkBehaviour
{
    [HideInInspector] public GameManager gameManager;
    [HideInInspector] public GameState owningGameState;

    [SerializeField] public CanvasGroup canvasGroup;
    
    [SerializeField] public bool instantShowOnStateStart = false;
    
    public virtual void SetupStateUI(GameManager gameManager, GameState gameState)
    {
        this.gameManager = gameManager;
        owningGameState = gameState;
        owningGameState.onStateStartClient += OnStateStart;
        owningGameState.onStateEndClient += OnStateEnd;
    }

    protected virtual void OnStateStart()
    {
        
    }
    
    protected virtual void OnStateEnd()
    {
        
    }

    public virtual void ShowStateUI(bool instant = false)
    {
        canvasGroup.DOKill(true);
        
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
        canvasGroup.DOKill(true);
        
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

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (owningGameState != null)
        {
            owningGameState.onStateStartClient -= OnStateStart;
            owningGameState.onStateEndClient -= OnStateEnd;
        }
    }
}
