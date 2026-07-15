namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PLegacy — passive (fires when the watched role becomes chained): grant the power's configured legacy
    /// power to the owner. The engine Power ref is power-local (the carrier holds it); the decision only says
    /// "grant to owner".
    /// </summary>
    public sealed class LegacyDecision : IPowerDecision
    {
        public PowerId Id => PowerId.Legacy;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(new GrantLegacyPower(ctx.OwnerSlot));
    }
}
