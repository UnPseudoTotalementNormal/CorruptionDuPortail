namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PMarqueHurluberluges — Ugues' signature power. Passive: fired once at game start, it grants Ugues his
    /// stolen one-shot power copies. Which powers are stolen (chosen faction, non-passive, not Ugues, not an
    /// already-stolen copy) + the random-distinct pick over the LIVE roster + the engine spawn/reparent are
    /// engine-coupled and stay power-local on the carrier (via <see cref="State.IStolenPowerGrant"/>); the
    /// decision only emits the "grant Ugues his stolen copies" intention — mirrors <see cref="LegacyDecision"/>.
    /// The pure pick mechanic itself lives in the EditMode-tested <c>StolenPowerSelector</c>.
    /// </summary>
    public sealed class MarqueHurluberlugesDecision : IPowerDecision
    {
        public PowerId Id => PowerId.MarqueHurluberluges;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
            => PowerOutcome.Accept(new GrantStolenPowers(ctx.OwnerSlot));
    }
}
