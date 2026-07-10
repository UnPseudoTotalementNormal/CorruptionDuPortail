namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// POmniscience (the hack) — active: target the picked character, store it as the hacked target
    /// (power-local state, emitted as the StoreHackTarget effect), and reveal its role to the owner
    /// (broadcast = the notify-to-target intention). Ends with a character refresh.
    /// </summary>
    public sealed class OmniscienceDecision : IPowerDecision
    {
        public PowerId Id => PowerId.Omniscience;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new StoreHackTarget(ctx.TargetSlot),
                new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true),
                RequestCharacterRefresh.Instance);
    }
}
