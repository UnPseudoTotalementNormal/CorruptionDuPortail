using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19: the vote candle stands by the player's cards but must never cover a card or its vote panel,
    /// whatever the table size. Table and cards as on the game's board (world x, z).
    /// </summary>
    public class CandleSpotTests
    {
        private static readonly TableRect Table = new TableRect(-11.38f, -7f, 11.38f, 7f);
        private const float Radius = 1.8f;

        [Test]
        public void FreeTable_KeepsItsOwnPlace()
        {
            CandleSpotResult _spot = CandleSpot.Find(-4.8f, -5.8f, Radius, Table, new List<TableRect>());
            Assert.That(_spot.Free, Is.True);
            Assert.That(_spot.X, Is.EqualTo(-4.8f).Within(1e-3f));
            Assert.That(_spot.Z, Is.EqualTo(-5.2f).Within(1e-3f), "pulled in so the dish stays on the table");
            Assert.That(_spot.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void ACardOnItsPlace_MovesAsideWithoutTouchingIt()
        {
            var _cards = new List<TableRect> { new TableRect(-6f, -7f, -3.2f, -1f) };
            CandleSpotResult _spot = CandleSpot.Find(-4.8f, -5.8f, Radius, Table, _cards);
            Assert.That(_spot.Free, Is.True);
            Assert.That(_cards[0].TouchesDisc(_spot.X, _spot.Z, Radius * _spot.Scale), Is.False);
            Assert.That(_spot.X, Is.LessThan(-6f), "the nearest free place is further left");
        }

        [Test]
        public void AFullNearLine_ShrinksOrGoesRight_NeverCoversACard()
        {
            // Twelve seats: a near line of six cards across the table, their vote panels down to the edge.
            var _cards = new List<TableRect>();
            for (int _i = 0; _i < 6; _i++)
            {
                float _x = -7.5f + _i * 3f;
                _cards.Add(new TableRect(_x - 1.35f, -6.9f, _x + 1.35f, -0.5f));
            }
            CandleSpotResult _spot = CandleSpot.Find(-4.8f, -5.8f, Radius, Table, _cards);
            Assert.That(_spot.Free, Is.True);
            foreach (TableRect _card in _cards)
            {
                Assert.That(_card.TouchesDisc(_spot.X, _spot.Z, Radius * _spot.Scale), Is.False);
            }
        }

        [Test]
        public void OnTheRight_MovesTowardTheRightEdgeFirst()
        {
            // Poyo's place (maquettes v3): right of his cards. A card under it: the nearest free place, further right.
            var _cards = new List<TableRect> { new TableRect(6f, -7f, 8f, -1f) };
            CandleSpotResult _spot = CandleSpot.Find(8.73f, -4.33f, Radius * 0.79f, Table, _cards);
            Assert.That(_spot.Free, Is.True);
            Assert.That(_cards[0].TouchesDisc(_spot.X, _spot.Z, Radius * 0.79f * _spot.Scale), Is.False);
            Assert.That(_spot.X, Is.GreaterThan(8f), "outward, toward the right edge");
        }

        [Test]
        public void Blocked_StaysInView_StepsBackTowardThePlayerFirst()
        {
            // A card's vote panel reaches the chosen place from behind: the candle steps back toward the player, it
            // does not run off past the edge of the view.
            var _cards = new List<TableRect> { new TableRect(6.5f, -3.9f, 10.5f, 2f) };
            CandleSpotResult _spot = CandleSpot.Find(8.73f, -4.33f, Radius * 0.79f, Table, _cards, 9f);
            Assert.That(_spot.Free, Is.True);
            Assert.That(_spot.X, Is.LessThanOrEqualTo(9f));
            Assert.That(_spot.Z, Is.LessThan(-4.33f), "stepped back toward the player");
            Assert.That(_cards[0].TouchesDisc(_spot.X, _spot.Z, Radius * 0.79f * _spot.Scale), Is.False);
        }

        [Test]
        public void FullTable_SmallerAtTheEdgeOnThePlayersSide_NeverOnACard()
        {
            // Fourteen seats (measured on the game's board): two lines of seven, the near line as wide as the table. The
            // candle never goes to the far side (it would hide the role board): smaller, at the near right corner.
            var _cards = new List<TableRect>();
            for (int _i = 0; _i < 7; _i++)
            {
                float _x = -8.82f + _i * 2.94f;
                _cards.Add(new TableRect(_x - 1.85f, -0.35f, _x + 1.85f, 3.85f));
                _cards.Add(new TableRect(_x - 1.85f, -5.55f, _x + 1.85f, -1.35f));
            }
            CandleSpotResult _spot = CandleSpot.Find(4.6f, -4.3f, Radius * 0.79f, Table, _cards, 9f, -0.5f);
            Assert.That(_spot.Free, Is.True);
            Assert.That(_spot.Z, Is.LessThan(-0.5f), "on the player's side");
            Assert.That(_spot.X, Is.GreaterThan(9f), "at the right edge");
            foreach (TableRect _card in _cards)
            {
                Assert.That(_card.TouchesDisc(_spot.X, _spot.Z, Radius * 0.79f * _spot.Scale), Is.False);
            }
        }

        [Test]
        public void NoRoomAnywhere_SmallestAtTheLeftCorner()
        {
            var _cards = new List<TableRect> { new TableRect(-12f, -8f, 12f, 8f) };
            CandleSpotResult _spot = CandleSpot.Find(-4.8f, -5.8f, Radius, Table, _cards);
            Assert.That(_spot.Free, Is.False);
            Assert.That(_spot.Scale, Is.EqualTo(0.5f));
            Assert.That(_spot.X, Is.LessThan(-9f));
        }
    }
}
