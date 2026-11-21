using Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using Board;
using Board.CardComponents;
using Characters;
using DG.Tweening;
using Extensions;
using FocusSystem;
using GameLogic.Validation;
using TMPro;
using TooltipSystem;
using TransformComposition;
using UI.BoardUI.Selection;
using UnityEngine;
using static Characters.Powers.Target.TargetUtils;

namespace UI.BoardUI
{
    public class CardPickerManager : MonoBehaviour
    {
        public static CardPickerManager instance;

        public event Action<Character> onCharacterSelected;
        public event Action<Role> onRoleSelected;
        public event Action onPickerCanceled;

        // Story 12.2 lane A: BoardManager + CharacterManager + FocusManager scene-wired, clearing the §4d Board
        // and §4f Focus survivors. SceneWiringGuard is the wiring control.
        [Header("Injected dependencies (Story 12.2 lane A)")]
        [SerializeField] private BoardManager boardManager;
        [SerializeField] private CharacterManager characterManager;
        [SerializeField] private FocusManager focusManager;

        [Header("Role picker settings")]
        [SerializeField] private float rolePickerCardSpacingAngle = 5f;
        [SerializeField] private float rolePickerCardSpacing = 1f;
        [SerializeField] private float rolePickerCardHeightOffset = -0.1f;
        [SerializeField] private Vector3 rolePickerSpawnOffset;
        [SerializeField] private Transform rolePickerCenter;
        // Fan that fits every role on screen and makes the hovered one readable (maquettes eventail-roles v1, A1).
        [Tooltip("Largeur max de l'éventail, du centre de la première carte au centre de la dernière : au-delà, les cartes se resserrent.")]
        [SerializeField] private float rolePickerMaxSpan = 24f;
        [Tooltip("Largeur max de l'éventail quand une carte est épinglée à droite (le joueur visé) : il reste à sa gauche.")]
        [SerializeField] private float rolePickerPinnedMaxSpan = 19f;
        [Tooltip("Rotation max entre la première et la dernière carte (degrés).")]
        [SerializeField] private float rolePickerMaxTotalAngle = 36f;
        [Tooltip("De combien les cartes du bord descendent à l'écran.")]
        [SerializeField] private float rolePickerEdgeDrop = 2f;
        [Tooltip("Largeur d'une carte de rôle dans l'éventail.")]
        [SerializeField] private float rolePickerCardWidth = 6.3f;
        [Tooltip("Échelle donnée par l'éventail à la carte survolée (s'ajoute au grossissement de son propre survol).")]
        [SerializeField] private float rolePickerHoverScale = 0.95f;
        [Tooltip("Grossissement à l'écran que le survol de la carte donne déjà (zoom et avancée vers la caméra), mesuré en jeu.")]
        [SerializeField] private float rolePickerCardHoverGrowth = 1.45f;
        [Tooltip("De combien la carte survolée passe au-dessus de la plus haute des autres, vers la caméra.")]
        [SerializeField] private float rolePickerHoverRaise = 0.3f;
        [Tooltip("Marge entre la carte survolée et ses voisines écartées.")]
        [SerializeField] private float rolePickerHoverGap = 0.3f;
        [Tooltip("Décalage de l'éventail quand une carte est épinglée à droite (joueur visé).")]
        [SerializeField] private float rolePickerPinnedOffsetX = -3f;

        [Header("Instruction UI")]
        [SerializeField] private CanvasGroup instructionCanvasGroup;
        [SerializeField] private RectTransform instructionParent;
        [SerializeField] private TMP_Text instructionTitle;
        [SerializeField] private TMP_Text instructionDescription;
        [SerializeField] private string instructionRoleTitle;
        [SerializeField] private string instructionPlayerTitle;
        [SerializeField] private RectTransform instructionRoleSelectionPosition;
        [SerializeField] private RectTransform instructionPlayerSelectionPosition;

        [Header("Character picker settings")]
        [SerializeField] private float characterPickerLiftHeight = 0.15f;

        [Header("Pinned Card Settings")]
        [Tooltip("Transform définissant la position, rotation et échelle de la carte épinglée à droite.")]
        [SerializeField] private Transform pinnedCardRightAnchor;

        [Header("Blur veil")]
        [Tooltip("CanvasGroup du FrostCanvas (voile flou rendu par FrostCamera).")]
        [SerializeField] private CanvasGroup frostCanvasGroup;

        // Layers de composition (TransformCompositor), pas des layers Unity.
        private const string CHARACTER_PICKER_LAYER = "CharacterPicker";
        private const string ROLE_PICKER_LAYER = "RolePicker";
        // Layer Unity rendu par AboveBlurCamera (au-dessus du voile flou).
        private const string ABOVE_BLUR_LAYER = "AboveBlur";
        private const float TWEEN_DURATION = 0.5f;
        private const float INSTRUCTION_FADE_DURATION = 0.25f;
        private const float FAN_HOVER_TWEEN_DURATION = 0.3f;

        private readonly List<Card> spawnedRoleCards = new();
        private readonly Dictionary<Card, Action<Card>> cardClickHandlers = new();
        private readonly List<Card> liftedCharacterCards = new();
        private readonly Dictionary<Card, int> liftedCharacterOriginalLayers = new();
        private readonly List<Action<Role>> subscribedRoleCallbacks = new();
        private readonly List<Action<Character>> subscribedCharacterCallbacks = new();
        private bool isPickerActive;
        private Card currentPinnedCardInstance;
        private Card hoveredRoleCard;
        private Vector3 rolePickerFanCenter;

        // Read-only view of the live picker for autoplay captures / diagnostics — no behaviour change.
        public bool IsPickerActive => isPickerActive;

        /// <summary>Whether the open picker offers this card (the cat paw shows the cross on the others).</summary>
        public bool IsCardPickable(Card _card) => isPickerActive && _card != null && cardClickHandlers.ContainsKey(_card);
        public bool IsRolePicker => isPickerActive && spawnedRoleCards.Count > 0;
        public IReadOnlyCollection<Card> PickableCards => cardClickHandlers.Keys;
        /// <summary>The role picker's cards, left to right in the fan (autoplay's hover tour).</summary>
        public IReadOnlyList<Card> FanCards => spawnedRoleCards;
        public IReadOnlyList<Card> LiftedCharacterCards => liftedCharacterCards;
        public float FrostAlpha => frostCanvasGroup ? frostCanvasGroup.alpha : -1f;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void OnDisable()
        {
            if (isPickerActive) Diagnostics.Logs.GameLog.Info("picker", "picker.cancel", "reason", "disabled");
            CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        [ContextMenu("ShowRolePicker")]
        public void TestRolePicker()
        {
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(_ctx => _ctx.targetType == TargetType.Role);
            ShowRolePicker(_validator, _role => Debug.Log("Selected role: " + _role.roleName));
        }

        [ContextMenu("ShowCharacterPicker")]
        public void TestCharacterPicker()
        {
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(_ctx => _ctx.targetType == TargetType.Character);
            ShowCharacterPicker(_validator, _c => Debug.Log("Selected character: " + _c.ownerClientId.Value));
        }

        public void ShowRolePicker(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _callback,
            string _description = null, SelectionFlowOptions _options = null)
        {
            CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);

            List<Character> _validRoles = GetValidRoleCandidates(_validator);
            if (_validRoles.Count == 0)
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.empty", "type", "role", "reason", "no-valid-target");
                onPickerCanceled?.Invoke();
                return;
            }

            isPickerActive = true;
            ShowInstructionPanel(PickerType.Role, _description, _options?.stepDescriptionArgs);
            SetupPinnedCard(_options);

            if (_callback != null)
            {
                onRoleSelected += _callback;
                subscribedRoleCallbacks.Add(_callback);
            }

            Vector3 _centerLocal = rolePickerCenter ? rolePickerCenter.localPosition : Vector3.zero;
            // A card pinned on the right (the targeted player) pushes the fan to the left so they do not overlap.
            rolePickerFanCenter = _centerLocal + (currentPinnedCardInstance ? new Vector3(rolePickerPinnedOffsetX, 0f, 0f) : Vector3.zero);
            hoveredRoleCard = null;

            for (int _i = 0; _i < _validRoles.Count; _i++)
            {
                Character _validRole = _validRoles[_i];
                Card _card = boardManager.AddNewCard(_validRole, false);
                if (!_card)
                {
                    continue;
                }
                spawnedRoleCards.Add(_card);
                _card.gameObject.SetLayerRecursively(ABOVE_BLUR_LAYER);

                _card.SetAnimationHandler(new CardRoleAnimation());
                _card.SetVisualUpdater(new CardRoleVisualUpdater());
                HidePlayerIdentity(_card);

                TransformCompositorComponent _compositor = _card.GetTransformCompositor();
                if (_compositor == null)
                {
                    continue;
                }

                TransformLayer _layer = _compositor.GetLayer(ROLE_PICKER_LAYER);
                _layer.DOKill();
                _layer.localPosition = _centerLocal + rolePickerSpawnOffset;

                Role _capturedRole = _card.roleInfo;
                Action<Card> _clickHandler = _ => OnRoleSelectedInternal(_capturedRole);
                _card.onCardClicked += _clickHandler;
                _card.onCardHovered += OnRoleCardHovered;
                _card.onCardUnhovered += OnRoleCardUnhovered;
                cardClickHandlers[_card] = _clickHandler;
            }

            LayoutRoleFan(TWEEN_DURATION);

            if (cardClickHandlers.Count == 0)
            {
                Diagnostics.Logs.GameLog.Warn("picker", "picker.empty", "type", "role", "reason", "no-card-spawned", "candidates", _validRoles.Count);
                CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);
                onPickerCanceled?.Invoke();
                return;
            }
            Diagnostics.Logs.GameLog.Info("picker", "picker.open", "type", "role", "candidates", cardClickHandlers.Count,
                "step", RoleFanLayout.Step(spawnedRoleCards.Count, FanSettings()), "pinned", currentPinnedCardInstance != null);
        }

        private RoleFanLayout.Settings FanSettings() => new()
        {
            spacing = rolePickerCardSpacing,
            spacingAngle = rolePickerCardSpacingAngle,
            maxSpan = currentPinnedCardInstance ? rolePickerPinnedMaxSpan : rolePickerMaxSpan,
            maxTotalAngle = rolePickerMaxTotalAngle,
            edgeDrop = rolePickerEdgeDrop,
            heightStep = rolePickerCardHeightOffset,
            cardWidth = rolePickerCardWidth,
            hoverScale = rolePickerHoverScale,
            cardHoverScale = rolePickerCardHoverGrowth,
            hoverRaise = rolePickerHoverRaise,
            hoverGap = rolePickerHoverGap,
        };

        /// <summary>Tweens every role card to its place in the fan, the hovered one lifted and its neighbours aside.</summary>
        private void LayoutRoleFan(float _duration)
        {
            int _hovered = hoveredRoleCard ? spawnedRoleCards.IndexOf(hoveredRoleCard) : -1;
            RoleFanLayout.Slot[] _slots = RoleFanLayout.Compute(spawnedRoleCards.Count, _hovered, rolePickerFanCenter, FanSettings());
            for (int _i = 0; _i < spawnedRoleCards.Count; _i++)
            {
                Card _card = spawnedRoleCards[_i];
                TransformCompositorComponent _compositor = _card ? _card.GetTransformCompositor() : null;
                if (_compositor == null)
                {
                    continue;
                }

                TransformLayer _layer = _compositor.GetLayer(ROLE_PICKER_LAYER);
                _layer.DOKill();
                _layer.DOLocalMove(_slots[_i].position, _duration).SetEase(Ease.OutQuint);
                _layer.DOLocalRotate(new Vector3(0f, _slots[_i].yaw, 0f), _duration).SetEase(Ease.OutQuint);
                _layer.DOScale(_slots[_i].scale, _duration).SetEase(Ease.OutQuint);
            }
        }

        private void OnRoleCardHovered(Card _card)
        {
            if (!isPickerActive || !spawnedRoleCards.Contains(_card) || hoveredRoleCard == _card)
            {
                return;
            }
            hoveredRoleCard = _card;
            LayoutRoleFan(FAN_HOVER_TWEEN_DURATION);
        }

        private void OnRoleCardUnhovered(Card _card)
        {
            if (hoveredRoleCard != _card)
            {
                return;
            }
            hoveredRoleCard = null;
            if (isPickerActive)
            {
                LayoutRoleFan(FAN_HOVER_TWEEN_DURATION);
            }
        }

        public void ShowCharacterPicker(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _callback, string _description = null, SelectionFlowOptions _options = null)
        {
            CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);

            List<Card> _validCards = boardManager.visibleCards.Where(_card =>
                _card &&
                _card.characterInfo &&
                IsTargetValidForPicker(_validator, _card.characterInfo.ownerClientId.Value, TargetType.Character)).ToList();
            if (_validCards.Count == 0)
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.empty", "type", "character", "reason", "no-valid-target");
                onPickerCanceled?.Invoke();
                return;
            }

            isPickerActive = true;
            ShowInstructionPanel(PickerType.Character, _description, _options?.stepDescriptionArgs);
            SetupPinnedCard(_options);

            if (_callback != null)
            {
                onCharacterSelected += _callback;
                subscribedCharacterCallbacks.Add(_callback);
            }

            for (int _i = 0; _i < _validCards.Count; _i++)
            {
                Card _card = _validCards[_i];
                liftedCharacterCards.Add(_card);
                liftedCharacterOriginalLayers[_card] = _card.gameObject.layer;
                _card.gameObject.SetLayerRecursively(ABOVE_BLUR_LAYER);
                MoveFocusParticlesToLayer(_card.gameObject, LayerMask.NameToLayer(ABOVE_BLUR_LAYER));

                TransformCompositorComponent _compositor = _card.GetTransformCompositor();
                if (_compositor == null)
                {
                    continue;
                }

                TransformLayer _layer = _compositor.GetLayer(CHARACTER_PICKER_LAYER);
                _layer.DOKill();

                _layer.DOLocalMove(new Vector3(0, characterPickerLiftHeight, 0), TWEEN_DURATION).SetEase(Ease.OutQuint);

                Character _capturedChar = _card.characterInfo;
                Action<Card> _clickHandler = _ => OnCharacterSelectedInternal(_capturedChar);
                _card.onCardClicked += _clickHandler;
                cardClickHandlers[_card] = _clickHandler;
            }

            if (cardClickHandlers.Count == 0)
            {
                Diagnostics.Logs.GameLog.Warn("picker", "picker.empty", "type", "character", "reason", "no-compositor", "candidates", _validCards.Count);
                CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);
                onPickerCanceled?.Invoke();
                return;
            }
            Diagnostics.Logs.GameLog.Info("picker", "picker.open", "type", "character", "candidates", cardClickHandlers.Count);
        }

        private void SetupPinnedCard(SelectionFlowOptions _options)
        {
            if (_options == null || (!pinnedCardRightAnchor)) return;

            if (_options.pinnedRole != null)
            {
                Character _roleCharacter = characterManager.GetCharacter(_options.pinnedRole.ownerClientId);
                currentPinnedCardInstance = boardManager.AddNewCard(_roleCharacter, false);
                if (currentPinnedCardInstance)
                {
                    currentPinnedCardInstance.SetAnimationHandler(new CardRoleAnimation());
                    currentPinnedCardInstance.SetVisualUpdater(new CardRoleVisualUpdater());
                    HidePlayerIdentity(currentPinnedCardInstance);
                }
            }
            else if (_options.pinnedCharacter != null)
            {
                currentPinnedCardInstance = boardManager.AddNewCard(_options.pinnedCharacter, false);
                if (currentPinnedCardInstance)
                {
                    TransformCompositorComponent _comp = currentPinnedCardInstance.GetTransformCompositor();
                    if (_comp != null)
                    {
                        TransformLayer _flipLayer = _comp.GetLayer("Flip");
                        if (_flipLayer != null) _flipLayer.localEulerAngles = new Vector3(0, 0, -180);
                    }
                    _ = currentPinnedCardInstance.ShowPseudoWithRevealedInfo(false, false);
                }
            }

            if (currentPinnedCardInstance != null)
            {
                // L'ancre peut être un placeholder désactivé, on copie juste ses valeurs
                // ou on le parente au parent de l'ancre pour qu'il reste actif.
                currentPinnedCardInstance.transform.SetParent(pinnedCardRightAnchor.parent, false);
                currentPinnedCardInstance.transform.position = pinnedCardRightAnchor.position;
                currentPinnedCardInstance.transform.rotation = pinnedCardRightAnchor.rotation;
                currentPinnedCardInstance.transform.localScale = pinnedCardRightAnchor.localScale;

                currentPinnedCardInstance.gameObject.SetLayerRecursively(ABOVE_BLUR_LAYER);

                TransformCompositorComponent _compositor = currentPinnedCardInstance.GetTransformCompositor();
                if (_compositor != null)
                {
                    _compositor.enabled = false;
                }
            }
        }

        private void OnRoleSelectedInternal(Role _role)
        {
            if (!isPickerActive)
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.pick.refused", "type", "role", "reason", "inactive");
                return;
            }
            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("picker", "picker.pick", "type", "role",
                "seat", _role != null ? (object)_role.ownerClientId : null, "role", _role != null ? _role.roleName.ToString() : null));
            // Tear down BEFORE invoking so a callback that starts a new picker (chained
            // selection) is not destroyed by the trailing cancel.
            Action<Role> _handler = onRoleSelected;
            CancelPicker(_invokeCanceled: false);
            _handler?.Invoke(_role);
        }

        private void OnCharacterSelectedInternal(Character _character)
        {
            if (!isPickerActive)
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.pick.refused", "type", "character", "reason", "inactive");
                return;
            }
            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("picker", "picker.pick", "type", "character",
                "seat", _character != null ? (object)_character.ownerClientId.Value : null));
            Action<Character> _handler = onCharacterSelected;
            CancelPicker(_invokeCanceled: false);
            _handler?.Invoke(_character);
        }

        public void CancelPicker()
        {
            CancelPicker(_invokeCanceled: true, _instantCharacterReset: false);
        }

        private void CancelPicker(bool _invokeCanceled, bool _instantCharacterReset = false)
        {
            bool _wasActive = isPickerActive;
            isPickerActive = false;
            HideInstructionPanel(_instantCharacterReset);

            foreach (KeyValuePair<Card, Action<Card>> _kvp in cardClickHandlers)
            {
                if (_kvp.Key)
                {
                    _kvp.Key.onCardClicked -= _kvp.Value;
                }
            }
            cardClickHandlers.Clear();

            if (currentPinnedCardInstance != null)
            {
                if (_instantCharacterReset)
                {
                    Destroy(currentPinnedCardInstance.gameObject);
                }
                else
                {
                    Card _capturedPinned = currentPinnedCardInstance;
                    _capturedPinned.transform.DOScale(Vector3.zero, TWEEN_DURATION)
                        .SetEase(Ease.InBack)
                        .OnComplete(() => {
                            if (_capturedPinned) Destroy(_capturedPinned.gameObject);
                        });
                }
                currentPinnedCardInstance = null;
            }

            Vector3 _roleCenterLocal = rolePickerCenter ? rolePickerCenter.localPosition : Vector3.zero;
            hoveredRoleCard = null;
            foreach (Card _card in spawnedRoleCards)
            {
                if (!_card) continue;
                _card.onCardHovered -= OnRoleCardHovered;
                _card.onCardUnhovered -= OnRoleCardUnhovered;

                if (_instantCharacterReset)
                {
                    Destroy(_card.gameObject);
                    continue;
                }

                TransformCompositorComponent _compositor = _card.GetTransformCompositor();
                if (_compositor == null)
                {
                    Destroy(_card.gameObject);
                    continue;
                }

                Card _captured = _card;
                TransformLayer _layer = _compositor.GetLayer(ROLE_PICKER_LAYER);
                _layer.DOKill();
                _layer.DOLocalRotate(Vector3.zero, TWEEN_DURATION).SetEase(Ease.OutQuint);
                _layer.DOScale(1f, TWEEN_DURATION).SetEase(Ease.OutQuint);
                _layer.DOLocalMove(_roleCenterLocal + rolePickerSpawnOffset, TWEEN_DURATION).SetEase(Ease.OutQuint)
                    .OnComplete(() => { if (_captured) Destroy(_captured.gameObject); });
            }
            spawnedRoleCards.Clear();

            foreach (Card _card in liftedCharacterCards)
            {
                if (!_card) continue;

                if (liftedCharacterOriginalLayers.TryGetValue(_card, out int _origLayer))
                {
                    _card.gameObject.SetLayerRecursively(_origLayer);
                    MoveFocusParticlesToLayer(_card.gameObject, _origLayer);
                }

                TransformCompositorComponent _compositor = _card.GetTransformCompositor();
                if (_compositor == null) continue;

                TransformLayer _layer = _compositor.GetLayer(CHARACTER_PICKER_LAYER);
                _layer.DOKill();

                if (_instantCharacterReset)
                {
                    _layer.localPosition = Vector3.zero;
                    _layer.localEulerAngles = Vector3.zero;
                    continue;
                }

                _layer.DOLocalMove(Vector3.zero, TWEEN_DURATION).SetEase(Ease.OutQuint);
                _layer.DOLocalRotate(Vector3.zero, TWEEN_DURATION).SetEase(Ease.OutQuint);
            }
            liftedCharacterCards.Clear();
            liftedCharacterOriginalLayers.Clear();

            foreach (Action<Role> _cb in subscribedRoleCallbacks)
            {
                onRoleSelected -= _cb;
            }
            subscribedRoleCallbacks.Clear();

            foreach (Action<Character> _cb in subscribedCharacterCallbacks)
            {
                onCharacterSelected -= _cb;
            }
            subscribedCharacterCallbacks.Clear();

            if (_wasActive && _invokeCanceled)
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.cancel", "reason", "cancel");
                onPickerCanceled?.Invoke();
            }
        }

        private enum PickerType { Role, Character }

        private void ShowInstructionPanel(PickerType _type, string _description, object[] _descriptionArgs)
        {
            if (!instructionCanvasGroup) return;

            if (instructionParent)
            {
                RectTransform _anchor = _type == PickerType.Role
                    ? instructionRoleSelectionPosition
                    : instructionPlayerSelectionPosition;
                if (_anchor)
                    instructionParent.anchoredPosition = _anchor.anchoredPosition;
            }

            if (instructionTitle)
                // T36: the titles (authored on the scene object) and the step descriptions (authored on each power) are
                // French game texts.
                instructionTitle.SetLoc(_type == PickerType.Role ? instructionRoleTitle : instructionPlayerTitle);
            // No description = an empty line, never the one a previous picker left (the portal showed « Choisissez
            // un joueur à corrompre. », 2026-11-20).
            if (instructionDescription)
            {
                if (_description != null)
                    instructionDescription.SetLoc(_description, _descriptionArgs ?? Array.Empty<object>());
                else
                    instructionDescription.text = string.Empty; // loc-ok: clears the line
            }

            instructionCanvasGroup.DOKill();
            instructionCanvasGroup.DoShowGroup(INSTRUCTION_FADE_DURATION, false, false);

            if (frostCanvasGroup)
            {
                frostCanvasGroup.DOKill();
                frostCanvasGroup.DoShowGroup(INSTRUCTION_FADE_DURATION, false, false);
            }
            // The board behind the veil keeps no bubble (one covered the role fan's instruction).
            if (TooltipManager.instance) TooltipManager.instance.SetPickerVeil(true);
        }

        private void HideInstructionPanel(bool _instant)
        {
            if (TooltipManager.instance) TooltipManager.instance.SetPickerVeil(false);
            if (frostCanvasGroup)
            {
                frostCanvasGroup.DOKill();
                if (_instant)
                    frostCanvasGroup.alpha = 0f;
                else
                    frostCanvasGroup.DoHideGroup(INSTRUCTION_FADE_DURATION, false, false);
            }

            if (!instructionCanvasGroup) return;
            instructionCanvasGroup.DOKill();
            if (_instant)
            {
                instructionCanvasGroup.alpha = 0f;
                return;
            }
            instructionCanvasGroup.DoHideGroup(INSTRUCTION_FADE_DURATION, false, false);
        }

        private static bool IsTargetValidForPicker(Validator<(ulong targetId, TargetType targetType)> _validator,
            ulong _targetId, TargetType _targetType)
        {
            return _validator == null || _validator.Evaluate((_targetId, _targetType));
        }

        private void MoveFocusParticlesToLayer(GameObject _target, int _layer)
        {
            if (focusManager == null) return;
            foreach (FocusObject _fo in focusManager.currentFocusObjects)
            {
                if (_fo.gameObject == _target && _fo.focusParticles != null)
                    _fo.focusParticles.gameObject.SetLayerRecursively(_layer);
            }
        }

        private static void HidePlayerIdentity(Card _card)
        {
            if (!_card || _card.visualComponents == null)
            {
                return;
            }

            if (_card.visualComponents.cardPlayerPseudo)
            {
                _card.visualComponents.cardPlayerPseudo.text = string.Empty;
                _card.visualComponents.cardPlayerPseudo.gameObject.SetActive(false);
            }

            if (_card.visualComponents.bsCardPlayerPseudo)
            {
                _card.visualComponents.bsCardPlayerPseudo.text = string.Empty;
                _card.visualComponents.bsCardPlayerPseudo.gameObject.SetActive(false);
            }

            if (_card.visualComponents.cardPlayerPseudoHolder)
            {
                _card.visualComponents.cardPlayerPseudoHolder.gameObject.SetActive(false);
            }

            if (_card.visualComponents.meIconCard)
            {
                _card.visualComponents.meIconCard.gameObject.SetActive(false);
            }
        }

        private List<Character> GetValidRoleCandidates(Validator<(ulong targetId, TargetType targetType)> _validator)
        {
            List<Character> _characters = characterManager.GetCharacters()
                .Where(_c => _c && _c.role != null)
                .OrderBy(_c => _c.role.roleID)
                .ThenBy(_c => _c.ownerClientId.Value)
                .ToList();

            List<Character> _validRoles = new();
            foreach (IGrouping<RoleID, Character> _roleGroup in _characters.GroupBy(_c => _c.role.roleID))
            {
                Character _validRepresentative = _roleGroup
                    .FirstOrDefault(_candidate =>
                        IsTargetValidForPicker(_validator, _candidate.ownerClientId.Value, TargetType.Role));

                if (_validRepresentative)
                {
                    _validRoles.Add(_validRepresentative);
                }
            }

            return _validRoles;
        }
    }
}
