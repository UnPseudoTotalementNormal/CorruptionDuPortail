using NUnit.Framework;
using Presentation;
using UnityEngine;

namespace Tests.Editor.Presentation
{
    /// <summary>
    /// GOLDEN for the pure hover-focus geometry (<see cref="HoverFocusMath"/>): the look-at faces the camera
    /// and the COMPUTED lift floats the rotated card above the table (no clip) at any angle, with ASYMMETRIC
    /// extents so a deployed vote canvas (extending the bottom) still clears the floor. A flat card: face
    /// normal +Y, face up +Z. Lowest-corner check is independent of the helper's own formula.
    /// </summary>
    public class HoverFocusMathTests
    {
        private static readonly Vector3 FaceNormal = Vector3.up;
        private static readonly Vector3 FaceUp = Vector3.forward;
        private const float ExtTop = 2.2f;
        private const float ExtBottom = -2.2f;
        private const float HalfW = 1.575f;
        private const float SurfaceY = -15.025f;
        private const float Offset = 0.2f;
        private static readonly Vector3 Pivot = new Vector3(0f, -15f, 0f);

        private static float LowestCornerY(HoverFocusPose _pose, float _extBottom)
        {
            Vector3 _center = Pivot + Vector3.up * _pose.WorldLift;
            Vector3 _up = _pose.Rotation * FaceUp.normalized;
            Vector3 _right = _pose.Rotation * Vector3.Cross(FaceUp.normalized, FaceNormal.normalized);
            float _min = float.MaxValue;
            foreach (float _v in new[] { ExtTop, _extBottom })
            {
                foreach (float _w in new[] { HalfW, -HalfW })
                {
                    _min = Mathf.Min(_min, _center.y + _v * _up.y + _w * _right.y);
                }
            }
            return _min;
        }

        [Test]
        public void Rotation_FacesTheCamera()
        {
            Vector3 _cam = new Vector3(3f, -10f, 4f);
            HoverFocusPose _pose = HoverFocusMath.Compute(Pivot, _cam, FaceNormal, FaceUp, ExtTop, ExtBottom, HalfW, SurfaceY, Offset);
            Vector3 _faceWorld = (_pose.Rotation * FaceNormal.normalized).normalized;
            Vector3 _toCam = (_cam - Pivot).normalized;
            Assert.That(Vector3.Dot(_faceWorld, _toCam), Is.GreaterThan(0.999f), "card face does not point at the camera");
        }

        [Test]
        public void Lift_FloatsLowestCornerAtSurfacePlusOffset()
        {
            foreach (Vector3 _cam in new[]
                     {
                         new Vector3(0f, -10f, 5f), new Vector3(6f, -11f, 2f),
                         new Vector3(-4f, -9f, -3f), new Vector3(0f, -12.5f, 8f),
                     })
            {
                HoverFocusPose _pose = HoverFocusMath.Compute(Pivot, _cam, FaceNormal, FaceUp, ExtTop, ExtBottom, HalfW, SurfaceY, Offset);
                Assert.That(LowestCornerY(_pose, ExtBottom), Is.EqualTo(SurfaceY + Offset).Within(1e-2f),
                    $"lowest corner not floating at surface+offset for camera {_cam}");
            }
        }

        [Test]
        public void AsymmetricBottom_ClearsTheDeployedVoteCanvas()
        {
            // The vote canvas extends the bottom far below the card body — the lift must clear THAT, not the
            // card body. Lowest corner (using the deeper bottom) still lands at surface+offset.
            const float _deepBottom = -3.6f;
            Vector3 _cam = new Vector3(0f, -10f, 5f);
            HoverFocusPose _pose = HoverFocusMath.Compute(Pivot, _cam, FaceNormal, FaceUp, ExtTop, _deepBottom, HalfW, SurfaceY, Offset);
            Assert.That(LowestCornerY(_pose, _deepBottom), Is.EqualTo(SurfaceY + Offset).Within(1e-2f),
                "deeper bottom extent (vote canvas) not cleared");
        }

        [Test]
        public void AlreadyAboveSurface_DoesNotPushDown()
        {
            Vector3 _highPivot = new Vector3(0f, SurfaceY + 100f, 0f);
            HoverFocusPose _pose = HoverFocusMath.Compute(_highPivot, new Vector3(0f, SurfaceY + 105f, 5f),
                FaceNormal, FaceUp, ExtTop, ExtBottom, HalfW, SurfaceY, Offset);
            Assert.That(_pose.WorldLift, Is.EqualTo(0f).Within(1e-4f), "must never lift the card DOWN toward the table");
        }

        [Test]
        public void DegenerateInputs_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                HoverFocusMath.Compute(Pivot, Pivot, FaceNormal, FaceUp, ExtTop, ExtBottom, HalfW, SurfaceY, Offset);
                HoverFocusMath.Compute(Pivot, new Vector3(0f, -10f, 5f), Vector3.zero, Vector3.zero, ExtTop, ExtBottom, HalfW, SurfaceY, Offset);
            });
        }
    }
}
