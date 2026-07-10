using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.State
{
    /// <summary>PBoundByInk's power-local chat id (assigned when the ink chat is created).</summary>
    public interface IInkChatState { int ChatId { get; } }

    /// <summary>PClandestineObservation's engine-computed report of who targeted the observed role.</summary>
    public interface IClandestineReport
    {
        bool HasCharacters { get; }
        string RoleLabel { get; }
        int DistinctTargetingCount { get; }
    }

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
