using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PLackOfAffection — runs on the CONTACTED target's client. If the target is a "chosen" faction, reveal
    /// the SENDER's (owner's) role to the target (server-local); and if this is the true-local target, post a
    /// local chat line. The faction-keyed contact SOUND stays adapter-side.
    /// </summary>
    public sealed class LackOfAffectionDecision : IPowerDecision
    {
        public PowerId Id => PowerId.LackOfAffection;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            if (ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen)
            {
                effects.Add(new RevealInfo(ctx.OwnerSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.TargetSlot, false));
            }
            if (ctx.IsTrueLocalTarget)
            {
                effects.Add(new ChatLocal($"{ctx.Roster.RoleNameOf(ctx.OwnerSlot)} est venu(e) vous voir...", ChatWindows.Server));
            }
            return PowerOutcome.Accept(effects);
        }
    }
}
