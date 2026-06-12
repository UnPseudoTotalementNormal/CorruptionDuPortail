#region

using System;
using System.Collections.Generic;
using Board.CardComponents;
using Board.UI.VoteCanvas;
using Characters;
using Cysharp.Threading.Tasks;
using GameLogic;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

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
        [field:SerializeField] public VoteCanvas voteCanvas { get; private set; }

        
        [Header("Card Components")]
        [field:SerializeField] public CardVisualComponents visualComponents { get; private set; }
        [SerializeReference, SerializeField] private ICardDisplay visualUpdater = new CardPlayerVisualUpdater();
        [SerializeReference, SerializeField] private ICardAnimation animationHandler = new CardPlayerAnimation();
        [SerializeField] private CardSoundHandler soundHandler;


        private PlaceCardSide placeCardSide = PlaceCardSide.Front;
        private bool isSubscribedToCharacter;
        public bool isPointerOver { get; private set; }
        private bool canShowBackInfo;
        
        private readonly CancellableTaskHandler showPseudoTaskHandler = new();

        public event Action<Character> onCardSetInfo;
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

        // Story 7.2 lane B: pushed by BoardManager.AddNewCard (the sole creator). A card is
        // prefab-instantiated and its CharacterManager target is a scene object, so neither lane A
        // ([SerializeField] can't ref a scene object from a prefab) nor lane C (not NGO-spawned)
        // applies — the creator injects it, and the local-identity subscription is deferred from
        // Awake to Initialize so the dependency is available when it is used.
        private CharacterManager characterManager;
        // Story 7.3: GameInfoRevealer pushed by the same lane-B creator.
        private GameInfoRevealer gameInfoRevealer;
        // Story 7.4: child components on this card prefab (e.g. CardCorruptedText) read the revealer
        // from their parent Card instead of hub-hopping through GameManager.gameInfoRevealer.
        public GameInfoRevealer GameInfoRevealer => gameInfoRevealer;

        // Assumption: child IPanelOpen set is fixed at Awake (no panels instantiated/added to the card hierarchy at runtime).
        private IPanelOpen[] panelOpenComponents;

        #region Unity Lifecycle

        private void Awake()
        {
            if (visualUpdater != null && visualComponents != null)
            {
                visualUpdater.Initialize(visualComponents);
            }
            
            if (animationHandler != null && visualComponents != null)
            {
                animationHandler.Initialize(visualComponents);
            }
            
            SetCanShowBackInfo(true);
            SetPlaceSide(placeCardSide);
            if (characterInfo == null && visualUpdater != null)
            {
                visualUpdater.SetUnknown();
            }

            panelOpenComponents = GetComponentsInChildren<IPanelOpen>(true);
        }

        // Lane B injection point (BoardManager.AddNewCard). Carries the deferred local-identity
        // subscription that used to live in Awake on the manager instance facade.
        public void Initialize(CharacterManager _characterManager, GameInfoRevealer _gameInfoRevealer)
        {
            characterManager = _characterManager;
            gameInfoRevealer = _gameInfoRevealer;
            if (characterManager != null)
            {
                characterManager.onLocalIdentityChanged += OnLocalIdentityChanged;
            }
        }

        private void OnLocalIdentityChanged()
        {
            if (characterInfo == null) return;
            
            _ = ShowPseudoWithRevealedInfo(true);
        }

        private void Update()
        {
            if (animationHandler == null) return;
            
            animationHandler.Update();
            
            if (isPointerOver || !animationHandler.isCardZoomed)
            {
                return;
            }

            if (!CanUnZoomCard())
            {
                return;
            }
            
            animationHandler.OnUnHover(cardCanvas, soundHandler != null ? soundHandler.PlayUnhoverSound : null);
        }

        private void OnDestroy()
        {
            UnsubscribeFromCharacterEvents();
            if (characterManager != null)
            {
                characterManager.onLocalIdentityChanged -= OnLocalIdentityChanged;
            }
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
            onCardSetInfo?.Invoke(_character);
        }
        
        /// <summary>
        /// Change the visual updater implementation at runtime.
        /// </summary>
        public void SetVisualUpdater(ICardDisplay _newVisualUpdater)
        {
            if (_newVisualUpdater == null)
            {
                Debug.LogError("Cannot set a null visual updater");
                return;
            }
            
            visualUpdater = _newVisualUpdater;
            
            if (visualComponents != null)
            {
                _newVisualUpdater.Initialize(visualComponents);
            }
            
            if (characterInfo != null)
            {
                visualUpdater.SetChainedOverlay(characterInfo.isChained.Value, true);
                SetPlaceSide(placeCardSide);
            }
        }
        
        /// <summary>
        /// Change the animation handler implementation at runtime.
        /// </summary>
        public void SetAnimationHandler(ICardAnimation _newAnimationHandler)
        {
            if (_newAnimationHandler == null)
            {
                Debug.LogError("Cannot set a null animation handler");
                return;
            }
            
            animationHandler = _newAnimationHandler;
            
            if (visualComponents != null)
            {
                _newAnimationHandler.Initialize(visualComponents);
            }
        }

        public void SetPlaceSide(PlaceCardSide _placeSide)
        {
            placeCardSide = _placeSide;

            if (visualUpdater == null || visualComponents == null) return;
            
            if (placeCardSide == PlaceCardSide.Front)
            {
                visualUpdater.ShowFrontSideInfo(voteCanvas != null ? voteCanvas.transform : null, visualComponents.cardEffectsParent);
            }
            else if (canShowBackInfo)
            {
                visualUpdater.ShowBackSideInfo(voteCanvas != null ? voteCanvas.transform : null, visualComponents.cardEffectsParent);
            }
        }

        public void SetCanShowBackInfo(bool _shouldShowBackInfo)
        {
            canShowBackInfo = _shouldShowBackInfo;

            if (visualUpdater == null || visualComponents == null) return;

            if (!canShowBackInfo)
            {
                visualUpdater.ShowFrontSideInfo(voteCanvas != null ? voteCanvas.transform : null, visualComponents.cardEffectsParent);
            }
            
            if (canShowBackInfo && placeCardSide == PlaceCardSide.Back)
            {
                visualUpdater.ShowBackSideInfo(voteCanvas != null ? voteCanvas.transform : null, visualComponents.cardEffectsParent);
            }
        }

        #endregion

        #region Info Display

        public async UniTask ShowPseudoWithRevealedInfo(bool _turnCard = false, bool _allowChangeSideInfo = true)
        {
            var _cancellationToken = showPseudoTaskHandler.GetNewToken();
            
            try
            {
                bool _isRevealed = (int)gameInfoRevealer
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
            
            bool _isRevealed = (int)gameInfoRevealer
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
            animationHandler.OnClick();
            soundHandler.PlayClickSound();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            onCardHovered?.Invoke(this);
            animationHandler.OnHover(cardCanvas, soundHandler.PlayHoverSound);
            isPointerOver = true;
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            onCardUnhovered?.Invoke(this);
            
            if (CanUnZoomCard())
            {
                animationHandler.OnUnHover(cardCanvas, soundHandler.PlayUnhoverSound);
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

            for (int _i = 0; _i < panelOpenComponents.Length; _i++)
            {
                if (panelOpenComponents[_i].isPanelOpen)
                {
                    return false;
                }
            }

            return true;
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
