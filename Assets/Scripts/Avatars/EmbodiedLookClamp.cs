using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Story 13.4 (Epic 13 — Player Embodiment). The PURE clamped-look math for the embodied
    /// seated Vote camera (DO4 — yaw ±75°, pitch ±40°, tunable). Single decision point so the
    /// look feel is EditMode-testable with zero scene/network setup
    /// (<c>EmbodiedLookClampTests</c> asserts the boundaries directly).
    ///
    /// It mirrors the accumulate-then-clamp shape already used by the lobby look in
    /// <see cref="AvatarMovementController.Update"/> (AvatarMovementController.cs:119-129): mouse
    /// X accumulates yaw, mouse Y accumulates pitch (mouse up = look up = NEGATIVE euler-X, hence
    /// the subtraction), each clamped to its bound. The seated camera (<see cref="AvatarEmbodiedCamera"/>)
    /// feeds <c>Time.deltaTime</c>-scaled deltas in and applies the returned angles to the camera
    /// transform RELATIVE to the seat facing (<c>seat.rotation * Quaternion.Euler(pitch, yaw, 0)</c>).
    ///
    /// Lives in the <b>Game</b> asmdef (next to the other avatar code), NOT Domain — it uses
    /// <see cref="Mathf"/>/<see cref="Vector2"/> (UnityEngine), forbidden by Domain's purity guard.
    /// It is otherwise a stateless float function (no Transform, no Time, no scene refs).
    /// </summary>
    public static class EmbodiedLookClamp
    {
        /// <summary>
        /// Accumulates a look delta onto the current yaw/pitch and clamps each to its bound.
        /// <paramref name="lookDelta"/>.x adds to yaw; <paramref name="lookDelta"/>.y SUBTRACTS from
        /// pitch (mouse-up looks up). Speeds scale the raw delta; clamps are symmetric (±value).
        /// </summary>
        public static EmbodiedLookAngles Apply(
            float _currentYaw, float _currentPitch, Vector2 _lookDelta,
            float _yawSpeed, float _pitchSpeed, float _yawClamp, float _pitchClamp)
        {
            float _yaw = Mathf.Clamp(_currentYaw + _lookDelta.x * _yawSpeed, -_yawClamp, _yawClamp);
            float _pitch = Mathf.Clamp(_currentPitch - _lookDelta.y * _pitchSpeed, -_pitchClamp, _pitchClamp);
            return new EmbodiedLookAngles(_yaw, _pitch);
        }
    }

    /// <summary>Immutable yaw/pitch pair returned by <see cref="EmbodiedLookClamp.Apply"/> (degrees).</summary>
    public readonly struct EmbodiedLookAngles
    {
        public readonly float Yaw;
        public readonly float Pitch;

        public EmbodiedLookAngles(float _yaw, float _pitch)
        {
            Yaw = _yaw;
            Pitch = _pitch;
        }
    }
}
