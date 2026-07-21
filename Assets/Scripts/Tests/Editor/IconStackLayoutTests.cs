using System.Collections.Generic;
using CorruptionDuPortail.Domain.PlayerIcons;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Pure placement coverage for the private player-icon stack (feat/targeting-icons). Covers the rows of
    /// the spec's edge-case matrix that are decidable WITHOUT the engine: 0 / 1 / N icons, collapsed vs
    /// expanded, overflow past the visible cap, and the step being honoured exactly.
    /// </summary>
    [Category("PlayerIcons")]
    public class IconStackLayoutTests
    {
        private const float CollapsedStep = 8f;
        private const float ExpandedStep = 34f;
        private const int MaxVisible = 3;

        private IconStackLayout _layout;

        [SetUp]
        public void SetUp() => _layout = new IconStackLayout();

        private IReadOnlyList<LayoutPoint> Compute(int _count, bool _expanded, int _maxVisible = MaxVisible) =>
            _layout.Compute(_count, _expanded, CollapsedStep, ExpandedStep, _maxVisible);

        // ---- counts ---------------------------------------------------------------------------

        [Test]
        public void NoIcons_YieldsNoPlacements([Values(true, false)] bool _expanded)
        {
            Assert.AreEqual(0, _layout.VisibleCount(0, _expanded, MaxVisible));
            Assert.AreEqual(0, _layout.OverflowCount(0, _expanded, MaxVisible));
            Assert.IsEmpty(Compute(0, _expanded));
        }

        [Test]
        public void NegativeCount_IsTreatedAsEmpty()
        {
            Assert.AreEqual(0, _layout.VisibleCount(-3, false, MaxVisible));
            Assert.AreEqual(0, _layout.OverflowCount(-3, false, MaxVisible));
            Assert.IsEmpty(Compute(-3, false));
        }

        [Test]
        public void SingleIcon_SitsAtTheOrigin([Values(true, false)] bool _expanded)
        {
            var _points = Compute(1, _expanded);

            Assert.AreEqual(1, _points.Count);
            Assert.AreEqual(new LayoutPoint(0f, 0f), _points[0]);
            Assert.AreEqual(0, _layout.OverflowCount(1, _expanded, MaxVisible));
        }

        [Test]
        public void CountBelowTheCap_ShowsEveryIcon_WithNoOverflow()
        {
            Assert.AreEqual(2, _layout.VisibleCount(2, false, MaxVisible));
            Assert.AreEqual(0, _layout.OverflowCount(2, false, MaxVisible));
            Assert.AreEqual(2, Compute(2, false).Count);
        }

        // ---- step ----------------------------------------------------------------------------

        [Test]
        public void Collapsed_AdvancesByTheCollapsedStep()
        {
            var _points = Compute(3, false);

            Assert.AreEqual(3, _points.Count);
            for (int _i = 0; _i < _points.Count; _i++)
            {
                Assert.AreEqual(_i * CollapsedStep, _points[_i].X, 1e-4f, $"icon {_i} X");
                Assert.AreEqual(0f, _points[_i].Y, 1e-4f, $"icon {_i} Y");
            }
        }

        [Test]
        public void Expanded_AdvancesByTheExpandedStep_AndSpreadsWider()
        {
            var _collapsed = Compute(3, false);
            var _expanded = Compute(3, true);

            for (int _i = 0; _i < _expanded.Count; _i++)
            {
                Assert.AreEqual(_i * ExpandedStep, _expanded[_i].X, 1e-4f, $"icon {_i} X");
            }
            Assert.Greater(_expanded[2].X, _collapsed[2].X,
                "Hovering must fan the pile OUT — the expanded step has to be the wider one.");
        }

        [Test]
        public void ZeroStep_StacksEveryIconOnTheSameSpot()
        {
            var _points = _layout.Compute(3, false, 0f, ExpandedStep, MaxVisible);

            Assert.AreEqual(3, _points.Count);
            foreach (var _point in _points)
            {
                Assert.AreEqual(0f, _point.X, 1e-4f);
            }
        }

        // ---- overflow -------------------------------------------------------------------------

        [Test]
        public void Collapsed_ClampsToMaxVisible_AndReportsTheRemainderAsOverflow()
        {
            Assert.AreEqual(MaxVisible, _layout.VisibleCount(7, false, MaxVisible));
            Assert.AreEqual(7 - MaxVisible, _layout.OverflowCount(7, false, MaxVisible));
            Assert.AreEqual(MaxVisible, Compute(7, false).Count);
        }

        [Test]
        public void Expanded_RevealsEveryOverflowedIcon()
        {
            Assert.AreEqual(7, _layout.VisibleCount(7, true, MaxVisible));
            Assert.AreEqual(0, _layout.OverflowCount(7, true, MaxVisible),
                "Expanding shows them all, so there is nothing left to count in a '+X' badge.");
            Assert.AreEqual(7, Compute(7, true).Count);
        }

        [Test]
        public void CountExactlyAtTheCap_HasNoOverflow()
        {
            Assert.AreEqual(MaxVisible, _layout.VisibleCount(MaxVisible, false, MaxVisible));
            Assert.AreEqual(0, _layout.OverflowCount(MaxVisible, false, MaxVisible));
        }

        [Test]
        public void NonPositiveCap_IsClampedToOne_RatherThanHidingEverything()
        {
            Assert.AreEqual(1, _layout.VisibleCount(4, false, 0));
            Assert.AreEqual(3, _layout.OverflowCount(4, false, 0));
            Assert.AreEqual(1, _layout.VisibleCount(4, false, -5));
        }

        // ---- value semantics -------------------------------------------------------------------

        [Test]
        public void LayoutPoint_IsValueEquatable()
        {
            Assert.AreEqual(new LayoutPoint(1.5f, -2f), new LayoutPoint(1.5f, -2f));
            Assert.AreNotEqual(new LayoutPoint(1.5f, -2f), new LayoutPoint(1.5f, 2f));
            Assert.AreEqual(new LayoutPoint(1.5f, -2f).GetHashCode(), new LayoutPoint(1.5f, -2f).GetHashCode());
        }
    }
}
