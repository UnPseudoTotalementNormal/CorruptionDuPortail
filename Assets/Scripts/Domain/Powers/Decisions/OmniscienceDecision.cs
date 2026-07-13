namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// POmniscience (the hack) — active: target the picked character, store it as the hacked target
    /// (power-local state, emitted as the StoreHackTarget effect), reveal its role to the owner AND
    /// mark it "hacked" for the owner only (both Personal broadcasts to the Robot; the Hacked flag
    /// drives the owner-only glitch visual, see CardHackGlitch). Ends with a character refresh.
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
                new RevealInfo(ctx.TargetSlot, RevealField.Hacked, RevealVisibility.Personal, ctx.OwnerSlot, true),
                RequestCharacterRefresh.Instance);
    }
}
