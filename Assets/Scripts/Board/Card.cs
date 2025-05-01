using System;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Card : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
    
    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;

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
    
    public async UniTask ShowPseudoWithRevealedInfo(bool _turnCard = false)
    {
        if (_turnCard)
        {
            await ShowBackSide();
        }
        
        cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
        if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId).isRoleRevealed > 0)
        {
            cardRoleText.text = roleInfo.roleName.ToString();
            ShowPowers();
            cardImage.sprite = await roleInfo.GetRolePortrait();
        }
        else
        {
            cardRoleText.text = "";
            cardImage.sprite = unknownCardSprite;
        }
        
        if (_turnCard)
        {
            await ShowFrontSide();
        }
    }
    
    public async UniTask ShowRoleWithRevealedInfo()
    {
        cardPlayerPseudo.text = "";
        if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId).isRoleRevealed > 0)
        {
            cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
        }
        cardRoleText.text = roleInfo.roleName.ToString();
        ShowPowers();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
    }

    public async UniTask ShowRoleOnly()
    {
        cardPlayerPseudo.text = "";
        cardRoleText.text = roleInfo.roleName.ToString();
        ShowPowers();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
    }

    public async UniTask ShowPseudoWithRole()
    {
        cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
        cardRoleText.text = roleInfo.roleName.ToString();
        ShowPowers();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
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

    public void OnPointerClick(PointerEventData _eventData)
    {
        onCardClicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData _eventData)
    {
        onCardHovered?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData _eventData)
    {
        onCardUnhovered?.Invoke(this);
    }
}
