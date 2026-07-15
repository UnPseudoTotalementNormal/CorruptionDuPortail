namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>PCorruptingMark's power-local state port: the last-corrupted slot (read by its concentrated effect).</summary>
    public interface ILastCorruptedState
    {
        void StoreLastCorrupted(int slot);
    }
}
