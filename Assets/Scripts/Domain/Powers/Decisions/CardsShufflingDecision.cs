using CorruptionDuPortail.Domain.Powers.State;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PCardsShuffling — the guess resolution: NewTargeting the clicked character; correct → record it +
    /// reveal its role to the owner; incorrect → append the guess-role's targeting list (or "none") to the
    /// message. Ends with one owner-directed broadcast chat. Engine reads via the guess report port.
    /// </summary>
    public sealed class CardsShufflingDecision : IPowerDecision
    {
        public PowerId Id => PowerId.CardsShuffling;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var guess = ctx.State<ICardsShufflingGuess>();
            string message = guess.IsCorrect
                ? $"Vous avez correctement deviné que {guess.ClickedPseudo} est {guess.GuessRoleName}."
                : $"Votre supposition était incorrecte, {guess.ClickedPseudo} n'est pas {guess.GuessRoleName}.";

            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            if (guess.IsCorrect)
            {
                effects.Add(new DiscoveredAdd(ctx.TargetSlot));
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
            }
            else if (guess.TargetedRoleNames.Count == 0)
            {
                message += $"\nLe role {guess.GuessRoleName} n'a ciblé aucun rôle.";
            }
            else
            {
                message += $"\nLe role {guess.GuessRoleName} a ciblé ces rôles:";
                foreach (var roleName in guess.TargetedRoleNames)
                {
                    message += $"\n- {roleName}";
                }
            }
            effects.Add(new ChatBroadcast(message, ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
            return PowerOutcome.Accept(effects, guess.IsCorrect ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
