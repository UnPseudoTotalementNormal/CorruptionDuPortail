using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PBlessing — active (char + role): target the picked character; if its role matches the picked role,
    /// heal it (unless already healed), reveal its role to the owner (broadcast), bless it, and announce it
    /// to the owner. NewTargeting is unconditional.
    /// </summary>
    public sealed class BlessingDecision : IPowerDecision
    {
        public PowerId Id => PowerId.Blessing;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            bool roleGuessed = ctx.Roster.SameRole(ctx.TargetSlot, ctx.SecondaryTargetSlot);
            if (roleGuessed)
            {
                if (!ctx.Roster.IsHealed(ctx.TargetSlot))
                {
                    effects.Add(new HealPlayer(ctx.TargetSlot));
                }
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
                effects.Add(new SetBlessed(ctx.TargetSlot));
                effects.Add(new ChatBroadcast(
                    $"{ctx.Roster.PseudoOf(ctx.TargetSlot)} est maintenant béni.", ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
            }
            return PowerOutcome.Accept(effects, roleGuessed ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
