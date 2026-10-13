using System.Collections.Generic;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Cosmetic idle secondary motion for the cat avatar (LOCAL, runs on every client; NO networking — each
    /// client sims its own, phase-offset per avatar so a table of cats never moves in lockstep). Each
    /// LateUpdate it layers procedural motion onto the rig: gentle breathing on the spine, ambient ear
    /// twitch/sway, a subtle whisker quiver, and a bow spring follow-through driven by the body's angular
    /// velocity (which trails a head/body turn — pairs with the head-look feature).
    ///
    /// ACCUMULATION-FREE, ADDITIVE OVER THE CLIP: each driven bone's REST localRotation is captured at Awake. Each
    /// frame the offset goes on top of the bone's pose of THIS frame: the Animator's pose when it wrote the bone
    /// (the artist's animations key the spine, ears, whiskers and bow), else the rest pose (a bone no clip keys
    /// keeps what we wrote last frame: `*=` on it would COMPOUND and wind it to garbage). "Did the Animator write
    /// it?" = its rotation is no longer the one we wrote last frame.
    ///
    /// PIVOT CORRECTNESS: every motion drives the BASE bone of its chain and post-multiplies its rest rotation
    /// (`rest * Euler(offset)`) — the offset is in the bone's LOCAL frame, so it pivots at the bone origin: the
    /// ear swings from the skull, the whisker from the cheek, the bow from the knot. Never a mid/tip bone,
    /// never composed in world/root space.
    ///
    /// Runs at execution order 150: after AvatarSeatingPresenter (100), BEFORE AvatarHeadLook (200). The
    /// head-look sets the head bone's aim on top; breathing on the spine is inherited by the head as a subtle
    /// bob, but the head-look delta preserves the gaze DIRECTION — idle never overrides where the head points.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class AvatarIdleMotion : MonoBehaviour
    {
        [Header("Breathing (spine) — Poyo-tuned")]
        [SerializeField] private float _breathAmplitudeDeg = 2f;
        [SerializeField] private float _breathSpeed = 0.5f;
        [Header("Ears (ambient idle)")]
        [SerializeField] private float _earAmplitudeDeg = 4f;
        [SerializeField] private float _earSpeed = 0.5f;
        [Header("Whiskers")]
        [SerializeField] private float _whiskerAmplitudeDeg = 1.5f;
        [Header("Bow follow-through (spring)")]
        [SerializeField] private float _bowStiffness = 120f;
        [SerializeField] private float _bowDamping = 14f;
        [SerializeField] private float _bowGain = 0.5f;
        [SerializeField] private float _bowMaxDeg = 12f;

        // A body-yaw jump beyond this in ONE frame is a teleport (seat snap, respawn), not a turn — its huge
        // angular velocity would whip the bow, so we ignore it. Spring is stepped with a clamped dt for
        // stability across frame hitches, and the velocity is capped.
        private const float _teleportDeg = 45f;
        private const float _maxSpringDt = 1f / 30f;
        private const float _maxBowVelocity = 720f;

        private Transform _root;
        private readonly List<Transform> _spine = new();
        private readonly List<Transform> _ears = new();
        private readonly List<Transform> _whiskers = new();
        private readonly List<Transform> _bow = new();
        private readonly List<Quaternion> _spineRest = new();
        private readonly List<Quaternion> _earsRest = new();
        private readonly List<Quaternion> _whiskersRest = new();
        private readonly List<Quaternion> _bowRest = new();
        // What we wrote last frame, per bone (see Drive).
        private readonly Dictionary<Transform, Quaternion> _written = new();
        private float _seed;
        private float _prevRootYaw;
        private float _bowOffset;   // shared spring offset (deg) for the whole bow chain
        private float _bowVelocity;
        private bool _warned;

        private void Awake()
        {
            PlayerAvatar _pa = GetComponentInParent<PlayerAvatar>();
            _root = _pa != null ? _pa.transform : transform;
            _seed = AvatarIdleMath.PhaseSeed(GetEntityId().GetHashCode());

            // Auto-find the BASE bone of each chain by name convention — no fragile per-bone Inspector wiring.
            // Capture each bone's REST localRotation so the idle is applied rest-relative (accumulation-free).
            foreach (Transform _t in GetComponentsInChildren<Transform>(true))
            {
                string _n = _t.name;
                if (_n.StartsWith("Spine"))
                {
                    _spine.Add(_t);
                    _spineRest.Add(_t.localRotation);
                }
                else if (_n.StartsWith("Ear1."))                       // base ear segment only (pivots at the skull)
                {
                    _ears.Add(_t);
                    _earsRest.Add(_t.localRotation);
                }
                else if (_n.StartsWith("Whisker_") && _n.Contains("_01.")) // base whisker segment (pivots at the cheek)
                {
                    _whiskers.Add(_t);
                    _whiskersRest.Add(_t.localRotation);
                }
                else if (_n.StartsWith("Bow_") && _n.Contains("_01."))  // bow base segment only (pivots at the knot)
                {
                    _bow.Add(_t);
                    _bowRest.Add(_t.localRotation);
                }
            }
            WarnIfMissing();
            _prevRootYaw = _root.eulerAngles.y;
        }

        private void LateUpdate()
        {
            float _dt = Time.deltaTime;
            if (_dt <= 0f)
            {
                return;
            }
            float _now = Time.time;

            // Breathing: a slow sine pitch on each spine joint, split across the chain so the total stays subtle.
            float _breath = Mathf.Sin((_now * _breathSpeed + _seed) * 2f * Mathf.PI) * _breathAmplitudeDeg;
            float _breathPer = _spine.Count > 0 ? _breath / _spine.Count : 0f;
            for (int _i = 0; _i < _spine.Count; _i++)
            {
                Drive(_spine[_i], _spineRest[_i], Quaternion.Euler(_breathPer, 0f, 0f));
            }

            // Whiskers: a faster, smaller quiver riding the breath phase.
            float _whisk = Mathf.Sin((_now * _breathSpeed * 4f + _seed) * 2f * Mathf.PI) * _whiskerAmplitudeDeg;
            for (int _i = 0; _i < _whiskers.Count; _i++)
            {
                Drive(_whiskers[_i], _whiskersRest[_i], Quaternion.Euler(0f, 0f, _whisk));
            }

            // Ears: slow Perlin wander on two DECORRELATED axes (ambient — NOT gaze-driven, per Goal-2 scope).
            for (int _i = 0; _i < _ears.Count; _i++)
            {
                float _off = _seed * 13f + _i * 53.1f;                 // strongly desync left/right ear
                float _swivel = (Mathf.PerlinNoise(_now * _earSpeed + _off, 137.7f) - 0.5f) * 2f * _earAmplitudeDeg;
                float _tilt = (Mathf.PerlinNoise(551.3f, _now * _earSpeed + _off) - 0.5f) * 2f * _earAmplitudeDeg;
                Drive(_ears[_i], _earsRest[_i], Quaternion.Euler(_tilt, _swivel, 0f));
            }

            // Bow follow-through: a shared damped spring perturbed by the body's angular velocity, so a turn
            // throws the bow and it trails back to neutral (inertia lags OPPOSITE the turn). Guarded against
            // teleport-sized yaw jumps (seat snaps) and frame hitches so it never whips or diverges.
            float _yaw = _root.eulerAngles.y;
            float _deltaYaw = Mathf.DeltaAngle(_prevRootYaw, _yaw);
            _prevRootYaw = _yaw;
            float _springDt = Mathf.Min(_dt, _maxSpringDt);
            if (Mathf.Abs(_deltaYaw) <= _teleportDeg)
            {
                _bowVelocity += -(_deltaYaw / _springDt) * _bowGain;
                _bowVelocity = Mathf.Clamp(_bowVelocity, -_maxBowVelocity, _maxBowVelocity);
            }
            _bowOffset = AvatarIdleMath.DampedSpring(_bowOffset, ref _bowVelocity, 0f, _bowStiffness, _bowDamping, _springDt);
            _bowOffset = Mathf.Clamp(_bowOffset, -_bowMaxDeg, _bowMaxDeg);
            for (int _i = 0; _i < _bow.Count; _i++)
            {
                Drive(_bow[_i], _bowRest[_i], Quaternion.Euler(0f, 0f, _bowOffset));
            }
        }

        // Offset on top of this frame's pose: the Animator's if it wrote the bone since our last write, else rest.
        private void Drive(Transform _bone, Quaternion _rest, Quaternion _offset)
        {
            Quaternion _basis = _written.TryGetValue(_bone, out Quaternion _last) && _bone.localRotation != _last
                ? _bone.localRotation
                : _rest;
            _bone.localRotation = _basis * _offset;
            _written[_bone] = _bone.localRotation;
        }

        private void WarnIfMissing()
        {
            if (_warned || (_spine.Count > 0 && _ears.Count > 0 && _whiskers.Count > 0 && _bow.Count > 0))
            {
                return;
            }
            _warned = true;
            Debug.LogWarning(
                $"AvatarIdleMotion: missing bone group(s) — spine:{_spine.Count} ears:{_ears.Count} " +
                $"whiskers:{_whiskers.Count} bow:{_bow.Count}. Those sub-motions are skipped.", this);
        }
    }
}
