using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCorruptionKnowledge — passive: on game start, reveal EVERY character's forceCorruptOnRoleRevealed
    /// flag to the owner (broadcast). Same shape as CorruptionInsight, different reveal field.
    /// </summary>
    public sealed class CorruptionKnowledgeDecision : IPowerDecision
    {
        public PowerId Id => PowerId.CorruptionKnowledge;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            foreach (var slot in ctx.Roster.Slots)
            {
                effects.Add(new RevealInfo(slot, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
            }
            return PowerOutcome.Accept(effects);
        }
    }
}
