using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.GameStates;
using Network.Services;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// The menu creates the cloud lobby locked and LobbyState opens it once the host has loaded GameScene: a client
    /// joining during that load got a broken scene sync ("Server Scene Handle already exist", stuck). Checks the
    /// opening waits for the load event, retries a failed unlock (a failure used to leave the lobby open, it would
    /// now leave the game unjoinable), and never reopens a lobby once the game has started.
    /// </summary>
    [Category("LobbyReady")]
    public class LobbyCloudLockTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _lobbyManagerGo;
        private LobbyState _lobbyState;
        private FakeLobbyBackend _backend;
        private GameLobby _lobby;

        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() {}
            public override void StateUpdateServer() {}
        }

        private sealed class FakeLobbyBackend : ILobbyBackend
        {
            public int UnlockFailuresLeft;
            public int UnlockCalls;
            public bool? Locked;

            public bool IsReady => true;
            public bool NeedsHeartbeat => false;

            public UniTask SetLockedAsync(GameLobby lobby, bool locked)
            {
                if (!locked)
                {
                    UnlockCalls++;
                    if (UnlockFailuresLeft > 0)
                    {
                        UnlockFailuresLeft--;
                        throw new LobbyBackendException("rate limited");
                    }
                }
                Locked = locked;
                return UniTask.CompletedTask;
            }

            public bool IsHost(GameLobby lobby) => true;
            public UniTask<GameLobby> CreateAsync(LobbyCreationSettings settings) => UniTask.FromResult<GameLobby>(null);
            public UniTask<List<GameLobby>> QueryAsync() => UniTask.FromResult(new List<GameLobby>());
            public UniTask<GameLobby> JoinByIdAsync(string lobbyId, string password) => UniTask.FromResult<GameLobby>(null);
            public UniTask<GameLobby> JoinByCodeAsync(string code) => UniTask.FromResult<GameLobby>(null);
            public UniTask<GameLobby> GetAsync(string lobbyId) => UniTask.FromResult<GameLobby>(null);
            public UniTask<GameLobby> RefreshAsync(GameLobby lobby) => UniTask.FromResult(lobby);
            public UniTask HeartbeatAsync(GameLobby lobby) => UniTask.CompletedTask;
            public UniTask LeaveAsync(GameLobby lobby) => UniTask.CompletedTask;
            public UniTask DeleteAsync(GameLobby lobby) => UniTask.CompletedTask;
        }

        private IEnumerator StartHost(bool sceneManagement)
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                EnableSceneManagement = sceneManagement
            };
            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            _gameManager.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _lobbyState = ScriptableObject.CreateInstance<LobbyState>();
            _lobbyState.gameManager = _gameManager;

            // The lobby the menu created: locked until GameScene is loaded.
            _lobbyManagerGo = new GameObject("LobbyManager");
            LobbyManager _lobbyManager = _lobbyManagerGo.AddComponent<LobbyManager>();
            _backend = new FakeLobbyBackend { Locked = true };
            _lobby = new GameLobby { Id = "lobby", IsLocked = true };
            ReflectionHelper.SetPrivateField(_lobbyManager, "backend", _backend);
            ReflectionHelper.SetPrivateField(_lobbyManager, "currentLobby", _lobby);

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening.");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            if (_lobbyManagerGo != null) Object.Destroy(_lobbyManagerGo);
            if (_lobbyState != null) Object.Destroy(_lobbyState);
            Object.Destroy(_gameManagerGo);
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Lobby_StaysLockedUntilTheSceneLoadEventCompletes()
        {
            yield return StartHost(sceneManagement: true);
            _lobbyState.OnStartStateServer();

            for (int i = 0; i < 5; i++)
            {
                _lobbyState.StateUpdateServer();
                yield return null;
            }
            Assert.AreEqual(0, _backend.UnlockCalls, "The lobby must stay locked while the host is still loading.");
            Assert.IsTrue(_lobby.IsLocked);

            ReflectionHelper.InvokePrivateMethod(_lobbyState, "OnSceneLoadEventCompleted", _gameManagerGo.scene.name,
                UnityEngine.SceneManagement.LoadSceneMode.Single, new List<ulong> { _networkManager.LocalClientId },
                new List<ulong>());

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _backend.Locked == false, 5f,
                "The lobby must open once the host's scene load event completes.");
            Assert.IsFalse(_lobby.IsLocked, "The local copy must learn the change at once, not at the next poll.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator Lobby_WithoutSceneManagement_OpensAtOnce()
        {
            yield return StartHost(sceneManagement: false);
            _lobbyState.OnStartStateServer();

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _backend.Locked == false, 5f,
                "With no scene load to wait for, the lobby must open at once.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator Lobby_FailedUnlock_IsRetried()
        {
            yield return StartHost(sceneManagement: false);
            _backend.UnlockFailuresLeft = 2;
            LogAssert.Expect(LogType.Error, new Regex("rate limited"));
            LogAssert.Expect(LogType.Error, new Regex("rate limited"));
            _lobbyState.OnStartStateServer();

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _backend.Locked == false, 10f,
                "A failed unlock must be retried, or the game stays unjoinable.");
            Assert.AreEqual(3, _backend.UnlockCalls);
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator Lobby_GameStartedDuringRetries_IsNeverReopened()
        {
            yield return StartHost(sceneManagement: false);
            _backend.UnlockFailuresLeft = 1;
            LogAssert.Expect(LogType.Error, new Regex("rate limited"));
            _lobbyState.OnStartStateServer();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _backend.UnlockCalls == 1, 5f, "No unlock attempt.");

            // The game starts before the retry: LobbyState locks the lobby again.
            _lobbyState.OnEndStateServer();

            float _until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < _until) yield return null;
            Assert.AreEqual(1, _backend.UnlockCalls, "A pending retry must not reopen a started game.");
            Assert.AreEqual(true, _backend.Locked);
        }
    }
}
