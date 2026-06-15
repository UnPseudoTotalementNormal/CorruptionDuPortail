using Board.BoardCameraSystem;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Avatars
{
    /// <summary>
    /// Story 13.3 (Epic 13 — Player Embodiment). The SINGLE state → camera-mode authority. It generalizes
    /// Story 13.2's hard-coded <c>is LobbyState</c> follow-camera gate into a proper arbiter driven by the
    /// pure <see cref="AvatarCameraModePolicy"/>: it reacts to <c>currentGameStateIndex.OnValueChanged</c>
    /// and, per resolved <see cref="CameraMode"/>, drives three local presentation toggles:
    ///
    ///  • <see cref="AvatarFollowCamera"/> active iff <c>FreeRoam</c> (the Lobby first-person camera);
    ///  • the <see cref="BoardCameraManager"/> <c>Avatar</c> input source = (<c>mode == Board</c>) — cuts
    ///    board-camera arrow neighbour-nav in FreeRoam/Embodied via the existing AND-gate, WITHOUT touching
    ///    the GameState source or removing any board camera (NFR1);
    ///  • the local owned avatar's movement input enabled iff <c>FreeRoam</c>
    ///    (<see cref="AvatarMovementController.SetMovementEnabled"/>).
    ///
    /// NFR2 — it is a PURE REACTION: subscribe-and-prime in <see cref="Start"/>, unsubscribe in
    /// <see cref="OnDestroy"/> (subscription symmetry, archi §5b); it NEVER writes the index and never
    /// touches the OnEnd → write → OnStart transition ordering. The 2.11a sequence golden stays green.
    ///
    /// EMBODIED FALL-BACK (scope): in 13.3 <c>VoteState → Embodied</c> only ROUTES the mode + LOCKS movement
    /// + cuts arrow nav; the concrete seated camera / seat-snap / clamped look is Story 13.4. Until then the
    /// Vote keeps its unchanged board-camera presentation (its current <c>forceBoardCamera</c>) — the mode
    /// entry exists so 13.4 fills the camera/seat behaviour WITHOUT touching this arbiter.
    ///
    /// Presentation-only <c>MonoBehaviour</c> (NOT a NetworkBehaviour): it reads the replicated
    /// <c>currentGameStateIndex</c> and toggles LOCAL cameras/input — it mutates no game state. Lane A:
    /// <see cref="gameManager"/> serialized concrete, narrowed by <see cref="Query"/> (Unity can't serialize
    /// an interface) — identical to <see cref="BoardCameraManager"/> / <see cref="AvatarFollowCamera"/>.
    /// </summary>
    public class AvatarCameraArbiter : MonoBehaviour
    {
        // Lane A (mirrors BoardCameraManager.gameManager / AvatarFollowCamera): read the narrow state-query
        // slice off the concrete serialized GameManager.
        [SerializeField] private GameManager gameManager;
        [SerializeField] private AvatarFollowCamera _followCamera;

        private IGameStateQuery Query => gameManager;

        private bool _subscribed;
        // Last resolved mode, cached so a late-spawning local avatar (below) starts in the right movement
        // state when its controller finally binds.
        private CameraMode _currentMode = CameraMode.Board;
        private AvatarMovementController _localMovement;

        private void Awake()
        {
            Assert.IsNotNull(gameManager, "AvatarCameraArbiter.gameManager is not wired — wire it in GameScene (like BoardCameraManager).");
            Assert.IsNotNull(_followCamera, "AvatarCameraArbiter._followCamera is not wired — wire the AvatarFollowCamera instance.");
        }

        private void Start()
        {
            // Pure reaction (NFR2): subscribe + prime with the current value (exactly BoardCameraManager.cs:65-67
            // / AvatarFollowCamera 13.2). Never writes the index.
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

            // Cleanup symmetry (archi §5b): this arbiter is the SOLE owner of the board-camera 'Avatar'
            // source. If we tore down while a non-Board mode had set it false, a surviving
            // BoardCameraManager.instance (a sanctioned static survivor) would keep arrow neighbour-nav cut
            // forever. Restore the neutral AND element (true) so the board returns to its pre-arbiter
            // behaviour. Idempotent + null-tolerant (no-op if the board manager already went away first).
            BoardCameraManager.instance?.SetActiveSource(BoardCameraInputActiveSource.Avatar, true);
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            _currentMode = AvatarCameraModePolicy.ResolveMode(Query.GetGameState(_newValue));
            ApplyMode();
        }

        private void ApplyMode()
        {
            // FreeRoam: the first-person Lobby camera outranks the board cams. Board/Embodied: it stands down
            // so the board cameras present (Embodied falls back to VoteState's board cam until 13.4).
            _followCamera.SetActive(_currentMode == CameraMode.FreeRoam);

            // Board-camera arrow neighbour-nav: ON only in Board. The Avatar source ANDs with the untouched
            // GameState source (Controller.cs:43-53), so FreeRoam/Embodied cut arrow nav cleanly without
            // removing/disabling any board camera and without touching the GameState source (NFR1). Order vs
            // BoardCameraManager's own subscriber is irrelevant — the AND is order-independent (NFR2-safe).
            // .instance is a recorded census survivor / opt-out (sanctioned, NOT a locator to remove);
            // null-tolerant for headless / early-boot.
            BoardCameraManager.instance?.SetActiveSource(BoardCameraInputActiveSource.Avatar, _currentMode == CameraMode.Board);

            // Avatar movement input: enabled ONLY in FreeRoam (the Lobby), locked in every other state.
            ApplyMovementEnabled();
        }

        private void ApplyMovementEnabled()
        {
            if (!TryBindLocalMovement())
            {
                // Avatar not spawned yet — re-applied when it binds (Update).
                return;
            }
            _localMovement.SetMovementEnabled(_currentMode == CameraMode.FreeRoam);
        }

        private void Update()
        {
            // Late-spawn binding: the local avatar may spawn a frame or two AFTER the Lobby state activated.
            // Keep trying to bind its movement controller and re-apply the cached mode's enable-state, so an
            // avatar that appears after the FreeRoam gate still starts walkable. Once bound this is a no-op.
            if (_localMovement == null)
            {
                ApplyMovementEnabled();
            }
        }

        // Mirrors AvatarFollowCamera.TryBindLocalAvatar (AvatarFollowCamera.cs:138-158): resolve the local
        // owned avatar via AvatarManager.For(NetworkManager.Singleton) + IsOwner, cache its movement
        // controller. (AvatarManager.For + NetworkManager.Singleton are the avatar layer's own resolution —
        // same as 13.2 — which is why the avatar types are NOT in DiSeamMigratedConsumers.)
        private bool TryBindLocalMovement()
        {
            if (_localMovement != null)
            {
                return true;
            }

            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null)
            {
                return false;
            }

            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _localMovement = _avatar.GetComponent<AvatarMovementController>();
                    return _localMovement != null;
                }
            }
            return false;
        }
    }
}
