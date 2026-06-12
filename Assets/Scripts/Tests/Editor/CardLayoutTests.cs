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
    }
}
