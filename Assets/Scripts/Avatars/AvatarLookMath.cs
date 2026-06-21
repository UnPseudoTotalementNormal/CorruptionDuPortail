using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// PURE look math for the rigged head-look + body follow-through (EditMode-testable, no scene/network —
    /// mirrors the <see cref="SeatRingGeometry"/> / EmbodiedLookClamp pure-helper pattern). Degrees throughout.
    ///
    /// Shared by <see cref="AvatarMovementController"/> (free-roam: head cone + body catch-up) and
    /// <see cref="AvatarHeadLook"/> (smoothing the displayed aim toward the networked look).
    /// </summary>
    public static class AvatarLookMath
    {
        /// <summary>Symmetric clamp to ±limit (the head yaw cone, or the pitch range).</summary>
        public static float ClampSymmetric(float _value, float _limit) =>
            Mathf.Clamp(_value, -_limit, _limit);

        /// <summary>
        /// Framerate-independent exponential ease of an ANGLE (deg) toward a target along the SHORTEST path
        /// (so it wraps cleanly across ±180). <paramref name="_responsiveness"/> higher = snappier. Returns
        /// the new current angle. With a fixed target and positive responsiveness it converges monotonically.
        /// </summary>
        public static float EaseAngle(float _current, float _target, float _responsiveness, float _deltaTime)
        {
            float _t = 1f - Mathf.Exp(-_responsiveness * _deltaTime);
            return _current + Mathf.DeltaAngle(_current, _target) * _t;
        }

        /// <summary>
        /// The head yaw OFFSET that leads the body: how far the look heading is ahead of the body facing,
        /// clamped to the cone. Shortest signed path. As the body catches up to the look, this returns to 0.
        /// </summary>
        public static float HeadYawOffset(float _bodyYaw, float _lookYaw, float _cone) =>
            ClampSymmetric(Mathf.DeltaAngle(_bodyYaw, _lookYaw), _cone);
    }
}
