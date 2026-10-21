using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>A card's centre in a screen row, in screen half-heights (see <see cref="ScreenRowLayout"/>).</summary>
    public readonly struct ScreenRowSlot
    {
        public readonly float X;
        public readonly float Y;

        public ScreenRowSlot(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Where the two day rows sit on the screen (T23, mockups jour-pov v1 R3): the players' cards (key 1) above the
    /// roles of the game with their two counters (key 2), both at the bottom, stacked. A closed row slides out under the
    /// bottom edge; when the roles are closed the cards come down to their place.
    ///
    /// Every length is in SCREEN HALF-HEIGHTS: the screen spans y in [-1, 1] (bottom to top) and x in
    /// [-aspect, aspect], origin at its centre. The presenter turns them into world units with the camera's field of
    /// view (one half-height = distance × tan(fov / 2)), so the rows keep their size whatever the window.
    /// Defaults = the mockup at 1600 × 900 (one half-height = 450 px).
    /// </summary>
    public static class ScreenRowLayout
    {
        /// <summary>Gap under the lowest row (mockup 22 px).</summary>
        public const float BottomMargin = 22f / 450f;
        /// <summary>Height of the roles row (mockup 110 px).</summary>
        public const float RolesHeight = 110f / 450f;
        /// <summary>Gap between the roles row and the cards: the mockup's 18 px plus the cards' vote panel ("A voté"), which
        /// hangs under each card (measured in game, 2026-10-21).</summary>
        public const float RowGap = 46f / 450f;
        /// <summary>Card height when they all fit (mockup R3, big cards: 184 px).</summary>
        public const float CardHeight = 184f / 450f;
        /// <summary>Gap between two cards, as a share of a card's width (mockup 14 px for 132 px).</summary>
        public const float CardGapRatio = 14f / 132f;
        /// <summary>Widest a row may be, as a share of the screen width.</summary>
        public const float MaxWidthShare = 0.92f;
        /// <summary>A closed row waits this far under the bottom edge.</summary>
        public const float HiddenMargin = 0.08f;
        /// <summary>My vote (anonymous or not) keeps my voted player's card raised by this share of its height.</summary>
        public const float RaisedShare = 0.14f;

        /// <summary>
        /// The card height for <paramref name="count"/> cards on a screen <paramref name="aspect"/> wide (width / height),
        /// cards <paramref name="cardAspect"/> wide (width / height): the mockup's size, smaller when the row would not fit.
        /// </summary>
        public static float CardHeightFor(int count, float aspect, float cardAspect)
        {
            if (count <= 0 || aspect <= 0f || cardAspect <= 0f)
            {
                return CardHeight;
            }
            float width = CardHeight * cardAspect;
            float total = RowWidth(count, width);
            float max = MaxWidthShare * 2f * aspect;
            return total <= max ? CardHeight : CardHeight * (max / total);
        }

        /// <summary>The centres of <paramref name="count"/> cards, left to right.</summary>
        public static ScreenRowSlot[] CardSlots(int count, float aspect, float cardAspect, bool cardsOpen, bool rolesOpen)
        {
            if (count <= 0)
            {
                return Array.Empty<ScreenRowSlot>();
            }
            float height = CardHeightFor(count, aspect, cardAspect);
            float width = height * cardAspect;
            float step = width * (1f + CardGapRatio);
            float left = -RowWidth(count, width) / 2f + width / 2f;
            float y = CardsCentreY(height, cardsOpen, rolesOpen);
            var slots = new ScreenRowSlot[count];
            for (int i = 0; i < count; i++)
            {
                slots[i] = new ScreenRowSlot(left + i * step, y);
            }
            return slots;
        }

        /// <summary>Vertical centre of the cards row (open: above the roles when they are open; closed: under the screen).</summary>
        public static float CardsCentreY(float cardHeight, bool cardsOpen, bool rolesOpen)
        {
            if (!cardsOpen)
            {
                return -1f - cardHeight / 2f - HiddenMargin;
            }
            float bottom = -1f + BottomMargin + (rolesOpen ? RolesHeight + RowGap : 0f);
            return bottom + cardHeight / 2f;
        }

        /// <summary>Vertical centre of the roles row (open: at the bottom; closed: under the screen).</summary>
        public static float RolesCentreY(bool rolesOpen) =>
            rolesOpen ? -1f + BottomMargin + RolesHeight / 2f : -1f - RolesHeight / 2f - HiddenMargin;

        private static float RowWidth(int count, float cardWidth) =>
            count * cardWidth + (count - 1) * cardWidth * CardGapRatio;
    }
}
