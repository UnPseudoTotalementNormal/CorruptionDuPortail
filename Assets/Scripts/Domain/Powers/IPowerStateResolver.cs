namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// Resolves a power's own replicated-state PORT (a narrow interface like ICardsShufflingState) for
    /// the decision to read/mutate through, without ever touching a NetworkVariable. In production the
    /// resolver is the power's thin NetworkBehaviour carrier; in EditMode tests it is a plain fake.
    /// </summary>
    public interface IPowerStateResolver
    {
        TPort Resolve<TPort>() where TPort : class;
    }
}
