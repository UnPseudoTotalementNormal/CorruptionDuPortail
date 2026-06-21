using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN for the pure seat-ring geometry (<see cref="SeatRingGeometry"/>): per-client rotation,
    /// equidistant spread, and the relative-offset INVARIANCE that keeps gaze consistent across clients.
    /// Pure math → asserted with ZERO scene/network setup.
    /// </summary>
    public class SeatRingGeometryTests
    {
        private static readonly Vector3 Center = new Vector3(10f, 0f, -5f);
        private static readonly Vector3 Forward = Vector3.forward;
        private const float Radius = 3f;
        private const float Front = 0f;

        private static SeatPose Compute(int _count, int _local, int _target) =>
            SeatRingGeometry.Compute(_count, _local, _target, Center, Forward, Radius, Front);

        [Test]
        public void LocalPlayer_AlwaysSitsAtFrontSpot()
        {
            // Relative offset 0 (local == target) → the FRONT spot = center + forward*radius, for ANY count
            // or index. This is the fixed "in front of the table" anchor every client rotates onto.
            Vector3 _expected = Center + Forward * Radius;
            foreach (int _count in new[] { 1, 2, 5, 8 })
            {
                SeatPose _pose = Compute(_count, 3 % _count, 3 % _count);
                Assert.That(Vector3.Distance(_pose.Position, _expected), Is.LessThan(1e-3f),
                    $"Local seat not at the front spot for count {_count}.");
            }
        }

        [Test]
        public void AllSeats_AreOnTheRing()
        {
            // Every seat is exactly `radius` from the center (equidistant ring).
            for (int _t = 0; _t < 6; _t++)
            {
                SeatPose _pose = Compute(6, 0, _t);
                Assert.That(Vector3.Distance(_pose.Position, Center), Is.EqualTo(Radius).Within(1e-3f),
                    $"Seat {_t} is not on the ring.");
            }
        }

        [Test]
        public void Seats_AreEquidistantlySpread()
        {
            // count=4 → consecutive seats are 90° apart around the center (XZ plane).
            Vector3 _prev = Compute(4, 0, 0).Position - Center;
            for (int _t = 1; _t < 4; _t++)
            {
                Vector3 _cur = Compute(4, 0, _t).Position - Center;
                Assert.That(Vector3.Angle(_prev, _cur), Is.EqualTo(90f).Within(1e-2f),
                    $"Gap to seat {_t} is not 90°.");
                _prev = _cur;
            }
        }

        [Test]
        public void SeatsFaceTheCenter()
        {
            // The seat rotation looks at the table center (rot*forward ≈ direction to center).
            for (int _t = 0; _t < 5; _t++)
            {
                SeatPose _pose = Compute(5, 1, _t);
                Vector3 _facing = _pose.Rotation * Vector3.forward;
                Vector3 _toCenter = (Center - _pose.Position).normalized;
                Assert.That(Vector3.Dot(_facing, _toCenter), Is.GreaterThan(0.999f),
                    $"Seat {_t} does not face the center.");
            }
        }

        [Test]
        public void AngularSeparation_IsFrameIndependent()
        {
            // THE gaze invariant: the angle subtended at the center between any two players is identical in
            // every client's locally-rotated frame (a rigid rotation preserves it). Players a=1, b=3, N=5.
            const int N = 5, A = 1, B = 3;
            Vector3 _a0 = Compute(N, 0, A).Position - Center;
            Vector3 _b0 = Compute(N, 0, B).Position - Center;
            Vector3 _a2 = Compute(N, 2, A).Position - Center;
            Vector3 _b2 = Compute(N, 2, B).Position - Center;
            Assert.That(Vector3.Angle(_a2, _b2), Is.EqualTo(Vector3.Angle(_a0, _b0)).Within(1e-2f),
                "Player-to-player angular separation changed between client frames — gaze would break.");
        }

        [Test]
        public void RelativeOffset_WrapsModuloCount()
        {
            // Target one seat "behind" local (offset N-1) sits at the same place as offset -1: equivalent to
            // the seat just before the front going the other way. Verify wrap: Compute(N, local=2, target=1)
            // == Compute(N, local=0, target=N-1) (both relative offset N-1).
            const int N = 4;
            SeatPose _wrapA = Compute(N, 2, 1);
            SeatPose _wrapB = Compute(N, 0, N - 1);
            Assert.That(Vector3.Distance(_wrapA.Position, _wrapB.Position), Is.LessThan(1e-3f),
                "Relative offset did not wrap modulo count.");
        }

        [Test]
        public void Guards_CountZeroAndDegenerateForward_DoNotThrow()
        {
            // count <= 0 collapses to the front spot (no div-by-zero); a vertical forward falls back to world
            // forward so the ring never degenerates to a point.
            Assert.DoesNotThrow(() =>
            {
                SeatPose _zero = SeatRingGeometry.Compute(0, 0, 0, Center, Forward, Radius, Front);
                Assert.That(Vector3.Distance(_zero.Position, Center + Forward * Radius), Is.LessThan(1e-3f));

                SeatPose _vertical = SeatRingGeometry.Compute(3, 0, 1, Center, Vector3.up, Radius, Front);
                Assert.That(Vector3.Distance(_vertical.Position, Center), Is.EqualTo(Radius).Within(1e-3f));
            });
        }
    }
}
