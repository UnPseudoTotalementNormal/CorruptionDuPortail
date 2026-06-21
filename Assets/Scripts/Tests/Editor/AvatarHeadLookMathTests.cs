using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN for the pure head-look math (<see cref="AvatarLookMath"/>): cone/pitch clamps, the shortest-path
    /// framerate-independent ease, and the body-catch-up convergence that returns the head yaw offset to 0.
    /// Pure math → asserted with ZERO scene/network setup (mirrors <see cref="SeatRingGeometryTests"/>).
    /// </summary>
    public class AvatarHeadLookMathTests
    {
        [Test]
        public void ClampSymmetric_BoundsToCone()
        {
            Assert.That(AvatarLookMath.ClampSymmetric(95f, 60f), Is.EqualTo(60f), "Above +cone must clamp.");
            Assert.That(AvatarLookMath.ClampSymmetric(-95f, 60f), Is.EqualTo(-60f), "Below -cone must clamp.");
            Assert.That(AvatarLookMath.ClampSymmetric(20f, 60f), Is.EqualTo(20f), "Inside the cone is untouched.");
        }

        [Test]
        public void HeadYawOffset_IsShortestSignedDeltaClampedToCone()
        {
            // Look 30 deg right of the body → +30 offset, inside the cone.
            Assert.That(AvatarLookMath.HeadYawOffset(0f, 30f, 60f), Is.EqualTo(30f).Within(1e-3f));
            // Wrap: body 350, look 10 → +20 (shortest path), not -340.
            Assert.That(AvatarLookMath.HeadYawOffset(350f, 10f, 60f), Is.EqualTo(20f).Within(1e-3f));
            // Look 120 deg away → clamped to the 60 cone.
            Assert.That(AvatarLookMath.HeadYawOffset(0f, 120f, 60f), Is.EqualTo(60f).Within(1e-3f));
        }

        [Test]
        public void EaseAngle_TakesShortestPath_AcrossWrap()
        {
            // From 350 toward 10 must move UP through 360/0 (positive delta), never down through 180.
            float _next = AvatarLookMath.EaseAngle(350f, 10f, 12f, 1f / 60f);
            Assert.That(_next, Is.GreaterThan(350f), "Must ease upward across the 0 boundary, not the long way.");
        }

        [Test]
        public void EaseAngle_ConvergesMonotonically_ToTarget()
        {
            float _current = 0f;
            const float _target = 75f;
            for (int _i = 0; _i < 600; _i++) // 10 s at 60 fps
            {
                _current = AvatarLookMath.EaseAngle(_current, _target, 8f, 1f / 60f);
            }
            Assert.That(_current, Is.EqualTo(_target).Within(0.5f), "Ease must converge to the target.");
        }

        [Test]
        public void BodyCatchUp_DrivesHeadOffset_ToZero()
        {
            // The body eases toward a fixed look heading; the head offset (look - body) must decay to ~0.
            const float _lookYaw = 50f;
            float _bodyYaw = 0f;
            const float _cone = 60f;

            float _initialOffset = AvatarLookMath.HeadYawOffset(_bodyYaw, _lookYaw, _cone);
            Assert.That(Mathf.Abs(_initialOffset), Is.GreaterThan(40f), "Precondition: head starts well off-centre.");

            for (int _i = 0; _i < 300; _i++) // 5 s at 60 fps
            {
                _bodyYaw = AvatarLookMath.EaseAngle(_bodyYaw, _lookYaw, 6f, 1f / 60f);
            }

            float _finalOffset = AvatarLookMath.HeadYawOffset(_bodyYaw, _lookYaw, _cone);
            Assert.That(Mathf.Abs(_finalOffset), Is.LessThan(0.5f), "Body catch-up must re-centre the head to ~0.");
        }
    }
}
