using Board.BoardCameraSystem;
using GameLogic;
using Reticle;
using Smartphone;
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
    ///  • <see cref="AvatarEmbodiedCamera"/> active iff the seated first-person board-camera NODE is the live
    ///    camera — the seated first-person is wired into the board-camera neighbour graph as a node, so in ANY
    ///    seated phase (Board night or Embodied day) the player can arrow between it and the board overviews
    ///    (it follows the board manager's current-camera event, not the raw mode);
    ///  • the <see cref="BoardCameraManager"/> <c>Avatar</c> input source = (<c>mode == Board || Embodied</c>)
    ///    — arrow neighbour-nav is live in every seated phase, cut only in FreeRoam, via the existing AND-gate,
    ///    WITHOUT touching the GameState source or removing any board camera (NFR1);
    ///  • the local owned avatar's movement input enabled iff <c>FreeRoam</c>
    ///    (<see cref="AvatarMovementController.SetMovementEnabled"/>);
    ///  • the <see cref="AvatarSeatingPresenter"/> is active in EVERY seated phase (<c>Board</c> or
    ///    <c>Embodied</c>) — i.e. the whole match except the lobby — driving the per-client rotated seating ring
    ///    (every avatar placed locally, NetworkTransform suppressed, networked gaze yaw applied).
    ///
    /// NFR2 — it is a PURE REACTION: subscribe-and-prime in <see cref="Start"/>, unsubscribe in
    /// <see cref="OnDestroy"/> (subscription symmetry, archi §5b); it NEVER writes the index and never
    /// touches the OnEnd → write → OnStart transition ordering. The 2.11a sequence golden stays green.
    ///
    /// SEATED WHOLE MATCH (except lobby): every in-game state is seated — Board (night) hides the others +
    /// defaults to a board overview; Embodied (day = vote + recap) shows the others + defaults to first-person.
    /// In both, the seated first-person is a navigable board-camera node and movement stays locked. Night vs day
    /// differs ONLY by visibility + the default camera; only the lobby (FreeRoam) walks around, not seated.
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
        [SerializeField] private AvatarEmbodiedCamera _embodiedCamera;
        [SerializeField] private AvatarSeatingPresenter _seatingPresenter;
        [SerializeField] private AvatarVisibilityController _visibility;
        [SerializeField] private ReticleInteractor _reticle;
        [Tooltip("Reusable broadcast of the resolved camera mode (e.g. the card hover reads it to gate the " +
                 "first-person look-at). Null-tolerant — unwired just means consumers see Board.")]
        [SerializeField] private CameraModeChannel _cameraModeChannel;
        // The scene smartphone/tablet. Drives the cursor + look gate: while it is open the OS cursor is freed
        // (to drive the tablet UI) and the first-person look is frozen. Null-tolerant — if unwired, the cursor
        // simply follows the camera mode and the look is never frozen.
        [SerializeField] private SmartphoneController _smartphone;
        // The emote wheel input. While the wheel is held open the seated/free-roam look is FROZEN (the mouse
        // drives the wheel's virtual stick, not the camera) — same look-gate as the tablet, but the cursor stays
        // LOCKED (the stick needs the delta). Null-tolerant — unwired just means the wheel never freezes the look.
        [SerializeField] private EmoteWheelInput _emoteWheel;

        private IGameStateQuery Query => gameManager;

        private bool _subscribed;
        // Whether we managed to subscribe to the board manager's current-camera event (it is a scene singleton
        // available by our Start; guarded + mirrored for a clean unsubscribe).
        private bool _boardCameraSubscribed;
        // Mirrors the tablet open state via its onPanelOpened/onPanelClosed events. Combined with the camera
        // mode to decide the cursor lock + look freeze.
        private bool _tabletOpen;
        // Mirrors the emote wheel open state via its Opened/Closed events. ANDed into the look gate (like the
        // tablet) so the mouse turns the wheel, not the camera, while it is up.
        private bool _wheelOpen;
        // Last resolved mode, cached so a late-spawning local avatar (below) starts in the right movement
        // state when its controller finally binds.
        private CameraMode _currentMode = CameraMode.Board;
        // During the Vote (Embodied) the first-person seated camera is wired into the board-camera neighbour
        // graph as a node: this tracks whether THAT node is the live camera (vs a board overview the player
        // arrowed to). The embodied camera / reticle / cursor-lock follow it. Defaults true on Vote entry (the
        // seated node is the Vote's starting camera) and is updated live by the board manager's camera event.
        private bool _firstPersonActive;
        private AvatarMovementController _localMovement;

        private void Awake()
        {
            Assert.IsNotNull(gameManager, "AvatarCameraArbiter.gameManager is not wired — wire it in GameScene (like BoardCameraManager).");
            Assert.IsNotNull(_followCamera, "AvatarCameraArbiter._followCamera is not wired — wire the AvatarFollowCamera instance.");
            Assert.IsNotNull(_embodiedCamera, "AvatarCameraArbiter._embodiedCamera is not wired — wire the AvatarEmbodiedCamera instance.");
            Assert.IsNotNull(_seatingPresenter, "AvatarCameraArbiter._seatingPresenter is not wired — wire the AvatarSeatingPresenter instance.");
            Assert.IsNotNull(_visibility, "AvatarCameraArbiter._visibility is not wired — wire the AvatarVisibilityController instance.");
            Assert.IsNotNull(_reticle, "AvatarCameraArbiter._reticle is not wired — wire the ReticleInteractor instance.");
        }

        private void Start()
        {
            // Pure reaction (NFR2): subscribe + prime with the current value (exactly BoardCameraManager.cs:65-67
            // / AvatarFollowCamera 13.2). Never writes the index.
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
            _subscribed = true;

            // Tablet open/close drives the cursor + look gate. Prime from the current state, then react.
            if (_smartphone != null)
            {
                _tabletOpen = _smartphone.IsOpen;
                _smartphone.onPanelOpened += OnTabletOpened;
                _smartphone.onPanelClosed += OnTabletClosed;
            }

            // Emote wheel open/close freezes the look (prime + react, mirroring the tablet).
            if (_emoteWheel != null)
            {
                _wheelOpen = _emoteWheel.IsOpen;
                _emoteWheel.Opened += OnEmoteWheelOpened;
                _emoteWheel.Closed += OnEmoteWheelClosed;
            }

            // The seated first-person is a board-camera node during the Vote; follow which board camera is live
            // so the embodied camera / reticle / cursor track the player arrowing between it and the overviews.
            // .instance is a scene singleton set in Awake (before any Start) — available here; guarded anyway.
            if (BoardCameraManager.instance != null)
            {
                BoardCameraManager.instance.onCurrentCameraChanged += OnCurrentBoardCameraChanged;
                _boardCameraSubscribed = true;
            }

            OnGameStateChanged(Query.currentGameStateIndex.Value, Query.currentGameStateIndex.Value);
        }

        private void OnDestroy()
        {
            // Mirror the Start subscription (subscribe-in-X ⇒ unsubscribe-in-its-teardown).
            if (_subscribed && gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }

            if (_smartphone != null)
            {
                _smartphone.onPanelOpened -= OnTabletOpened;
                _smartphone.onPanelClosed -= OnTabletClosed;
            }

            if (_emoteWheel != null)
            {
                _emoteWheel.Opened -= OnEmoteWheelOpened;
                _emoteWheel.Closed -= OnEmoteWheelClosed;
            }

            // Mirror the Start subscription to the board manager's current-camera event.
            if (_boardCameraSubscribed && BoardCameraManager.instance != null)
            {
                BoardCameraManager.instance.onCurrentCameraChanged -= OnCurrentBoardCameraChanged;
            }

            // Never leave a teardown with a locked/hidden cursor (e.g. scene unload mid-Vote) — restore the
            // free OS cursor so menus/other scenes are usable.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Cleanup symmetry (archi §5b): this arbiter is the SOLE owner of the board-camera 'Avatar'
            // source. If we tore down while a non-Board mode had set it false, a surviving
            // BoardCameraManager.instance (a sanctioned static survivor) would keep arrow neighbour-nav cut
            // forever. Restore the neutral AND element (true) so the board returns to its pre-arbiter
            // behaviour. Idempotent + null-tolerant (no-op if the board manager already went away first).
            BoardCameraManager.instance?.SetActiveSource(BoardCameraInputActiveSource.Avatar, true);
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            CameraMode _newMode = AvatarCameraModePolicy.ResolveMode(Query.GetGameState(_newValue));
            _currentMode = _newMode;

            // The seated first-person is the default camera ONLY in Embodied (day) — there the embodied camera
            // outranks the board cams, so a stale FP current is harmless. Entering any OTHER mode (a night/Board
            // phase that defaults to a board overview, or the lobby) while FP is still the live board camera
            // would strand a frozen first-person view (in Board the embodied driver is off at entry). Hand the
            // board back to its default first. Guarded: only when FP is actually the current camera.
            if (_newMode != CameraMode.Embodied
                && BoardCameraManager.instance != null
                && BoardCameraManager.instance.CurrentBoardCameraId == BoardCameraIdEnum.SeatedFirstPerson)
            {
                BoardCameraManager.instance.ResetToStartingCamera();
            }

            ApplyMode();
        }

        private void ApplyMode()
        {
            // FreeRoam: the first-person Lobby camera outranks the board cams. Board/Embodied: it stands down.
            // The Vote's first-person is NO LONGER force-activated here — it is a board-camera node (driven by
            // ApplyFirstPersonPresentation), so the player can arrow between it and the board overviews.
            _followCamera.SetActive(_currentMode == CameraMode.FreeRoam);

            // Board-camera arrow neighbour-nav: ON in Board AND Embodied (the Vote now navigates the same
            // board-camera set, with the seated first-person wired in as a node). Only FreeRoam (Lobby) cuts it.
            // The Avatar source ANDs with the untouched GameState source (Controller.cs:43-53), so this never
            // touches the GameState source or removes/disables any board camera (NFR1); order vs the board
            // manager's own subscriber is irrelevant (AND is order-independent). .instance is a recorded census
            // survivor / opt-out (sanctioned, NOT a locator to remove); null-tolerant for headless / early-boot.
            BoardCameraManager.instance?.SetActiveSource(BoardCameraInputActiveSource.Avatar,
                _currentMode == CameraMode.Board || _currentMode == CameraMode.Embodied);

            // Avatar movement input: enabled ONLY in FreeRoam (the Lobby), locked in every other state.
            ApplyMovementEnabled();

            // Seated ring: the player is seated for the WHOLE match except the lobby — both Board (night) and
            // Embodied (day) place every avatar on the ring, independent of which camera is live. Only FreeRoam
            // (lobby) stands it down. The presenter self-handles late-spawning avatars + player-count changes.
            _seatingPresenter.SetActive(_currentMode == CameraMode.Board || _currentMode == CameraMode.Embodied);

            // Avatar body visibility (single owner): Board (night) hides everyone ("you only see each other
            // during the day"); FreeRoam (lobby) / Embodied (day = vote + recap) show everyone except the local
            // first-person body. This is the ONLY thing that now distinguishes night (Board) from day (Embodied).
            _visibility.SetMode(_currentMode);

            // Broadcast the mode on the reusable channel (cards gate their first-person look-at hover on it).
            _cameraModeChannel?.Set(_currentMode);

            // Entering the Vote the seated first-person node is the starting camera, so assume it active until
            // the board manager's camera event says otherwise. Outside Embodied this flag is unused.
            _firstPersonActive = _currentMode == CameraMode.Embodied;
            ApplyFirstPersonPresentation();
        }

        // The first-person-specific toggles. During the Vote they follow which board camera is live — the
        // seated first-person node (embodied camera + reticle + locked cursor) vs a board overview (board cam,
        // no reticle, free cursor). Outside Embodied the embodied camera + reticle are simply off (the Lobby
        // first-person is the separate follow camera).
        private void ApplyFirstPersonPresentation()
        {
            // The seated first-person is reachable in BOTH night (Board) and day (Embodied) — it is on whenever
            // its board-camera node is the live one (tracked by _firstPersonActive). FreeRoam (lobby) uses the
            // separate follow camera, so the embodied camera + reticle are off there.
            bool _embodied = (_currentMode == CameraMode.Board || _currentMode == CameraMode.Embodied) && _firstPersonActive;
            // First-person targeting: the center-screen reticle is live only on the seated first-person node so
            // cards/buttons can be hovered + clicked without an OS cursor.
            _embodiedCamera.SetActive(_embodied);
            _reticle.SetActive(_embodied);
            // Broadcast whether the seated first-person node is the live camera so consumers (the card look-at
            // hover) don't aim a card at an OVERHEAD board-overview camera — which would lay it flat. Tracks
            // both mode transitions and arrow-nav (this method runs on both). Null-tolerant.
            _cameraModeChannel?.SetSeatedFirstPersonLive(_embodied);
            // Cursor lock + look freeze derive from the first-person state AND the tablet state — re-apply both.
            ApplyCursorAndLook();
        }

        // The board manager reports the live camera. Whenever the player is seated (Board night or Embodied
        // day), track whether the seated first-person node is the live one so the embodied presentation follows
        // the player's arrow navigation between it and the board overviews. Ignored only in FreeRoam (lobby).
        private void OnCurrentBoardCameraChanged(BoardCameraIdEnum _id)
        {
            if (_currentMode != CameraMode.Board && _currentMode != CameraMode.Embodied)
            {
                return;
            }
            bool _fp = _id == BoardCameraIdEnum.SeatedFirstPerson;
            if (_fp == _firstPersonActive)
            {
                return;
            }
            _firstPersonActive = _fp;
            ApplyFirstPersonPresentation();
        }

        private void OnTabletOpened()
        {
            _tabletOpen = true;
            ApplyCursorAndLook();
        }

        private void OnTabletClosed()
        {
            _tabletOpen = false;
            ApplyCursorAndLook();
        }

        private void OnEmoteWheelOpened()
        {
            _wheelOpen = true;
            ApplyCursorAndLook();
        }

        private void OnEmoteWheelClosed()
        {
            _wheelOpen = false;
            ApplyCursorAndLook();
        }

        // Single source of truth for the OS cursor + the first-person look gate. First-person modes
        // (FreeRoam/Embodied) lock + hide the cursor so the mouse drives the look — UNLESS the tablet is open,
        // which frees the cursor (to click the tablet UI) and freezes the look so the camera no longer turns
        // with the mouse. Board mode always shows the cursor (board/UI is click-driven).
        private void ApplyCursorAndLook()
        {
            bool _firstPerson = _currentMode == CameraMode.FreeRoam
                || ((_currentMode == CameraMode.Board || _currentMode == CameraMode.Embodied) && _firstPersonActive);
            bool _lockCursor = _firstPerson && !_tabletOpen;

            Cursor.lockState = _lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_lockCursor;

            // Freeze the look whenever the tablet OR the emote wheel is open (both look-readers ignore it;
            // default-on otherwise). The wheel keeps the cursor LOCKED (above) but takes the mouse for its
            // virtual stick, so the camera must not also turn.
            bool _lookEnabled = !_tabletOpen && !_wheelOpen;
            if (TryBindLocalMovement())
            {
                _localMovement.SetLookEnabled(_lookEnabled);
            }
            _embodiedCamera.SetLookEnabled(_lookEnabled);
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
                // The late-bound controller also needs the current look-gate state primed (it may have
                // spawned while the tablet was open).
                if (_localMovement != null)
                {
                    _localMovement.SetLookEnabled(!_tabletOpen);
                }
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
