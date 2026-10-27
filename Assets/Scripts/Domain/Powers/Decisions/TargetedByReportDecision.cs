using System.Collections.Generic;
using Characters;
using CorruptionDuPortail.Domain.PlayerIcons;
using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PTargetedByReport (Orpheline passive, Lot C.2) — for every ÉLU (chosen role) who targeted the
    /// Orpheline this night, place THIS power's private icon on that TARGETER's thumbnail, visible only to
    /// the Orpheline (viewer = owner). Anomalies and marginals are left out (GD 2026-10-27: seeing that the
    /// Mage Occulte targeted her was too strong for the investigation). Pure: reads the targeter slots from
    /// the report port, keeps those whose role faction is chosen per the roster, and emits one
    /// <see cref="AddPlayerIcon"/> each. No élu targeter ⇒ Accept with an empty effect list (a no-op that
    /// still "ran"), never Reject. The icon id is an opaque value set by the adapter.
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
                if (ctx.Roster.FactionOf(_targeter) != FactionType.chosen)
                {
                    continue;
                }
                // The marked thumbnail is the TARGETER's; the sole viewer is the Orpheline (owner).
                _effects.Add(new AddPlayerIcon(IconId, _targeter, ctx.OwnerSlot,
                    PlayerIconLifetime.ClearAtAwakeningStart));
            }
            return PowerOutcome.Accept(_effects);
        }
    }
}
