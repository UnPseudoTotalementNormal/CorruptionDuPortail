using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// PURE helpers for the legless hop locomotion (EditMode-testable, no scene — mirrors the
    /// <see cref="AvatarLookMath"/> / <see cref="AvatarIdleMath"/> pure-helper pattern).
    /// </summary>
    public static class AvatarHopMath
    {
        /// <summary>
        /// Half-sine hop arc in [0,1] over a unit of phase: 0 at the ground (integer phase), peak 1 at the
        /// apex (phase 0.5). Clamped ≥ 0 so the body never dips below the floor. One hop per unit of phase.
        /// </summary>
        public static float Arc(float _phase) => Mathf.Max(0f, Mathf.Sin(_phase * Mathf.PI));

        /// <summary>
        /// Squash/stretch factor in [-1,1] derived from the arc: +1 tall-and-thin at the apex, -1
        /// short-and-wide at the ground (the landing/launch absorb).
        /// </summary>
        public static float SquashStretch(float _phase) => (Arc(_phase) - 0.5f) * 2f;

        /// <summary>
        /// Framerate-independent ease of the hop amplitude toward a target (1 moving, 0 stopped) — gives a
        /// clean rest-on-ground when the avatar stops instead of freezing mid-arc.
        /// </summary>
        public static float EaseAmplitude(float _current, float _target, float _responsiveness, float _dt)
        {
            float _t = 1f - Mathf.Exp(-_responsiveness * _dt);
            return _current + (_target - _current) * _t;
        }
    }
}
