using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// The day circle (T19 + T23, then Wouh 2026-11-15 and maquettes cercle-jour v2 + v3): everyone on one circle, the
    /// player too, 24 m from the middle, grown only until a quarter of every cat stays in sight. The numbers are the
    /// game's: a seated view of 54.5° high (85° wide), the others at the player's size by day (click capsule 4.4 m wide
    /// and 17.6 m high measured at 75 %), the eye 11.3 m above the seat, 2 m between neighbours, the head turning ±75°.
    /// </summary>
    public class DayRingLayoutTests
    {
        private const float Fov = 54.5f;
        private const float CatHeight = 16.2f;
        private const float CatRadius = 4.4f / 0.75f;
        private const float ClickHeight = 17.6f / 0.75f;
        private const float Eye = 11.3f;
        private const float Gap = 2f;
        private const float MinVisible = 0.25f;
        private const int FewPlayers = 4;
        private const float Ring = 24f;

        // Yaw clamp ±75° plus half the horizontal field of a 16:9 screen.
        private static readonly float YawReach = 75f + (float)(Math.Atan(Math.Tan(Fov * 0.5 * Math.PI / 180.0) * 16.0 / 9.0) * 180.0 / Math.PI);

        private static float Radius(int count)
        {
            float start = DayRingLayout.SingleStartRadius(count, FewPlayers, Ring, 0f);
            return DayRingLayout.SingleRadius(count, start, CatRadius, ClickHeight, Eye, Gap, MinVisible, 4f * start);
        }

        [Test]
        public void PlayerDistance_IsWhereTheCatTakesItsShareOfTheScreen()
        {
            float distance = DayRingLayout.PlayerDistance(CatHeight, Fov, 0.6f);
            double screen = 2.0 * distance * Math.Tan(Fov * 0.5 * Math.PI / 180.0);
            Assert.That(CatHeight / screen, Is.EqualTo(0.6).Within(1e-4));
        }

        [Test]
        public void SingleStartRadius_GrowsPerExtraPlayerOnly()
        {
            Assert.That(DayRingLayout.SingleStartRadius(2, FewPlayers, Ring, 2f), Is.EqualTo(Ring).Within(1e-3f));
            Assert.That(DayRingLayout.SingleStartRadius(4, FewPlayers, Ring, 2f), Is.EqualTo(Ring).Within(1e-3f));
            Assert.That(DayRingLayout.SingleStartRadius(12, FewPlayers, Ring, 2f), Is.EqualTo(Ring + 16f).Within(1e-3f));
            Assert.That(DayRingLayout.SingleStartRadius(12, FewPlayers, Ring, 0f), Is.EqualTo(Ring).Within(1e-3f));
        }

        [Test]
        public void VotedSizes_24MetresUpToTen_About32AtTwelve()
        {
            for (int count = 2; count <= 10; count++)
            {
                Assert.That(Radius(count), Is.EqualTo(Ring).Within(1e-3f), $"{count} players");
            }
            Assert.That(Radius(12), Is.InRange(31f, 34f), "12 players (C2: 32 m)");
        }

        [Test]
        public void TheCircleNeverShrinksWithMorePlayers()
        {
            float previous = 0f;
            for (int count = 2; count <= GameValues.MAX_PLAYERS; count++)
            {
                float radius = Radius(count);
                Assert.That(radius, Is.GreaterThanOrEqualTo(previous - 1e-3f), $"{count} players");
                previous = radius;
            }
        }

        [Test]
        public void EveryCountUpToTheMaximum_KeepsEveryCatClickable_AndApart()
        {
            for (int count = 2; count <= GameValues.MAX_PLAYERS; count++)
            {
                float radius = Radius(count);
                Assert.That(radius, Is.LessThan(4f * Ring - 0.01f), $"{count} players: found room before the cap");
                Assert.That(DayRingLayout.LeastVisibleShare(count, radius, radius, CatRadius, ClickHeight, Eye),
                    Is.GreaterThanOrEqualTo(MinVisible), $"{count} players: a quarter of every cat in sight");
                for (int seat = 1; seat < count; seat++)
                {
                    Assert.That(Math.Abs(DayRingLayout.SeatYaw(count, seat, radius, radius)), Is.LessThan(YawReach - 10f),
                        $"{count} players, seat {seat}: the head turns far enough to see it");
                    Assert.That(DayRingLayout.SeatDistance(count, seat, radius, radius), Is.GreaterThan(CatRadius * 2f),
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
            Assert.That(DayRingLayout.LeastVisibleShare(count, Ring, Ring, CatRadius, ClickHeight, Eye), Is.LessThan(MinVisible),
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
        }
    }
}
