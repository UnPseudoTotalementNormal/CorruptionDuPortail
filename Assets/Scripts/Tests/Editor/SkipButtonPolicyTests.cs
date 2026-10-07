using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// The board's single skip button (GD task "Détails d'interface"): red while usable, grey and inert once used.
    /// It ends the awakening at night and skips the vote during the vote.
    /// </summary>
    [Category("UI")]
    public class SkipButtonPolicyTests
    {
        [Test]
        public void Night_AwakePlayer_CanStopTheAwakening()
        {
            SkipButtonState _state = SkipButtonPolicy.Resolve(false, true, false, false);
            Assert.AreEqual(SkipButtonMode.StopAwakening, _state.Mode);
            Assert.IsTrue(_state.Interactable);
        }

        [Test]
        public void Night_AsleepPlayer_ButtonIsGrey()
        {
            SkipButtonState _state = SkipButtonPolicy.Resolve(false, false, true, false);
            Assert.AreEqual(SkipButtonMode.StopAwakening, _state.Mode);
            Assert.IsFalse(_state.Interactable, "Once the awakening is stopped (or outside it) the button is grey.");
        }

        [Test]
        public void Vote_NotVotedYet_CanSkipTheVote()
        {
            SkipButtonState _state = SkipButtonPolicy.Resolve(true, false, true, false);
            Assert.AreEqual(SkipButtonMode.SkipVote, _state.Mode);
            Assert.IsTrue(_state.Interactable);
        }

        [Test]
        public void Vote_AlreadyVoted_ButtonIsGrey()
        {
            SkipButtonState _state = SkipButtonPolicy.Resolve(true, false, true, true);
            Assert.AreEqual(SkipButtonMode.SkipVote, _state.Mode);
            Assert.IsFalse(_state.Interactable, "A vote is final: after skipping (or voting for a player) the button is grey.");
        }

        [Test]
        public void Vote_PlayerWhoCannotVote_ButtonIsGrey()
        {
            Assert.IsFalse(SkipButtonPolicy.Resolve(true, true, false, false).Interactable);
        }
    }
}
