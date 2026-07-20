namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// Whether a power's use turned out RIGHT or WRONG from the caster's point of view — the answer to
    /// "did my power work?" that playtesters asked for. Orthogonal to <see cref="PowerOutcome.Accepted"/>,
    /// which only says the decision fired at all: every verdict-bearing decision Accepts on BOTH branches.
    ///
    /// The verdict grades the CASTER'S CHOICE, not the world state that followed. A power that guessed the
    /// right role is <see cref="Correct"/> even when the effect changed nothing (Drooly healing a target
    /// who was not corrupted): the guess was the player's decision, the rest is hidden information they
    /// could not have known. Design call — see investigations/power-use-verdict-feedback-investigation.md.
    /// </summary>
    public enum PowerVerdict
    {
        /// <summary>No notion of correctness — unconditional-effect powers (Legacy, Reincarnation, …). The default.</summary>
        None = 0,
        /// <summary>The caster's guess/target was right.</summary>
        Correct = 1,
        /// <summary>The caster's guess/target was wrong.</summary>
        Incorrect = 2,
    }
}
