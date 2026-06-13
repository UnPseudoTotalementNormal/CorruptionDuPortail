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
