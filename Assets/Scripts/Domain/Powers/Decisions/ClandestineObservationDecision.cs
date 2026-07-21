using System.Linq;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PClandestineObservation (Traqueuse) — active, multi-target (Lot D): the owner picks as many players as
    /// there are non-chosen in the starting composition, and learns how many of them are "chosen" (élus). The
    /// count is announced to the owner alone. Pure: the roster supplies each picked slot's faction.
    /// </summary>
    public sealed class ClandestineObservationDecision : IPowerDecision
    {
        public PowerId Id => PowerId.ClandestineObservation;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            // Copy the roster out of the `in` parameter — an `in`/`ref` param can't be captured in a lambda.
            IRosterView roster = ctx.Roster;
            int chosenCount = ctx.TargetSlots.Count(slot => roster.FactionOf(slot) == FactionType.chosen);
            string message = $"Parmi les joueurs observés, {chosenCount} sont des élus.";
            return PowerOutcome.Accept(new ChatBroadcast(message, ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
        }
    }
}
