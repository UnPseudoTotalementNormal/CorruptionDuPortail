using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Avatars
{
    /// <summary>
    /// Story 13.2 (Epic 13 — Player Embodiment). FIRST-PERSON Lobby camera for the LOCAL player
    /// (Poyo's design call 2026-06-13 — overrides the epic's original third-person follow; FR4/AC#4 +
    /// scope_decisions updated). A new Cinemachine camera mode that <b>coexists</b> with
    /// <see cref="Board.BoardCameraSystem.BoardCameraManager"/> (board cameras are not removed): it outranks
    /// them via priority while active and stands down everywhere else so the untouched phases present
    /// exactly as today (NFR1).
    ///
    /// ARBITER-DRIVEN (Story 13.3): this camera NO LONGER reads game state itself. The single
    /// <see cref="AvatarCameraArbiter"/> owns the state→camera-mode decision and drives
    /// <see cref="SetActive"/> (on iff the resolved mode is <c>FreeRoam</c> = the Lobby). 13.2's own
    /// <c>currentGameStateIndex</c> subscription + <c>is LobbyState</c> gate were removed here — the
    /// pose-copy / local-avatar bind below are unchanged; only the who-decides-active moved out. Hiding the
    /// local body in first-person is now owned by <see cref="AvatarVisibilityController"/> (the single
    /// renderer-visibility authority), not this camera.
    ///
    /// FIRST-PERSON model: the bare CinemachineCamera's transform IS the camera pose the brain reads, so we
    /// copy the bound local avatar's eye-height pose onto it each LateUpdate (yaw follows the body; pitch is
    /// deferred — embodied clamped look is Story 13.4). The local avatar's own model is hidden while the
    /// first-person camera is active so the player does not see the inside of their own capsule; remote
    /// clients still see this avatar normally. Eye offset/feel are placeholder defaults — Poyo tunes them.
    /// </summary>
    public class AvatarFollowCamera : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera _camera;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Local-space offset of the eye from the avatar root (≈ head height).")]
        [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 1.7f, 0f);
        [SerializeField] private int _activePriority = 100;
        [SerializeField] private int _inactivePriority = -100;

        private bool _active;
        private PlayerAvatar _boundAvatar;
        private Transform _boundEye;

        /// <summary>Whether the first-person camera is currently outranking the board cameras.</summary>
        public bool IsActive => _active;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "AvatarFollowCamera._camera is not wired — wire the first-person CinemachineCamera.");
            Deactivate();
        }

        /// <summary>
        /// Story 13.3 entry point: the <see cref="AvatarCameraArbiter"/> drives this (on iff the resolved
        /// camera mode is <c>FreeRoam</c>). Replaces 13.2's own state subscription — this camera no longer
        /// decides when it is active.
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
            TryBindLocalAvatar();
        }

        private void Deactivate()
        {
            _active = false;
            if (_camera != null)
            {
                _camera.Priority = _inactivePriority;
            }
            // Local body visibility is owned by AvatarVisibilityController (driven by the arbiter mode) —
            // this camera no longer toggles renderers.
            _boundAvatar = null;
            _boundEye = null;
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            // The local avatar may spawn a frame or two after the Lobby activates — keep trying to bind.
            if (_boundAvatar == null)
            {
                TryBindLocalAvatar();
                if (_boundAvatar == null)
                {
                    return;
                }
            }

            // First person: copy the eye pivot's world pose (body yaw + local look pitch driven by the
            // owner's AvatarMovementController). Fallback to the avatar root + eye offset if no pivot is wired.
            if (_boundEye != null)
            {
                _camera.transform.SetPositionAndRotation(_boundEye.position, _boundEye.rotation);
            }
            else
            {
                Transform _avatarTransform = _boundAvatar.transform;
                _camera.transform.SetPositionAndRotation(
                    _avatarTransform.position + _avatarTransform.rotation * _eyeOffset,
                    _avatarTransform.rotation);
            }
        }

        private void TryBindLocalAvatar()
        {
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null)
            {
                return;
            }

            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                // The local client's own avatar is the one it owns.
                if (_avatar != null && _avatar.IsOwner)
                {
                    _boundAvatar = _avatar;
                    _boundEye = _avatar.EyePivot;
                    return;
                }
            }
        }
    }
}
