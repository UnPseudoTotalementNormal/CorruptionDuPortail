using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>PBoundByInk's power-local chat id (assigned when the ink chat is created).</summary>
    public interface IInkChatState { int ChatId { get; } }

    /// <summary>
    /// PTargetedByReport's engine-computed list of the logical slots that targeted the Orpheline this
    /// night (Lot C.2). Deduplicated upstream by RoleTargetSystem.GetAllTargetersForTarget (a HashSet).
    /// </summary>
    public interface ITargetedByReport { IReadOnlyList<int> TargeterSlots { get; } }

    /// <summary>
    /// PChainedByTheShadows's once-per-night guard: true once the sole-anomaly extra use has already been
    /// granted this night, so a second correct guess the same night does not grant another (Lot B, B2).
    /// The flag lives power-local (server-side) and resets at each awakening start.
    /// </summary>
    public interface IExtraUseState { bool BonusConsumedThisNight { get; } }

    /// <summary>One reduced guess for PVisionOfTheImpossible: slot, whether its role matches a guessed role, pseudo.</summary>
    public readonly struct VisionGuess
    {
        public int Slot { get; }
        public bool Matches { get; }
        public string Pseudo { get; }
        public VisionGuess(int slot, bool matches, string pseudo) { Slot = slot; Matches = matches; Pseudo = pseudo; }
    }

    public interface IVisionGuesses { IReadOnlyList<VisionGuess> Guesses { get; } }

    /// <summary>PCardsShuffling's engine-computed guess result (pseudo/role names + targeting list).</summary>
    public interface ICardsShufflingGuess
    {
        bool IsCorrect { get; }
        string ClickedPseudo { get; }
        string GuessRoleName { get; }
        IReadOnlyList<string> TargetedRoleNames { get; }
    }
}
