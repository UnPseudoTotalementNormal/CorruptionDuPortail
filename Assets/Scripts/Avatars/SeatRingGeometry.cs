using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// A computed seat placement around the table ring: a world position and a facing rotation.
    /// Plain struct (Unity compiles at C# 9 — no record struct).
    /// </summary>
    public readonly struct SeatPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public SeatPose(Vector3 _position, Quaternion _rotation)
        {
            Position = _position;
            Rotation = _rotation;
        }
    }

    /// <summary>
    /// PURE seat-ring geometry (EditMode-testable, no NetworkManager — mirrors the EmbodiedLookClamp
    /// pure-helper pattern). Computes where an avatar sits around a single ring center.
    ///
    /// PER-CLIENT ROTATION (the core idea): the *relative* arrangement of avatars (who sits next to whom,
    /// by the replicated avatar-list order) is identical on every client. Each client rigidly rotates the
    /// whole ring so that ITS OWN avatar (relative offset 0) lands at the fixed FRONT spot "in front of the
    /// table" — the interaction anchor. A rigid rotation is an isometry: it preserves every angle, so the
    /// "A looks at B" relation is identical on every client even though world positions differ per client
    /// (this is why seated positions are NOT networked, yet gaze stays consistent — provided head yaw is
    /// stored RELATIVE to seat facing, which rotates with the frame).
    /// </summary>
    public static class SeatRingGeometry
    {
        private const float Epsilon = 1e-4f;

        /// <summary>
        /// The seat pose for the avatar at <paramref name="_targetIndex"/> in the replicated avatar list,
        /// as seen from the local client whose own avatar is at <paramref name="_localIndex"/>.
        ///
        /// Relative offset = (target - local) mod count → the local avatar (offset 0) is always at
        /// <paramref name="_frontAngleDeg"/> (the front spot); others spread equidistant by 360/count.
        /// Seats face the ring center. Count &lt;= 0 and a degenerate forward are guarded (no div-by-zero,
        /// no crash) — they collapse to the front spot / identity facing.
        /// </summary>
        public static SeatPose Compute(
            int _count,
            int _localIndex,
            int _targetIndex,
            Vector3 _center,
            Vector3 _centerForward,
            float _radius,
            float _frontAngleDeg)
        {
            if (_count <= 0)
            {
                _count = 1;
            }

            // Front direction: the ring center's forward, flattened onto the XZ plane. Degenerate (straight
            // up/down) falls back to world forward so the ring never collapses to a point.
            Vector3 _forward = _centerForward;
            _forward.y = 0f;
            _forward = _forward.sqrMagnitude < Epsilon ? Vector3.forward : _forward.normalized;

            float _step = 360f / _count;
            int _rel = (((_targetIndex - _localIndex) % _count) + _count) % _count;
            float _angle = _frontAngleDeg + _rel * _step;

            Vector3 _dir = Quaternion.AngleAxis(_angle, Vector3.up) * _forward;
            Vector3 _position = _center + _dir * _radius;

            Vector3 _toCenter = _center - _position;
            Quaternion _rotation = _toCenter.sqrMagnitude > Epsilon
                ? Quaternion.LookRotation(_toCenter, Vector3.up)
                : Quaternion.identity;

            return new SeatPose(_position, _rotation);
        }
    }
}
