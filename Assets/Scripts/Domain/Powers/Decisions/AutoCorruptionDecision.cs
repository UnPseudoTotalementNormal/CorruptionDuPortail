namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PAutoCorruption — passive: on game start, corrupt the power's OWN owner. One brick, no target.
    /// </summary>
    public sealed class AutoCorruptionDecision : IPowerDecision
    {
        public PowerId Id => PowerId.AutoCorruption;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(new CorruptPlayer(ctx.OwnerSlot));
    }
}
