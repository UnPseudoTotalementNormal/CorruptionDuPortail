using System;
using System.Collections.Generic;
using Characters;
using GameLogic.Validation;
using static Characters.Powers.Target.TargetUtils;

namespace UI.BoardUI.Selection
{
    /// <summary>
    /// Autoplay seam. When <see cref="SelectionFlowService.Autopilot"/> is set, every selection request is answered by
    /// the autopilot instead of the on-screen picker, so a bot drives the exact same power / state callbacks a click
    /// would — same validator, same callback, no UI. Only the dev-only autoplay driver ever sets it: it is null in
    /// normal play and the picker path is untouched.
    /// <para>
    /// Contract: an implementation answers LATER (never synchronously inside the Start* call — power code may still be
    /// setting state after it asks), with exactly one of <c>_onPicked</c> / <c>_onCanceled</c>, the latter when no
    /// candidate passes the validator (what the real picker does with an empty list).
    /// </para>
    /// </summary>
    public interface ISelectionAutopilot
    {
        void PickCharacter(Validator<(ulong targetId, TargetType targetType)> _validator,
            IReadOnlyCollection<ulong> _excludedIds, Action<Character> _onPicked, Action _onCanceled);

        void PickRole(Validator<(ulong targetId, TargetType targetType)> _validator, Action<Role> _onPicked,
            Action _onCanceled);

        /// <summary>Drops any answer still pending (mirrors <see cref="ISelectionFlowService.CancelSelection"/>).</summary>
        void Cancel();
    }
}
