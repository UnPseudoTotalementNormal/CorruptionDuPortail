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
        private Transform _seat;
        private float _yaw;
        private float _pitch;

        private InputActionAsset _runtimeActions;
        private InputAction _lookAction;

        // The local owned avatar's renderers — hidden while embodied (first-person), shown again on exit.
        // Remote clients still see this avatar seated normally (only the LOCAL view hides its own body).
        private PlayerAvatar _boundAvatar;
        private Renderer[] _boundRenderers;

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
            // Story 13.4 code-review fix: if torn down while active (mid-Vote), the bound LOCAL avatar's
            // renderers were hidden by HideBoundModel — and that avatar OUTLIVES this camera. Restore them so
            // the local body does not stay invisible on its own avatar for the rest of the match.
            ShowBoundModel();

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

        private void Activate()
        {
            _active = true;
            _camera.Priority = _activePriority;
            // Re-centre the look on the seat facing each time we enter the Vote.
            _yaw = 0f;
            _pitch = 0f;
            // Seat + local-model binding happen in LateUpdate (the avatar/seats may not be ready yet).
            _seat = null;
            _lookAction?.Enable();
        }

        private void Deactivate()
        {
            _active = false;
            if (_camera != null)
            {
                _camera.Priority = _inactivePriority;
            }
            // Leaving embodied: show the local body again so the board cameras see it normally.
            ShowBoundModel();
            _boundAvatar = null;
            _boundRenderers = null;
            _seat = null;
            _lookAction?.Disable();
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            // Late binding: the local avatar / its seat may resolve a frame or two after the Vote activates
            // (mirror AvatarFollowCamera.TryBindLocalAvatar). Keep trying; null-tolerant — no crash.
            if (_seat == null)
            {
                TryBind();
                if (_seat == null)
                {
                    return;
                }
            }

            // Accumulate the clamped look from the mouse delta (pure math, EditMode-tested).
            Vector2 _look = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
            EmbodiedLookAngles _angles = EmbodiedLookClamp.Apply(
                _yaw, _pitch, _look, _yawSpeed, _pitchSpeed, _yawClamp, _pitchClamp);
            _yaw = _angles.Yaw;
            _pitch = _angles.Pitch;

            // First-person seated: place the camera at the seat's eye height and look around RELATIVE to
            // the seat facing (the seat faces the table → yaw 0 / pitch 0 looks straight at it).
            _camera.transform.SetPositionAndRotation(
                _seat.position + _seat.rotation * _eyeOffset,
                _seat.rotation * Quaternion.Euler(_pitch, _yaw, 0f));
        }

        // Resolve the local seat (route A: the LOCAL client's own global seat) + the local avatar's
        // renderers to hide. AvatarManager.For + NetworkManager.Singleton are the avatar layer's own
        // resolution — same as 13.2/13.3 — which is why the avatar types are NOT in DiSeamMigratedConsumers.
        private void TryBind()
        {
            NetworkManager _networkManager = NetworkManager.Singleton;
            AvatarManager _manager = AvatarManager.For(_networkManager);
            if (_manager == null)
            {
                return;
            }

            Transform _localSeat = _manager.GetSeat(_networkManager.LocalClientId);
            if (_localSeat == null)
            {
                return;
            }
            _seat = _localSeat;

            // Hide the local owned avatar's body (first-person). Null-tolerant — purely cosmetic.
            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _boundAvatar = _avatar;
                    _boundRenderers = _avatar.GetComponentsInChildren<Renderer>();
                    HideBoundModel();
                    break;
                }
            }
        }

        private void HideBoundModel() => SetBoundModelVisible(false);
        private void ShowBoundModel() => SetBoundModelVisible(true);

        private void SetBoundModelVisible(bool _visible)
        {
            if (_boundRenderers == null)
            {
                return;
            }
            foreach (Renderer _renderer in _boundRenderers)
            {
                if (_renderer != null)
                {
                    _renderer.enabled = _visible;
                }
            }
        }
    }
}
