using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// <see cref="TooltipHoverTimeline"/>: the hover bubble waits before the first opening, then follows the pointer
    /// from one trigger to the next at once, and closes after a short grace (mockups infobulles v1, T1).
    /// </summary>
    [Category("Tooltip")]
    public class TooltipHoverTimelineTests
    {
        private const int Hand = 11;
        private const int Mouth = 22;

        [Test]
        public void FirstBubble_WaitsForTheOpenDelay()
        {
            var _timeline = new TooltipHoverTimeline();

            Assert.AreEqual(TooltipAction.None, _timeline.Tick(Hand, 0f));
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(Hand, 0.29f));
            Assert.AreEqual(TooltipAction.Show, _timeline.Tick(Hand, 0.3f));
            Assert.AreEqual(Hand, _timeline.Shown);
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(Hand, 1f));
        }

        [Test]
        public void SweepingAcrossTriggers_OpensNothing()
        {
            var _timeline = new TooltipHoverTimeline();

            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.15f);
            _timeline.Tick(Mouth, 0.2f);
            _timeline.Tick(Mouth, 0.35f);
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(0, 0.4f));
            Assert.AreEqual(0, _timeline.Shown);
        }

        [Test]
        public void NextTrigger_OpensAtOnceWhileABubbleIsShown()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);

            Assert.AreEqual(TooltipAction.Show, _timeline.Tick(Mouth, 0.5f));
            Assert.AreEqual(Mouth, _timeline.Shown);
        }

        [Test]
        public void LeavingATrigger_ClosesAfterTheGrace()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);

            Assert.AreEqual(TooltipAction.None, _timeline.Tick(0, 1f));
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(0, 1.1f));
            Assert.AreEqual(TooltipAction.Hide, _timeline.Tick(0, 1.25f));
            Assert.AreEqual(0, _timeline.Shown);
        }

        [Test]
        public void ComingBackWithinTheGrace_KeepsTheBubble()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);

            _timeline.Tick(0, 1f);
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(Hand, 1.1f));
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(0, 1.2f));
            Assert.AreEqual(TooltipAction.None, _timeline.Tick(0, 1.3f));
            Assert.AreEqual(Hand, _timeline.Shown);
        }

        [Test]
        public void JustAfterABubbleClosed_TheNextOneOpensAtOnce()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);
            _timeline.Tick(0, 1f);
            _timeline.Tick(0, 1.2f);

            Assert.AreEqual(TooltipAction.Show, _timeline.Tick(Mouth, 1.5f));
        }

        [Test]
        public void LongAfterABubbleClosed_TheDelayIsBack()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);
            _timeline.Tick(0, 1f);
            _timeline.Tick(0, 1.2f);

            Assert.AreEqual(TooltipAction.None, _timeline.Tick(Mouth, 2f));
            Assert.AreEqual(TooltipAction.Show, _timeline.Tick(Mouth, 2.5f));
        }

        [Test]
        public void Hide_ClosesAtOnce_AndOnlyOnce()
        {
            var _timeline = new TooltipHoverTimeline();
            _timeline.Tick(Hand, 0f);
            _timeline.Tick(Hand, 0.3f);

            Assert.AreEqual(TooltipAction.Hide, _timeline.Hide(0.5f));
            Assert.AreEqual(TooltipAction.None, _timeline.Hide(0.6f));
            Assert.AreEqual(0, _timeline.Shown);
        }
    }
}
