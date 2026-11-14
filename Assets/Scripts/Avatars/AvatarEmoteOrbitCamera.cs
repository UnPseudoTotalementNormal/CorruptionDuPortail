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
        [Tooltip("MANUAL fallback orbit radius (m), used only when Auto Frame is off.")]
        [SerializeField] private float _distance = 3.2f;
        [Tooltip("MANUAL fallback pivot height (m) above the avatar root, used only when Auto Frame is off.")]
        [SerializeField] private float _pivotHeight = 1.1f;

        [Header("Auto-framing (from the avatar's visual bounds)")]
        [Tooltip("Derive the pivot height and the orbit radius from the avatar's RENDERER BOUNDS instead of the " +
                 "fixed values above. Robust to the model's scale — the raw root pivot sits at the FEET and a " +
                 "fixed radius can end up INSIDE the model.")]
        [SerializeField] private bool _autoFrame = true;
        [Tooltip("Names of the renderers measured for the auto-framing: the cat's own body and head. The other parts " +
                 "(arms, ears, fur shells, cosmetics, nameplate) carry loose skinned bounds that pushed the orbit " +
                 "past the far side of the table. Empty, or no match = every active renderer.")]
        [SerializeField] private string[] _frameRenderers = { "Body", "Head" };
        [Tooltip("Orbit radius as a multiple of the avatar's bounding radius. ~2.5 frames the whole body.")]
        [SerializeField] private float _distanceMultiplier = 2.5f;
        [Tooltip("Floor for the auto radius (m) — never get closer than this whatever the bounds say.")]
        [SerializeField] private float _minDistance = 1.5f;
        [Tooltip("Nudge (m) applied to the auto pivot height. 0 = the exact vertical centre of the visual bounds; " +
                 "positive aims higher (towards the head).")]
        [SerializeField] private float _heightBias = 0f;
        [Tooltip("Yaw degrees per horizontal mouse-delta unit.")]
        [SerializeField] private float _yawSensitivity = 0.15f;
        [Tooltip("Pitch degrees per vertical mouse-delta unit.")]
        [SerializeField] private float _pitchSensitivity = 0.12f;
        [Tooltip("Clamped pitch bounds (deg) so the orbit never flips over the top/bottom.")]
        [SerializeField] private float _minPitch = -10f;
        [SerializeField] private float _maxPitch = 60f;
        [Tooltip("Starting pitch (deg) on activation — a slight downward tilt reads the emote well.")]
        [SerializeField] private float _startPitch = 15f;
        [Header("Walls")]
        [Tooltip("Layers that stop the orbit: the room walls, ceiling, floor and table. The cats' own colliders are " +
                 "always ignored.")]
        [SerializeField] private LayerMask _blockingLayers = 1;
        [Tooltip("Radius (m) of the sphere swept from the cat to the camera; the camera stops this far from a wall.")]
        [SerializeField] private float _collisionRadius = 1f;
        [Tooltip("Share of the orbit radius regained per second once the wall is out of the way. Getting closer is " +
                 "instant so the camera never shows the other side of a wall.")]
        [SerializeField] private float _radiusReturnSpeed = 2f;

        [Header("Transition")]
        [Tooltip("Blend time (s) into and out of the emote view. The brain's default 0.75 s felt slow for an emote.")]
        [SerializeField] private float _blendTime = 0.25f;
        [SerializeField] private int _activePriority = 200;
        [SerializeField] private int _inactivePriority = -100;

        private bool _active;
        private bool _bound;
        private bool _seeded;
        private float _yaw;
        private float _pitch;
        private AvatarManager _manager;
        private PlayerAvatar _boundAvatar;
        // Auto-framing, measured ONCE per bind — a skinned mesh's bounds breathe with the animation, so
        // re-measuring every frame would make the camera creep/jitter while the emote plays.
        private float _autoPivotHeight;
        private float _autoDistance;
        // Radius actually used this frame: the framing radius shortened by the walls, eased back out.
        private float _currentRadius;
        private bool _blockedLogged;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Whether the emote orbit camera is currently outranking the other cameras.</summary>
        public bool IsActive => _active;

        /// <summary>Orbit radius measured by the last <see cref="MeasureAutoFrame"/> (tests).</summary>
        internal float AutoDistance => _autoDistance;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "AvatarEmoteOrbitCamera._camera is not wired — wire the emote orbit CinemachineCamera.");
            Deactivate();
            CinemachineCore.GetBlendOverride += OverrideBlend;
        }

        private void OnDestroy()
        {
            CinemachineCore.GetBlendOverride -= OverrideBlend;
        }

        // Shorter blend into and out of the emote view; every other transition keeps the brain's default.
        private CinemachineBlendDefinition OverrideBlend(ICinemachineCamera _from, ICinemachineCamera _to,
            CinemachineBlendDefinition _default, Object _owner)
        {
            if (_camera == null || (!ReferenceEquals(_from, _camera) && !ReferenceEquals(_to, _camera)))
            {
                return _default;
            }
            return new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, _blendTime);
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
            _blockedLogged = false;
            _poseLogged = false;
            // Place the camera on the activation frame: the brain updates before our LateUpdate, so without this it
            // would start the blend towards wherever the camera was left after the previous emote.
            TryBind();
            if (_bound)
            {
                UpdatePose();
            }
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

            UpdatePose();
        }

        private void UpdatePose()
        {
            // Frame on the visual CENTRE at a radius that clears the model. The raw root pivot is at the FEET and
            // a fixed radius can land inside the mesh, so auto-framing measures the renderer bounds instead.
            float _height = _autoFrame ? _autoPivotHeight : _pivotHeight;
            float _radius = _autoFrame ? _autoDistance : _distance;

            Vector3 _pivot = _boundAvatar.transform.position + Vector3.up * _height;
            EmoteOrbit.ComputePose(_pivot, _yaw, _pitch, _radius, out Vector3 _position, out Quaternion _rotation);

            // Stop in front of the walls: closer at once, back out smoothly when the wall is no longer in the way.
            float _clear = ClearRadius(_pivot, _position - _pivot, _radius, out Collider _blocker,
                Camera.main != null ? Camera.main.transform : null);
            _currentRadius = _clear < _currentRadius
                ? _clear
                : Mathf.MoveTowards(_currentRadius, _clear, _radiusReturnSpeed * _radius * Time.deltaTime);
            if (_blocker != null && !_blockedLogged)
            {
                _blockedLogged = true;
                Diagnostics.Logs.GameLog.Info("camera", "camera.emote.orbit-blocked",
                    "by", _blocker.name, "radius", _radius, "clear", _clear);
            }
            if (_currentRadius < _radius)
            {
                EmoteOrbit.ComputePose(_pivot, _yaw, _pitch, _currentRadius, out _position, out _rotation);
            }
            _camera.transform.SetPositionAndRotation(_position, _rotation);
            if (!_poseLogged)
            {
                _poseLogged = true;
                Vector3 _avatarPosition = _boundAvatar.transform.position;
                Vector3 _cameraPosition = _position;
                Collider _hit = _blocker;
                Vector3 _eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("camera", "camera.emote.pose",
                    "avatar", _avatarPosition, "pivot", _pivot, "camera", _cameraPosition, "radius", _radius, "current", _currentRadius, "eye", _eye,
                    "blocker", _hit != null ? PathOf(_hit.transform) : null));
            }
        }

        private bool _poseLogged;

        private static string PathOf(Transform _transform)
        {
            string _path = _transform.name;
            for (Transform _t = _transform.parent; _t != null; _t = _t.parent)
            {
                _path = _t.name + "/" + _path;
            }
            return _path;
        }

        // Distance from the pivot the camera can reach along _offset before a blocking collider (not a cat's own, nor one
        // hanging under _view, the camera that renders).
        internal float ClearRadius(Vector3 _pivot, Vector3 _offset, float _radius, out Collider _blocker, Transform _view = null)
        {
            _blocker = null;
            if (_radius <= 0f || _offset.sqrMagnitude < 1e-6f)
            {
                return _radius;
            }
            int _count = Physics.SphereCastNonAlloc(_pivot, _collisionRadius, _offset.normalized, _hits, _radius,
                _blockingLayers, QueryTriggerInteraction.Ignore);
            float _clear = _radius;
            for (int _i = 0; _i < _count; _i++)
            {
                RaycastHit _hit = _hits[_i];
                // distance 0 = the sphere already overlaps it at the pivot (the cat's own body, the seat): not a wall.
                // What hangs under the rendering camera (the day's rows of cards) moves with it: stopping in front of it
                // pulled the camera closer, the cards followed, until the camera sat inside the cat's head (playtest
                // report 2026-11-13).
                if (_hit.distance <= 0f || _hit.distance >= _clear || _hit.collider.GetComponentInParent<PlayerAvatar>() != null
                    || (_view != null && _hit.collider.transform.IsChildOf(_view)))
                {
                    continue;
                }
                _clear = _hit.distance;
                _blocker = _hit.collider;
            }
            return Mathf.Max(0f, _clear);
        }

        /// <summary>
        /// Measure the avatar's combined RENDERER bounds once (the <see cref="_frameRenderers"/>, else every renderer)
        /// and derive the framing from it: the pivot height is
        /// the vertical centre of the visible body (the root pivot sits at the FEET), and the orbit radius is a
        /// multiple of the bounding radius (a fixed radius ends up INSIDE a larger model). Falls back to the manual
        /// values if the avatar has no renderers. Bounds are read from renderers even while disabled, so this is
        /// valid whether or not the local body has been revealed yet this frame.
        /// </summary>
        internal void MeasureAutoFrame(PlayerAvatar _avatar)
        {
            Renderer[] _renderers = _avatar.GetComponentsInChildren<Renderer>(true);
            bool _any = TryEncapsulate(_renderers, true, out Bounds _bounds)
                || TryEncapsulate(_renderers, false, out _bounds);

            if (!_any)
            {
                _autoPivotHeight = _pivotHeight;
                _autoDistance = _distance;
                return;
            }

            _autoPivotHeight = (_bounds.center.y - _avatar.transform.position.y) + _heightBias;
            _autoDistance = Mathf.Max(_minDistance, _bounds.extents.magnitude * _distanceMultiplier);
        }

        // Union of the active renderers' bounds, only those named in _frameRenderers when _named.
        private bool TryEncapsulate(Renderer[] _renderers, bool _named, out Bounds _bounds)
        {
            _bounds = default;
            bool _any = false;
            foreach (Renderer _renderer in _renderers)
            {
                // Cosmetic parts the player does not wear are deactivated GameObjects (CatAppearance): not the body.
                if (_renderer == null || !_renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (_named && System.Array.IndexOf(_frameRenderers, _renderer.name) < 0)
                {
                    continue;
                }
                if (!_any)
                {
                    _bounds = _renderer.bounds;
                    _any = true;
                }
                else
                {
                    _bounds.Encapsulate(_renderer.bounds);
                }
            }
            return _any;
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
                    MeasureAutoFrame(_avatar);
                    _currentRadius = _autoFrame ? _autoDistance : _distance;
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
