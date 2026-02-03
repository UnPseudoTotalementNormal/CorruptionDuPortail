#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using AYellowpaper.SerializedCollections;
using Board.UI.VoteCanvas;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Extensions;
using FMODUnity;
using GameLogic;
using TMPro;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

#endregion

public class Card : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Canvas cardCanvas;
    

    public Transform cardScalerTransform;
    public Transform cardDisplacerTransform;
    public Transform cardPivotTransform;
    public Transform cardEffectsParent;
    
    public VoteCanvas voteCanvas;
    
    [Header("Front Side References")]
    public List<CanvasGroup> objectsToShowOnFrontSidePlacementOnly = new();
    [FormerlySerializedAs("cardName")] public TMP_Text cardPlayerPseudo;
    public TMP_Text cardRoleText;
    public CanvasGroup chainedOverlay;
    public Image cardImage;
    public Image factionLogoImage;
    public Image factionLogoBackgroundImage;
    public Image unknownFogOverlay;
    
    [Header("Back Side References")]
    public List<CanvasGroup> objectsToShowOnBackSidePlacementOnly = new();
    public TMP_Text bsCardPlayerPseudo;
    public Image bsFactionLogoImage;
 
    [Header("Info")]
    [SerializeField] private Sprite unknownCardSprite;
    [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogo;
    [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogoBackground;
    [HideInInspector] public Character characterInfo;
    [HideInInspector] public Role roleInfo;

    public PlaceCardSide placeCardSide = PlaceCardSide.Front;
    private bool isSubscribedToCharacter = false;

    [Header("Animation values")]
    public float hoverZoom = 1.15f;
    public float rotateTime = 1;
    public float chainFadeTime = 0.5f;
    
    public event Action<Card> onCardClicked;
    public event Action<Card> onCardHovered;
    public event Action<Card> onCardUnhovered;

    private System.Threading.CancellationTokenSource showPseudoCts;

    private bool lastIsChainedStatus = false;
    private float lastZoomStartTime = 0;
    
    private bool isPointerOver = false;
    private bool isCardZoomed = false;

    private bool canShowBackInfo = false;
    
    [Header("Sounds")]
    [SerializeField] private EventReference cardFlipSound;
    [SerializeField] private EventReference cardUnflipSound;
    [SerializeField] private EventReference cardHoverSound;
    [SerializeField] private EventReference cardUnhoverSound;
    [SerializeField] private EventReference cardClickSound;
    
    private void Awake()
    {
        SetCanShowBackInfo(true);
        SetPlaceSide(PlaceCardSide.Front);
        if (characterInfo == null)
        {
            SetUnknownCard();
        }
    }

    private void Update()
    {
        if (isPointerOver || !isCardZoomed)
        {
            return;
        }

        if (!CanUnZoomCard())
        {
            return;
        }
        OnUnOverZoom();
    }

    private bool CanUnZoomCard()
    {
        if (Time.time - lastZoomStartTime < 0.15f)
        {
            return false;
        }
        return !GetComponentsInChildren<IPanelOpen>().Any(_ip => _ip.isPanelOpen);
    }

    public void SetInfo(Character _character)
    {
        UnsubscribeFromCharacterEvents();
        characterInfo = _character;
        roleInfo = characterInfo.GetRole();
        SetChainedOverlay(characterInfo.isChained.Value, true);
        SubscribeToCharacterEvents();
    }

    public void SetPlaceSide(PlaceCardSide _placeSide)
    {
        placeCardSide = _placeSide;
        
        if (placeCardSide == PlaceCardSide.Front)
        {
            ShowInfoOnFrontSide();
        }
        else
        {
            if (canShowBackInfo)
            {
                ShowInfoOnBackSide();
            }
        }
    }
    
    public void SetCanShowBackInfo(bool _shouldShowBackInfo)
    {
        canShowBackInfo = _shouldShowBackInfo;

        if (!canShowBackInfo)
        {
            ShowInfoOnFrontSide();
        }
        
        if (canShowBackInfo && placeCardSide == PlaceCardSide.Back)
        {
            ShowInfoOnBackSide();
        }
    }

    private void ShowInfoOnFrontSide()
    {
        voteCanvas.transform.localRotation = Quaternion.Euler(0, 0, 0);
        cardEffectsParent.localRotation = Quaternion.Euler(0, 0, 0);
        
        foreach (CanvasGroup _canvasGroup in objectsToShowOnFrontSidePlacementOnly)
        {
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(1, 0.25f);
        }
        
        foreach (CanvasGroup _canvasGroup in objectsToShowOnBackSidePlacementOnly)
        {
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(0, 0.25f);
        }
    }

    private void ShowInfoOnBackSide()
    {
        voteCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);
        cardEffectsParent.localRotation = Quaternion.Euler(0, 0, 180);
        
        foreach (CanvasGroup _canvasGroup in objectsToShowOnFrontSidePlacementOnly)
        {
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(0, 0.25f);
        }
        
        foreach (CanvasGroup _canvasGroup in objectsToShowOnBackSidePlacementOnly)
        {
            _canvasGroup.DOKill();
            _canvasGroup.DOFade(1, 0.25f);
        }
    }

    private void SubscribeToCharacterEvents()
    {
        if (characterInfo == null || isSubscribedToCharacter) return;
        characterInfo.onRoleUpdated += UpdateInfoFromCharacter;
        characterInfo.isChained.OnValueChanged += OnChainedChanged;
        
        isSubscribedToCharacter = true;
    }

    private void UnsubscribeFromCharacterEvents()
    {
        if (characterInfo == null || !isSubscribedToCharacter) return;
        characterInfo.onRoleUpdated -= UpdateInfoFromCharacter;
        characterInfo.isChained.OnValueChanged -= OnChainedChanged;
        
        isSubscribedToCharacter = false;
    }

    private void OnDestroy()
    {
        UnsubscribeFromCharacterEvents();
    }

    private void UpdateInfoFromCharacter()
    {
        roleInfo = characterInfo.GetRole();
    }

    private void OnChainedChanged(bool _previous, bool _current)
    {
        SetChainedOverlay(_current, false);
    }

    #region Info Methods
    
    public async UniTask ShowPseudoWithRevealedInfo(bool _turnCard = false, bool _allowChangeSideInfo = true)
    {
        showPseudoCts?.Cancel();
        showPseudoCts = new System.Threading.CancellationTokenSource();
        var _cancellationToken = showPseudoCts.Token;
        try
        {
            // If the card is revealed, we can turn it
            if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId.Value).isRoleRevealed > 0)
            {
                SetPlaceSide(PlaceCardSide.Front);
                if (_turnCard)
                {
                    await ShowBackSide().AttachExternalCancellation(_cancellationToken);
                }
                
                SetCardPseudo( characterInfo.GetOwnerPseudo());
                cardRoleText.text = roleInfo.roleName.ToString();
                UpdateFaction(roleInfo.factionType);
                cardImage.sprite = await roleInfo.GetRolePortrait().AttachExternalCancellation(_cancellationToken);
                
                if (_turnCard)
                {
                    await ShowFrontSide().AttachExternalCancellation(_cancellationToken);
                }
            }
            else // If not revealed, show the back only
            {
                if (_allowChangeSideInfo)
                {
                    SetPlaceSide(PlaceCardSide.Back);
                }
                SetCardPseudo(characterInfo.GetOwnerPseudo());
                cardRoleText.text = "";
                cardImage.sprite = unknownCardSprite;
                UpdateFaction(FactionType.unknown);
                if (_turnCard)
                {
                    await ShowBackSide().AttachExternalCancellation(_cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    
    private void SetCardPseudo(string _pseudo)
    {
        cardPlayerPseudo.text = _pseudo;
        bsCardPlayerPseudo.text = _pseudo;
    }

    private void UpdateFaction(FactionType _factionType)
    {
        factionLogoImage.sprite = factionLogo[_factionType];
        bsFactionLogoImage.sprite = factionLogoImage.sprite;
        factionLogoBackgroundImage.sprite = factionLogoBackground[_factionType];
        bool _factionActive = factionLogoImage.sprite != null;
        factionLogoImage.gameObject.SetActive(_factionActive);
        bsFactionLogoImage.gameObject.SetActive(_factionActive);
        factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
        unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
    }

    public void CancelShowPseudoWithRevealedInfo()
    {
        showPseudoCts?.Cancel();
    }

    public async UniTask ShowRoleWithRevealedInfo()
    {
        SetCardPseudo("");
        if ((int)GameManager.instance.gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId.Value).isRoleRevealed > 0)
        {
            SetCardPseudo( characterInfo.GetOwnerPseudo());
        }
        cardRoleText.text = roleInfo.roleName.ToString();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
    }

    public async UniTask ShowRoleOnly()
    {
        SetCardPseudo("");
        cardRoleText.text = roleInfo.roleName.ToString();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
    }

    public async UniTask ShowPseudoWithRole()
    {
        SetCardPseudo( characterInfo.GetOwnerPseudo());
        cardRoleText.text = roleInfo.roleName.ToString();
        cardImage.sprite = await roleInfo.GetRolePortrait();;
    }

    public void ShowPseudoOnly()
    {
        SetUnknownCard();
        SetCardPseudo( characterInfo.GetOwnerPseudo());
    }
    
    public void SetUnknownCard()
    {
        SetCardPseudo("");
        cardRoleText.text = "";
        cardImage.sprite = unknownCardSprite;
    }

    #endregion

    public void UpdateChainOverlay(bool _instant = false)
    {
        SetChainedOverlay(characterInfo.isChained.Value, _instant);
    }
    
    public void SetChainedOverlay(bool _isChained, bool _instant = false)
    {
        if (lastIsChainedStatus == _isChained)
        {
            return;
        }
        
        chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : chainFadeTime);
        lastIsChainedStatus = _isChained;
    }

    public async UniTask ShowBackSide(bool _isInstant = false)
    {
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
            cardFlipSound.TryPlayOneShot();
            cardDisplacerTransform.DOLocalMoveY(4, rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                cardDisplacerTransform.DOLocalMoveY(0, rotateTime / 2f).SetEase(Ease.OutQuint);
            };
            cardPivotTransform.DORotate(new Vector3(0, 0, -180), rotateTime * 0.75f);
            await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
        }
    }
    
    public async UniTask ShowFrontSide(bool _isInstant = false)
    {
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
            cardUnflipSound.TryPlayOneShot();
            cardDisplacerTransform.DOLocalMoveY(4, rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                cardDisplacerTransform.DOLocalMoveY(0, rotateTime / 2f).SetEase(Ease.OutQuint);
            };
            cardPivotTransform.DORotate(Vector3.zero, rotateTime * 0.75f);
            await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
        }
    }

    public void OnPointerClick(PointerEventData _eventData)
    {
        onCardClicked?.Invoke(this);
        cardScalerTransform.DOKill(true);
        cardScalerTransform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
        cardClickSound.TryPlayOneShot();
    }

    public void OnPointerEnter(PointerEventData _eventData)
    {
        onCardHovered?.Invoke(this);
        OnOverZoom();
        isPointerOver = true;
    }
    

    public void OnPointerExit(PointerEventData _eventData)
    {
        onCardUnhovered?.Invoke(this);
        
        if (CanUnZoomCard())
        {
            OnUnOverZoom();
        }
        
        isPointerOver = false;
    }
    
    private void OnOverZoom()
    {
        if (isCardZoomed)
        {
            return;
        }
        
        cardScalerTransform.DOKill();
        cardScalerTransform.DOScale(Vector3.one * hoverZoom, 0.35f).SetEase(Ease.OutQuint);
        cardDisplacerTransform.DOKill();
        cardDisplacerTransform.DOLocalMoveY(0.35f, 0.35f).SetEase(Ease.OutQuint);
        
        lastZoomStartTime = Time.time;
        
        isCardZoomed = true;
        
        cardCanvas.sortingOrder += 1;
        
        cardHoverSound.TryPlayOneShot();
    }

    private void OnUnOverZoom()
    {
        if (!isCardZoomed)
        {
            return;
        }
        
        cardScalerTransform.DOKill();
        cardScalerTransform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutQuint);
        cardDisplacerTransform.DOKill();
        cardDisplacerTransform.DOLocalMoveY(0, 0.35f).SetEase(Ease.OutQuint);
        
        isCardZoomed = false;
        
        cardCanvas.sortingOrder -= 1;
        
        cardUnhoverSound.TryPlayOneShot();
    }

    public enum PlaceCardSide
    {
        Front, // used when the role is discovered,
        Back   // used when the role is not discovered
    }
}
