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
        [Tooltip("First-person look pitch (up/down) speed from the vertical mouse delta.")]
        [SerializeField] private float _lookPitchSpeed = 0.12f;
        [Tooltip("Pitch clamp (degrees) for the first-person look up/down.")]
        [SerializeField] private float _pitchClamp = 80f;
        [Tooltip("Max angle (deg) the head may LEAD the body before the body has to catch up — the look cone.")]
        [SerializeField] private float _headYawCone = 60f;
        [Tooltip("How fast the body yaw eases to catch up to the look heading (higher = head re-centres faster).")]
        [SerializeField] private float _bodyCatchUpSpeed = 6f;
        [Tooltip("Gravity (m/s^2), keeps the CharacterController grounded against the floor collider.")]
        [SerializeField] private float _gravity = -15f;

        private CharacterController _characterController;
        private InputActionAsset _runtimeActions;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private float _verticalVelocity;

        // First-person look pitch lives on the avatar's eye pivot. Body yaw stays on the avatar root (networked).
        private Transform _eyePivot;
        private float _pitch;

        // Head-leads-body-follow: _lookYaw is the free look HEADING (where the player wants to look),
        // accumulated independently of the body. The eye/head carries the clamped offset (look - body) so the
        // view turns immediately; the body yaw eases toward _lookYaw so the head re-centres. The offset + pitch
        // are published (relative to body) via PlayerAvatar so every client aims this avatar's rigged head bone.
        private PlayerAvatar _playerAvatar;
        private float _lookYaw;
        // Sparse publish guard (mirrors AvatarEmbodiedCamera): only push the head-look onto the network when
        // it moved past a sub-degree threshold, so a still/slow look does not write the NetworkVariable every
        // frame. Remote smoothing is handled by AvatarHeadLook's easing.
        // NaN-armed (mirrors AvatarEmbodiedCamera) so the FIRST publish after each free-roam entry always
        // fires — this re-zeroes a stale head-look left over from the Vote (otherwise the rigged head stays
        // cranked at the seated angle until the player happens to move the mouse past the epsilon).
        private float _lastPublishedYaw = float.NaN;
        private float _lastPublishedPitch = float.NaN;
        private const float _publishEpsilon = 0.25f;

        // Story 13.3 will drive this from the camera-mode arbiter (movement only in the Lobby). 13.2 leaves
        // it on for the owner; the Lobby is the only walkable phase today.
        private bool _movementEnabled = true;

        // Look freeze: the arbiter zeroes the mouse-look while the tablet is open (the cursor is freed to
        // drive the tablet UI, so the body/eye must not turn with the mouse). Defaults on — unchanged feel
        // until the tablet opens.
        private bool _lookEnabled = true;

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

            // The eye pivot (head-height child) carries the first-person pitch + head yaw offset. Cache the
            // PlayerAvatar too (it owns the EyePivot and the SeatedYaw/Pitch publish hook). Null-tolerant.
            _playerAvatar = GetComponent<PlayerAvatar>();
            _eyePivot = _playerAvatar != null ? _playerAvatar.EyePivot : null;
            // Seed the look heading from the current body facing so there is no snap on the first frame.
            _lookYaw = transform.eulerAngles.y;
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

        /// <summary>
        /// Story 13.3 hook: enable/disable free-roam movement with the camera-mode arbiter.
        ///
        /// Story 13.4 code-review fix: also toggles the <see cref="CharacterController"/> itself off outside
        /// the Lobby. A seated/locked body keeps an ENABLED controller otherwise, and a CharacterController
        /// resolves overlap penetration on its next move step — re-seating the body onto a seat that overlaps
        /// the table/another capsule (then ever moving) would EJECT it (the "avatar flying up" class fixed in
        /// commit 7bfcb42). With the controller disabled while embodied/locked, no depenetration can fire; it
        /// is re-enabled only when free-roam movement returns (the Lobby). Owner-gated effect (only the local
        /// owner's controller is driven by the arbiter); cosmetic, replicates via the owner-auth NetworkTransform.
        /// </summary>
        public void SetMovementEnabled(bool _enabled)
        {
            _movementEnabled = _enabled;
            if (_characterController != null)
            {
                _characterController.enabled = _enabled;
            }

            // Head-look hygiene across mode switches (owner only — only the owner publishes the look).
            if (!IsOwner)
            {
                return;
            }
            if (_enabled)
            {
                // Fresh free-roam entry: re-seed the heading to the current body facing, neutralize pitch, and
                // NaN-arm the publish so the next frame force-publishes a centred head (clears stale Vote gaze).
                _lookYaw = transform.eulerAngles.y;
                _pitch = 0f;
                _lastPublishedYaw = float.NaN;
                _lastPublishedPitch = float.NaN;
            }
            else
            {
                // Leaving free-roam (Board / Embodied): publish a NEUTRAL head once so the avatar does not keep
                // its last free-roam look cranked while no free-roam publisher is active (e.g. on the Board view).
                _playerAvatar?.PublishSeatedLook(0f, 0f);
                _lastPublishedYaw = 0f;
                _lastPublishedPitch = 0f;
            }
        }

        /// <summary>
        /// Arbiter hook: freeze/unfreeze the mouse-look. Frozen while the tablet is open so the freed cursor
        /// can drive the tablet UI without the body yaw / eye pitch following the mouse. Movement (WASD) is
        /// untouched — only the look is gated.
        /// </summary>
        public void SetLookEnabled(bool _enabled) => _lookEnabled = _enabled;

        private void Update()
        {
            if (!IsOwner || !_movementEnabled || _moveAction == null)
            {
                return;
            }

            // Look — head LEADS, body FOLLOWS. X accumulates the free look heading; Y pitches up/down. The head
            // (eye pivot + the networked head-look that aims the rigged bone) turns immediately within a cone;
            // the body yaw eases toward the heading so the head re-centres. Others see BOTH (body via
            // NetworkTransform, head tilt/lead via SeatedYaw/Pitch). Embodied clamped look is the seated path.
            Vector2 _look = _lookEnabled ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
            // Keep the accumulated heading bounded to [0,360) so it never drifts into coarse float ulps over a
            // long session (all consumers go through DeltaAngle, so the wrap is transparent).
            _lookYaw = Mathf.Repeat(_lookYaw + _look.x * _lookYawSpeed, 360f);
            // Mouse up (positive Y) looks up → negative pitch.
            _pitch = AvatarLookMath.ClampSymmetric(_pitch - _look.y * _lookPitchSpeed, _pitchClamp);

            float _bodyYaw = transform.eulerAngles.y;
            float _headYawOffset = AvatarLookMath.HeadYawOffset(_bodyYaw, _lookYaw, _headYawCone);
            if (_eyePivot != null)
            {
                _eyePivot.localRotation = Quaternion.Euler(_pitch, _headYawOffset, 0f);
            }

            // Body catch-up: ease the avatar yaw toward the look heading so the head offset decays toward 0.
            float _easedBodyYaw = AvatarLookMath.EaseAngle(_bodyYaw, _lookYaw, _bodyCatchUpSpeed, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, _easedBodyYaw, 0f);

            // Publish the head-look (offset relative to body + pitch) so every client aims the rigged head bone.
            // Sparse: only on a past-threshold change (mirrors AvatarEmbodiedCamera) — avoids a per-frame write.
            if (_playerAvatar != null &&
                (float.IsNaN(_lastPublishedYaw) ||
                 Mathf.Abs(Mathf.DeltaAngle(_lastPublishedYaw, _headYawOffset)) > _publishEpsilon ||
                 Mathf.Abs(Mathf.DeltaAngle(_lastPublishedPitch, _pitch)) > _publishEpsilon))
            {
                _playerAvatar.PublishSeatedLook(_headYawOffset, _pitch);
                _lastPublishedYaw = _headYawOffset;
                _lastPublishedPitch = _pitch;
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
