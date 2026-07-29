using System.Collections.Generic;
using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PVisionOfTheImpossible — active (multi-guess): for each guessed character (in order), emit one
    /// NewTargeting; on the FIRST whose role matches a guessed role, append the "found" line and STOP.
    /// If none matched, the "not found" line. Ends with one owner-directed broadcast chat.
    /// </summary>
    public sealed class VisionOfTheImpossibleDecision : IPowerDecision
    {
        public PowerId Id => PowerId.VisionOfTheImpossible;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            string message = string.Empty;
            foreach (var guess in ctx.State<IVisionGuesses>().Guesses)
            {
                effects.Add(new NewTargeting(ctx.OwnerSlot, guess.Slot));
                if (guess.Matches)
                {
                    if (message != string.Empty) message += "\n";
                    message += $"{guess.Pseudo} est l'un de ces personnages.";
                    break;
                }
            }
            bool foundOne = message != string.Empty;
            if (!foundOne) message = "Aucun personnage n'a été trouvé.";
            effects.Add(new ChatBroadcast(message, ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
            return PowerOutcome.Accept(effects, foundOne ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
