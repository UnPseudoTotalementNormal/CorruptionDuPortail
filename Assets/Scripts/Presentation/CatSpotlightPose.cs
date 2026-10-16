using UnityEngine;

namespace Presentation
{
    /// <summary>
    /// Where a cat's own spotlight hangs (board task T11, <see cref="CatSpotlights"/>). Pure, unit-tested.
    /// </summary>
    public static class CatSpotlightPose
    {
        private const float Epsilon = 1e-6f;

        /// <summary>
        /// The light <paramref name="_distance"/> away from <paramref name="_target"/> (the cat's head), raised
        /// <paramref name="_elevationDeg"/> above the horizontal on the side the cat faces (the table): 90 = straight above,
        /// lower = tilted toward the table, lighting the face more. The light points at the target. A degenerate facing
        /// (straight up / down) falls back to world forward.
        /// </summary>
        public static Pose Compute(Vector3 _target, Vector3 _seatForward, float _elevationDeg, float _distance)
        {
            Vector3 _forward = _seatForward;
            _forward.y = 0f;
            _forward = _forward.sqrMagnitude < Epsilon ? Vector3.forward : _forward.normalized;

            float _elevation = Mathf.Clamp(_elevationDeg, 0f, 90f) * Mathf.Deg2Rad;
            Vector3 _toLight = Vector3.up * Mathf.Sin(_elevation) + _forward * Mathf.Cos(_elevation);
            // Straight down, "up" is undefined: keep the cookie-less cone rolled along the cat's facing.
            Vector3 _up = _toLight.y > 1f - Epsilon ? _forward : Vector3.up;
            return new Pose(_target + _toLight * _distance, Quaternion.LookRotation(-_toLight, _up));
        }
    }
}
