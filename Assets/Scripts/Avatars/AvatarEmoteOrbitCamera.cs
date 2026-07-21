using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// The third-person EMOTE camera for the LOCAL player. While the local player emotes, the
    /// <see cref="AvatarCameraArbiter"/> activates this camera so the player watches their OWN avatar play the
    /// emote — a self-feedback view the first-person embodied camera cannot give (the local body is hidden there).
    ///
    /// SIBLING of <see cref="AvatarEmbodiedCamera"/> and built on the SAME coexistence model: a bare
    /// <see cref="CinemachineCamera"/> whose transform IS the pose the brain reads (driven each
    /// <c>LateUpdate</c>). It OUTRANKS the embodied + board cameras via a high priority while active and STANDS
    /// DOWN (drops priority) on exit — board/embodied cameras are never removed/disabled.
    ///
    /// ORBIT: the mouse ORBITS the camera around the avatar (yaw + clamped pitch, accumulated from the mouse
    /// delta) — the character itself does NOT turn (the arbiter freezes the embodied head-look during the emote,
    /// so no seated gaze is published). Mouse MOVEMENT never stops the emote; it just orbits.
    ///
    /// FEEL values are placeholder <c>[SerializeField]</c> defaults — Poyo tunes them. Presentation-only
    /// (local camera/input), NOT a NetworkBehaviour.
    /// </summary>
    // After AvatarEmbodiedCamera's 120 (and the seating presenter's 100 that snaps the avatar root we pivot on),
    // so we orbit around the already-snapped body pose and do not lag a frame behind on entry.
    [DefaultExecutionOrder(130)]
    public class AvatarEmoteOrbitCamera : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _camera;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Orbit radius (m) from the avatar pivot to the camera.")]
        [SerializeField] private float _distance = 3.2f;
        [Tooltip("Pivot height (m) above the avatar root — aim roughly at the chest/head so the emote is framed.")]
        [SerializeField] private float _pivotHeight = 1.1f;
        [Tooltip("Yaw degrees per horizontal mouse-delta unit.")]
        [SerializeField] private float _yawSensitivity = 0.15f;
        [Tooltip("Pitch degrees per vertical mouse-delta unit.")]
        [SerializeField] private float _pitchSensitivity = 0.12f;
        [Tooltip("Clamped pitch bounds (deg) so the orbit never flips over the top/bottom.")]
        [SerializeField] private float _minPitch = -10f;
        [SerializeField] private float _maxPitch = 60f;
        [Tooltip("Starting pitch (deg) on activation — a slight downward tilt reads the emote well.")]
        [SerializeField] private float _startPitch = 15f;
        [SerializeField] private int _activePriority = 200;
        [SerializeField] private int _inactivePriority = -100;

        private bool _active;
        private bool _bound;
        private bool _seeded;
        private float _yaw;
        private float _pitch;
        private AvatarManager _manager;
        private PlayerAvatar _boundAvatar;

        /// <summary>Whether the emote orbit camera is currently outranking the other cameras.</summary>
        public bool IsActive => _active;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "AvatarEmoteOrbitCamera._camera is not wired — wire the emote orbit CinemachineCamera.");
            Deactivate();
        }

        /// <summary>Arbiter contract (mirror <see cref="AvatarEmbodiedCamera.SetActive"/>): on iff the local
        /// player is emoting.</summary>
        public void SetActive(bool _isActive)
        {
            if (_isActive)
            {
                Activate();
            }
            else
            {
                Deactivate();
            }
        }

        private void Activate()
        {
            _active = true;
            if (_camera != null)
            {
                _camera.Priority = _activePriority;
            }
            // Re-seat the orbit on (re)entry: bind + seed the starting yaw from the avatar facing in LateUpdate
            // (the avatar may not be resolved yet on the activation frame).
            _bound = false;
            _seeded = false;
            _manager = null;
            _pitch = _startPitch;
        }

        private void Deactivate()
        {
            _active = false;
            if (_camera != null)
            {
                _camera.Priority = _inactivePriority;
            }
            _boundAvatar = null;
            _bound = false;
            _manager = null;
        }

        private void LateUpdate()
        {
            // _camera-null guard (not just the Awake Assert, which is stripped in release): SetActive(true) sets
            // _active even if the camera is unwired, which would NRE here every frame.
            if (!_active || _camera == null)
            {
                return;
            }

            if (!_bound)
            {
                TryBind();
                if (!_bound)
                {
                    return;
                }
            }

            // The avatar/manager can be torn down mid-emote (scene unload) — re-bind next frame instead of
            // dereferencing a destroyed Unity object.
            if (_manager == null || _boundAvatar == null)
            {
                _bound = false;
                return;
            }

            // Accumulate the orbit from the mouse delta (movement orbits — it never stops the emote).
            Vector2 _delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            _yaw += _delta.x * _yawSensitivity;
            _pitch = Mathf.Clamp(_pitch - _delta.y * _pitchSensitivity, _minPitch, _maxPitch);

            Vector3 _pivot = _boundAvatar.transform.position + Vector3.up * _pivotHeight;
            EmoteOrbit.ComputePose(_pivot, _yaw, _pitch, _distance, out Vector3 _position, out Quaternion _rotation);
            _camera.transform.SetPositionAndRotation(_position, _rotation);
        }

        // Resolve the local owned avatar (mirror AvatarEmbodiedCamera.TryBind). On the first bind, seed the
        // orbit yaw so the camera starts IN FRONT of the avatar (facing its front), so the player sees the
        // emote head-on; further orbiting is relative to that.
        private void TryBind()
        {
            NetworkManager _networkManager = NetworkManager.Singleton;
            AvatarManager _resolved = AvatarManager.For(_networkManager);
            if (_resolved == null)
            {
                return;
            }

            foreach (PlayerAvatar _avatar in _resolved.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _boundAvatar = _avatar;
                    _manager = _resolved;
                    _bound = true;
                    if (!_seeded)
                    {
                        // Camera in FRONT of the avatar = its facing yaw + 180 (behind the pivot's back would be
                        // its facing; +180 puts the camera where it looks back at the front).
                        _yaw = _avatar.transform.eulerAngles.y + 180f;
                        _seeded = true;
                    }
                    break;
                }
            }
        }
    }
}
