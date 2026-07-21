using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCursedVision — active: target is targeted, corrupted, and its corruption revealed to the owner; a
    /// card effect + a server chat line announce whether the target is a "chosen" (élu). Card flag + verdict
    /// branch on the target's faction. The owner used to corrupt itself as a cost; that self-corruption was
    /// removed in the role-adjustment pass (the Repenti now begins the game corrupted via AutoCorruption
    /// instead of paying per use).
    /// </summary>
    public sealed class CursedVisionDecision : IPowerDecision
    {
        /// <summary>Adapter-supplied CursedVision card-effect id (set on the prefab).</summary>
        public int CardEffectId;

        public PowerId Id => PowerId.CursedVision;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            bool targetIsChosen = ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen;
            bool cardHidden = !targetIsChosen;
            string verdict = targetIsChosen
                ? $"{ctx.Roster.PseudoOf(ctx.TargetSlot)} est un élu."
                : $"{ctx.Roster.PseudoOf(ctx.TargetSlot)} n'est pas un élu.";

            return PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new CorruptPlayer(ctx.TargetSlot),
                new RevealInfo(ctx.TargetSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.OwnerSlot, false),
                new AddCardEffect(CardEffectId, ctx.TargetSlot, cardHidden),
                new ChatLocal(verdict, ChatWindows.Server));
        }
    }
}
