using CorruptionDuPortail.Domain.Localization;
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
                ? Loc.Token("Vous avez correctement deviné que {0} est {1}.", guess.ClickedPseudo, Loc.Token(guess.GuessRoleName))
                : Loc.Token("Votre supposition était incorrecte, {0} n'est pas {1}.", guess.ClickedPseudo, Loc.Token(guess.GuessRoleName));

            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            if (guess.IsCorrect)
            {
                effects.Add(new DiscoveredAdd(ctx.TargetSlot));
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
            }
            else if (guess.TargetedRoleNames.Count == 0)
            {
                message += "\n" + Loc.Token("Le rôle {0} n'a ciblé aucun rôle.", Loc.Token(guess.GuessRoleName));
            }
            else
            {
                message += "\n" + Loc.Token("Le rôle {0} a ciblé ces rôles :", Loc.Token(guess.GuessRoleName));
                foreach (var roleName in guess.TargetedRoleNames)
                {
                    message += "\n- " + Loc.Token(roleName);
                }
            }
            effects.Add(new ChatBroadcast(message, ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
            return PowerOutcome.Accept(effects, guess.IsCorrect ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
