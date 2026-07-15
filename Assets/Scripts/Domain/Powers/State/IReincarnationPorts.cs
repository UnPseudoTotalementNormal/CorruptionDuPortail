namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PReincarnation's power-local passive-broadcast port: flip (and replicate) the power's isPassive flag.
    /// Engine-side (an NGO Everyone-RPC) so it lives on the carrier, not in the pure Domain.
    /// </summary>
    public interface ISetPassiveState
    {
        void SetPassive(bool value);
    }

    /// <summary>
    /// PReincarnation's power-local grant port: give the owner every power of the from-role's character.
    /// The engine Power refs are power-local, so the pure decision only emits the GrantRolePowers intention.
    /// </summary>
    public interface IGrantRolePowers
    {
        void GrantRolePowers(int ownerSlot, int fromRoleSlot);
    }
}
