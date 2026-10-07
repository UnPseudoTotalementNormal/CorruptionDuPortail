using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PLackOfAffection — runs on the CONTACTED target's client. If the target is a "chosen" faction, reveal
    /// the SENDER's (owner's) role AND corruption state to the target (server-local); and if this is the true-local
    /// target, post a local chat line: naming the sender's role for a chosen, only the visit otherwise (GD wording:
    /// a non-élu "ne recevra que la visite et non l'identité"). The faction-keyed contact SOUND stays adapter-side.
    /// </summary>
    public sealed class LackOfAffectionDecision : IPowerDecision
    {
        public PowerId Id => PowerId.LackOfAffection;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            bool chosen = ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen;
            if (chosen)
            {
                effects.Add(new RevealInfo(ctx.OwnerSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.TargetSlot, false));
                effects.Add(new RevealInfo(ctx.OwnerSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.TargetSlot, false));
            }
            if (ctx.IsTrueLocalTarget)
            {
                string line = chosen ? $"{ctx.Roster.RoleNameOf(ctx.OwnerSlot)} est venu(e) vous voir..." : "Quelqu'un est venu vous voir...";
                effects.Add(new ChatLocal(line, ChatWindows.Server));
            }
            return PowerOutcome.Accept(effects);
        }
    }
}
