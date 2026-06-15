using System.Collections;
using System.Reflection;
using Avatars;
using Board.BoardCameraSystem;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Avatars
{
    /// <summary>
    /// Story 13.3 — proves the <see cref="AvatarCameraArbiter"/> REACTS to
    /// <c>currentGameStateIndex</c> changes and drives the three local presentation toggles per
    /// resolved <see cref="CameraMode"/>: the <see cref="AvatarFollowCamera"/> active flag (on iff
    /// FreeRoam) and the <see cref="BoardCameraManager"/> <c>Avatar</c> input source (true iff Board).
    ///
    /// Harness: a single StartHost (host == server, RTT 0) — the same faithful substrate as
    /// GameLoopTransitionOrderingTests — with a GameManager seeded with three STUB states whose TYPES
    /// drive the policy: a plain board state (→ Board), a <see cref="LobbyState"/> subclass (→ FreeRoam)
    /// and a <see cref="VoteState"/> subclass (→ Embodied). The stubs override the heavy
    /// <c>OnStateCreated</c>/lifecycle (which would NRE without a CharacterManager) to no-ops; only the
    /// index is driven directly (Value write, never SwitchGameState), so no state lifecycle runs and the
    /// transition ordering is untouched (NFR2).
    ///
    /// SCOPE CHOICE (documented per Task 6): wiring a full spawned, owner-authoritative avatar into this
    /// reaction test is disproportionate, so the arbiter's MOVEMENT effect is covered separately by the
    /// policy golden (the FreeRoam row) + the direct <see cref="AvatarMovementController.SetMovementEnabled"/>
    /// round-trip below; this reaction test asserts the camera + board-source effects (with no avatar
    /// registered, AvatarManager.For returns null and the arbiter's movement apply is a safe no-op).
    /// </summary>
    public class AvatarCameraArbiterTests
    {
        // Stub states: TYPE drives the policy, lifecycle neutralized so SetupGameStates' OnStateCreated
        // pass does not NRE without a CharacterManager.
        private class BoardStubState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

        // StateUpdate* are overridden no-op too: GameManager.Update drives the CURRENT state's
        // StateUpdateServer/Client every frame, and VoteState.StateUpdateServer would tick its vote timer
        // (uninitialized → 0) and call Loop.NextGameState() — corrupting the index under test and NRE-ing on
        // StopCoroutine(null). Neutralizing them keeps the index under the test's sole control.
        private class LobbyStubState : LobbyState
        {
            public override void OnStateCreated() { }
            public override void OnStartStateServer() { }
            public override void OnStartStateClient() { }
            public override void StateUpdateServer() { }
            public override void StateUpdateClient() { }
        }

        private class VoteStubState : VoteState
        {
            public override void OnStateCreated() { }
            public override void OnStartStateServer() { }
            public override void OnStartStateClient() { }
            public override void StateUpdateServer() { }
            public override void StateUpdateClient() { }
        }

        private const int BoardIndex = 0;
        private const int LobbyIndex = 1;
        private const int VoteIndex = 2;

        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;

        private GameObject _boardCameraManagerGo;
        private GameObject _followCameraGo;
        private AvatarFollowCamera _followCamera;
        private GameObject _arbiterGo;

        private BoardStubState _boardState;
        private LobbyStubState _lobbyState;
        private VoteStubState _voteState;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                EnableSceneManagement = false
            };
            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;

            // Index 0 = board stub (its OnStart* run at spawn — harmless), 1 = lobby stub, 2 = vote stub.
            _boardState = ScriptableObject.CreateInstance<BoardStubState>();
            _lobbyState = ScriptableObject.CreateInstance<LobbyStubState>();
            _voteState = ScriptableObject.CreateInstance<VoteStubState>();
            _gameManager.gameStates.Add(_boardState, new GameStateSettings { isInGameLoop = false });
            _gameManager.gameStates.Add(_lobbyState, new GameStateSettings { isInGameLoop = false });
            _gameManager.gameStates.Add(_voteState, new GameStateSettings { isInGameLoop = false });

            _gameManagerGo.GetComponent<NetworkObject>().Spawn();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_gameManager);
            yield return null;
            yield return null;

            // BoardCameraManager: created on an INACTIVE GameObject so its Awake/Start never run (Start
            // needs InputManager + a camera rig we do not have). We set the static `instance` by hand; the
            // arbiter only touches its ControllerBase active-source dictionary (SetActiveSource/GetActiveSource),
            // which is independent of Start.
            _boardCameraManagerGo = new GameObject("BoardCameraManager");
            _boardCameraManagerGo.SetActive(false);
            BoardCameraManager.instance = _boardCameraManagerGo.AddComponent<BoardCameraManager>();

            // AvatarFollowCamera with a real CinemachineCamera wired (so Awake's assert passes). Built
            // inactive, wired, then activated so Awake sees the populated field.
            _followCameraGo = new GameObject("AvatarFollowCamera");
            _followCameraGo.SetActive(false);
            CinemachineCamera _cmCamera = _followCameraGo.AddComponent<CinemachineCamera>();
            _followCamera = _followCameraGo.AddComponent<AvatarFollowCamera>();
            ReflectionHelper.SetPrivateField(_followCamera, "_camera", _cmCamera);
            _followCameraGo.SetActive(true);

            // The arbiter: wired inactive, then activated so Awake's asserts see populated fields and Start
            // subscribes + primes with the current index (0 = Board).
            _arbiterGo = new GameObject("AvatarCameraArbiter");
            _arbiterGo.SetActive(false);
            AvatarCameraArbiter _arbiter = _arbiterGo.AddComponent<AvatarCameraArbiter>();
            ReflectionHelper.SetPrivateField(_arbiter, "gameManager", _gameManager);
            ReflectionHelper.SetPrivateField(_arbiter, "_followCamera", _followCamera);
            _arbiterGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_arbiterGo != null) Object.Destroy(_arbiterGo);
            if (_followCameraGo != null) Object.Destroy(_followCameraGo);
            if (_boardCameraManagerGo != null) Object.Destroy(_boardCameraManagerGo);
            BoardCameraManager.instance = null;

            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            // Statics survive across PlayMode sessions when domain reload is off.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);

            if (_gameManagerGo != null) Object.Destroy(_gameManagerGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);

            if (_boardState != null) Object.Destroy(_boardState);
            if (_lobbyState != null) Object.Destroy(_lobbyState);
            if (_voteState != null) Object.Destroy(_voteState);
            yield return null;
        }

        private bool AvatarSourceActive() => BoardCameraManager.instance.GetActiveSource("Avatar");

        private IEnumerator AssertModeEffects(string _label, bool _expectFollowActive, bool _expectAvatarSource)
        {
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _followCamera.IsActive == _expectFollowActive && AvatarSourceActive() == _expectAvatarSource,
                5f,
                $"Arbiter did not settle the expected effects for {_label} " +
                $"(followActive={_expectFollowActive}, avatarSource={_expectAvatarSource}). " +
                $"Observed followActive={_followCamera.IsActive}, avatarSource={AvatarSourceActive()}.");

            Assert.AreEqual(_expectFollowActive, _followCamera.IsActive, $"{_label}: AvatarFollowCamera active state wrong.");
            Assert.AreEqual(_expectAvatarSource, AvatarSourceActive(), $"{_label}: BoardCameraManager 'Avatar' source wrong.");
        }

        [UnityTest]
        public IEnumerator Arbiter_DrivesCameraAndBoardSource_AcrossModes()
        {
            // Primed at index 0 (Board): follow cam stands down, board arrow-nav source ON.
            yield return AssertModeEffects("Board(prime)", _expectFollowActive: false, _expectAvatarSource: true);

            // → Lobby (FreeRoam): follow cam outranks the board cams, board arrow-nav source OFF.
            _gameManager.currentGameStateIndex.Value = LobbyIndex;
            yield return AssertModeEffects("FreeRoam", _expectFollowActive: true, _expectAvatarSource: false);

            // → Vote (Embodied): follow cam stands down (board-cam fall-back until 13.4), arrow-nav still OFF.
            _gameManager.currentGameStateIndex.Value = VoteIndex;
            yield return AssertModeEffects("Embodied", _expectFollowActive: false, _expectAvatarSource: false);

            // → back to a board state: follow cam down, board arrow-nav source back ON (untouched-phase parity).
            _gameManager.currentGameStateIndex.Value = BoardIndex;
            yield return AssertModeEffects("Board", _expectFollowActive: false, _expectAvatarSource: true);
        }

        [Test]
        public void MovementController_SetMovementEnabled_RoundTrips()
        {
            // The arbiter applies movement via AvatarMovementController.SetMovementEnabled (FreeRoam only).
            // Direct round-trip of the hook (the FreeRoam mapping is pinned by the EditMode policy golden).
            GameObject _go = new GameObject("AvatarMovement");
            _go.SetActive(false);
            _go.AddComponent<NetworkObject>();          // NetworkBehaviour needs a NetworkObject sibling.
            _go.AddComponent<CharacterController>();     // RequireComponent dependency.
            AvatarMovementController _controller = _go.AddComponent<AvatarMovementController>();
            _go.SetActive(true);

            FieldInfo _field = typeof(AvatarMovementController).GetField(
                "_movementEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_field, "_movementEnabled field not found — did the 13.3 hook change?");

            _controller.SetMovementEnabled(false);
            Assert.IsFalse((bool)_field.GetValue(_controller), "SetMovementEnabled(false) did not disable movement.");

            _controller.SetMovementEnabled(true);
            Assert.IsTrue((bool)_field.GetValue(_controller), "SetMovementEnabled(true) did not enable movement.");

            Object.Destroy(_go);
        }
    }
}
