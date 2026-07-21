using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PChainedByTheShadows — active (char + role pick): target the picked character; if its role matches
    /// the picked role (secondary target's role), reveal its role to the owner, CORRUPT it (always, whatever
    /// its faction), and if it is a "chosen" faction add it to the chaining list. NewTargeting is
    /// unconditional. All the branch logic lives here, pure — the roster supplies the role-match + faction reads.
    ///
    /// Lot B (Abyss): on a correct guess, if the owner is the SOLE anomaly still in play (no other anomaly
    /// that is not chained/eliminated) and the once-per-night bonus has not fired yet, grant one extra use of
    /// this power for the night. The bonus fires on success regardless of the target's faction (independent of
    /// the chain, which needs "chosen").
    /// </summary>
    public sealed class ChainedByShadowsDecision : IPowerDecision
    {
        public PowerId Id => PowerId.ChainedByShadows;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            bool roleGuessed = ctx.Roster.SameRole(ctx.TargetSlot, ctx.SecondaryTargetSlot);
            if (roleGuessed)
            {
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
                // On a correct guess the target is ALWAYS corrupted ("il le corrompt"), whatever its faction.
                // Chaining (chosen only) corrupts again downstream — redundant but idempotent for an élu.
                effects.Add(new CorruptPlayer(ctx.TargetSlot));
                if (ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen)
                {
                    effects.Add(new AddToChain(ctx.TargetSlot));
                }
                if (OwnerIsSoleAnomalyInPlay(ctx) && ctx.State<IExtraUseState>() is { BonusConsumedThisNight: false })
                {
                    effects.Add(new GrantExtraUse(ctx.OwnerSlot));
                }
            }
            return PowerOutcome.Accept(effects, roleGuessed ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }

        // "Still in play" = anomaly faction and neither chained nor eliminated. Exactly one such slot means the
        // owner (who is casting, so necessarily in play) is the only anomaly left.
        private static bool OwnerIsSoleAnomalyInPlay(in PowerContext ctx)
        {
            IRosterView roster = ctx.Roster;
            if (roster == null)
            {
                return false;
            }
            return roster.Slots.Count(s =>
                roster.FactionOf(s) == FactionType.anomaly && !roster.IsChained(s) && !roster.IsEliminated(s)) == 1;
        }
    }
}
