#region

using Characters;
using DG.Tweening;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

public class StateUI : NetworkBehaviour
{
    [HideInInspector] public GameManager gameManager;
    // Story 8.3 (Epic 8 / D2): loop-command slice of the pushed gameManager, narrowed to IGameLoop
    // (D-NFR6). StateUI subclasses observe loop events through Loop; the concrete field stays for
    // anything else they read off the manager.
    protected IGameLoop Loop => gameManager;
    // Story 7.2 lane B: set by GameState.OnStateCreated from the state's injected characterManager.
    [HideInInspector] public CharacterManager characterManager;
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
