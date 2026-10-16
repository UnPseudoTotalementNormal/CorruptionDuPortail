using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// The night parchment (board task T21): it unrolls, stays whole for the time it is given, then burns away in a
    /// second and reports itself done, so the presenter can hide it.
    /// </summary>
    [Category("Awakening")]
    public class ParchmentBannerTimelineTests
    {
        private const double Whole = 2.5;

        [Test]
        public void BeforeItStarts_ItIsRolledUpAndIntact()
        {
            ParchmentBannerFrame _frame = ParchmentBannerTimeline.Evaluate(-0.1, Whole);
            Assert.AreEqual(0, _frame.Unroll);
            Assert.AreEqual(0, _frame.Burn);
            Assert.IsFalse(_frame.Done);
        }

        [Test]
        public void Unrolling_EasesOutAndEndsFullyOpen()
        {
            double _half = ParchmentBannerTimeline.Evaluate(ParchmentBannerTimeline.UnrollSeconds / 2, Whole).Unroll;
            Assert.Greater(_half, 0.5, "Ease-out: more than half open at half the unrolling time.");
            Assert.Less(_half, 1);
            Assert.AreEqual(1, ParchmentBannerTimeline.Evaluate(ParchmentBannerTimeline.UnrollSeconds, Whole).Unroll);
        }

        [Test]
        public void WhileWhole_NothingBurnsAndTheRollersShow()
        {
            ParchmentBannerFrame _frame = ParchmentBannerTimeline.Evaluate(Whole - 0.01, Whole);
            Assert.AreEqual(1, _frame.Unroll);
            Assert.AreEqual(0, _frame.Burn);
            Assert.IsTrue(_frame.RollersVisible);
        }

        [Test]
        public void Burning_TheFrontAdvancesAndTheRollersAreGone()
        {
            ParchmentBannerFrame _start = ParchmentBannerTimeline.Evaluate(Whole + 0.25, Whole);
            ParchmentBannerFrame _later = ParchmentBannerTimeline.Evaluate(Whole + 0.75, Whole);
            Assert.IsFalse(_start.RollersVisible);
            Assert.Greater(_start.Burn, 0);
            Assert.Greater(_later.Burn, _start.Burn);
            Assert.Less(_later.Burn, ParchmentBannerTimeline.BurnEnd);
            Assert.IsFalse(_later.Done);
        }

        [Test]
        public void AfterTheBurn_NothingIsLeftAndItIsDone()
        {
            ParchmentBannerFrame _frame = ParchmentBannerTimeline.Evaluate(Whole + ParchmentBannerTimeline.BurnSeconds, Whole);
            Assert.AreEqual(ParchmentBannerTimeline.BurnEnd, _frame.Burn);
            Assert.IsTrue(_frame.Done);
        }

        [Test]
        public void AWholeTimeShorterThanTheUnrolling_StillUnrollsBeforeBurning()
        {
            ParchmentBannerFrame _frame = ParchmentBannerTimeline.Evaluate(ParchmentBannerTimeline.UnrollSeconds - 0.01, 0.1);
            Assert.AreEqual(0, _frame.Burn, "It never burns before it is open.");
            Assert.Less(_frame.Unroll, 1);
        }
    }
}
