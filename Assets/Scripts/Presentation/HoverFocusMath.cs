using UnityEngine;

namespace Presentation
{
    /// <summary>The hover-focus pose: a world rotation that faces the camera + the LOCAL lift (in the host
    /// transform's local space) that floats the object just above its surface without clipping.</summary>
    public readonly struct HoverFocusPose
    {
        public readonly Quaternion Rotation;
        public readonly float LocalLiftY;

        public HoverFocusPose(Quaternion _rotation, float _localLiftY)
        {
            Rotation = _rotation;
            LocalLiftY = _localLiftY;
        }
    }

    /// <summary>
    /// PURE geometry for "look at the camera + lift to float above a flat surface" (EditMode-testable, no
    /// scene). Reusable by any flat element (cards, …). The element's FACE axes are PARAMETERS — the v1 hover
    /// guessed the axis and clipped/faced wrong; here the caller passes the real local face normal + up.
    ///
    /// Look-at: rotate so the local face-normal points at the camera and the local face-up stays world-upright.
    /// Lift: a flat rect of half-extents (halfHeight along face-up, halfWidth along face-right) rotated by R
    /// drops its lowest corner by <c>halfHeight·|R·up|.y + halfWidth·|R·right|.y</c>; lift so that lowest point
    /// sits at <c>surfaceY + offset</c>. The lift is returned in the host's LOCAL space (÷ root world scale),
    /// clamped ≥ 0 (never push the object DOWN into the table).
    /// </summary>
    public static class HoverFocusMath
    {
        private const float Epsilon = 1e-5f;

        public static HoverFocusPose Compute(
            Vector3 _cardWorldPos,
            Vector3 _cameraWorldPos,
            Vector3 _localFaceNormal,
            Vector3 _localFaceUp,
            float _halfHeight,
            float _halfWidth,
            float _surfaceY,
            float _offset,
            float _rootWorldScaleY)
        {
            // World target basis: face the camera; keep upright by flattening world-up perpendicular to the
            // look direction (degenerate fallbacks so LookRotation never gets collinear inputs).
            Vector3 _n = _cameraWorldPos - _cardWorldPos;
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

            // Drop of the rotated rect's lowest corner below its centre, then lift so it sits at surface+offset.
            Vector3 _right = Vector3.Cross(_lu, _ln);
            float _drop = _halfHeight * Mathf.Abs((_rotation * _lu).y) + _halfWidth * Mathf.Abs((_rotation * _right).y);
            float _worldLift = (_surfaceY + _offset + _drop) - _cardWorldPos.y;
            if (_worldLift < 0f)
            {
                _worldLift = 0f;
            }

            float _localLift = Mathf.Abs(_rootWorldScaleY) > Epsilon ? _worldLift / _rootWorldScaleY : _worldLift;
            return new HoverFocusPose(_rotation, _localLift);
        }
    }
}
