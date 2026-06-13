using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// Story 13.2 (Epic 13 — Player Embodiment). OWNER-AUTHORITATIVE movement controller for the local
    /// player's avatar. Reads the Input System Move/Look actions and drives a <see cref="CharacterController"/>;
    /// position replicates to every other client via the avatar's owner-authoritative <c>NetworkTransform</c>
    /// (NFR3 — cosmetic presence only; NO game state is mutated, no per-frame RPC, no custom NetworkVariable&lt;Vector3&gt;).
    ///
    /// OWNERSHIP: the avatar is spawned OWNED by its client (AvatarManager.SpawnAvatar), so <c>IsOwner</c> is
    /// the correct gate here — only the owning client moves its body, every other client receives the
    /// interpolated transform. Bots (clientId &gt;= 100) never get an avatar (DO2), so plain <c>IsOwner</c> is
    /// safe (no simulated identity drives a body).
    ///
    /// INPUT: reads a RUNTIME CLONE of the project's InputActionAsset (Instantiate) so enabling/disabling its
    /// Player map is isolated from the scene PlayerInput's instance. Move/Look are CONTINUOUS values — read
    /// directly via the Input System (compliant: not legacy Input.GetKey), rather than routed through the
    /// discrete-event InputManager wrapper (whose generic value path is stubbed) — documented deviation.
    ///
    /// SCOPE: Lobby free-roam movement + look-yaw only. The state-driven enable/disable across phases is
    /// Story 13.3 (call <see cref="SetMovementEnabled"/>); embodied seated look (pitch clamp) is Story 13.4.
    /// FEEL values (<see cref="_moveSpeed"/> etc.) are placeholder defaults — Poyo tunes them in the Inspector.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AvatarMovementController : NetworkBehaviour
    {
        [Header("Input (wire the project InputActionAsset)")]
        [SerializeField] private InputActionAsset _inputActions;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Planar walk speed (m/s).")]
        [SerializeField] private float _moveSpeed = 3.5f;
        [Tooltip("Body yaw speed applied from the Look X delta (degrees per unit of mouse delta).")]
        [SerializeField] private float _lookYawSpeed = 0.12f;
        [Tooltip("Gravity (m/s^2), keeps the CharacterController grounded against the floor collider.")]
        [SerializeField] private float _gravity = -15f;

        private CharacterController _characterController;
        private InputActionAsset _runtimeActions;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private float _verticalVelocity;

        // Story 13.3 will drive this from the camera-mode arbiter (movement only in the Lobby). 13.2 leaves
        // it on for the owner; the Lobby is the only walkable phase today.
        private bool _movementEnabled = true;

        private void Awake()
        {
            // Non-networked init (the CharacterController is a local component, not an NGO replica).
            _characterController = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Owner-only: a non-owner avatar is driven purely by the replicated NetworkTransform.
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            if (_inputActions == null)
            {
                Debug.LogError("AvatarMovementController._inputActions is not wired — wire the project InputActionAsset on the avatar prefab.");
                enabled = false;
                return;
            }

            // Clone so enabling the Player map here does not fight the scene PlayerInput's own asset instance.
            _runtimeActions = Instantiate(_inputActions);
            InputActionMap _playerMap = _runtimeActions.FindActionMap("Player", throwIfNotFound: true);
            _moveAction = _playerMap.FindAction("Move", throwIfNotFound: true);
            _lookAction = _playerMap.FindAction("Look", throwIfNotFound: true);
            _playerMap.Enable();
        }

        public override void OnNetworkDespawn()
        {
            if (_runtimeActions != null)
            {
                _runtimeActions.Disable();
                Destroy(_runtimeActions);
                _runtimeActions = null;
            }
            base.OnNetworkDespawn();
        }

        /// <summary>Story 13.3 hook: enable/disable free-roam movement with the camera-mode arbiter.</summary>
        public void SetMovementEnabled(bool _enabled) => _movementEnabled = _enabled;

        private void Update()
        {
            if (!IsOwner || !_movementEnabled || _moveAction == null)
            {
                return;
            }

            // Look (yaw the body from the horizontal mouse delta). Pitch / clamped embodied look is 13.4.
            Vector2 _look = _lookAction.ReadValue<Vector2>();
            if (Mathf.Abs(_look.x) > Mathf.Epsilon)
            {
                transform.Rotate(Vector3.up, _look.x * _lookYawSpeed, Space.World);
            }

            // Planar move relative to the avatar's facing.
            Vector2 _move = _moveAction.ReadValue<Vector2>();
            Vector3 _planar = (transform.right * _move.x + transform.forward * _move.y) * _moveSpeed;

            // Gravity keeps the controller pinned to the floor collider (room walls bound it — no NavMesh).
            if (_characterController.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }
            _verticalVelocity += _gravity * Time.deltaTime;

            Vector3 _velocity = new Vector3(_planar.x, _verticalVelocity, _planar.z);
            _characterController.Move(_velocity * Time.deltaTime);
        }
    }
}
