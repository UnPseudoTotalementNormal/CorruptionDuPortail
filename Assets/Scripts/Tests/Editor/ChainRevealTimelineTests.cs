using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19 (maquettes enchainement v2: E1 + S2 + F2): the chained cat is lifted, falls back at the clip's
    /// impact, the sound is sucked out just before it, and the flattest pose is held half a second.
    /// </summary>
    public class ChainRevealTimelineTests
    {
        [Test]
        public void Lift_RisesSlowly_ThenFallsToTheSeatAtTheImpact()
        {
            Assert.That(ChainRevealTimeline.Lift(0f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.Lift(1.0f), Is.GreaterThan(0f).And.LessThan(0.5f), "still rising slowly");
            Assert.That(ChainRevealTimeline.Lift(ChainRevealTimeline.Release - 0.01f), Is.EqualTo(1f).Within(0.06f), "held at the top");
            float justBefore = ChainRevealTimeline.Lift(ChainRevealTimeline.Impact - 0.01f);
            Assert.That(justBefore, Is.LessThan(0.1f), "almost down just before the impact");
            Assert.That(ChainRevealTimeline.Lift(ChainRevealTimeline.Impact), Is.EqualTo(0f), "on its seat at the impact");
            Assert.That(ChainRevealTimeline.Lift(5f), Is.EqualTo(0f));
        }

        [Test]
        public void Fall_Accelerates()
        {
            float t0 = ChainRevealTimeline.Release, t1 = (t0 + ChainRevealTimeline.Impact) / 2f;
            float firstHalf = ChainRevealTimeline.Lift(t0) - ChainRevealTimeline.Lift(t1);
            float secondHalf = ChainRevealTimeline.Lift(t1) - ChainRevealTimeline.Lift(ChainRevealTimeline.Impact - 1e-4f);
            Assert.That(secondHalf, Is.GreaterThan(firstHalf * 2f), "falls faster and faster");
        }

        [Test]
        public void Sound_IsSuckedOut_SilentBeforeTheImpact_AndBackWithTheSplatch()
        {
            Assert.That(ChainRevealTimeline.SuckVolume(ChainRevealTimeline.SuckStart - 0.01f), Is.EqualTo(1f));
            float mid = (ChainRevealTimeline.SuckStart + ChainRevealTimeline.SilenceStart) / 2f;
            Assert.That(ChainRevealTimeline.SuckVolume(mid), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(ChainRevealTimeline.SuckCutoff(mid), Is.LessThan(3000f), "muffled while sucked out");
            Assert.That(ChainRevealTimeline.SuckVolume(ChainRevealTimeline.SilenceStart + 0.01f), Is.EqualTo(0f), "a beat of silence");
            Assert.That(ChainRevealTimeline.SuckVolume(ChainRevealTimeline.Impact), Is.EqualTo(1f), "the splatch is heard");
            Assert.That(ChainRevealTimeline.SuckCutoff(ChainRevealTimeline.Impact), Is.EqualTo(22000f), "and not muffled");
        }

        [Test]
        public void Music_StaysOut_UntilTheFreezeIsOver()
        {
            float endOfFreeze = ChainRevealTimeline.Flat + ChainRevealTimeline.FreezeSeconds;
            Assert.That(ChainRevealTimeline.MusicVolume(ChainRevealTimeline.Impact + 0.1f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.MusicVolume(endOfFreeze - 0.01f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.MusicVolume(endOfFreeze + ChainRevealTimeline.MusicBackSeconds), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Freeze_HoldsTheFlattestPoseHalfASecond_WithTheCameraJumpedIn()
        {
            Assert.That(ChainRevealTimeline.IsFrozen(ChainRevealTimeline.Flat - 0.01f), Is.False);
            Assert.That(ChainRevealTimeline.IsFrozen(ChainRevealTimeline.Flat), Is.True);
            Assert.That(ChainRevealTimeline.IsFrozen(ChainRevealTimeline.Flat + 0.49f), Is.True);
            Assert.That(ChainRevealTimeline.IsFrozen(ChainRevealTimeline.Flat + 0.5f), Is.False);
            Assert.That(ChainRevealTimeline.PunchIn(ChainRevealTimeline.Impact - 0.01f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.PunchIn(ChainRevealTimeline.Flat + 0.25f), Is.EqualTo(1f), "held during the freeze");
            Assert.That(ChainRevealTimeline.PunchIn(ChainRevealTimeline.Flat + 0.5f + ChainRevealTimeline.PunchOutSeconds), Is.EqualTo(0f));
        }
    }
}
