namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PChainedByTheShadows's power-local grant port (Abyss, Lot B): give the owner one extra use of this
    /// power for the current night and mark the once-per-night bonus consumed. The use-count NetworkVariable
    /// is engine-coupled — only the carrier knows it — so, like <see cref="ILegacyGrant"/>, the pure decision
    /// just emits the GrantExtraUse intention and the carrier realises it here.
    /// </summary>
    public interface IExtraUseGrant
    {
        void GrantExtraUse();
    }
}
