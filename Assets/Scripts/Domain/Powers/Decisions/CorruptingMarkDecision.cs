namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCorruptingMark — active: target the picked character, store it as last-corrupted (power-local state),
    /// raise the corruption-succeeded event, and corrupt it. Validity is an adapter precondition.
    /// </summary>
    public sealed class CorruptingMarkDecision : IPowerDecision
    {
        public PowerId Id => PowerId.CorruptingMark;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new StoreLastCorrupted(ctx.TargetSlot),
                new CorruptionSucceeded(ctx.TargetSlot),
                new CorruptPlayer(ctx.TargetSlot));
    }
}
