using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// Story 13.4 (Epic 13 — Player Embodiment). The EMBODIED SEATED Vote camera for the LOCAL player.
    /// It fills the <see cref="CameraMode.Embodied"/> slot the Story 13.3 <see cref="AvatarCameraArbiter"/>
    /// already routes <c>VoteState</c> to: a first-person camera placed at the LOCAL client's own seat
    /// (DO3 route A — fixed global seats + camera-at-local-seat, see <see cref="AvatarCameraArbiter"/> /
    /// <see cref="AvatarManager.GetSeat"/>), with CLAMPED look (yaw ±75° / pitch ±40°, DO4) around the
    /// seat facing. Movement is locked by the arbiter for the whole Vote.
    ///
    /// SIBLING of <see cref="AvatarFollowCamera"/> and built on the SAME coexistence model: the bare
    /// <see cref="CinemachineCamera"/>'s transform IS the pose the brain reads (we drive it each
    /// <c>LateUpdate</c>); it OUTRANKS the board cameras via priority while active and STANDS DOWN
    /// (drops priority) on exit — board cameras are never removed/disabled (NFR1). Arbiter-driven via
    /// the same <see cref="SetActive"/> / <see cref="IsActive"/> contract.
    ///
    /// INPUT: reads a RUNTIME CLONE of the project's InputActionAsset (Instantiate) — the same
    /// Input-System-direct path as <see cref="AvatarMovementController"/> (Look is a continuous mouse
    /// delta; routing it through the discrete-event InputManager wrapper is not viable, documented
    /// deviation). The Player map is enabled only while embodied. Binding defaults to FREE-MOUSE look;
    /// a hold-to-look gate would be a one-action addition (Poyo's call) — flagged, not assumed.
    ///
    /// SCOPE: seated camera + clamped look only. The body seat-SNAP (so others see you seated) is
    /// owner-side on <see cref="AvatarMovementController.SeatAtSeat"/>, driven by the arbiter. FEEL
    /// values are placeholder <c>[SerializeField]</c> defaults — Poyo tunes them. NOT a NetworkBehaviour
    /// (presentation-only, local cameras/input).
    /// </summary>
    public class AvatarEmbodiedCamera : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _camera;
        [SerializeField] private InputActionAsset _inputActions;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Local-space offset of the eye from the seat transform (≈ seated head height).")]
        [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 1.2f, 0f);
        [Tooltip("Look yaw (left/right) speed from the horizontal mouse delta.")]
        [SerializeField] private float _yawSpeed = 0.12f;
        [Tooltip("Look pitch (up/down) speed from the vertical mouse delta.")]
        [SerializeField] private float _pitchSpeed = 0.12f;
        [Tooltip("Clamped look bounds (degrees) relative to the seat facing (DO4: yaw ±75°, pitch ±40°).")]
        [SerializeField] private float _yawClamp = 75f;
        [SerializeField] private float _pitchClamp = 40f;
        [SerializeField] private int _activePriority = 100;
        [SerializeField] private int _inactivePriority = -100;

        private bool _active;
        // Look freeze (arbiter-driven): zeroed while the tablet is open so the freed cursor drives the tablet
        // UI without the seated gaze following the mouse. Defaults on — unchanged feel until the tablet opens.
        private bool _lookEnabled = true;
        // Bound once the local avatar + its manager resolve (and the local body is hidden). The seat POSE is
        // recomputed every frame (GetSeatPose is pure + cheap) so a changing player count re-spreads the ring.
        private bool _bound;
        private AvatarManager _manager;
        private ulong _localId;
        private float _yaw;
        private float _pitch;
        // Last look published to the networked SeatedYaw/SeatedPitch — so we only write the NetworkVariables
        // when the gaze actually moves (project-context: mutate a NetworkVariable by event, NEVER per frame —
        // it would saturate the tick during a continuous look). NaN sentinel forces the first publish (which
        // clears any stale gaze carried over from the previous Vote).
        private float _lastPublishedYaw = float.NaN;
        private float _lastPublishedPitch = float.NaN;
        private const float LookPublishEpsilon = 0.25f;

        private InputActionAsset _runtimeActions;
        private InputAction _lookAction;

        // The local owned avatar — bound for the seat-pose eye anchor + publishing the seated look. Hiding
        // the local body in first-person is owned by AvatarVisibilityController (the single renderer authority).
        private PlayerAvatar _boundAvatar;

        /// <summary>Whether the embodied seated camera is currently outranking the board cameras.</summary>
        public bool IsActive => _active;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "AvatarEmbodiedCamera._camera is not wired — wire the embodied seated CinemachineCamera.");

            // Clone the project asset so enabling the Player map here does not fight other consumers'
            // instances (mirror AvatarMovementController). Null-tolerant: only look is lost if unwired.
            if (_inputActions != null)
            {
                _runtimeActions = Instantiate(_inputActions);
                InputActionMap _playerMap = _runtimeActions.FindActionMap("Player", throwIfNotFound: false);
                _lookAction = _playerMap?.FindAction("Look", throwIfNotFound: false);
            }

            Deactivate();
        }

        private void OnDestroy()
        {
            if (_runtimeActions != null)
            {
                _runtimeActions.Disable();
                Destroy(_runtimeActions);
                _runtimeActions = null;
            }
        }

        /// <summary>
        /// Story 13.4 entry point: the <see cref="AvatarCameraArbiter"/> drives this (on iff the resolved
        /// camera mode is <c>Embodied</c> = the Vote). Same contract as <see cref="AvatarFollowCamera.SetActive"/>.
        /// </summary>
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

        /// <summary>
        /// Arbiter hook: freeze/unfreeze the seated mouse-look. Frozen while the tablet is open so the freed
        /// cursor can drive the tablet UI without the gaze following the mouse.
        /// </summary>
        public void SetLookEnabled(bool _isEnabled) => _lookEnabled = _isEnabled;

        private void Activate()
        {
            _active = true;
            _camera.Priority = _activePriority;
            // Re-centre the look on the seat facing each time we enter the Vote.
            _yaw = 0f;
            _pitch = 0f;
            // Force a fresh publish of the (re-centred) yaw on the first frame so remote viewers don't briefly
            // see the previous Vote's stale gaze before we write again.
            _lastPublishedYaw = float.NaN;
            // Seat pose + local-model binding happen in LateUpdate (the avatar/manager may not be ready yet).
            _bound = false;
            _manager = null;
            _lookAction?.Enable();
        }

        private void Deactivate()
        {
            _active = false;
            if (_camera != null)
            {
                _camera.Priority = _inactivePriority;
            }
            // Local body visibility is owned by AvatarVisibilityController — nothing to restore here.
            _boundAvatar = null;
            _bound = false;
            _manager = null;
            _lookAction?.Disable();
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            // Late binding: the local avatar / its manager may resolve a frame or two after the Vote activates
            // (mirror AvatarFollowCamera.TryBindLocalAvatar). Keep trying; null-tolerant — no crash.
            if (!_bound)
            {
                TryBind();
                if (!_bound)
                {
                    return;
                }
            }

            // The manager/avatar can be torn down (scene unload, host shutdown) while the Vote is still
            // active — re-bind next frame rather than dereferencing a destroyed Unity object.
            if (_manager == null || _boundAvatar == null)
            {
                _bound = false;
                return;
            }

            // Accumulate the clamped look from the mouse delta (pure math, EditMode-tested).
            Vector2 _look = (_lookEnabled && _lookAction != null) ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
            EmbodiedLookAngles _angles = EmbodiedLookClamp.Apply(
                _yaw, _pitch, _look, _yawSpeed, _pitchSpeed, _yawClamp, _pitchClamp);
            _yaw = _angles.Yaw;
            _pitch = _angles.Pitch;

            // Publish the seated head look (yaw + pitch, relative to seat facing) so remote viewers see where
            // we look — but ONLY when it actually moved (or on the first frame), never every frame (bandwidth
            // rule). Owner-gated inside PublishSeatedLook; the presenter renders it on every other client.
            if (float.IsNaN(_lastPublishedYaw)
                || Mathf.Abs(_yaw - _lastPublishedYaw) > LookPublishEpsilon
                || Mathf.Abs(_pitch - _lastPublishedPitch) > LookPublishEpsilon)
            {
                _boundAvatar?.PublishSeatedLook(_yaw, _pitch);
                _lastPublishedYaw = _yaw;
                _lastPublishedPitch = _pitch;
            }

            // The local seat pose = relative offset 0 = the fixed FRONT spot (recomputed each frame: a
            // changing player count re-spreads the ring, but the local seat stays the front anchor).
            SeatPose _seat = _manager.GetSeatPose(_localId);

            // First-person seated: the camera POSITION is the avatar's eye anchor (the EyePivot — a bare,
            // NON-animated child placed by the seating presenter), so the eye is at the real (scale-correct)
            // head height instead of a hardcoded floor-level offset. Never anchor to the rigged model — its
            // animations would shake the camera. Fallback to seat + offset only if no EyePivot is wired.
            // Look stays RELATIVE to the seat facing (yaw 0 / pitch 0 looks straight at the table).
            Transform _eye = _boundAvatar.EyePivot;
            Vector3 _eyePosition = _eye != null ? _eye.position : _seat.Position + _seat.Rotation * _eyeOffset;
            _camera.transform.SetPositionAndRotation(
                _eyePosition,
                _seat.Rotation * Quaternion.Euler(_pitch, _yaw, 0f));
        }

        // Resolve the local manager + the local owned avatar (route A: the LOCAL client's own seat = front
        // spot). AvatarManager.For + NetworkManager.Singleton are the avatar layer's own resolution — same as
        // 13.2/13.3 — which is why the avatar types are NOT in DiSeamMigratedConsumers.
        private void TryBind()
        {
            NetworkManager _networkManager = NetworkManager.Singleton;
            AvatarManager _resolved = AvatarManager.For(_networkManager);
            if (_resolved == null)
            {
                return;
            }

            // Bind once the local owned avatar exists (its EyePivot is the camera's eye anchor).
            foreach (PlayerAvatar _avatar in _resolved.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _boundAvatar = _avatar;
                    _manager = _resolved;
                    _localId = _networkManager.LocalClientId;
                    _bound = true;
                    break;
                }
            }
        }
    }
}
