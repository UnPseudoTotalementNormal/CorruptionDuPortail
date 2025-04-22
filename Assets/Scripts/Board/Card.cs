using System;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Card : MonoBehaviour
{
    [FormerlySerializedAs("cardName")] public TMP_Text cardPlayerPseudo;
    public TMP_Text cardRoleText;
    public TMP_Text powerText;
    
    public Image cardImage;

    public CanvasGroup chainedOverlay;
 
    [Header("Info")]
    public Sprite unknownCardSprite;
    [HideInInspector] public Character characterInfo;
    [HideInInspector] public Role roleInfo;

    [Header("Animation values")]
    public float rotateTime = 1;
    public float chainFadeTime = 0.5f;

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
        SetChainedOverlay(characterInfo.isChained, true);
    }
    
    #region Info Methods

    private void ShowPowers()
    {
        foreach (Power _power in roleInfo.powers)
        {
            //todo: draw all powers
            powerText.text = _power.powerName.ToString();
        }
    }

    public async UniTask ShowRoleOnly()
    {
        cardPlayerPseudo.text = "";
        cardRoleText.text = roleInfo.roleName.ToString();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
        ShowPowers();
    }
    
    public async UniTask ShowPseudoWithRevealedInfo()
    {
        cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
        if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId).isRoleRevealed > 0)
        {
            cardRoleText.text = roleInfo.roleName.ToString();
            cardImage.sprite = await roleInfo.GetRolePortrait();
            ShowPowers();
        }
        else
        {
            cardRoleText.text = "???";
            cardImage.sprite = unknownCardSprite;
        }
    }

    public async UniTask ShowPseudoWithRole()
    {
        cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
        cardRoleText.text = roleInfo.roleName.ToString();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
        ShowPowers();
    }

    public void ShowPseudoOnly()
    {
        SetUnknownCard();
        cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
    }
    
    public void SetUnknownCard()
    {
        cardPlayerPseudo.text = "";
        cardRoleText.text = "";
        powerText.text = "";
        cardImage.sprite = unknownCardSprite;
    }

    #endregion
    
    public void SetChainedOverlay(bool _isChained, bool _instant = false)
    {
        chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : chainFadeTime);
    }

    public async UniTask ShowBackSide()
    {
        transform.DOLocalMoveY(4, rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
        {
            transform.DOLocalMoveY(0, rotateTime / 2f).SetEase(Ease.OutQuint);
        };
        transform.DORotate(new Vector3(0, 0, -180), rotateTime * 0.75f);
        await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
    }
    
    public async UniTask ShowFrontSide()
    {
        transform.DOLocalMoveY(4, rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
        {
            transform.DOLocalMoveY(0, rotateTime / 2f).SetEase(Ease.OutQuint);
        };
        transform.DORotate(Vector3.zero, rotateTime * 0.75f);
        await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
    }
}
