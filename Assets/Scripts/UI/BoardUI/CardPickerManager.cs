using System;
using System.Collections.Generic;
using System.Linq;
using Board;
using Board.CardComponents;
using Characters;
using DG.Tweening;
using Extensions;
using GameLogic.Validation;
using TMPro;
using TransformComposition;
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

        [Header("Role picker settings")]
        [SerializeField] private float rolePickerCardSpacingAngle = 5f;
        [SerializeField] private float rolePickerCardSpacing = 1f;
        [SerializeField] private float rolePickerCardHeightOffset = -0.1f;
        [SerializeField] private Vector3 rolePickerSpawnOffset;
        [SerializeField] private Transform rolePickerCenter;

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

        private readonly List<Card> spawnedRoleCards = new();
        private readonly Dictionary<Card, Action<Card>> cardClickHandlers = new();
        private readonly List<Card> liftedCharacterCards = new();
        private readonly Dictionary<Card, int> liftedCharacterOriginalLayers = new();
        private readonly List<Action<Role>> subscribedRoleCallbacks = new();
        private readonly List<Action<Character>> subscribedCharacterCallbacks = new();
        private bool isPickerActive;

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
            string _description = null)
        {
            CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);

            List<Character> _validRoles = GetValidRoleCandidates(_validator);
            if (_validRoles.Count == 0)
            {
                onPickerCanceled?.Invoke();
                return;
            }

            isPickerActive = true;
            ShowInstructionPanel(PickerType.Role, _description);

            if (_callback != null)
            {
                onRoleSelected += _callback;
                subscribedRoleCallbacks.Add(_callback);
            }

            float _totalAngle = (_validRoles.Count - 1) * rolePickerCardSpacingAngle;
            float _startAngle = -_totalAngle / 2f;
            float _radius = _validRoles.Count > 1
                ? rolePickerCardSpacing / (2f * Mathf.Sin(rolePickerCardSpacingAngle * Mathf.Deg2Rad / 2f))
                : rolePickerCardSpacing;
            Vector3 _centerLocal = rolePickerCenter ? rolePickerCenter.localPosition : Vector3.zero;

            for (int _i = 0; _i < _validRoles.Count; _i++)
            {
                Character _validRole = _validRoles[_i];
                Card _card = BoardManager.instance.AddNewCard(_validRole, false);
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

                float _angle = _startAngle + (_i * rolePickerCardSpacingAngle);
                float _angleRad = _angle * Mathf.Deg2Rad;
                float _xPos = Mathf.Sin(_angleRad) * _radius;
                float _zPos = (Mathf.Cos(_angleRad) * _radius) - _radius;

                Vector3 _targetPosition = _centerLocal + new Vector3(_xPos, rolePickerCardHeightOffset * _i, _zPos);

                _layer.localPosition = _centerLocal + rolePickerSpawnOffset;
                _layer.DOLocalRotate(new Vector3(0, _angle, 0), TWEEN_DURATION).SetEase(Ease.OutQuint);
                _layer.DOLocalMove(_targetPosition, TWEEN_DURATION).SetEase(Ease.OutQuint);

                Role _capturedRole = _card.roleInfo;
                Action<Card> _clickHandler = _ => OnRoleSelectedInternal(_capturedRole);
                _card.onCardClicked += _clickHandler;
                cardClickHandlers[_card] = _clickHandler;
            }

            if (cardClickHandlers.Count == 0)
            {
                CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);
                onPickerCanceled?.Invoke();
            }
        }

        public void ShowCharacterPicker(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _callback, string _description = null)
        {
            CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);

            List<Card> _validCards = BoardManager.instance.visibleCards.Where(_card =>
                _card &&
                _card.characterInfo &&
                IsTargetValidForPicker(_validator, _card.characterInfo.ownerClientId.Value, TargetType.Character)).ToList();
            if (_validCards.Count == 0)
            {
                onPickerCanceled?.Invoke();
                return;
            }

            isPickerActive = true;
            ShowInstructionPanel(PickerType.Character, _description);

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
                CancelPicker(_invokeCanceled: false, _instantCharacterReset: true);
                onPickerCanceled?.Invoke();
            }
        }

        private void OnRoleSelectedInternal(Role _role)
        {
            if (!isPickerActive) return;
            // Tear down BEFORE invoking so a callback that starts a new picker (chained
            // selection) is not destroyed by the trailing cancel.
            Action<Role> _handler = onRoleSelected;
            CancelPicker(_invokeCanceled: false);
            _handler?.Invoke(_role);
        }

        private void OnCharacterSelectedInternal(Character _character)
        {
            if (!isPickerActive) return;
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

            Vector3 _roleCenterLocal = rolePickerCenter ? rolePickerCenter.localPosition : Vector3.zero;
            foreach (Card _card in spawnedRoleCards)
            {
                if (!_card) continue;

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
                _layer.DOLocalMove(_roleCenterLocal + rolePickerSpawnOffset, TWEEN_DURATION).SetEase(Ease.OutQuint)
                    .OnComplete(() => { if (_captured) Destroy(_captured.gameObject); });
            }
            spawnedRoleCards.Clear();

            foreach (Card _card in liftedCharacterCards)
            {
                if (!_card) continue;

                if (liftedCharacterOriginalLayers.TryGetValue(_card, out int _origLayer))
                    _card.gameObject.SetLayerRecursively(_origLayer);

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
                onPickerCanceled?.Invoke();
            }
        }

        private enum PickerType { Role, Character }

        private void ShowInstructionPanel(PickerType _type, string _description)
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
                instructionTitle.text = _type == PickerType.Role ? instructionRoleTitle : instructionPlayerTitle;
            if (instructionDescription && _description != null)
                instructionDescription.text = _description;

            instructionCanvasGroup.DOKill();
            instructionCanvasGroup.DoShowGroup(INSTRUCTION_FADE_DURATION, false, false);

            if (frostCanvasGroup)
            {
                frostCanvasGroup.DOKill();
                frostCanvasGroup.DoShowGroup(INSTRUCTION_FADE_DURATION, false, false);
            }
        }

        private void HideInstructionPanel(bool _instant)
        {
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
            List<Character> _characters = CharacterManager.instance.GetCharacters()
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
