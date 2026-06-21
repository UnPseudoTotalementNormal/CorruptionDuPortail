using UnityEngine;

namespace Presentation
{
    /// <summary>The hover-focus pose: a world rotation that faces the camera + the WORLD-space lift that
    /// floats the object just above its surface without clipping. The caller converts the world lift into
    /// whatever local space it animates.</summary>
    public readonly struct HoverFocusPose
    {
        public readonly Quaternion Rotation;
        public readonly float WorldLift;

        public HoverFocusPose(Quaternion _rotation, float _worldLift)
        {
            Rotation = _rotation;
            WorldLift = _worldLift;
        }
    }

    /// <summary>
    /// PURE geometry for "look at the camera + lift to float above a flat surface" (EditMode-testable, no
    /// scene). All extents + the returned lift are WORLD units; the caller handles its own local-space scale.
    ///
    /// Look-at: rotate so the local face-normal points at the camera and the local face-up stays world-upright.
    /// Lift: the object's vertical extents are measured (DYNAMICALLY by the caller, including the deployed vote
    /// canvas) RELATIVE TO THE ROTATION PIVOT as <paramref name="_extentTopWorld"/> (up, +) and
    /// <paramref name="_extentBottomWorld"/> (down, usually −). The 4 rotated corners give the lowest point;
    /// lift so it sits at <paramref name="_surfaceY"/> + <paramref name="_offset"/> (never pushes DOWN).
    /// </summary>
    public static class HoverFocusMath
    {
        private const float Epsilon = 1e-5f;

        public static HoverFocusPose Compute(
            Vector3 _pivotWorldPos,
            Vector3 _cameraWorldPos,
            Vector3 _localFaceNormal,
            Vector3 _localFaceUp,
            float _extentTopWorld,
            float _extentBottomWorld,
            float _halfWidthWorld,
            float _surfaceY,
            float _offset)
        {
            // World target basis: face the camera; keep upright by flattening world-up perpendicular to the
            // look direction (degenerate fallbacks so LookRotation never gets collinear inputs).
            Vector3 _n = _cameraWorldPos - _pivotWorldPos;
            Vector3 _N = _n.sqrMagnitude > Epsilon ? _n.normalized : Vector3.up;
            Vector3 _U = Vector3.up - Vector3.Dot(Vector3.up, _N) * _N;
            _U = _U.sqrMagnitude > Epsilon ? _U.normalized : Vector3.forward;

            // Local face basis (orthonormalized): normal + an up made perpendicular to it.
            Vector3 _ln = _localFaceNormal.sqrMagnitude > Epsilon ? _localFaceNormal.normalized : Vector3.up;
            Vector3 _lu = _localFaceUp - Vector3.Dot(_localFaceUp, _ln) * _ln;
            _lu = _lu.sqrMagnitude > Epsilon ? _lu.normalized : Vector3.forward;

            // R maps the local face basis (normal→N, up→U): R = world∘inverse(local) using LookRotation frames.
            Quaternion _rotation =
                Quaternion.LookRotation(_N, _U) * Quaternion.Inverse(Quaternion.LookRotation(_ln, _lu));

            // Lowest of the 4 rotated corners (the asymmetric extents from the pivot): the min over the two
            // vertical extents minus the (symmetric) width contribution. Alloc-free. Then lift so the lowest
            // world point sits at surface + offset.
            float _upY = (_rotation * _lu).y;
            float _rightY = (_rotation * Vector3.Cross(_lu, _ln)).y;
            float _lowest = Mathf.Min(_extentTopWorld * _upY, _extentBottomWorld * _upY)
                            - Mathf.Abs(_halfWidthWorld * _rightY);

            // SIGNED lift: negative means the lowest point is already above surface+offset. The caller decides
            // the floor (e.g. clamp the result so the object never goes below its rest), so a continuous
            // tracker can ease the object back DOWN toward the target, not only up.
            float _worldLift = (_surfaceY + _offset - _lowest) - _pivotWorldPos.y;
            return new HoverFocusPose(_rotation, _worldLift);
        }
    }
}
