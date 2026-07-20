using System.Collections.Generic;
using CorruptionDuPortail.Domain.PlayerIcons;
using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PTargetedByReport (Orpheline passive, Lot C.2) — end of night: for every player who targeted the
    /// Orpheline this night, place THIS power's private icon on that TARGETER's thumbnail, visible only to
    /// the Orpheline (viewer = owner). Pure: reads the targeter slots from the report port and emits one
    /// <see cref="AddPlayerIcon"/> per targeter. No targeters ⇒ Accept with an empty effect list (a no-op
    /// that still "ran"), never Reject. The icon id is an opaque value set by the adapter.
    /// </summary>
    public sealed class TargetedByReportDecision : IPowerDecision
    {
        public PowerId Id => PowerId.TargetedByReport;
        public bool IsPassive => true;

        /// <summary>Opaque icon identity, set by the adapter to the declaring Power's NetworkObjectId.</summary>
        public ulong IconId;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var _report = ctx.State<ITargetedByReport>();
            var _effects = new List<EffectDescriptor>();
            foreach (int _targeter in _report.TargeterSlots)
            {
                // The marked thumbnail is the TARGETER's; the sole viewer is the Orpheline (owner).
                _effects.Add(new AddPlayerIcon(IconId, _targeter, ctx.OwnerSlot,
                    PlayerIconLifetime.ClearAtAwakeningStart));
            }
            return PowerOutcome.Accept(_effects);
        }
    }
}
