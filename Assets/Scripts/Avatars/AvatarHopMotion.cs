using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Legless hop locomotion (LOCAL, deterministic, no networking). The cat has no legs and there is no walk
    /// clip, so instead of striding it scoots forward in little HOPS. Each LateUpdate this drives the cat's
    /// VISUAL ROOT (the transform this sits on — CatVisual / Cat_Avatar root) in a hop cycle: a vertical arc +
    /// squash &amp; stretch + a small forward pitch on the up-beat. The cycle is PACED BY DISTANCE TRAVELLED of
    /// the networked root, so cadence scales with speed and the body rests flat on the ground when stopped.
    ///
    /// DETERMINISTIC-LOCAL: everything derives from the already-replicated root position — every client
    /// recomputes the identical hop with zero bandwidth and no NetworkVariable/RPC.
    ///
    /// SINGLE-WRITER: writes ONLY this visual-root transform (localPosition/localScale/localRotation, relative
    /// to the captured base — accumulation-free, pivots at its origin). It never touches the spine/bones (idle
    /// owns those) nor the networked avatar root (movement owns that). Runs at order 160 (after idle 150,
    /// before head-look 200) so head-look re-aims the head on top of the pitched body. The EyePivot is a child
    /// of the networked root, NOT of this visual root, so the hop does NOT bob the first-person camera.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public class AvatarHopMotion : MonoBehaviour
    {
        [Header("Hop — Poyo-tuned")]
        [Tooltip("Hop apex height in the visual-root's local units.")]
        [SerializeField] private float _hopHeight = 0.1f;
        [Tooltip("Metres of travel per hop — smaller = quicker hops. Tune against the move speed (~12 m/s): " +
                 "stride 3 ≈ 4 hops/s; too small buzzes.")]
        [SerializeField] private float _strideLength = 3f;
        [Tooltip("Tall-and-thin amount at the apex.")]
        [SerializeField] private float _stretch = 0.08f;
        [Tooltip("Short-and-wide amount at the ground.")]
        [SerializeField] private float _squash = 0.08f;
        [Tooltip("Forward pitch (deg) at the apex of the hop.")]
        [SerializeField] private float _upBeatPitchDeg = 6f;
        [Header("Stop / start")]
        [Tooltip("Planar speed (m/s) above which the avatar counts as moving.")]
        [SerializeField] private float _moveSpeedThreshold = 0.3f;
        [Tooltip("How fast the hop amplitude eases in/out when starting/stopping.")]
        [SerializeField] private float _amplitudeEaseSpeed = 8f;

        // A planar SPEED above this is a teleport (seat snap, spawn placement, seated-ring re-spread), not real
        // travel — speed-based (not raw distance) so it catches a one-frame jump of ANY size and never lets the
        // hop fire on the seated body or pop on spawn. Well above any real move speed (~12 m/s).
        private const float _teleportSpeed = 25f;

        private Transform _visual;     // this transform — the cat visual root we bounce
        private Transform _root;       // networked root (PlayerAvatar) — the distance source
        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private Vector3 _baseLocalScale;
        private Vector3 _prevRootPos;
        private float _phase;
        private float _amplitude;
        private bool _initialized;

        private void Awake()
        {
            _visual = transform;
            PlayerAvatar _pa = GetComponentInParent<PlayerAvatar>();
            _root = _pa != null ? _pa.transform : transform;
            _baseLocalPos = _visual.localPosition;
            _baseLocalRot = _visual.localRotation;
            _baseLocalScale = _visual.localScale;
            _prevRootPos = _root.position;
        }

        // Re-arm the first-frame skip so a disable/enable (or a spawn placement that lands after Awake) seeds
        // _prevRootPos without spiking a hop.
        private void OnEnable() => _initialized = false;

        // Leave the cat at its base pose, not frozen mid-bounce, if the component is disabled.
        private void OnDisable()
        {
            if (_visual == null)
            {
                return;
            }
            _visual.localPosition = _baseLocalPos;
            _visual.localRotation = _baseLocalRot;
            _visual.localScale = _baseLocalScale;
        }

        private void LateUpdate()
        {
            float _dt = Time.deltaTime;
            if (_dt <= 0f)
            {
                return;
            }

            // Planar distance the networked root travelled since last frame (Y stripped).
            Vector3 _delta = _root.position - _prevRootPos;
            _prevRootPos = _root.position;
            _delta.y = 0f;
            float _dist = _delta.magnitude;
            float _speed = _dist / _dt;
            // First frame after (re)enable, or a teleport-speed jump (seat snap, spawn, ring re-spread): consume
            // it without advancing the hop, so the seated body never bobs and a fresh avatar never pops.
            if (!_initialized || _speed > _teleportSpeed)
            {
                _initialized = true;
                _dist = 0f;
                _speed = 0f;
            }

            // Advance the hop phase ONLY by distance travelled (cadence scales with speed; frozen when still).
            if (_strideLength > Mathf.Epsilon)
            {
                _phase = Mathf.Repeat(_phase + _dist / _strideLength, 1f);
            }

            // Ease amplitude toward 1 while moving / 0 while stopped → rest flat on the ground (no float).
            float _target = _speed >= _moveSpeedThreshold ? 1f : 0f;
            _amplitude = AvatarHopMath.EaseAmplitude(_amplitude, _target, _amplitudeEaseSpeed, _dt);

            float _arc = AvatarHopMath.Arc(_phase) * _amplitude;
            float _ss = AvatarHopMath.SquashStretch(_phase) * _amplitude;

            // Single writer on the visual-root transform, relative to the captured base (accumulation-free).
            float _sxz = Mathf.Max(0.01f, 1f - _squash * _ss);  // clamp ≥0 so a mis-tuned squash can't invert
            _visual.localPosition = _baseLocalPos + new Vector3(0f, _hopHeight * _arc, 0f);
            _visual.localScale = Vector3.Scale(_baseLocalScale, new Vector3(_sxz, 1f + _stretch * _ss, _sxz));
            _visual.localRotation = _baseLocalRot * Quaternion.Euler(_upBeatPitchDeg * _arc, 0f, 0f);
        }
    }
}
