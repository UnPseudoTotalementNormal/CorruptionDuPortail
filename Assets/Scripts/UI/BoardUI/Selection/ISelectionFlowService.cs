using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Pick <paramref name="_count"/> DISTINCT characters in a row (Observation Clandestine). Each step
        /// re-shows the character picker excluding the already-picked players; when the last is picked the
        /// full list is handed back. Cancelling any step cancels the whole flow (partial picks discarded).
        /// </summary>
        void StartMultiCharacterSelection(Validator<(ulong targetId, TargetType targetType)> _validator,
            int _count, Action<List<Character>> _onAllSelected, SelectionFlowOptions _options = null);

        void StartCharacterThenRoleSelection(
            Validator<(ulong targetId, TargetType targetType)> _validator, Action<Character, Role> _onComplete,
            SelectionFlowOptions _options = null);

        void CancelSelection(bool _invokeCanceled = false);
    }
}
