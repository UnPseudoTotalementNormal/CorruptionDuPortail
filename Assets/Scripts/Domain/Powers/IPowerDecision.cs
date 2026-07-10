namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// A single power's decision logic — a pure POCO. Given a <see cref="PowerContext"/> it returns a
    /// <see cref="PowerOutcome"/> (ordered effect intentions + uses consumed). No engine, no NGO, no
    /// singleton: fully EditMode-testable. There is exactly one implementation per power; there is NO
    /// shared resolver and NO central switch over powers.
    /// </summary>
    public interface IPowerDecision
    {
        PowerId Id { get; }
        /// <summary>Passive powers resolve on a lifecycle hook (e.g. game start); active ones on use.</summary>
        bool IsPassive { get; }
        PowerOutcome Decide(in PowerContext ctx);
    }
}
