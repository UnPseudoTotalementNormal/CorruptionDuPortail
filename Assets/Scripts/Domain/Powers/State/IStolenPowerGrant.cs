namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PMarqueHurluberluges' (Ugues') power-local grant port: steal + hand the owner his one-shot power
    /// copies. The eligibility filter (chosen faction, non-passive, not Ugues, not already-stolen) + the
    /// random-distinct pick over the LIVE roster + the engine spawn/reparent (GivePowerToCharacter) are all
    /// engine-coupled, so — like <see cref="ILegacyGrant"/> and <see cref="IGrantRolePowers"/> — they live on
    /// the carrier and the pure decision only emits the GrantStolenPowers intention.
    /// </summary>
    public interface IStolenPowerGrant
    {
        void GrantStolen(int ownerSlot);
    }
}
