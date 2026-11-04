using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T19 + T23 (Poyo 2026-10-21): by day the chained cat floats ahead of the player and takes 60 % of the screen's height;
    /// the circle is sized for the number of players (mockups cercle-jour v1) so every cat stays in sight and clickable.
    /// The numbers are the game's: seated view of 40°, the floating chained cat 16.2 m high, a click capsule 4.4 m wide and
    /// 17.6 m high, the eye 11.3 m above the seat, 2 m between neighbours, half of every cat in sight, 32 m and 12 m at 4
    /// players, one circle from 10 on.
    /// </summary>
    public class DayRingLayoutTests
    {
        private const float Fov = 40f;
        private const float CatHeight = 16.2f;
        private const float CatRadius = 4.4f;
        private const float ClickHeight = 17.6f;
        private const float Eye = 11.3f;
        private const float Gap = 2f;
        private const float MinVisible = 0.5f;
        private const int FewPlayers = 4;
        private const int FullPlayers = 10;
        private const float FewDistance = 32f;
        private const float FewRadius = 12f;

        // Yaw clamp ±75° plus half the horizontal field of a 16:9 screen at a 40° vertical field.
        private static readonly float YawReach = 75f + (float)(Math.Atan(Math.Tan(Fov * 0.5 * Math.PI / 180.0) * 16.0 / 9.0) * 180.0 / Math.PI);

        private static float Distance => DayRingLayout.PlayerDistance(CatHeight, Fov, 0.6f);

        private static float PlayerAt(int count)
        {
            DayRingLayout.Seats(count, FewPlayers, FullPlayers, FewDistance, FewRadius, Distance, out float player, out _);
            return player;
        }

        private static float Radius(int count)
        {
            DayRingLayout.Seats(count, FewPlayers, FullPlayers, FewDistance, FewRadius, Distance, out float player, out float start);
            return DayRingLayout.OthersRadius(count, player, start, CatRadius, ClickHeight, Eye, Gap, MinVisible);
        }

        private static float NearestSeat(int count)
        {
            float nearest = float.MaxValue;
            for (int seat = 1; seat < count; seat++)
            {
                nearest = Math.Min(nearest, DayRingLayout.SeatDistance(count, seat, PlayerAt(count), Radius(count)));
            }
            return nearest;
        }

        // Half the horizontal field of a 16:9 screen at a 40° vertical field.
        private static readonly float HalfScreenYaw = (float)(Math.Atan(Math.Tan(Fov * 0.5 * Math.PI / 180.0) * 16.0 / 9.0) * 180.0 / Math.PI);

        [Test]
        public void PlayerDistance_IsWhereTheCatTakesItsShareOfTheScreen()
        {
            float distance = Distance;
            double screen = 2.0 * distance * Math.Tan(Fov * 0.5 * Math.PI / 180.0);
            Assert.That(CatHeight / screen, Is.EqualTo(0.6).Within(1e-4));
            Assert.That(distance, Is.EqualTo(37f).Within(0.5f), "the middle is about 37 m away");
        }

        [Test]
        public void AtFourPlayers_EveryCatIsWholeOnScreen_WithoutTurningTheHead()
        {
            for (int seat = 1; seat < 4; seat++)
            {
                float distance = DayRingLayout.SeatDistance(4, seat, PlayerAt(4), Radius(4));
                float halfWidth = (float)(Math.Asin(CatRadius / distance) * 180.0 / Math.PI);
                Assert.That(Math.Abs(DayRingLayout.SeatYaw(4, seat, PlayerAt(4), Radius(4))) + halfWidth, Is.LessThan(HalfScreenYaw), $"seat {seat}");
            }
        }

        [Test]
        public void FewerPlayers_SitCloser_AFullTableIsUnchanged()
        {
            for (int count = FewPlayers; count < FullPlayers; count++)
            {
                Assert.That(NearestSeat(count), Is.GreaterThanOrEqualTo(NearestSeat(count + 1) - 1e-3f), $"{count} players: the neighbour is no nearer with fewer");
                Assert.That(PlayerAt(count), Is.LessThan(PlayerAt(count + 1)), $"{count} players: the player sits closer");
            }
            for (int count = FullPlayers; count <= GameValues.MAX_PLAYERS; count++)
            {
                Assert.That(PlayerAt(count), Is.EqualTo(Distance).Within(1e-3f), $"{count} players");
            }
            Assert.That(Radius(FullPlayers), Is.EqualTo(Distance).Within(1e-3f));
        }

        [Test]
        public void TheChainedCatStaysClearOfTheFarCat()
        {
            for (int count = FewPlayers; count <= GameValues.MAX_PLAYERS; count++)
            {
                Assert.That(PlayerAt(count) + Radius(count), Is.GreaterThanOrEqualTo(Distance + CatRadius), $"{count} players");
            }
        }

        [Test]
        public void TheCircleGrowsWithThePlayers_ButNeverShrinks()
        {
            float previous = 0f;
            for (int count = 2; count <= GameValues.MAX_PLAYERS; count++)
            {
                float radius = Radius(count);
                Assert.That(radius, Is.GreaterThanOrEqualTo(previous - 1e-3f), $"{count} players");
                previous = radius;
            }
            Assert.That(Radius(GameValues.MAX_PLAYERS), Is.GreaterThan(Distance), "a full table needs a bigger circle");
        }

        [Test]
        public void EveryCountUpToTheMaximum_KeepsEveryCatClickable_AndApart()
        {
            for (int count = 2; count <= GameValues.MAX_PLAYERS; count++)
            {
                float radius = Radius(count);
                float player = PlayerAt(count);
                Assert.That(radius, Is.LessThan(player * 2f - 0.01f), $"{count} players: found room before the cap");
                Assert.That(DayRingLayout.LeastVisibleShare(count, player, radius, CatRadius, ClickHeight, Eye),
                    Is.GreaterThanOrEqualTo(MinVisible), $"{count} players: half of every cat in sight");
                for (int seat = 1; seat < count; seat++)
                {
                    Assert.That(Math.Abs(DayRingLayout.SeatYaw(count, seat, player, radius)), Is.LessThan(YawReach - 10f),
                        $"{count} players, seat {seat}: the head turns far enough to see it");
                    Assert.That(DayRingLayout.SeatDistance(count, seat, player, radius), Is.GreaterThan(CatRadius * 2f),
                        $"{count} players, seat {seat}: never inside the player's view");
                }
                if (count > 2)
                {
                    double chord = 2.0 * radius * Math.Sin(Math.PI / count);
                    Assert.That(chord, Is.GreaterThanOrEqualTo(2f * CatRadius + Gap - 1e-3f), $"{count} players: neighbours apart");
                }
            }
        }

        [Test]
        public void OnOneCircle_AFullTableHidesTheSecondNeighbour()
        {
            int count = GameValues.MAX_PLAYERS;
            Assert.That(DayRingLayout.LeastVisibleShare(count, Distance, Distance, CatRadius, ClickHeight, Eye), Is.LessThan(MinVisible),
                "why the circle grows: the nearest neighbour hides most of the next one");
        }

        [Test]
        public void SeatYaw_TheFarSideIsAhead_NeighboursOnTheSides()
        {
            Assert.That(DayRingLayout.SeatYaw(8, 4, 30f, 30f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(DayRingLayout.SeatYaw(8, 2, 30f, 30f), Is.EqualTo(45f).Within(1e-3f));
            Assert.That(DayRingLayout.SeatYaw(8, 6, 30f, 30f), Is.EqualTo(-45f).Within(1e-3f));
        }

        [Test]
        public void RestPitch_LooksDownAtTheMiddle()
        {
            Assert.That(DayRingLayout.RestPitch(10f, 0f, 10f), Is.EqualTo(45f).Within(1e-3f));
            Assert.That(DayRingLayout.RestPitch(Eye, 0.8f * CatHeight, Distance), Is.InRange(-4f, -1f), "the view aims a little above the eye");
        }
    }
}
