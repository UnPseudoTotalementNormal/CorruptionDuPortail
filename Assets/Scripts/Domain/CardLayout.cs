namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// How the board's cards are arranged (Poyo, 2026-10-08: no card, vote button or vote count may be hidden, in the
    /// top view or seated first person, hovered or not; three arrangements to choose from, all measured by the
    /// autoplay card visibility probe at every table size from 5 to 14).
    /// </summary>
    public enum CardGridPreset
    {
        /// <summary>Lines aligned on the board's left edge, the grid raised towards the characters bar.</summary>
        RaisedLeft = 0,
        /// <summary>Each line centred on the board; a single line sits full size a little lower than the first of two.</summary>
        Centred = 1,
        /// <summary>Each line centred on the board; a single line sits full size at the top, like the first line of two.</summary>
        CentredTop = 2,
    }

    /// <summary>One table size's grid: cards per line, card scale, line spacing and first line z (board units).</summary>
    public readonly struct CardGrid
    {
        public readonly int Columns;
        public readonly float Scale;
        public readonly float LineSpacing;
        public readonly float FirstLineZ;
        public readonly bool Centred;

        public CardGrid(int columns, float scale, float lineSpacing, float firstLineZ, bool centred)
        {
            Columns = columns;
            Scale = scale;
            LineSpacing = lineSpacing;
            FirstLineZ = firstLineZ;
            Centred = centred;
        }
    }

    /// <summary>
    /// Pure board card-placement layout (Story 11.2, reworked 2026-10-08). No engine types: the adapter
    /// (<c>BoardManager</c>) passes the board's anchors as plain floats and wraps the result into a <c>Vector3</c>.
    /// Board units (the board's local space): a full-size card is 6.3 × 8.8; its vote panel hangs below it and, when the
    /// card is hovered (which also enlarges it), slides further down (vote count text down to 6.89 × scale × ~1.1 under
    /// the card's centre); the "Moi" tag stands up to 2.6 × scale above the local player's card. What a grid must fit,
    /// measured in game (1600 × 900, autoplay probe): between the characters bar (z ≈ +9.8 from the first line's
    /// historical origin) and the bottom HUD / skip button (z ≈ −13.6). Two lines can only fit that with the hovered
    /// vote text clear of the next line at about 0.86 scale; a single line keeps full size.
    /// </summary>
    public sealed class CardLayout
    {
        // Full-size card in board units, and the historical spacings.
        public const float CardWidth = 6.3f;
        public const float ColumnSpacing = 7f;

        /// <summary>The grid for <paramref name="cardCount"/> cards in <paramref name="preset"/>.</summary>
        public CardGrid GridFor(int cardCount, CardGridPreset preset)
        {
            bool _centred = preset != CardGridPreset.RaisedLeft;
            if (cardCount <= 6)
            {
                // One line, full size. Top = where the first of two lines sits; Centred = lower, but not below 0.8:
                // closer to the seated player, a hovered card's vote button edge went under its neighbour (UI raycast:
                // the neighbour's face canvas outranks the vote panel), measured 2026-10-08.
                float _z = preset == CardGridPreset.Centred ? 1.2f : 2.8f;
                return new CardGrid(6, 1f, 0f, _z, _centred);
            }
            if (cardCount <= 12)
            {
                // Two lines of 6: the far line's hovered vote text must clear the near line (spacing), the near line's
                // must stay above the bottom HUD (scale), the "Moi" tag under the characters bar (first line z).
                return new CardGrid(6, 0.86f, 10.6f, 3.6f, _centred);
            }
            // 13-14 seats: two lines of 7. Beyond (no preset ships it), more columns and smaller cards.
            int _columns = System.Math.Max(7, (cardCount + 1) / 2);
            return new CardGrid(_columns, 0.84f * 7f / _columns, 10.4f * 7f / _columns, 3.5f, _centred);
        }

        /// <summary>
        /// Local placement of card <paramref name="cardIndex"/> of <paramref name="cardCount"/> in <paramref name="grid"/>.
        /// Lines fill left to right, then the next line one <c>LineSpacing</c> further along −z. Left-aligned: the
        /// first column's left edge stays on the full-size first column's left edge (<paramref name="originX"/> −
        /// half a card). Centred: each line is centred on <paramref name="centreX"/>. y is never modified.
        /// </summary>
        public CardPlacement Place(int cardIndex, int cardCount, CardGrid grid, float originX, float originY, float originZ,
            float centreX)
        {
            if (cardIndex < 0)
            {
                cardIndex = 0;
            }
            int _line = cardIndex / grid.Columns;
            int _column = cardIndex % grid.Columns;
            float _spacing = ColumnSpacing * grid.Scale;
            float _x;
            if (grid.Centred)
            {
                int _inLine = System.Math.Min(grid.Columns, System.Math.Max(1, cardCount - _line * grid.Columns));
                _x = centreX + (_column - (_inLine - 1) / 2f) * _spacing;
            }
            else
            {
                _x = originX - CardWidth / 2f * (1f - grid.Scale) + _column * _spacing;
            }
            return new CardPlacement(_x, originY, originZ + grid.FirstLineZ - _line * grid.LineSpacing);
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
