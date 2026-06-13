using GameLogic;
using GameLogic.GameStates;
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
    /// Lobby gating mirrors BoardCameraManager's subscription (BoardCameraManager.cs:65-86): it reacts to
    /// <c>currentGameStateIndex.OnValueChanged</c> and activates ONLY while the current state is the
    /// <see cref="LobbyState"/>. The general state→camera-mode arbiter (free-roam / fixed / embodied) is
    /// Story 13.3 — this is the minimal Lobby-only hook so 13.2 ships a working camera without regressing
    /// the loop.
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
        // Lane A (mirrors BoardCameraManager.gameManager): read the narrow state-query slice.
        [SerializeField] private GameManager gameManager;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Local-space offset of the eye from the avatar root (≈ head height).")]
        [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 1.7f, 0f);
        [SerializeField] private int _activePriority = 100;
        [SerializeField] private int _inactivePriority = -100;

        private IGameStateQuery Query => gameManager;
        private bool _active;
        private bool _subscribed;
        private PlayerAvatar _boundAvatar;
        private Renderer[] _boundRenderers;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "AvatarFollowCamera._camera is not wired — wire the first-person CinemachineCamera.");
            Assert.IsNotNull(gameManager, "AvatarFollowCamera.gameManager is not wired — wire it in GameScene (like BoardCameraManager).");
            Deactivate();
        }

        private void Start()
        {
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
            _subscribed = true;
            OnGameStateChanged(Query.currentGameStateIndex.Value, Query.currentGameStateIndex.Value);
        }

        private void OnDestroy()
        {
            // Mirror the Start subscription (subscribe-in-X ⇒ unsubscribe-in-its-teardown).
            if (_subscribed && gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            GameState _gameState = Query.GetGameState(_newValue);
            if (_gameState is LobbyState)
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
            // Leaving first person: show the local body again so the board cameras see it normally.
            ShowBoundModel();
            _boundAvatar = null;
            _boundRenderers = null;
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

            // First person: place the camera at the avatar's eye, looking where the body faces.
            Transform _avatarTransform = _boundAvatar.transform;
            _camera.transform.SetPositionAndRotation(
                _avatarTransform.position + _avatarTransform.rotation * _eyeOffset,
                _avatarTransform.rotation);
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
                    _boundRenderers = _avatar.GetComponentsInChildren<Renderer>();
                    HideBoundModel();
                    return;
                }
            }
        }

        private void HideBoundModel()
        {
            if (_boundRenderers == null)
            {
                return;
            }
            foreach (Renderer _renderer in _boundRenderers)
            {
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }
            }
        }

        private void ShowBoundModel()
        {
            if (_boundRenderers == null)
            {
                return;
            }
            foreach (Renderer _renderer in _boundRenderers)
            {
                if (_renderer != null)
                {
                    _renderer.enabled = true;
                }
            }
        }
    }
}
