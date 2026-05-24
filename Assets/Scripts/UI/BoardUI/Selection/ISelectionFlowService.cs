using System;
using Characters;
using GameLogic.Validation;
using static Characters.Powers.Target.TargetUtils;

namespace UI.BoardUI.Selection
{
    public interface ISelectionFlowService
    {
        void StartRoleSelection(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _onRoleSelected,
            SelectionFlowOptions _options = null);

        void StartCharacterSelection(Validator<(ulong targetId, TargetType targetType)> _validator,
            Action<Character> _onCharacterSelected, SelectionFlowOptions _options = null);

        void StartCharacterThenRoleSelection(
            Validator<(ulong targetId, TargetType targetType)> _validator, Action<Character, Role> _onComplete,
            SelectionFlowOptions _options = null);

        void CancelSelection(bool _invokeCanceled = false);
    }
}
