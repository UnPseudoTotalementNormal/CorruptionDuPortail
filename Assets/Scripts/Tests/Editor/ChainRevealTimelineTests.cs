using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19 (maquettes enchainement v4: D2 + Y1): the chained cat is carried to the middle of the circle while
    /// it rises, its card is shown in front of it, then it falls to the floor, frozen on its flattest pose, and goes back
    /// to its seat under the black.
    /// </summary>
    public class ChainRevealTimelineTests
    {
        [Test]
        public void Travel_EasesOut_AndReachesTheMiddleWhenTheLiftEnds()
        {
            Assert.That(ChainRevealTimeline.Travel(0f), Is.EqualTo(0f));
            float firstHalf = ChainRevealTimeline.Travel(ChainRevealTimeline.LiftEnd / 2f);
            Assert.That(firstHalf, Is.GreaterThan(0.75f), "most of the way done in the first half (slow at the end)");
            Assert.That(ChainRevealTimeline.Travel(ChainRevealTimeline.LiftEnd), Is.EqualTo(1f));
            Assert.That(ChainRevealTimeline.Travel(5f), Is.EqualTo(1f));
        }

        [Test]
        public void Height_RisesWithTheTravel_FloatsWhileTheCardIsShown_ThenFallsToTheFloor()
        {
            Assert.That(ChainRevealTimeline.Height(0f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.Height(ChainRevealTimeline.LiftEnd), Is.EqualTo(1f).Within(1e-4f));
            for (float t = ChainRevealTimeline.LiftEnd; t < ChainRevealTimeline.Release; t += 0.1f)
            {
                Assert.That(ChainRevealTimeline.Height(t), Is.EqualTo(1f).Within(ChainRevealTimeline.BobShare + 1e-4f), $"floating at {t}");
            }
            Assert.That(ChainRevealTimeline.Height(ChainRevealTimeline.Impact - 0.01f), Is.LessThan(0.1f), "almost down just before the impact");
            Assert.That(ChainRevealTimeline.Height(ChainRevealTimeline.Impact), Is.EqualTo(0f), "on the floor at the impact");
            Assert.That(ChainRevealTimeline.Height(ChainRevealTimeline.BlackOut), Is.EqualTo(0f));
        }

        [Test]
        public void SoundBeats_FollowEachOther_BeforeTheBlack()
        {
            // ChainRevealDirector plays one sound on each: the release, the impact, the get-up.
            Assert.That(ChainRevealTimeline.Release, Is.GreaterThan(ChainRevealTimeline.LiftEnd));
            Assert.That(ChainRevealTimeline.Impact, Is.GreaterThan(ChainRevealTimeline.Release));
            Assert.That(ChainRevealTimeline.GetUp, Is.EqualTo(ChainRevealTimeline.Flat + ChainRevealTimeline.FreezeSeconds).Within(1e-5f));
            Assert.That(ChainRevealTimeline.GetUp, Is.GreaterThan(ChainRevealTimeline.Impact));
            Assert.That(ChainRevealTimeline.GetUp, Is.LessThan(ChainRevealTimeline.BlackOut), "the cat gets up before the black");
        }

        [Test]
        public void Fall_Accelerates()
        {
            float t0 = ChainRevealTimeline.Release, t1 = (t0 + ChainRevealTimeline.Impact) / 2f;
            float firstHalf = ChainRevealTimeline.Height(t0) - ChainRevealTimeline.Height(t1);
            float secondHalf = ChainRevealTimeline.Height(t1) - ChainRevealTimeline.Height(ChainRevealTimeline.Impact - 1e-4f);
            Assert.That(secondHalf, Is.GreaterThan(firstHalf * 2f), "falls faster and faster");
        }

        [Test]
        public void ClipTime_MatchesTheClipsImpactAndFreezesOnTheFlattestPose()
        {
            Assert.That(ChainRevealTimeline.ClipTime(0f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.ClipTime(ChainRevealTimeline.LiftEnd), Is.EqualTo(ChainRevealTimeline.ClipHold).Within(1e-4f));
            Assert.That(ChainRevealTimeline.ClipTime(ChainRevealTimeline.Release), Is.EqualTo(ChainRevealTimeline.ClipRelease).Within(1e-4f));
            Assert.That(ChainRevealTimeline.ClipTime(ChainRevealTimeline.Impact), Is.EqualTo(ChainRevealTimeline.ClipImpact).Within(1e-4f),
                "the clip's own squash lands when the cat reaches the floor");
            float frozen = ChainRevealTimeline.Flat + ChainRevealTimeline.FreezeSeconds / 2f;
            Assert.That(ChainRevealTimeline.ClipTime(frozen), Is.EqualTo(ChainRevealTimeline.ClipFlat));
            Assert.That(ChainRevealTimeline.ClipTime(ChainRevealTimeline.Flat + ChainRevealTimeline.FreezeSeconds + 0.1f),
                Is.GreaterThan(ChainRevealTimeline.ClipFlat), "plays on after the freeze (it gets up)");
            Assert.That(ChainRevealTimeline.ClipTime(ChainRevealTimeline.BlackOut), Is.GreaterThan(ChainRevealTimeline.ClipEnd),
                "standing in its chained idle when the black comes");
        }

        [Test]
        public void ClipTime_NeverGoesBack()
        {
            float previous = -1f;
            for (float t = 0f; t < ChainRevealTimeline.BlackOut; t += 0.02f)
            {
                float clip = ChainRevealTimeline.ClipTime(t);
                Assert.That(clip, Is.GreaterThanOrEqualTo(previous), $"at {t}");
                previous = clip;
            }
        }

        [Test]
        public void CardFlight_ComesWhileTheCatFloats_AndIsBackBeforeItFalls()
        {
            Assert.That(ChainRevealTimeline.CardIn, Is.GreaterThanOrEqualTo(ChainRevealTimeline.LiftEnd), "the cat is in place first");
            Assert.That(ChainRevealTimeline.CardFlight(ChainRevealTimeline.CardIn), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.CardFlight(ChainRevealTimeline.CardArrived), Is.EqualTo(1f));
            Assert.That(ChainRevealTimeline.CardFlight((ChainRevealTimeline.CardArrived + ChainRevealTimeline.CardOut) / 2f), Is.EqualTo(1f));
            Assert.That(ChainRevealTimeline.CardFlight(ChainRevealTimeline.CardBack), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.CardBack, Is.LessThanOrEqualTo(ChainRevealTimeline.Release), "back in its row before the fall");
        }

        [Test]
        public void Darkness_FallsAtTheStart_AndHoldsUntilTheBlack()
        {
            Assert.That(ChainRevealTimeline.Darkness(0f), Is.EqualTo(0f));
            Assert.That(ChainRevealTimeline.Darkness(ChainRevealTimeline.DarkenSeconds), Is.EqualTo(1f));
            Assert.That(ChainRevealTimeline.Darkness(ChainRevealTimeline.BlackOut - 0.01f), Is.EqualTo(1f));
        }

        [Test]
        public void HoverDistance_IsTheMiddle_UnlessTheCatWouldNotFit()
        {
            // Today's circle: the player 14 m from the middle, a 14.85 m cat, 40° field → pushed back (mockups v4 D2).
            float today = ChainRevealTimeline.HoverDistance(14f, 14.85f, 40f, 0.68f);
            Assert.That(today, Is.EqualTo(30f).Within(0.5f));
            // A big circle (Poyo: then D1): the middle itself.
            Assert.That(ChainRevealTimeline.HoverDistance(60f, 14.85f, 40f, 0.68f), Is.EqualTo(60f));
        }

        [Test]
        public void FeetHeight_RisesFromTheSeatThenLandsOnTheFloorBelowIt()
        {
            const float Lift = 9f, Seat = 10f;
            Assert.AreEqual(0f, ChainRevealTimeline.FeetHeight(0f, Lift, Seat), 1e-4f, "starts on its seat");
            float _before = ChainRevealTimeline.FeetHeight(ChainRevealTimeline.Release - 1e-3f, Lift, Seat);
            float _after = ChainRevealTimeline.FeetHeight(ChainRevealTimeline.Release + 1e-3f, Lift, Seat);
            Assert.AreEqual(_before, _after, 0.05f, "no jump when it is released");
            Assert.AreEqual(-Seat, ChainRevealTimeline.FeetHeight(ChainRevealTimeline.Impact, Lift, Seat), 1e-4f, "on the floor");
            Assert.AreEqual(-Seat, ChainRevealTimeline.FeetHeight(ChainRevealTimeline.BlackOut, Lift, Seat), 1e-4f, "stays there");
        }
    }
}
