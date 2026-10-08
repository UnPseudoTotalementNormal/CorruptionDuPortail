namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure board card-placement layout (Story 11.2). Mirrors <c>BoardManager.GetCardPlacedPosition</c>
    /// exactly: cards are laid out left-to-right from a spawn origin at a fixed horizontal
    /// <paramref name="spacing"/>, wrapping to a new line (back to the origin x, one
    /// <paramref name="lineSpacing"/> further along -z) as soon as a card would reach OR pass
    /// <paramref name="maxX"/>. No engine types — the adapter reads the scene Transforms' local
    /// positions + the board's spacing constants and passes plain floats, then wraps the result back
    /// into a <c>Vector3</c>. Decision-only (NFR4): returns coordinates; the adapter moves the card.
    /// </summary>
    public sealed class CardLayout
    {
        /// <summary>
        /// Computes the local placement of the card at <paramref name="cardIndex"/>. The original
        /// advances x by <paramref name="spacing"/> per step and THEN wraps when x ≥
        /// <paramref name="maxX"/>; y is never modified (only x and z change). A non-positive index
        /// returns the origin.
        /// </summary>
        public CardPlacement GetPlacedPosition(
            int cardIndex,
            float originX,
            float originY,
            float originZ,
            float spacing,
            float lineSpacing,
            float maxX)
        {
            float x = originX;
            float y = originY;
            float z = originZ;

            while (cardIndex > 0)
            {
                x += spacing;
                if (x >= maxX)
                {
                    x = originX;
                    z -= lineSpacing;
                }

                cardIndex--;
            }

            return new CardPlacement(x, y, z);
        }

        /// <summary>Number of lines <paramref name="cardCount"/> cards take with this spacing (0 for no card).</summary>
        public int LineCount(int cardCount, float originX, float spacing, float maxX)
        {
            if (cardCount <= 0)
            {
                return 0;
            }

            CardPlacement last = GetPlacedPosition(cardCount - 1, originX, 0f, 0f, spacing, 1f, maxX);
            return 1 + (int)System.Math.Round(-last.Z);
        }

        /// <summary>
        /// Uniform scale for the cards (and their spacings) so that <paramref name="cardCount"/> cards take no more
        /// depth than <paramref name="maxDepth"/> (the board's visible band): 1 while they fit, smaller once a further
        /// line would leave it (13+ seats: the third line was off screen, under the power bar, found by autoplay
        /// 2026-10-07; lines spaced for the vote panels, Poyo 2026-10-07). Shrinking the spacing also fits more cards
        /// per line (wrapping at <see cref="ScaledMaxX"/>), so the scale only drops as far as needed. Never below
        /// <paramref name="minScale"/>.
        /// </summary>
        public float FitScale(int cardCount, float originX, float spacing, float lineSpacing, float maxX, float cardWidth,
            float cardDepth, float maxDepth, float minScale = 0.5f)
        {
            for (float s = 1f; s > minScale; s -= 0.01f)
            {
                int lines = LineCount(cardCount, originX, spacing * s, ScaledMaxX(originX, spacing, maxX, cardWidth, s));
                if (lines == 0 || ((lines - 1) * lineSpacing + cardDepth) * s <= maxDepth + 1e-4f)
                {
                    return s;
                }
            }

            return minScale;
        }

        /// <summary>
        /// Wrap limit for cards at <paramref name="scale"/>: a smaller card may go further right, as long as its right
        /// edge stays within the right edge of the last full-size column (the board's edge). Equals the full-size
        /// layout's own wrap point at scale 1, so small tables are laid out exactly as before.
        /// </summary>
        public float ScaledMaxX(float originX, float spacing, float maxX, float cardWidth, float scale)
        {
            float lastColumnX = originX;
            while (lastColumnX + spacing < maxX)
            {
                lastColumnX += spacing;
            }

            return lastColumnX + cardWidth / 2f * (1f - scale) + 1e-3f;
        }
    }

    /// <summary>Plain-float local placement returned by <see cref="CardLayout"/> (adapter wraps it into a Vector3).</summary>
    public readonly struct CardPlacement
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public CardPlacement(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
