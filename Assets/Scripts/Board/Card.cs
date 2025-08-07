#region

using System;
using System.Collections.Generic;
using AudioSystem;
using AYellowpaper.SerializedCollections;
using Board.UI.VoteCanvas;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using GameLogic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

#endregion

public class Card : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [FormerlySerializedAs("cardName")] public TMP_Text cardPlayerPseudo;
    public TMP_Text cardRoleText;
    public TMP_Text powerText;

    public Transform cardScalerTransform;
    public Transform cardDisplacerTransform;
    public Transform cardPivotTransform;
    
    public Image cardImage;
    public Image factionLogoImage;
    public Image factionLogoBackgroundImage;

    public VoteCanvas voteCanvas;
    public CanvasGroup chainedOverlay;
 
    [Header("Info")]
    [SerializeField] private Sprite unknownCardSprite;
    [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogo;
    [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogoBackground;
    [HideInInspector] public Character characterInfo;
    [HideInInspector] public Role roleInfo;

    [Header("Animation values")]
    public float hoverZoom = 1.15f;
    public float rotateTime = 1;
    public float chainFadeTime = 0.5f;
    
    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;

    private System.Threading.CancellationTokenSource showPseudoCts;

    private bool isSubscribedToUpdate = false;
    private bool lastIsChainedStatus = false;

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
        if (!isSubscribedToUpdate)
        {
            isSubscribedToUpdate = true;
            GameManager.instance.onCharactersListUpdated += UpdateInfo;
        }
    }

    private void UpdateInfo(List<Character> _characters)
    {
        characterInfo = _characters.Find(_character => _character.ownerClientId == characterInfo.ownerClientId);
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
        showPseudoCts?.Cancel();
        showPseudoCts = new System.Threading.CancellationTokenSource();
        var _cancellationToken = showPseudoCts.Token;
        try
        {
            if (_turnCard)
            {
                await ShowBackSide().AttachExternalCancellation(_cancellationToken);
            }
            cardPlayerPseudo.text = characterInfo.GetOwnerPseudo();
            if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId).isRoleRevealed > 0)
            {
                cardRoleText.text = roleInfo.roleName.ToString();
                factionLogoImage.sprite = factionLogo[roleInfo.factionType];
                factionLogoBackgroundImage.sprite = factionLogoBackground[roleInfo.factionType];
                ShowPowers();
                cardImage.sprite = await roleInfo.GetRolePortrait().AttachExternalCancellation(_cancellationToken);
            }
            else
            {
                cardRoleText.text = "";
                cardImage.sprite = unknownCardSprite;
                factionLogoImage.sprite = factionLogo[FactionType.unknown];
                factionLogoBackgroundImage.sprite = factionLogoBackground[FactionType.unknown];
            }
            if (_turnCard)
            {
                await ShowFrontSide().AttachExternalCancellation(_cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void CancelShowPseudoWithRevealedInfo()
    {
        showPseudoCts?.Cancel();
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

    public void UpdateChainOverlay(bool _instant = false)
    {
        SetChainedOverlay(characterInfo.isChained, _instant);
    }
    
    public void SetChainedOverlay(bool _isChained, bool _instant = false)
    {
        if (lastIsChainedStatus == _isChained)
        {
            return;
        }
        
        chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : chainFadeTime);
        if (_isChained && !_instant)
        {
            GameAudioManager.instance.PlayOneShot(characterInfo.role.onChainingSound.GetPath());
        }
        lastIsChainedStatus = _isChained;
    }

    public async UniTask ShowBackSide(bool _isInstant = false)
    {
        var _rotateTime = (_isInstant) ? 0 : rotateTime;
        if (Mathf.Approximately(Mathf.Abs(cardPivotTransform.eulerAngles.z), 180)) // Already on back side
        {
            return;
        }

        if (_isInstant)
        {
            cardPivotTransform.eulerAngles = new Vector3(0, 0, 180);
        }
        else
        {
            cardDisplacerTransform.DOLocalMoveY(4, _rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                cardDisplacerTransform.DOLocalMoveY(0, _rotateTime / 2f).SetEase(Ease.OutQuint);
            };
            cardPivotTransform.DORotate(new Vector3(0, 0, -180), _rotateTime * 0.75f);
            await UniTask.Delay(TimeSpan.FromSeconds(_rotateTime));
        }
    }
    
    public async UniTask ShowFrontSide(bool _isInstant = false)
    {
        var _rotateTime = (_isInstant) ? 0 : rotateTime;
        if (Mathf.Approximately(Mathf.Abs(cardPivotTransform.eulerAngles.z), 0)) // Already front side
        {
            return;
        }

        if (_isInstant)
        {
            cardPivotTransform.eulerAngles = Vector3.zero;
        }
        else
        {
            cardDisplacerTransform.DOLocalMoveY(4, _rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                cardDisplacerTransform.DOLocalMoveY(0, _rotateTime / 2f).SetEase(Ease.OutQuint);
            };
            cardPivotTransform.DORotate(Vector3.zero, _rotateTime * 0.75f);
            await UniTask.Delay(TimeSpan.FromSeconds(_rotateTime));
        }
    }

    public void OnPointerClick(PointerEventData _eventData)
    {
        onCardClicked?.Invoke(this);
        cardScalerTransform.DOKill(true);
        cardScalerTransform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
    }

    public void OnPointerEnter(PointerEventData _eventData)
    {
        onCardHovered?.Invoke(this);
        cardScalerTransform.DOKill();
        cardScalerTransform.DOScale(Vector3.one * hoverZoom, 0.35f).SetEase(Ease.OutQuint);
    }

    public void OnPointerExit(PointerEventData _eventData)
    {
        onCardUnhovered?.Invoke(this);
        cardScalerTransform.DOKill();
        cardScalerTransform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutQuint);
    }
}
