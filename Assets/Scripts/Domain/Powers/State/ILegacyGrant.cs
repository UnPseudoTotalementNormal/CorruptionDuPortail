namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PLegacy's power-local grant port: hand the power's configured legacy power to the owner (and mark
    /// it inherited). The engine Power reference is power-local — only the carrier knows it — so the pure
    /// decision just emits the GrantLegacyPower intention and the carrier realises it here.
    /// </summary>
    public interface ILegacyGrant
    {
        void GrantLegacy(int ownerSlot);
    }
}
