using System;
using System.Collections.Generic;
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
        // Story 10.5 (Epic 10 / D4): recorded-callers-only façade. Every gameplay consumer (the 15
        // targeting powers via Power's base field + TakeDownThePortalState lane-B) now resolves this POCO
        // singleton through the composition root, so no registered consumer reads the global. Guard #1
        // forbids the qualified instance accessor in the migrated set. This service still reads the
        // FocusManager global itself (recorded survivor → Epic 11 when its logic is POCO-ised).
        public static SelectionFlowService instance => _instance; // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests

        private Action onCanceled;
        private bool clearFocusOnFinish = true;
        private bool isFlowActive;
        private bool isSubscribedToPickerCancel;

        private SelectionFlowService()
        {
        }

        /// <summary>
        /// Autoplay seam (see <see cref="ISelectionAutopilot"/>): when set, selections are answered by the autopilot
        /// instead of the CardPickerManager UI. Set only by the dev-only autoplay driver; null in normal play.
        /// </summary>
        public ISelectionAutopilot Autopilot { get; set; }

        public void StartRoleSelection(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _onRoleSelected,
            SelectionFlowOptions _options = null)
        {
            if (Autopilot != null)
            {
                Autopilot.PickRole(_validator, _onRoleSelected, _options?.onCanceled);
                return;
            }

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
            }, _resolvedOptions.GetStepDescription(0), _resolvedOptions);
        }

        public void StartCharacterSelection(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _onCharacterSelected, SelectionFlowOptions _options = null)
        {
            if (Autopilot != null)
            {
                Autopilot.PickCharacter(_validator, Array.Empty<ulong>(), _onCharacterSelected, _options?.onCanceled);
                return;
            }

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
            }, _resolvedOptions.GetStepDescription(0), _resolvedOptions);
        }

        public void StartCharacterThenRoleSelection(
            Validator<(ulong targetId, TargetType targetType)> _validator, Action<Character, Role> _onComplete,
            SelectionFlowOptions _options = null)
        {
            if (Autopilot != null)
            {
                ISelectionAutopilot _pilot = Autopilot;
                Action _onCanceled = _options?.onCanceled;
                _pilot.PickCharacter(_validator, Array.Empty<ulong>(),
                    _character => _pilot.PickRole(_validator, _role => _onComplete?.Invoke(_character, _role), _onCanceled),
                    _onCanceled);
                return;
            }

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

                SelectionFlowOptions _step2Options = new SelectionFlowOptions 
                { 
                    focusType = _resolvedOptions.focusType,
                    stepDescriptions = _resolvedOptions.stepDescriptions,
                    pinnedCharacter = _character
                };

                CardPickerManager.instance.ShowRolePicker(_validator, _role =>
                {
                    CompleteFlow();
                    _onComplete?.Invoke(_character, _role);
                }, _step2Options.GetStepDescription(1), _step2Options);
            }, _resolvedOptions.GetStepDescription(0), _resolvedOptions);
        }

        public void StartMultiCharacterSelection(Validator<(ulong targetId, TargetType targetType)> _validator,
            int _count, Action<List<Character>> _onAllSelected, SelectionFlowOptions _options = null)
        {
            if (Autopilot != null)
            {
                if (_count <= 0)
                {
                    _onAllSelected?.Invoke(new List<Character>());
                    return;
                }

                AutopilotPickNextCharacter(Autopilot, _validator, _count, new List<Character>(), _onAllSelected, _options?.onCanceled);
                return;
            }

            if (!CanUsePicker())
            {
                return;
            }
            if (_count <= 0)
            {
                _onAllSelected?.Invoke(new List<Character>());
                return;
            }

            ResetCurrentSelection(_invokeCanceled: false, _clearFocus: true);
            SelectionFlowOptions _resolvedOptions = _options ?? new SelectionFlowOptions();
            _resolvedOptions.focusType ??= FocusType.Cards;

            StartFlow(_resolvedOptions);
            PickNextCharacter(_validator, _count, new List<Character>(), _resolvedOptions, _onAllSelected);
        }

        // Re-shows the character picker once per remaining pick. The per-step validator is the caller's rules
        // AND "not one of the already-picked players", so the same player can never be chosen twice. The whole
        // flow shares ONE StartFlow/CompleteFlow bracket — cancelling any step drops the partial picks.
        private void PickNextCharacter(Validator<(ulong targetId, TargetType targetType)> _baseValidator,
            int _count, List<Character> _picked, SelectionFlowOptions _options, Action<List<Character>> _onAllSelected)
        {
            var _stepValidator = new Validator<(ulong targetId, TargetType targetType)>();
            _stepValidator.AddRule(_ctx => _baseValidator == null || _baseValidator.Evaluate(_ctx));
            _stepValidator.AddRule(_ctx => _picked.All(_c => _c == null || _c.ownerClientId.Value != _ctx.targetId));

            ApplyFocus(_options.focusType, _stepValidator);

            CardPickerManager.instance.ShowCharacterPicker(_stepValidator, _character =>
            {
                _picked.Add(_character);
                if (_picked.Count >= _count)
                {
                    CompleteFlow();
                    _onAllSelected?.Invoke(_picked);
                }
                else
                {
                    PickNextCharacter(_baseValidator, _count, _picked, _options, _onAllSelected);
                }
            }, _options.GetStepDescription(_picked.Count), _options);
        }

        // Autopilot twin of PickNextCharacter: one distinct pick per step, the already-picked players excluded.
        private static void AutopilotPickNextCharacter(ISelectionAutopilot _pilot,
            Validator<(ulong targetId, TargetType targetType)> _validator, int _count, List<Character> _picked,
            Action<List<Character>> _onAllSelected, Action _onCanceled)
        {
            List<ulong> _excluded = _picked.Where(_c => _c != null).Select(_c => _c.ownerClientId.Value).ToList();
            _pilot.PickCharacter(_validator, _excluded, _character =>
            {
                _picked.Add(_character);
                if (_picked.Count >= _count)
                {
                    _onAllSelected?.Invoke(_picked);
                }
                else
                {
                    AutopilotPickNextCharacter(_pilot, _validator, _count, _picked, _onAllSelected, _onCanceled);
                }
            }, _onCanceled);
        }

        public void CancelSelection(bool _invokeCanceled = false)
        {
            Autopilot?.Cancel();
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
