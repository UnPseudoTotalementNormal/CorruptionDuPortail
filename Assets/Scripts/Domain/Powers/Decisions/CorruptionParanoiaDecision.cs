namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCorruptionParanoia — passive: on game start, reveal the owner's OWN corruption to itself
    /// (broadcast). The whole power's logic lives here, in one pure file. Reuses the shared
    /// <see cref="RevealInfo"/> effect brick from the Domain effect vocabulary.
    /// </summary>
    public sealed class CorruptionParanoiaDecision : IPowerDecision
    {
        public PowerId Id => PowerId.CorruptionParanoia;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            return PowerOutcome.Accept(
                new RevealInfo(ctx.OwnerSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
        }
    }
}
