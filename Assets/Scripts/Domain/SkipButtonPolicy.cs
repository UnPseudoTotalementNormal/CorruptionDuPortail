namespace CorruptionDuPortail.Domain
{
    /// <summary>What the board's single skip button does right now.</summary>
    public enum SkipButtonMode
    {
        /// <summary>Night: "Arrêter l'éveil" ends the local player's awakening.</summary>
        StopAwakening,

        /// <summary>Day vote: "Passer le vote" casts the local player's vote for nobody.</summary>
        SkipVote,
    }

    /// <summary>The skip button's label mode and whether a click does anything (red) or not (grey).</summary>
    public readonly struct SkipButtonState
    {
        public readonly SkipButtonMode Mode;
        public readonly bool Interactable;

        public SkipButtonState(SkipButtonMode mode, bool interactable)
        {
            Mode = mode;
            Interactable = interactable;
        }
    }

    /// <summary>
    /// Pure rule behind the board's skip button (one button for the night and the vote, GD task "Détails
    /// d'interface"): red while it can still be used, grey once used or when it has nothing to do. During the vote it
    /// skips the vote and turns grey as soon as the local player has voted (a vote is final, the server refuses a
    /// second one). Anywhere else it ends the awakening and is live only while the local player is awake.
    /// </summary>
    public static class SkipButtonPolicy
    {
        public static SkipButtonState Resolve(bool isVoteState, bool isAwakened, bool canVote, bool hasVoted)
        {
            if (isVoteState) return new SkipButtonState(SkipButtonMode.SkipVote, canVote && !hasVoted);
            return new SkipButtonState(SkipButtonMode.StopAwakening, isAwakened);
        }
    }
}
