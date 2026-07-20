using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PDroolyHealing (Glooby) — active (char + role): NewTargeting is unconditional; if the picked
    /// character's role matches the picked role, heal it (only when it is actually corrupted or already
    /// healed — healing a healthy target is a no-op) and reveal its role to the owner.
    ///
    /// VERDICT semantics: the guess is graded on the ROLE MATCH ALONE. Guessing right on a target who
    /// happened to be healthy is still <see cref="PowerVerdict.Correct"/> — the caster could not know the
    /// corruption state, so the deduction is what gets graded. Design call, see
    /// investigations/power-use-verdict-feedback-investigation.md.
    ///
    /// Stays adapter-side (engine/state-coupled, not effect intentions): the per-night healed roster used
    /// for the end-of-night chat announce, and the success/failure FMOD one-shot.
    /// </summary>
    public sealed class DroolyHealingDecision : IPowerDecision
    {
        public PowerId Id => PowerId.DroolyHealing;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };

            bool roleGuessed = ctx.Roster.SameRole(ctx.TargetSlot, ctx.SecondaryTargetSlot);
            if (!roleGuessed)
            {
                return PowerOutcome.Accept(effects, PowerVerdict.Incorrect);
            }

            // v1 parity: the heal only fires on a corrupted/already-healed target, and only that branch
            // refreshed the roster. The reveal fires on any role match, healed or not.
            if (ctx.Roster.IsCorrupted(ctx.TargetSlot) || ctx.Roster.IsHealed(ctx.TargetSlot))
            {
                effects.Add(new HealPlayer(ctx.TargetSlot));
                effects.Add(RequestCharacterRefresh.Instance);
            }
            effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));

            return PowerOutcome.Accept(effects, PowerVerdict.Correct);
        }
    }
}
