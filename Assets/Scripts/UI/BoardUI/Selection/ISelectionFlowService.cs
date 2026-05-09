using System;
using Board;
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

        void StartCharacterThenRoleSelection(Card _focusedCharacterCard,
            Validator<(ulong targetId, TargetType targetType)> _roleValidator, Action<Role> _onRoleSelected,
            SelectionFlowOptions _options = null);

        void CancelSelection(bool _invokeCanceled = false);
    }
}
