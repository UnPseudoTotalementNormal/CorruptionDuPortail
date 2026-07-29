using CorruptionDuPortail.Domain.Powers;

namespace UI.PowerFeedback
{
    /// <summary>
    /// THE SWAP POINT for power success/failure feedback.
    ///
    /// <see cref="PowerVerdictFeedbackRouter"/> owns the plumbing (which power, is it mine, when) and hands
    /// the finished grade to every implementation registered on it. An implementation only answers "show
    /// this verdict now" — it never touches NGO, ownership, or Power.
    ///
    /// Replacing the placeholder screen-border flash with the real designed feedback means: write a new
    /// MonoBehaviour implementing this interface, drop it in the router's list, delete
    /// <see cref="VerdictVignetteFeedback"/>. Power.cs, the 9 decision POCOs and the router stay untouched.
    /// </summary>
    public interface IPowerVerdictFeedback
    {
        /// <summary>
        /// Present the outcome of the LOCAL player's power use. Called on the caster's client only, and
        /// never with <see cref="PowerVerdict.None"/> — implementations do not need to guard for it.
        /// May be called again while a previous presentation is still running (rapid successive uses):
        /// implementations are responsible for restarting cleanly.
        /// </summary>
        void Play(PowerVerdict verdict);
    }
}
