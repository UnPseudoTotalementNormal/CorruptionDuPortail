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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        // Fan that fits every role on screen (maquettes eventail-roles v1, A1); the hovered card rises along its slant, the
        // others never move, and the cards' places at rest decide which one the pointer is on (v2, H2 + Z1).
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
        [Tooltip("Hauteur d'une carte de rôle dans l'éventail.")]
        [SerializeField] private float rolePickerCardHeight = 8.8f;
        [Tooltip("Échelle donnée par l'éventail à la carte survolée (s'ajoute au grossissement de son propre survol : 0,84 × 1,45 = 1,22 à l'écran, maquette H2).")]
        [SerializeField] private float rolePickerHoverScale = 0.84f;
        [Tooltip("De combien la carte survolée sort de l'éventail, le long de sa pente (maquette H2 : 0,39 hauteur de carte à l'écran).")]
        [SerializeField] private float rolePickerHoverLift = 2.6f;
        [Tooltip("De combien le survol d'une carte (son avancée vers la caméra) la fait déjà monter à l'écran, mesuré en jeu (155 px sur 1600).")]
        [SerializeField] private float rolePickerHoverForwardRise = 2.9f;
        [Tooltip("Grossissement à l'écran que le survol de la carte donne déjà (zoom et avancée vers la caméra), mesuré en jeu.")]
        [SerializeField] private float rolePickerCardHoverGrowth = 1.45f;
        [Tooltip("De combien la carte survolée passe au-dessus de la plus haute des autres, vers la caméra.")]
        [SerializeField] private float rolePickerHoverRaise = 0.3f;
        [Tooltip("De combien le survol d'une carte (avancée vers la caméra) l'écarte du centre de l'écran, mesuré en jeu (1,45 / 1,1 de zoom).")]
        [SerializeField] private float rolePickerHoverPerspective = 1.32f;
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
        // Z1: the fan at rest (where each card is looked for under the pointer), the last pointer position read, the
        // card the button went down on.
        private RoleFanLayout.Slot[] fanRestSlots = Array.Empty<RoleFanLayout.Slot>();
        private Vector2 lastFanPointer = new(float.NaN, float.NaN);
        private Card fanPressedCard;
        private readonly List<RaycastResult> fanRaycastResults = new();

        /// <summary>What the last pointer reading found under the pointer (autoplay journal when a hover is lost).</summary>
        public string LastFanProbe { get; private set; } = "none";

        // Read-only view of the live picker for autoplay captures / diagnostics — no behaviour change.
        public bool IsPickerActive => isPickerActive;

        /// <summary>Whether the open picker offers this card (the cat paw shows the cross on the others).</summary>
        public bool IsCardPickable(Card _card) => isPickerActive && _card != null && cardClickHandlers.ContainsKey(_card);
        public bool IsRolePicker => isPickerActive && spawnedRoleCards.Count > 0;
        public IReadOnlyCollection<Card> PickableCards => cardClickHandlers.Keys;
        /// <summary>The role picker's cards, left to right in the fan (autoplay's hover tour).</summary>
        public IReadOnlyList<Card> FanCards => spawnedRoleCards;
        /// <summary>The fan card laid out as hovered (grown, straight), null when none.</summary>
        public Card HoveredFanCard => hoveredRoleCard;
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
                _card.pointerDrivenByPicker = true;
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

            fanRestSlots = RoleFanLayout.Compute(spawnedRoleCards.Count, -1, rolePickerFanCenter, FanSettings());
            lastFanPointer = new Vector2(float.NaN, float.NaN);
            fanPressedCard = null;
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
            cardHeight = rolePickerCardHeight,
            hoverLift = rolePickerHoverLift,
            hoverForwardRise = rolePickerHoverForwardRise,
            hoverPerspective = rolePickerHoverPerspective,
            perspectiveCenterX = rolePickerCenter ? rolePickerCenter.localPosition.x : 0f,
        };

        /// <summary>Tweens every role card to its place in the fan, the hovered one risen along its slant.</summary>
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

        // Maquettes eventail-roles v2, Z1. The fan's cards ignore their own pointer events: the card hovered is the one
        // whose place AT REST is under the pointer (above the fan, the risen card keeps it), so sliding along the fan
        // goes card by card even under the risen card; a press then a release on the same card chooses it. Read only
        // when the pointer moves or the button changes: a hover set otherwise (autoplay) is not undone by a still mouse.
        private void Update()
        {
            if (!IsRolePicker)
            {
                return;
            }
            Mouse _mouse = Mouse.current;
            if (_mouse == null)
            {
                return;
            }
            Vector2 _screen = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width / 2f, Screen.height / 2f) : _mouse.position.ReadValue();
            bool _pressed = _mouse.leftButton.wasPressedThisFrame, _released = _mouse.leftButton.wasReleasedThisFrame;
            if (_screen == lastFanPointer && !_pressed && !_released)
            {
                return;
            }
            lastFanPointer = _screen;

            Card _under = FanCardUnder(_screen, out string _blocked);
            if (_under != hoveredRoleCard)
            {
                SetFanHover(_under);
            }
            if (_pressed)
            {
                fanPressedCard = _under;
            }
            if (_released)
            {
                Card _pressedOn = fanPressedCard;
                fanPressedCard = null;
                if (_under != null && _under == _pressedOn)
                {
                    ClickFanCard(_under);
                }
                else if (_pressedOn != null)
                {
                    Diagnostics.Logs.GameLog.Info("picker", "picker.fan.click.refused", "pressed", RoleNameOf(_pressedOn),
                        "released", _under ? RoleNameOf(_under) : null, "reason", _under == null ? _blocked ?? "off-fan" : "other-card");
                }
            }
        }

        /// <summary>The fan card whose place at rest is under <paramref name="_screen"/> (Z1), null when none or when
        /// something drawn in front of the fan (a menu) takes the pointer (<paramref name="_blocked"/> says why).</summary>
        private Card FanCardUnder(Vector2 _screen, out string _blocked)
        {
            _blocked = null;
            Card _first = spawnedRoleCards.FirstOrDefault(_c => _c);
            // The camera the cards' canvases are raycast with (Camera.main can be another one later in a game: the
            // right cards came out one off, autoplay probe of 2026-11-21).
            Canvas _cardCanvas = _first ? _first.GetComponentInChildren<Canvas>() : null;
            Camera _camera = _cardCanvas && _cardCanvas.worldCamera ? _cardCanvas.worldCamera : Camera.main;
            if (_camera == null || _first == null || fanRestSlots.Length != spawnedRoleCards.Count)
            {
                _blocked = "no-fan";
                return null;
            }
            // Only a screen UI (a menu) drawn in front of the fan takes the pointer. The board's objects under the blur
            // veil (the power bar's 3D objects, the board's cards) can rank before the fan in the raycast while hidden
            // behind it: they do not count (autoplay probe, 2026-11-21).
            if (EventSystem.current != null)
            {
                fanRaycastResults.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = _screen }, fanRaycastResults);
                foreach (RaycastResult _result in fanRaycastResults)
                {
                    Card _card = _result.gameObject.GetComponentInParent<Card>();
                    if (_card != null && spawnedRoleCards.Contains(_card))
                    {
                        break;
                    }
                    Canvas _canvas = _result.gameObject.GetComponentInParent<Canvas>();
                    if (_canvas != null && _canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
                    {
                        _blocked = "covered";
                        LastFanProbe = $"screen={_screen.x:0},{_screen.y:0} covered-by={_result.gameObject.name}";
                        return null;
                    }
                }
            }

            // The pointer's ray met in the fan's plane (the cards' parent space: x right, z up on screen, y to the camera).
            Transform _space = _first.transform.parent;
            Ray _ray = _camera.ScreenPointToRay(_screen);
            Vector3 _origin = _space ? _space.InverseTransformPoint(_ray.origin) : _ray.origin;
            Vector3 _direction = _space ? _space.InverseTransformVector(_ray.direction) : _ray.direction;
            if (Mathf.Abs(_direction.y) < 1e-6f)
            {
                _blocked = "parallel";
                return null;
            }
            Vector2 PointAt(float _height)
            {
                Vector3 _p = _origin + _direction * ((_height - _origin.y) / _direction.y);
                return new Vector2(_p.x, _p.z);
            }
            Vector3 _point = _origin + _direction * ((rolePickerFanCenter.y - _origin.y) / _direction.y);

            RoleFanLayout.Settings _settings = FanSettings();
            int _hovered = hoveredRoleCard ? spawnedRoleCards.IndexOf(hoveredRoleCard) : -1;
            RoleFanLayout.Slot _slot = default;
            if (_hovered >= 0)
            {
                // Where the risen card appears: its own hover spreads it from the screen's centre.
                _slot = RoleFanLayout.Compute(spawnedRoleCards.Count, _hovered, rolePickerFanCenter, _settings)[_hovered];
                _slot.position.x = _settings.perspectiveCenterX
                    + (_slot.position.x - _settings.perspectiveCenterX) * Mathf.Max(1f, _settings.hoverPerspective);
                _slot.position.z += _settings.hoverForwardRise;
            }
            int _index = RoleFanLayout.HitTest(fanRestSlots, PointAt, new Vector2(_point.x, _point.z), _settings, _hovered, _slot,
                _settings.hoverScale * _settings.cardHoverScale);
            LastFanProbe = $"screen={_screen.x:0},{_screen.y:0} plane={_point.x:0.00},{_point.z:0.00} y={_point.y:0.00} " +
                $"top={(fanRaycastResults.Count > 0 ? fanRaycastResults[0].gameObject.name : "none")} cam={_camera.name} index={_index}";
            return _index >= 0 ? spawnedRoleCards[_index] : null;
        }

        /// <summary>Diagnostics (autoplay): where the picker maps a screen point in the fan's plane, and the place at rest
        /// of a fan card, in the cards' parent space.</summary>
        public string DescribeFanCalibration(Card _card, Vector2 _renderedCentre)
        {
            int _i = spawnedRoleCards.IndexOf(_card);
            if (_i < 0 || _i >= fanRestSlots.Length) return "not-in-fan";
            Canvas _cardCanvas = _card.GetComponentInChildren<Canvas>();
            Camera _camera = _cardCanvas && _cardCanvas.worldCamera ? _cardCanvas.worldCamera : Camera.main;
            Transform _space = _card.transform.parent;
            if (_camera == null || _space == null) return "no-camera";
            Ray _ray = _camera.ScreenPointToRay(_renderedCentre);
            Vector3 _o = _space.InverseTransformPoint(_ray.origin), _d = _space.InverseTransformVector(_ray.direction);
            Vector3 _p = _o + _d * ((rolePickerFanCenter.y - _o.y) / _d.y);
            Vector3 _pivot = _camera.WorldToScreenPoint(_card.transform.position);
            Vector3 _local = _space.InverseTransformPoint(_card.transform.position);
            RoleFanLayout.Slot _s = fanRestSlots[_i];
            return $"idx={_i} rendered={_renderedCentre.x:0},{_renderedCentre.y:0} pivotScreen={_pivot.x:0},{_pivot.y:0} " +
                $"plane={_p.x:0.00},{_p.z:0.00} slot={_s.position.x:0.00},{_s.position.y:0.00},{_s.position.z:0.00} yaw={_s.yaw:0.0} " +
                $"cardLocal={_local.x:0.00},{_local.y:0.00},{_local.z:0.00} cam={_camera.name} camPos={_camera.transform.position} camRot={_camera.transform.eulerAngles}";
        }

        private void SetFanHover(Card _card)
        {
            Card _previous = hoveredRoleCard;
            if (_previous)
            {
                _previous.DriveHover(false);
            }
            if (_card)
            {
                _card.DriveHover(true);
            }
        }

        private void ClickFanCard(Card _card)
        {
            if (!IsCardPickable(_card))
            {
                Diagnostics.Logs.GameLog.Info("picker", "picker.fan.click.refused", "pressed", RoleNameOf(_card), "reason", "not-pickable");
                return;
            }
            _card.DriveClick();
        }

        private static string RoleNameOf(Card _card) => _card && _card.roleInfo != null ? _card.roleInfo.roleName.ToString() : null;

        /// <summary>Hovers a card of the role picker's fan as the pointer would (autoplay without real input).</summary>
        public void HoverFanCard(Card _card)
        {
            if (IsRolePicker && (_card == null || spawnedRoleCards.Contains(_card)) && _card != hoveredRoleCard)
            {
                SetFanHover(_card);
            }
        }

        /// <summary>Chooses a card of the role picker's fan as a click on it would (autoplay without real input).</summary>
        public void ChooseFanCard(Card _card)
        {
            if (IsRolePicker && spawnedRoleCards.Contains(_card))
            {
                ClickFanCard(_card);
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
            // The fan's cards no longer get pointer events: the hovered one is let go here (its zoom, its sound).
            Card _wasHovered = hoveredRoleCard;
            hoveredRoleCard = null;
            if (_wasHovered)
            {
                _wasHovered.DriveHover(false);
            }
            fanRestSlots = Array.Empty<RoleFanLayout.Slot>();
            fanPressedCard = null;
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
