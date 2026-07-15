namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PEmbraceOfShadows — active (char+role): NewTargeting is unconditional; if the target's role matches
    /// the picked role, corrupt it, raise the success event, and reveal its corruption + role to the owner
    /// (server-local); otherwise raise the failure event. Success/failure SOUNDS stay adapter-side.
    /// </summary>
    public sealed class EmbraceOfShadowsDecision : IPowerDecision
    {
        public PowerId Id => PowerId.EmbraceOfShadows;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            if (ctx.Roster.SameRole(ctx.TargetSlot, ctx.SecondaryTargetSlot))
            {
                return PowerOutcome.Accept(
                    new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                    new CorruptPlayer(ctx.TargetSlot),
                    new CorruptionSucceeded(ctx.TargetSlot),
                    new RevealInfo(ctx.TargetSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.OwnerSlot, false),
                    new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, false));
            }
            return PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new CorruptionFailed(ctx.TargetSlot));
        }
    }
}
