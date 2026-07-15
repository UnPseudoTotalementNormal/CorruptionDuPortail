using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PChainedByTheShadows — active (char + role pick): target the picked character; if its role matches
    /// the picked role (secondary target's role), reveal its role to the owner (broadcast); and if it is a
    /// "chosen" faction, add it to the chaining list. NewTargeting is unconditional. All the branch logic
    /// lives here, pure — the roster supplies the role-match + faction reads.
    /// </summary>
    public sealed class ChainedByShadowsDecision : IPowerDecision
    {
        public PowerId Id => PowerId.ChainedByShadows;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            if (ctx.Roster.SameRole(ctx.TargetSlot, ctx.SecondaryTargetSlot))
            {
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
                if (ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen)
                {
                    effects.Add(new AddToChain(ctx.TargetSlot));
                }
            }
            return PowerOutcome.Accept(effects);
        }
    }
}
