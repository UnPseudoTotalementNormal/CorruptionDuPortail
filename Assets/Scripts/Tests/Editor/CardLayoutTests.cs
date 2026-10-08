using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 11.2 — EditMode characterization of <see cref="CardLayout.GetPlacedPosition"/>, the pure
    /// extraction of <c>BoardManager.GetCardPlacedPosition</c>. Pins the origin case, the per-step
    /// horizontal advance, the wrap-on-/-over-maxX boundary (the original uses <c>&gt;=</c>), multi-line
    /// wrapping, the non-positive-index guard, and that y is never modified.
    /// </summary>
    [Category("CardLayout")]
    public class CardLayoutTests
    {
        private const float Tol = 1e-4f;
        private readonly CardLayout _layout = new();

        // Origin (0,0,0), spacing 7, lineSpacing 9, maxX 20 → x advances 0,7,14 then wraps at index 3.
        private CardPlacement At(int index, float maxX = 20f, float originX = 0f, float originY = 0f, float originZ = 0f)
            => _layout.GetPlacedPosition(index, originX, originY, originZ, 7f, 9f, maxX);

        private static void AssertAt(CardPlacement p, float x, float y, float z)
        {
            Assert.AreEqual(x, p.X, Tol, "X");
            Assert.AreEqual(y, p.Y, Tol, "Y");
            Assert.AreEqual(z, p.Z, Tol, "Z");
        }

        [Test]
        public void IndexZero_ReturnsOrigin()
        {
            AssertAt(At(0), 0f, 0f, 0f);
        }

        [Test]
        public void WithinLine_AdvancesBySpacing()
        {
            AssertAt(At(1), 7f, 0f, 0f);
            AssertAt(At(2), 14f, 0f, 0f);
        }

        [Test]
        public void OverflowingMaxX_WrapsToNextLine()
        {
            // index 3: x would be 21 ≥ 20 → wrap back to origin x, one lineSpacing further along -z.
            AssertAt(At(3), 0f, 0f, -9f);
            AssertAt(At(4), 7f, 0f, -9f);
        }

        [Test]
        public void AtExactlyMaxX_Wraps_BecauseComparisonIsGreaterOrEqual()
        {
            // maxX 14: at index 2 x hits exactly 14 ⇒ 14 >= 14 wraps (proves the boundary uses >=, not >).
            AssertAt(At(2, maxX: 14f), 0f, 0f, -9f);
        }

        [Test]
        public void MultipleLines_KeepWrapping()
        {
            // maxX 20: indices 0..2 line 0; 3..5 line 1; 6..8 line 2 (each line restarts at x=0).
            AssertAt(At(6), 0f, 0f, -18f);
            AssertAt(At(7), 7f, 0f, -18f);
        }

        [Test]
        public void NonPositiveIndex_ReturnsOrigin()
        {
            AssertAt(At(0), 0f, 0f, 0f);
            AssertAt(At(-5), 0f, 0f, 0f);
        }

        [Test]
        public void OriginIsHonoured_AndYNeverChanges()
        {
            // Non-zero origin; y must stay at originY across a wrap.
            var p0 = _layout.GetPlacedPosition(0, 3f, 5f, 11f, 7f, 9f, 20f);
            Assert.AreEqual(3f, p0.X, Tol);
            Assert.AreEqual(5f, p0.Y, Tol);
            Assert.AreEqual(11f, p0.Z, Tol);

            // index 3 from origin x=3: 3→10→17→24(≥20 wrap)→ x=3, z=11-9=2; y stays 5.
            var p3 = _layout.GetPlacedPosition(3, 3f, 5f, 11f, 7f, 9f, 20f);
            Assert.AreEqual(3f, p3.X, Tol);
            Assert.AreEqual(5f, p3.Y, Tol, "y must never change");
            Assert.AreEqual(2f, p3.Z, Tol);
        }

        // Board of width 40 from x=0 at spacing 7: 6 cards per line (0..35, 42 wraps).
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(6, 1)]
        [TestCase(7, 2)]
        [TestCase(12, 2)]
        [TestCase(13, 3)]
        public void LineCount_MatchesTheWrap(int cards, int lines)
        {
            Assert.AreEqual(lines, _layout.LineCount(cards, 0f, 7f, 40f));
        }

        // Board of width 40 at spacing 7 (6 per line), line spacing 10.3, card 6.3 x 8.8; visible depth = two full-size
        // lines: 10.3 + 8.8 = 19.1.
        private float Fit(int cards) => _layout.FitScale(cards, 0f, 7f, 10.3f, 40f, 6.3f, 8.8f, 19.1f);

        private void AssertFits(int cards, float s)
        {
            float maxX = _layout.ScaledMaxX(0f, 7f, 40f, 6.3f, s);
            int lines = _layout.LineCount(cards, 0f, 7f * s, maxX);
            Assert.LessOrEqual(((lines - 1) * 10.3f + 8.8f) * s, 19.1f + Tol, "fits the visible depth");
            // No card's right edge passes the right edge of the last full-size column (35 + 3.15).
            int perLine = (cards + lines - 1) / lines;
            Assert.LessOrEqual((perLine - 1) * 7f * s + 6.3f / 2f * s, 35f + 6.3f / 2f + Tol, "stays on the board");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(12)]
        public void FitScale_IsOne_UpToTwoFullLines(int cards)
        {
            Assert.AreEqual(1f, Fit(cards), Tol, "up to 12 seats the cards keep their full size (far-line vote buttons)");
        }

        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        public void FitScale_BigTables_StayOnTwoLines(int cards)
        {
            float s = Fit(cards);
            Assert.Greater(s, 0.7f, "a smaller spacing fits more cards per line: two lines, not three");
            AssertFits(cards, s);
            Assert.AreEqual(2, _layout.LineCount(cards, 0f, 7f * s, _layout.ScaledMaxX(0f, 7f, 40f, 6.3f, s)));
        }

        [Test]
        public void ScaledMaxX_AtFullScale_KeepsTheOriginalWrap()
        {
            // Full size: columns 0..35, the 7th (42) wraps exactly as with maxX 40.
            float maxX = _layout.ScaledMaxX(0f, 7f, 40f, 6.3f, 1f);
            Assert.AreEqual(6, _layout.LineCount(6, 0f, 7f, maxX) * 6);
            Assert.AreEqual(2, _layout.LineCount(7, 0f, 7f, maxX));
            Assert.AreEqual(2, _layout.LineCount(12, 0f, 7f, maxX));
        }

        [Test]
        public void FitScale_NeverGoesBelowTheMinimum()
        {
            Assert.AreEqual(0.5f, _layout.FitScale(500, 0f, 7f, 10.3f, 40f, 6.3f, 8.8f, 19.1f, 0.5f), Tol);
        }
    }
}
