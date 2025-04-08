using System;
using Characters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Card : MonoBehaviour
{
    public TMP_Text cardName;
    public TMP_Text powerText;
    
    public Image cardImage;
 
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
        powerText.text = roleInfo.powers[0].powerName.ToString();
    }

    public void ShowRoleOnly()
    {
        cardName.text = roleInfo.roleName.ToString();
    }

    public void ShowPseudoWithRole()
    {
        cardName.text = roleInfo.roleName + " (" + characterInfo.GetOwnerPseudo() + ")";
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
}
