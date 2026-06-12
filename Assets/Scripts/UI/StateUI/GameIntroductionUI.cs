using System;
using AYellowpaper.SerializedCollections;
using Characters;
using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;

public class GameIntroductionUI : StateUI
{
    [HideInInspector] public GameIntroductionState gameIntroductionState;
    
    public TMP_Text preRoleText;
    public TMP_Text roleText;
    
    public float preRoleShowDuration = 0.5f;
    public float roleShowDuration = 0.5f;
    
    public SerializedDictionary<FactionType, Color> factionColors = new()
    {
        { FactionType.anomaly, new Color() },
        { FactionType.chosen, new Color() },
        { FactionType.marginal, new Color() }
    };

    public override void SetupStateUI(GameManager gameManager, GameState gameState)
    {
        base.SetupStateUI(gameManager, gameState);
        gameIntroductionState = (GameIntroductionState)owningGameState;
        
        gameIntroductionState.onPreRoleTextShown += ShowPreRoleText;
        gameIntroductionState.onRoleTextShown += ShowRoleText;
    }
    
    protected override void OnStateStart()
    {
        base.OnStateStart();

        preRoleText.alpha = 0;
        roleText.alpha = 0;
    }

    private void ShowPreRoleText()
    {
        preRoleText.DOFade(1, preRoleShowDuration);
    }
    
    private void ShowRoleText()
    {
        var _localCharacter = CharacterQuery.GetLocalCharacter(false);
        roleText.text = _localCharacter.role.roleName.ToString();
        roleText.color = factionColors[_localCharacter.role.factionType];
        roleText.alpha = 0;
        
        roleText.transform.localScale = Vector3.one * 0.5f;

        roleText.transform.DOScale(Vector3.one, roleShowDuration).SetEase(Ease.OutQuint);
        roleText.DOFade(1, roleShowDuration);
    }
}
