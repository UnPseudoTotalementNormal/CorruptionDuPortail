using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19: the end of the vote builds up (maquettes compte-a-rebours v1, C2 = the last minute, louder and
    /// louder; B1 = a clock that speeds up). Calm before the last minute, a soft climb, the final seconds in full; when
    /// everyone has voted the few seconds left become the final phase.
    /// </summary>
    public class VoteCountdownTests
    {
        [Test]
        public void Tension_IsCalmBeforeTheLastMinute()
        {
            Assert.That(VoteCountdown.Tension(300f), Is.EqualTo(0f));
            Assert.That(VoteCountdown.Tension(61f), Is.EqualTo(0f));
        }

        [Test]
        public void Tension_ClimbsSoftlyThenFullyAndNeverGoesBack()
        {
            float _previous = -1f;
            for (float _t = 70f; _t >= -1f; _t -= 0.25f)
            {
                float _tension = VoteCountdown.Tension(_t);
                Assert.That(_tension, Is.GreaterThanOrEqualTo(_previous - 1e-5f), $"tension went down at {_t} s");
                Assert.That(_tension, Is.InRange(0f, 1f));
                _previous = _tension;
            }
            Assert.That(VoteCountdown.Tension(10f), Is.EqualTo(VoteCountdown.SoftTension).Within(1e-4f), "the soft phase ends at the soft tension");
            Assert.That(VoteCountdown.Tension(0f), Is.EqualTo(1f));
        }

        [Test]
        public void Tension_EveryoneVoted_TheSecondsLeftAreTheFinalPhase()
        {
            // The timer fell to 5 s: those 5 s go from the soft tension to the full one.
            Assert.That(VoteCountdown.Tension(5f, 5f), Is.EqualTo(VoteCountdown.SoftTension).Within(1e-4f));
            Assert.That(VoteCountdown.Tension(2.5f, 5f), Is.EqualTo(VoteCountdown.Tension(5f)).Within(1e-4f), "the whole final phase, compressed into 5 s");
            Assert.That(VoteCountdown.Tension(0f, 5f), Is.EqualTo(1f));
        }

        [Test]
        public void Ticks_OnlyInTheLastMinute_OncePerSecondThenFaster()
        {
            Assert.That(VoteCountdown.IsTicking(120f), Is.False);
            Assert.That(VoteCountdown.IsTicking(60f), Is.True);
            Assert.That(VoteCountdown.IsTicking(0f), Is.False);
            Assert.That(VoteCountdown.TickInterval(45f), Is.EqualTo(1f));
            Assert.That(VoteCountdown.TickInterval(10f), Is.EqualTo(1f));
            Assert.That(VoteCountdown.TickInterval(5f), Is.LessThan(1f));
            Assert.That(VoteCountdown.TickInterval(0f), Is.EqualTo(VoteCountdown.FastestTickInterval).Within(1e-4f));
        }

        [Test]
        public void Ticks_GetLouder()
        {
            Assert.That(VoteCountdown.TickVolume(59f), Is.LessThan(0.2f), "barely audible a minute before the end");
            Assert.That(VoteCountdown.TickVolume(30f), Is.GreaterThan(VoteCountdown.TickVolume(59f)));
            Assert.That(VoteCountdown.TickVolume(5f), Is.GreaterThan(VoteCountdown.TickVolume(30f)));
            Assert.That(VoteCountdown.TickVolume(0f), Is.EqualTo(1f));
        }

        [Test]
        public void TurnAgitation_CalmThenNervousOnTheLastThird()
        {
            Assert.That(VoteCountdown.TurnAgitation(30f, 30f), Is.EqualTo(0f));
            Assert.That(VoteCountdown.TurnAgitation(11f, 30f), Is.EqualTo(0f), "two thirds of the turn: still calm");
            Assert.That(VoteCountdown.TurnAgitation(5f, 30f), Is.InRange(0.2f, 0.9f));
            Assert.That(VoteCountdown.TurnAgitation(0f, 30f), Is.EqualTo(1f));
        }

        [Test]
        public void Melt_FollowsTheShareOfTheVoteGone()
        {
            Assert.That(VoteCountdown.Melt(300f, 300f), Is.EqualTo(0f));
            Assert.That(VoteCountdown.Melt(150f, 300f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(VoteCountdown.Melt(-2f, 300f), Is.EqualTo(1f));
            Assert.That(VoteCountdown.Melt(10f, 0f), Is.EqualTo(1f), "no duration: burnt");
        }

        [Test]
        public void SpanAfterJump_EveryoneVotedOnlyOnABigDrop()
        {
            Assert.That(VoteCountdown.SpanAfterJump(212f, 5f, 10f), Is.EqualTo(5f), "everyone voted: 5 s of final phase");
            Assert.That(VoteCountdown.SpanAfterJump(212f, 211f, 10f), Is.EqualTo(10f), "a normal second");
            Assert.That(VoteCountdown.SpanAfterJump(8f, 5f, 10f), Is.EqualTo(5f), "dropped to 5 s from inside the final phase");
            Assert.That(VoteCountdown.SpanAfterJump(212f, 0.2f, 10f), Is.EqualTo(1f), "never shorter than a second");
        }
    }
}
