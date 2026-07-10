namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PReincarnation — active: target the picked role's character, broadcast isPassive=true, then grant the
    /// owner every power of that role. Verbatim order: NewTargeting → passive broadcast → grant.
    /// </summary>
    public sealed class ReincarnationDecision : IPowerDecision
    {
        public PowerId Id => PowerId.Reincarnation;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new SetPassiveBroadcast(true),
                new GrantRolePowers(ctx.OwnerSlot, ctx.TargetSlot));
    }
}
