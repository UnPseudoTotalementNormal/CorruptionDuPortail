using System.Collections;
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
    /// Story 13.4 — proves the embodied seated Vote mode:
    /// <see cref="Arbiter_DrivesEmbodiedCamera_AcrossModes"/> — the <see cref="AvatarCameraArbiter"/>
    /// drives <see cref="AvatarEmbodiedCamera.IsActive"/> ON iff the resolved mode is <c>Embodied</c>
    /// (VoteState) and the <see cref="AvatarFollowCamera"/> ON iff <c>FreeRoam</c> — never both. Same
    /// StartHost + stub-state substrate as <c>AvatarCameraArbiterTests</c> (index driven directly, no
    /// state lifecycle, transition ordering untouched — NFR2).
    ///
    /// The seated-ring geometry itself (per-client rotation, equidistant spread, relative-offset gaze
    /// invariance) is pure and lives in EditMode <c>SeatRingGeometryTests</c>; seated placement is now
    /// client-local (NetworkTransform suppressed by <see cref="AvatarSeatingPresenter"/>), so the former
    /// "SeatAtSeat replicates via transform" PlayMode round-trip no longer reflects how seating works.
    /// </summary>
    public class AvatarEmbodiedModeTests
    {
        private class BoardStubState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

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
        private GameObject _embodiedCameraGo;
        private AvatarEmbodiedCamera _embodiedCamera;
        private GameObject _seatingPresenterGo;
        private AvatarSeatingPresenter _seatingPresenter;
        private GameObject _visibilityGo;
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

            // BoardCameraManager on an INACTIVE GameObject (Awake/Start never run); only its ControllerBase
            // active-source dictionary is touched by the arbiter.
            _boardCameraManagerGo = new GameObject("BoardCameraManager");
            _boardCameraManagerGo.SetActive(false);
            BoardCameraManager.instance = _boardCameraManagerGo.AddComponent<BoardCameraManager>();

            // Follow camera (FreeRoam) with a real CinemachineCamera so Awake's assert passes.
            _followCameraGo = new GameObject("AvatarFollowCamera");
            _followCameraGo.SetActive(false);
            CinemachineCamera _followCm = _followCameraGo.AddComponent<CinemachineCamera>();
            _followCamera = _followCameraGo.AddComponent<AvatarFollowCamera>();
            ReflectionHelper.SetPrivateField(_followCamera, "_camera", _followCm);
            _followCameraGo.SetActive(true);

            // Embodied camera (Vote) with a real CinemachineCamera; _inputActions left null (Awake is
            // null-tolerant — only the look read is skipped, irrelevant to the active-flag assertion).
            _embodiedCameraGo = new GameObject("AvatarEmbodiedCamera");
            _embodiedCameraGo.SetActive(false);
            CinemachineCamera _embodiedCm = _embodiedCameraGo.AddComponent<CinemachineCamera>();
            _embodiedCamera = _embodiedCameraGo.AddComponent<AvatarEmbodiedCamera>();
            ReflectionHelper.SetPrivateField(_embodiedCamera, "_camera", _embodiedCm);
            _embodiedCameraGo.SetActive(true);

            // Seating presenter (Embodied): a plain MonoBehaviour the arbiter toggles. No AvatarManager is
            // wired in this reaction fixture, so its LateUpdate resolves For(Singleton) == null and no-ops —
            // exactly what we want here (we assert camera modes, not seating geometry, which is EditMode-tested).
            _seatingPresenterGo = new GameObject("AvatarSeatingPresenter");
            _seatingPresenter = _seatingPresenterGo.AddComponent<AvatarSeatingPresenter>();

            // Visibility controller (arbiter now asserts it). No AvatarManager wired → its LateUpdate no-ops.
            _visibilityGo = new GameObject("AvatarVisibilityController");
            AvatarVisibilityController _visibility = _visibilityGo.AddComponent<AvatarVisibilityController>();

            // Arbiter: wired inactive, then activated so Awake's asserts see populated fields and Start
            // subscribes + primes with the current index (0 = Board).
            _arbiterGo = new GameObject("AvatarCameraArbiter");
            _arbiterGo.SetActive(false);
            AvatarCameraArbiter _arbiter = _arbiterGo.AddComponent<AvatarCameraArbiter>();
            ReflectionHelper.SetPrivateField(_arbiter, "gameManager", _gameManager);
            ReflectionHelper.SetPrivateField(_arbiter, "_followCamera", _followCamera);
            ReflectionHelper.SetPrivateField(_arbiter, "_embodiedCamera", _embodiedCamera);
            ReflectionHelper.SetPrivateField(_arbiter, "_seatingPresenter", _seatingPresenter);
            ReflectionHelper.SetPrivateField(_arbiter, "_visibility", _visibility);
            _arbiterGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_arbiterGo != null) Object.Destroy(_arbiterGo);
            if (_seatingPresenterGo != null) Object.Destroy(_seatingPresenterGo);
            if (_visibilityGo != null) Object.Destroy(_visibilityGo);
            if (_followCameraGo != null) Object.Destroy(_followCameraGo);
            if (_embodiedCameraGo != null) Object.Destroy(_embodiedCameraGo);
            if (_boardCameraManagerGo != null) Object.Destroy(_boardCameraManagerGo);
            BoardCameraManager.instance = null;

            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);

            if (_gameManagerGo != null) Object.Destroy(_gameManagerGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);

            // Wait for NGO to null the static Singleton on the destroyed NM (its OnDestroy clears it). This
            // test spawns an extra NetworkObject (the seat-snap avatar), so the destroy frame must fully
            // settle before the next test class' SetUp — a LIVE stale Singleton would make its host NM
            // self-destruct on OnEnable (mirror MultiClientGameFixture / CoexistenceGateTests teardown).
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => NetworkManager.Singleton == null, 5f,
                "NetworkManager.Singleton was not cleared after teardown.");

            if (_boardState != null) Object.Destroy(_boardState);
            if (_lobbyState != null) Object.Destroy(_lobbyState);
            if (_voteState != null) Object.Destroy(_voteState);
            yield return null;
        }

        private IEnumerator AssertCameras(string _label, bool _expectFollow, bool _expectEmbodied)
        {
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _followCamera.IsActive == _expectFollow && _embodiedCamera.IsActive == _expectEmbodied,
                5f,
                $"Arbiter did not settle the expected cameras for {_label} " +
                $"(follow={_expectFollow}, embodied={_expectEmbodied}). " +
                $"Observed follow={_followCamera.IsActive}, embodied={_embodiedCamera.IsActive}.");

            Assert.AreEqual(_expectFollow, _followCamera.IsActive, $"{_label}: AvatarFollowCamera active state wrong.");
            Assert.AreEqual(_expectEmbodied, _embodiedCamera.IsActive, $"{_label}: AvatarEmbodiedCamera active state wrong.");
        }

        [UnityTest]
        public IEnumerator Arbiter_DrivesEmbodiedCamera_AcrossModes()
        {
            // Primed at index 0 (Board): both stand down → board cameras present.
            yield return AssertCameras("Board(prime)", _expectFollow: false, _expectEmbodied: false);

            // → Lobby (FreeRoam): follow cam on, embodied off.
            _gameManager.currentGameStateIndex.Value = LobbyIndex;
            yield return AssertCameras("FreeRoam", _expectFollow: true, _expectEmbodied: false);

            // → Vote (Embodied): embodied cam on, follow off (Story 13.4 — the seated camera now realises Embodied).
            _gameManager.currentGameStateIndex.Value = VoteIndex;
            yield return AssertCameras("Embodied", _expectFollow: false, _expectEmbodied: true);

            // → back to Board: both stand down again (untouched-phase parity, NFR1).
            _gameManager.currentGameStateIndex.Value = BoardIndex;
            yield return AssertCameras("Board", _expectFollow: false, _expectEmbodied: false);
        }

    }
}
