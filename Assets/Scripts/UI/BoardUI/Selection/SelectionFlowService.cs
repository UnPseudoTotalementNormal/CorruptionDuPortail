using System;
using System.Linq;
using Board;
using Characters;
using FocusSystem;
using GameLogic.Validation;
using UnityEngine;
using FocusType = FocusSystem.FocusType;
using static Characters.Powers.Target.TargetUtils;

namespace UI.BoardUI.Selection
{
    public sealed class SelectionFlowService : ISelectionFlowService
    {
        private static readonly SelectionFlowService _instance = new();
        public static SelectionFlowService instance => _instance;

        private Action onCanceled;
        private bool clearFocusOnFinish = true;
        private bool isFlowActive;
        private bool isSubscribedToPickerCancel;

        private SelectionFlowService()
        {
        }

        public void StartRoleSelection(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _onRoleSelected,
            SelectionFlowOptions _options = null)
        {
            if (!CanUsePicker())
            {
                return;
            }

            ResetCurrentSelection(_invokeCanceled: false, _clearFocus: true);
            SelectionFlowOptions _resolvedOptions = _options ?? new SelectionFlowOptions();

            StartFlow(_resolvedOptions);
            ApplyFocus(_resolvedOptions.focusType, _validator);

            CardPickerManager.instance.ShowRolePicker(_validator, _role =>
            {
                CompleteFlow();
                _onRoleSelected?.Invoke(_role);
            }, _resolvedOptions.GetStepDescription(0));
        }

        public void StartCharacterSelection(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _onCharacterSelected, SelectionFlowOptions _options = null)
        {
            if (!CanUsePicker())
            {
                return;
            }

            ResetCurrentSelection(_invokeCanceled: false, _clearFocus: true);
            SelectionFlowOptions _resolvedOptions = _options ?? new SelectionFlowOptions();
            _resolvedOptions.focusType ??= FocusType.Cards;

            StartFlow(_resolvedOptions);
            ApplyFocus(_resolvedOptions.focusType, _validator);

            CardPickerManager.instance.ShowCharacterPicker(_validator, _character =>
            {
                CompleteFlow();
                _onCharacterSelected?.Invoke(_character);
            }, _resolvedOptions.GetStepDescription(0));
        }

        public void StartCharacterThenRoleSelection(
            Validator<(ulong targetId, TargetType targetType)> _validator, Action<Character, Role> _onComplete,
            SelectionFlowOptions _options = null)
        {
            if (!CanUsePicker())
            {
                return;
            }

            ResetCurrentSelection(_invokeCanceled: false, _clearFocus: true);
            SelectionFlowOptions _resolvedOptions = _options ?? new SelectionFlowOptions();
            _resolvedOptions.focusType ??= FocusType.Cards;

            StartFlow(_resolvedOptions);
            ApplyFocus(_resolvedOptions.focusType, _validator);

            CardPickerManager.instance.ShowCharacterPicker(_validator, _character =>
            {
                Card _chosenCard = FindBoardCard(_character);
                FocusManager.instance?.UnfocusAll();
                if (_chosenCard)
                {
                    FocusManager.instance?.FocusObject(_chosenCard.gameObject);
                }

                CardPickerManager.instance.ShowRolePicker(_validator, _role =>
                {
                    CompleteFlow();
                    _onComplete?.Invoke(_character, _role);
                }, _resolvedOptions.GetStepDescription(1));
            }, _resolvedOptions.GetStepDescription(0));
        }

        public void CancelSelection(bool _invokeCanceled = false)
        {
            ResetCurrentSelection(_invokeCanceled, _clearFocus: true);
        }

        private void StartFlow(SelectionFlowOptions _options)
        {
            isFlowActive = true;
            onCanceled = _options.onCanceled;
            clearFocusOnFinish = _options.clearFocusOnFinish;
            SubscribeToPickerCancel();
        }

        private void CompleteFlow()
        {
            bool _shouldClearFocus = clearFocusOnFinish;
            ClearFlowState();

            if (_shouldClearFocus)
            {
                FocusManager.instance?.UnfocusAll();
            }
        }

        private void ResetCurrentSelection(bool _invokeCanceled, bool _clearFocus)
        {
            bool _wasActive = isFlowActive;
            Action _cancelCallback = onCanceled;
            bool _shouldClearFocus = clearFocusOnFinish;

            UnsubscribeFromPickerCancel();
            CardPickerManager.instance?.CancelPicker();
            ClearFlowState();

            if (_clearFocus && _shouldClearFocus)
            {
                FocusManager.instance?.UnfocusAll();
            }

            if (_invokeCanceled && _wasActive)
            {
                _cancelCallback?.Invoke();
            }
        }

        private void OnPickerCanceled()
        {
            Action _cancelCallback = onCanceled;
            bool _shouldClearFocus = clearFocusOnFinish;

            ClearFlowState();

            if (_shouldClearFocus)
            {
                FocusManager.instance?.UnfocusAll();
            }

            _cancelCallback?.Invoke();
        }

        private void ClearFlowState()
        {
            UnsubscribeFromPickerCancel();
            onCanceled = null;
            clearFocusOnFinish = true;
            isFlowActive = false;
        }

        private void SubscribeToPickerCancel()
        {
            if (isSubscribedToPickerCancel || CardPickerManager.instance == null)
            {
                return;
            }

            CardPickerManager.instance.onPickerCanceled += OnPickerCanceled;
            isSubscribedToPickerCancel = true;
        }

        private void UnsubscribeFromPickerCancel()
        {
            if (!isSubscribedToPickerCancel || CardPickerManager.instance == null)
            {
                return;
            }

            CardPickerManager.instance.onPickerCanceled -= OnPickerCanceled;
            isSubscribedToPickerCancel = false;
        }

        private static bool CanUsePicker()
        {
            if (CardPickerManager.instance != null)
            {
                return true;
            }

            Debug.LogError("SelectionFlowService: CardPickerManager.instance is null.");
            return false;
        }

        private static void ApplyFocus(FocusType? _focusType,
            Validator<(ulong targetId, TargetType targetType)> _validator)
        {
            if (_focusType == null || FocusManager.instance == null)
            {
                return;
            }

            switch (_focusType.Value)
            {
                case FocusType.Cards:
                    FocusManager.instance.SetFocusOnType(FocusType.Cards,
                        _id => IsTargetValid(_validator, _id, TargetType.Character));
                    break;
                case FocusType.Roles:
                    FocusManager.instance.SetFocusOnType(FocusType.Roles,
                        _id => IsTargetValid(_validator, _id, TargetType.Role));
                    break;
                case FocusType.Powers:
                    FocusManager.instance.SetFocusOnType(FocusType.Powers, _id => true);
                    break;
            }
        }

        private static bool IsTargetValid(Validator<(ulong targetId, TargetType targetType)> _validator,
            ulong _targetId, TargetType _targetType)
        {
            return _validator == null || _validator.Evaluate((_targetId, _targetType));
        }

        private static Card FindBoardCard(Character _character)
        {
            if (_character == null || BoardManager.instance == null)
            {
                return null;
            }

            return BoardManager.instance.visibleCards.FirstOrDefault(_card =>
                _card && _card.characterInfo &&
                _card.characterInfo.ownerClientId.Value == _character.ownerClientId.Value);
        }
    }
}
