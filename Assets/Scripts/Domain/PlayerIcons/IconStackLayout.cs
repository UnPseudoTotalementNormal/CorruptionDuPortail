using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.PlayerIcons
{
    /// <summary>
    /// Pure placement of the private power icons stacked on ONE CharactersBar thumbnail. Mirrors
    /// <see cref="CardLayout"/> exactly in spirit: no engine types (the Domain asmdef forbids them
    /// structurally), plain floats out, and the adapter turns a <see cref="LayoutPoint"/> into whatever
    /// the renderer needs. Offsets are LOCAL units, never RectTransform anchors — when the thumbnails
    /// become 3D objects only the icon renderer changes, the stacking maths does not.
    ///
    /// Collapsed: at most <c>maxVisible</c> icons are laid out at <c>collapsedStep</c> apart (a tight,
    /// overlapping pile), the rest are reported as an overflow count for the "+X" badge. Expanded (the
    /// hover state): EVERY icon is laid out at <c>expandedStep</c> apart and the overflow is zero.
    /// </summary>
    public sealed class IconStackLayout
    {
        /// <summary>
        /// How many icons actually get a placement. Collapsed caps at <paramref name="maxVisible"/>
        /// (clamped to at least 1); expanded shows them all. A non-positive count yields 0.
        /// </summary>
        public int VisibleCount(int iconCount, bool expanded, int maxVisible)
        {
            if (iconCount <= 0)
            {
                return 0;
            }
            if (expanded)
            {
                return iconCount;
            }

            int cap = maxVisible < 1 ? 1 : maxVisible;
            return iconCount < cap ? iconCount : cap;
        }

        /// <summary>
        /// How many icons are hidden behind the pile — the number the "+X" badge shows. Always 0 when
        /// expanded, and never negative.
        /// </summary>
        public int OverflowCount(int iconCount, bool expanded, int maxVisible)
        {
            int overflow = (iconCount <= 0 ? 0 : iconCount) - VisibleCount(iconCount, expanded, maxVisible);
            return overflow < 0 ? 0 : overflow;
        }

        /// <summary>
        /// The ordered local offsets of the laid-out icons, first icon at the origin and each following
        /// one advanced along +X by the active step. Y is always 0 today (the stack grows sideways from
        /// the bottom-left corner) but is carried so a future vertical/3D fan needs no signature change.
        /// Returns an empty list for a non-positive count.
        /// </summary>
        public IReadOnlyList<LayoutPoint> Compute(
            int iconCount,
            bool expanded,
            float collapsedStep,
            float expandedStep,
            int maxVisible)
        {
            int visible = VisibleCount(iconCount, expanded, maxVisible);
            var points = new List<LayoutPoint>(visible);
            if (visible <= 0)
            {
                return points;
            }

            float step = expanded ? expandedStep : collapsedStep;
            for (int i = 0; i < visible; i++)
            {
                points.Add(new LayoutPoint(i * step, 0f));
            }
            return points;
        }
    }

    /// <summary>Plain-float local offset returned by <see cref="IconStackLayout"/> (the adapter wraps it into a Vector2/Vector3).</summary>
    public readonly struct LayoutPoint : System.IEquatable<LayoutPoint>
    {
        public readonly float X;
        public readonly float Y;

        public LayoutPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(LayoutPoint other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is LayoutPoint o && Equals(o);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public override string ToString() => $"({X}, {Y})";
    }
}
