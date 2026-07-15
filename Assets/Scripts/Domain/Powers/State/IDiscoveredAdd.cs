namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// PCardsShuffling's power-local write port: add a slot to the power's discovered-list (an NGO
    /// NetworkList on the carrier). The pure decision only emits the DiscoveredAdd intention.
    /// </summary>
    public interface IDiscoveredAdd
    {
        void DiscoveredAdd(int slot);
    }
}
