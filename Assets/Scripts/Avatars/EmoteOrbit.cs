using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Pure orbit-pose math for the emote third-person camera (EditMode-tested, no Unity scene state). Given a
    /// pivot (the avatar), an orbit yaw/pitch, and a distance, it returns the camera POSITION on the orbit sphere
    /// and a ROTATION that looks back at the pivot. Kept separate from <see cref="AvatarEmoteOrbitCamera"/> so the
    /// geometry can be golden-tested without a live Cinemachine camera.
    /// </summary>
    public static class EmoteOrbit
    {
        public static void ComputePose(Vector3 _pivot, float _yawDeg, float _pitchDeg, float _distance,
            out Vector3 _position, out Quaternion _rotation)
        {
            Quaternion _orbit = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
            _position = _pivot + _orbit * new Vector3(0f, 0f, -Mathf.Max(0f, _distance));
            Vector3 _toPivot = _pivot - _position;
            _rotation = _toPivot.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(_toPivot, Vector3.up)
                : _orbit;
        }
    }

    /// <summary>
    /// Pure stop-condition for a running emote (EditMode-tested). The emote holds until the player ACTS: any key
    /// press OR any mouse-button press ends it. Mouse MOVEMENT is deliberately NOT a stop — it drives the orbit.
    /// </summary>
    public static class EmoteStop
    {
        public static bool ShouldStop(bool _anyKeyThisFrame, bool _anyMouseButtonThisFrame)
            => _anyKeyThisFrame || _anyMouseButtonThisFrame;
    }
}
