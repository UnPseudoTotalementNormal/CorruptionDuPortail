using NUnit.Framework;
using Presentation;
using UnityEngine;

namespace Tests.Editor.Presentation
{
    /// <summary>
    /// GOLDEN for the pure hover-focus geometry (<see cref="HoverFocusMath"/>): the look-at faces the camera
    /// and the COMPUTED lift floats the rotated card above the table (no clip) at any angle. A flat card:
    /// face normal +Y (up), face up +Z. Lowest-corner check is independent of the helper's own drop formula.
    /// </summary>
    public class HoverFocusMathTests
    {
        private static readonly Vector3 FaceNormal = Vector3.up;
        private static readonly Vector3 FaceUp = Vector3.forward;
        private const float HalfH = 2.2f;
        private const float HalfW = 1.575f;
        private const float SurfaceY = -15.025f;
        private const float Offset = 0.2f;
        private const float ScaleY = 0.5f;
        private static readonly Vector3 CardPos = new Vector3(0f, -15f, 0f);

        private static float LowestCornerY(HoverFocusPose _pose, Vector3 _cardPos)
        {
            float _worldLift = _pose.LocalLiftY * ScaleY;
            Vector3 _center = _cardPos + Vector3.up * _worldLift;
            Vector3 _up = _pose.Rotation * FaceUp.normalized;
            Vector3 _right = _pose.Rotation * Vector3.Cross(FaceUp.normalized, FaceNormal.normalized);
            float _min = float.MaxValue;
            foreach (float _sh in new[] { -1f, 1f })
            {
                foreach (float _sw in new[] { -1f, 1f })
                {
                    float _y = _center.y + _sh * HalfH * _up.y + _sw * HalfW * _right.y;
                    _min = Mathf.Min(_min, _y);
                }
            }
            return _min;
        }

        [Test]
        public void Rotation_FacesTheCamera()
        {
            Vector3 _cam = new Vector3(3f, -10f, 4f);
            HoverFocusPose _pose = HoverFocusMath.Compute(CardPos, _cam, FaceNormal, FaceUp, HalfH, HalfW, SurfaceY, Offset, ScaleY);
            Vector3 _faceWorld = (_pose.Rotation * FaceNormal.normalized).normalized;
            Vector3 _toCam = (_cam - CardPos).normalized;
            Assert.That(Vector3.Dot(_faceWorld, _toCam), Is.GreaterThan(0.999f), "card face does not point at the camera");
        }

        [Test]
        public void Lift_FloatsLowestCornerAtSurfacePlusOffset()
        {
            // Try several camera elevations/azimuths — the lowest corner must always land at surface+offset.
            foreach (Vector3 _cam in new[]
                     {
                         new Vector3(0f, -10f, 5f), new Vector3(6f, -11f, 2f),
                         new Vector3(-4f, -9f, -3f), new Vector3(0f, -12.5f, 8f),
                     })
            {
                HoverFocusPose _pose = HoverFocusMath.Compute(CardPos, _cam, FaceNormal, FaceUp, HalfH, HalfW, SurfaceY, Offset, ScaleY);
                float _lowest = LowestCornerY(_pose, CardPos);
                Assert.That(_lowest, Is.EqualTo(SurfaceY + Offset).Within(1e-2f),
                    $"lowest corner not floating at surface+offset for camera {_cam} (got {_lowest})");
            }
        }

        [Test]
        public void AlreadyAboveSurface_DoesNotPushDown()
        {
            Vector3 _highCard = new Vector3(0f, SurfaceY + 100f, 0f);
            HoverFocusPose _pose = HoverFocusMath.Compute(_highCard, new Vector3(0f, SurfaceY + 105f, 5f),
                FaceNormal, FaceUp, HalfH, HalfW, SurfaceY, Offset, ScaleY);
            Assert.That(_pose.LocalLiftY, Is.EqualTo(0f).Within(1e-4f), "must never lift the card DOWN toward the table");
        }

        [Test]
        public void DegenerateInputs_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                HoverFocusMath.Compute(CardPos, CardPos, FaceNormal, FaceUp, HalfH, HalfW, SurfaceY, Offset, ScaleY);
                HoverFocusMath.Compute(CardPos, new Vector3(0f, -10f, 5f), Vector3.zero, Vector3.zero, HalfH, HalfW, SurfaceY, Offset, 0f);
            });
        }
    }
}
