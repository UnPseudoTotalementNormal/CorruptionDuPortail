using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCorruptionInsight — passive: on game start, reveal EVERY character's corruption to the owner
    /// (broadcast). Pure: reads the roster slots from the context, emits one RevealInfo each.
    /// </summary>
    public sealed class CorruptionInsightDecision : IPowerDecision
    {
        public PowerId Id => PowerId.CorruptionInsight;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            foreach (var slot in ctx.Roster.Slots)
            {
                effects.Add(new RevealInfo(slot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
            }
            return PowerOutcome.Accept(effects);
        }
    }
}
