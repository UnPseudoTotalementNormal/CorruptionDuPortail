#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.CardComponents;
using Board.UI.VoteCanvas;
using Characters;
using Cysharp.Threading.Tasks;
using GameLogic;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;

#endregion

namespace Board
{
    /// <summary>
    /// Main card class.
    /// Orchestrates the different components (visual, animation, sound) without containing their logic.
    /// </summary>
    public class Card : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Core References")]
        [SerializeField] private Canvas cardCanvas;
        [field:SerializeField] public Transform cardEffectsParent { get; private set; }
        [field:SerializeField] public VoteCanvas voteCanvas { get; private set; }

        [Header("Card Components")]
        [SerializeField] private CardVisualUpdater visualUpdater;
        [SerializeField] private CardAnimationHandler animationHandler;
        [SerializeField] private CardSoundHandler soundHandler;

        private PlaceCardSide placeCardSide = PlaceCardSide.Front;
        private bool isSubscribedToCharacter;
        private bool isPointerOver;
        private bool canShowBackInfo;
        
        private readonly CancellableTaskHandler showPseudoTaskHandler = new();

        public event Action<Card> onCardClicked;
        public event Action<Card> onCardHovered;
        public event Action<Card> onCardUnhovered;
        public event Action onCardStartMoving
        {
            add => animationHandler.onCardStartMoving += value;
            remove => animationHandler.onCardStartMoving -= value;
        }
        
        public event Action onCardStopMoving
        {
            add => animationHandler.onCardStopMoving += value;
            remove => animationHandler.onCardStopMoving -= value;
        }

        [HideInInspector] public Character characterInfo;
        [HideInInspector] public Role roleInfo;
        
        // Backwards compatibility properties - delegates to components
        public Transform cardScalerTransform => animationHandler.cardScalerTransform;
        public Transform cardDisplacerTransform => animationHandler.cardDisplacerTransform;
        public Transform cardPivotTransform => animationHandler.cardPivotTransform;
        public float rotateTime => animationHandler.rotateTime;

        #region Unity Lifecycle

        private void Awake()
        {
            SetCanShowBackInfo(true);
            SetPlaceSide(PlaceCardSide.Front);
            if (characterInfo == null)
            {
                visualUpdater.SetUnknown();
            }
        }

        private void Update()
        {
            if (isPointerOver || !animationHandler.isCardZoomed)
            {
                return;
            }

            if (!CanUnZoomCard())
            {
                return;
            }
            
            animationHandler.ZoomOut(cardCanvas, soundHandler.PlayUnhoverSound);
        }

        private void OnDestroy()
        {
            UnsubscribeFromCharacterEvents();
            showPseudoTaskHandler.Dispose();
        }

        #endregion

        #region Setup

        public void SetInfo(Character _character)
        {
            UnsubscribeFromCharacterEvents();
            characterInfo = _character;
            roleInfo = characterInfo.GetRole();
            visualUpdater.SetChainedOverlay(characterInfo.isChained.Value, true);
            SubscribeToCharacterEvents();
        }

        public void SetPlaceSide(PlaceCardSide _placeSide)
        {
            placeCardSide = _placeSide;
            
            if (placeCardSide == PlaceCardSide.Front)
            {
                visualUpdater.ShowFrontSideInfo(voteCanvas.transform, cardEffectsParent);
            }
            else if (canShowBackInfo)
            {
                visualUpdater.ShowBackSideInfo(voteCanvas.transform, cardEffectsParent);
            }
        }

        public void SetCanShowBackInfo(bool _shouldShowBackInfo)
        {
            canShowBackInfo = _shouldShowBackInfo;

            if (!canShowBackInfo)
            {
                visualUpdater.ShowFrontSideInfo(voteCanvas.transform, cardEffectsParent);
            }
            
            if (canShowBackInfo && placeCardSide == PlaceCardSide.Back)
            {
                visualUpdater.ShowBackSideInfo(voteCanvas.transform, cardEffectsParent);
            }
        }

        #endregion

        #region Info Display

        public async UniTask ShowPseudoWithRevealedInfo(bool _turnCard = false, bool _allowChangeSideInfo = true)
        {
            var _cancellationToken = showPseudoTaskHandler.GetNewToken();
            
            try
            {
                bool _isRevealed = (int)GameManager.instance.gameInfoRevealer
                    .GetCharacterInfo(characterInfo.ownerClientId.Value).isRoleRevealed > 0;

                if (_isRevealed)
                {
                    await ShowRevealedCard(_turnCard, _cancellationToken);
                }
                else
                {
                    await ShowUnrevealedCard(_turnCard, _allowChangeSideInfo, _cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        public void CancelShowPseudoWithRevealedInfo()
        {
            showPseudoTaskHandler.Cancel();
        }

        public async UniTask ShowRoleWithRevealedInfo()
        {
            visualUpdater.SetPseudo("");
            
            bool _isRevealed = (int)GameManager.instance.gameInfoRevealer
                .GetCharacterInfo(characterInfo.ownerClientId.Value).isRoleRevealed > 0;
            
            if (_isRevealed)
            {
                visualUpdater.SetPseudo(characterInfo.GetOwnerPseudo());
            }
            
            visualUpdater.SetRoleText(roleInfo.roleName.ToString());
            await visualUpdater.SetRolePortrait(roleInfo);
        }

        public async UniTask ShowRoleOnly()
        {
            visualUpdater.SetPseudo("");
            visualUpdater.SetRoleText(roleInfo.roleName.ToString());
            await visualUpdater.SetRolePortrait(roleInfo);
        }

        public async UniTask ShowPseudoWithRole()
        {
            visualUpdater.SetPseudo(characterInfo.GetOwnerPseudo());
            visualUpdater.SetRoleText(roleInfo.roleName.ToString());
            await visualUpdater.SetRolePortrait(roleInfo);
        }

        public void ShowPseudoOnly()
        {
            visualUpdater.SetUnknownWithPseudo(characterInfo.GetOwnerPseudo());
        }

        public void SetUnknownCard()
        {
            visualUpdater.SetUnknown();
        }

        #endregion

        #region Chain Overlay

        public void UpdateChainOverlay(bool _instant = false)
        {
            visualUpdater.SetChainedOverlay(characterInfo.isChained.Value, _instant);
        }

        public void SetChainedOverlay(bool _isChained, bool _instant = false)
        {
            visualUpdater.SetChainedOverlay(_isChained, _instant);
        }

        #endregion

        #region Card Flip

        public async UniTask ShowBackSide(bool _isInstant = false)
        {
            await animationHandler.FlipToBack(_isInstant, soundHandler.PlayFlipSound);
        }

        public async UniTask ShowFrontSide(bool _isInstant = false)
        {
            await animationHandler.FlipToFront(_isInstant, soundHandler.PlayUnflipSound);
        }

        #endregion

        #region Pointer Event Handlers

        public void OnPointerClick(PointerEventData _eventData)
        {
            onCardClicked?.Invoke(this);
            animationHandler.PunchScale();
            soundHandler.PlayClickSound();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            onCardHovered?.Invoke(this);
            animationHandler.ZoomIn(cardCanvas, soundHandler.PlayHoverSound);
            isPointerOver = true;
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            onCardUnhovered?.Invoke(this);
            
            if (CanUnZoomCard())
            {
                animationHandler.ZoomOut(cardCanvas, soundHandler.PlayUnhoverSound);
            }
            
            isPointerOver = false;
        }

        #endregion

        #region Character Events

        private void SubscribeToCharacterEvents()
        {
            if (characterInfo == null || isSubscribedToCharacter) return;
            
            characterInfo.onRoleUpdated += OnRoleUpdated;
            characterInfo.isChained.OnValueChanged += OnChainedChanged;
            isSubscribedToCharacter = true;
        }

        private void UnsubscribeFromCharacterEvents()
        {
            if (characterInfo == null || !isSubscribedToCharacter) return;
            
            characterInfo.onRoleUpdated -= OnRoleUpdated;
            characterInfo.isChained.OnValueChanged -= OnChainedChanged;
            isSubscribedToCharacter = false;
        }

        private void OnRoleUpdated()
        {
            roleInfo = characterInfo.GetRole();
        }

        private void OnChainedChanged(bool _previous, bool _current)
        {
            visualUpdater.SetChainedOverlay(_current, false);
        }

        #endregion

        private bool CanUnZoomCard()
        {
            if (!animationHandler.CanUnZoom())
            {
                return false;
            }
            return !GetComponentsInChildren<IPanelOpen>().Any(_ip => _ip.isPanelOpen);
        }

        private async UniTask ShowRevealedCard(bool _turnCard, System.Threading.CancellationToken _cancellationToken)
        {
            SetPlaceSide(PlaceCardSide.Front);
            
            if (_turnCard)
            {
                await ShowBackSide().AttachExternalCancellation(_cancellationToken);
            }
            
            visualUpdater.SetPseudo(characterInfo.GetOwnerPseudo());
            visualUpdater.SetRoleText(roleInfo.roleName.ToString());
            visualUpdater.SetFaction(roleInfo.factionType);
            await visualUpdater.SetRolePortrait(roleInfo).AttachExternalCancellation(_cancellationToken);
            
            if (_turnCard)
            {
                await ShowFrontSide().AttachExternalCancellation(_cancellationToken);
            }
        }

        private async UniTask ShowUnrevealedCard(bool _turnCard, bool _allowChangeSideInfo, System.Threading.CancellationToken _cancellationToken)
        {
            if (_allowChangeSideInfo)
            {
                SetPlaceSide(PlaceCardSide.Back);
            }
            
            visualUpdater.SetUnknownWithPseudo(characterInfo.GetOwnerPseudo());
            visualUpdater.SetRoleText("");
            
            if (_turnCard)
            {
                await ShowBackSide().AttachExternalCancellation(_cancellationToken);
            }
        }

        public enum PlaceCardSide
        {
            Front, // used when the role is discovered
            Back   // used when the role is not discovered
        }
    }
}
