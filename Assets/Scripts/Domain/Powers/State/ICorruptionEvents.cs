namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// Power-local port for corruption-outcome events (IFailablePower / ICorrupterPower). Each power's
    /// carrier fires ITS specific event, so CorruptionSucceeded/Failed effects stay uniform in the Domain.
    /// </summary>
    public interface ICorruptionEvents
    {
        void RaiseSucceeded(int slot);
        void RaiseFailed(int slot);
    }
}
