using System;
using Characters;
using Characters.Powers;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Card : MonoBehaviour
{
    public TMP_Text cardName;
    public TMP_Text powerText;
    
    public Image cardImage;

    public CanvasGroup chainedOverlay;
 
    [Header("Info")]
    public Sprite unknownCardSprite;
    [HideInInspector] public Character characterInfo;
    [HideInInspector] public Role roleInfo;

    private void Awake()
    {
        if (characterInfo == null)
        {
            SetUnknownCard();
        }
    }

    public void SetInfo(Character _character)
    {
        characterInfo = _character;
        roleInfo = characterInfo.GetRole();
        ShowPseudoOnly();
    }


    #region TextInfo Methods

    private void ShowPowers()
    {
        foreach (Power _power in roleInfo.powers)
        {
            //todo: draw all powers
            powerText.text = _power.powerName.ToString();
        }
    }

    public void ShowRoleOnly()
    {
        cardName.text = roleInfo.roleName.ToString();
        ShowPowers();
    }

    public void ShowPseudoWithRole()
    {
        cardName.text = roleInfo.roleName + " (" + characterInfo.GetOwnerPseudo() + ")";
        ShowPowers();
    }

    public void ShowPseudoOnly()
    {
        SetUnknownCard();
        cardName.text = characterInfo.GetOwnerPseudo();
    }
    
    public void SetUnknownCard()
    {
        cardName.text = "???";
        powerText.text = "???";
        cardImage.sprite = unknownCardSprite;
    }

    #endregion
    
    public void SetChainedOverlay(bool _isChained, bool _instant = false)
    {
        chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : 0.5f);
    }
}
