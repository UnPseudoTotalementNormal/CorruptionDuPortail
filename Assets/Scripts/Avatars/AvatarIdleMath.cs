using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// PURE helpers for the cosmetic idle secondary motion (EditMode-testable, no scene — mirrors the
    /// <see cref="AvatarLookMath"/> / SeatRingGeometry pure-helper pattern).
    /// </summary>
    public static class AvatarIdleMath
    {
        /// <summary>
        /// A deterministic per-instance phase in [0,1). Same id → same value; different ids spread out — so
        /// looping idles (breathing, whiskers) do NOT beat in lockstep across a table of avatars.
        /// </summary>
        public static float PhaseSeed(int _instanceId)
        {
            // Knuth multiplicative hash → take the low 24 bits as a well-spread fraction.
            uint _h = (uint)_instanceId * 2654435761u;
            return (_h & 0xFFFFFFu) / (float)0x1000000u;
        }

        /// <summary>
        /// One semi-implicit damped-spring step toward <paramref name="_target"/>. Updates <paramref name="_velocity"/>
        /// by ref and returns the new position. Converges to the target; for low damping it overshoots then
        /// settles. Drives the bow follow-through (perturb the velocity by the body's angular velocity, pull to 0).
        /// </summary>
        public static float DampedSpring(float _current, ref float _velocity, float _target,
                                         float _stiffness, float _damping, float _dt)
        {
            float _force = -_stiffness * (_current - _target) - _damping * _velocity;
            _velocity += _force * _dt;
            return _current + _velocity * _dt;
        }
    }
}
