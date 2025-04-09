using System;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
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
        if (characterInfo.isChained)
        {
            ShowPseudoWithRole();
        }
        else
        {
            ShowPseudoOnly();
        }
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
        cardName.text = $"{characterInfo.GetOwnerPseudo()}\n{roleInfo.roleName}";
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
