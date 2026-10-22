using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T19 + T23 (Poyo 2026-10-21): by day the chained cat floats in the middle of a bigger circle and takes 60 % of the
    /// screen's height there; the circle grows with the number of players so every cat stays in sight and clickable.
    /// The numbers are the game's: seated view of 40°, the floating chained cat 16.2 m high, a click capsule 4.4 m wide and
    /// 17.6 m high, the eye 16.4 m above the seat, 2 m between neighbours, half of every cat in sight.
    /// </summary>
    public class DayRingLayoutTests
    {
        private const float Fov = 40f;
        private const float CatHeight = 16.2f;
        private const float CatRadius = 4.4f;
        private const float ClickHeight = 17.6f;
        private const float Eye = 16.4f;
        private const float Gap = 2f;
        private const float MinVisible = 0.5f;

        // Yaw clamp ±75° plus half the horizontal field of a 16:9 screen at a 40° vertical field.
        private static readonly float YawReach = 75f + (float)(Math.Atan(Math.Tan(Fov * 0.5 * Math.PI / 180.0) * 16.0 / 9.0) * 180.0 / Math.PI);

        private static float Distance => DayRingLayout.PlayerDistance(CatHeight, Fov, 0.6f);

        private static float Radius(int count) =>
            DayRingLayout.OthersRadius(count, Distance, CatRadius, ClickHeight, Eye, Gap, MinVisible);

        [Test]
        public void PlayerDistance_IsWhereTheCatTakesItsShareOfTheScreen()
        {
            float distance = Distance;
            double screen = 2.0 * distance * Math.Tan(Fov * 0.5 * Math.PI / 180.0);
            Assert.That(CatHeight / screen, Is.EqualTo(0.6).Within(1e-4));
            Assert.That(distance, Is.EqualTo(37f).Within(0.5f), "the middle is about 37 m away");
        }

        [Test]
        public void FewPlayers_SitOnOneCircle()
        {
            for (int count = 2; count <= 9; count++)
            {
                Assert.That(Radius(count), Is.EqualTo(Distance).Within(1e-3f), $"{count} players");
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
                Assert.That(radius, Is.LessThan(Distance * 2f - 0.01f), $"{count} players: found room before the cap");
                Assert.That(DayRingLayout.LeastVisibleShare(count, Distance, radius, CatRadius, ClickHeight, Eye),
                    Is.GreaterThanOrEqualTo(MinVisible), $"{count} players: half of every cat in sight");
                for (int seat = 1; seat < count; seat++)
                {
                    Assert.That(Math.Abs(DayRingLayout.SeatYaw(count, seat, Distance, radius)), Is.LessThan(YawReach - 10f),
                        $"{count} players, seat {seat}: the head turns far enough to see it");
                    Assert.That(DayRingLayout.SeatDistance(count, seat, Distance, radius), Is.GreaterThan(CatRadius * 2f),
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
            Assert.That(DayRingLayout.RestPitch(Eye, 0.8f * CatHeight, Distance), Is.InRange(3f, 8f));
        }
    }
}
