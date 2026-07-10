namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PInfiniteMessage — passive (fires on the power being reparented): set the owner's messageLeft to
    /// int.MaxValue. Trivial decision, one state-write effect.
    /// </summary>
    public sealed class InfiniteMessageDecision : IPowerDecision
    {
        public PowerId Id => PowerId.InfiniteMessage;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(new SetMessageLeft(ctx.OwnerSlot, int.MaxValue));
    }
}
