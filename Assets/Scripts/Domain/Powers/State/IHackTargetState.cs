namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>
    /// POmniscience's power-local state port: the hacked target slot (later read by the win condition).
    /// The runtime carrier implements it; EditMode tests never need it (the decision only emits the
    /// StoreHackTarget effect, it does not read the state).
    /// </summary>
    public interface IHackTargetState
    {
        void StoreHackTarget(int slot);
    }
}
