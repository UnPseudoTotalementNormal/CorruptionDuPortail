using System.Collections;
using System.Collections.Generic;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// [LIVENESS B2] Locks THE #1 risk of the chantier (arch-liveness-heartbeat §4/§8.7): with two ignition
    /// sources now calling <see cref="GameManager.HandlePlayerLeft"/> (liveness ~5s + transport backstop ~12s),
    /// the SECOND call for the same client must be a TOTAL no-op — no double-chain, no double win-check, no NRE.
    /// Both sources call the identical public method, so calling it twice IS "both orders" (the pipeline is
    /// source-agnostic). Driven directly on a live host (the closest to "no real network" that still gives a
    /// real server context — <c>IsServer</c> gates the handler), mirroring the self-contained LeaveUnblockSeam
    /// harness. The tracker-level exactly-once guarantee is covered SEPARATELY by the EditMode
    /// LivenessTracker battery (§8.2); this proves the handler-level idempotency.
    /// </summary>
    [Category("Desingleton")]
    public class PlayerLeaveIdempotencyTests
    {
        private const string ChainingLogFragment = "chaining instantly";
        private const string BenignUtpSocketNoise = "socket receive requests were marked as failed";

        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _charactersParentGo;
        private GameObject _dummyCharPrefab;

        private readonly List<GameState> _seededStates = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            // Bind the loopback socket to 7788 (like the seam fixture), NOT the default 7777 — the known
            // port-7777 bind flake mass-fails StartHost when a prior test's socket lingers. Isolating the port
            // keeps this host-only idempotency fixture off that contended port.
            var _transport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            _transport.SetConnectionData("127.0.0.1", 7788);
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _transport,
                EnableSceneManagement = false
            };

            _dummyCharPrefab = new GameObject("CharacterPrefab_Idempotency");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;

            // Index 0 is a NON-lobby state, so HandlePlayerLeft takes the mid-game (chaining) branch. No victory
            // state seeded → TryResolveVictoryAfterLeave returns false; no ChainingManager → the instant-chain
            // fallback applies ChainCharacterServer directly. Both are NRE-free in this minimal substrate.
            AddSeededState(ScriptableObject.CreateInstance<SeamDummyState>());
            AddSeededState(ScriptableObject.CreateInstance<SeamDummyState>());

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();

            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);

            _gameManager.GetComponent<NetworkObject>().Spawn();
            _characterManager.GetComponent<NetworkObject>().Spawn();

            _charactersParentGo = new GameObject("CharactersParent");
            _charactersParentGo.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", _charactersParentGo.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        private void AddSeededState(GameState _state)
        {
            _seededStates.Add(_state);
            _gameManager.gameStates.Add(_state, new GameStateSettings { isInGameLoop = false });
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            if (_gameManagerGo != null) Object.Destroy(_gameManagerGo);
            if (_characterManagerGo != null) Object.Destroy(_characterManagerGo);
            if (_charactersParentGo != null) Object.Destroy(_charactersParentGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);
            if (_dummyCharPrefab != null) Object.Destroy(_dummyCharPrefab);

            foreach (var _state in _seededStates)
            {
                if (_state != null) Object.Destroy(_state);
            }
            _seededStates.Clear();
            yield return null;
        }

        private IEnumerator SpawnCharacter(ulong _ownerId, Role _role, System.Action<Character> _onSpawned)
        {
            Character _character = _characterManager.AddNewCharacter(_ownerId);
            Assert.IsNotNull(_character, $"AddNewCharacter returned null for id {_ownerId}.");
            _character.role = _role;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_character, 5f);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(_ownerId, false) != null, 5f,
                $"CharacterManager never projected the spawned Character for id {_ownerId}.");
            _onSpawned(_character);
        }

        // ─────────────────────────── mid-game leaver: no double-chain ───────────────────────────

        [UnityTest]
        public IEnumerator HandlePlayerLeft_CalledTwice_MidGameLeaver_ChainsOnce_NoError()
        {
            Character _leaver = null;
            yield return SpawnCharacter(1, new Role { factionType = FactionType.anomaly, roleName = "Leaver" }, _s => _leaver = _s);
            Assert.IsNotNull(_leaver);

            var _errors = new List<string>();
            int _chainingLogCount = 0;
            Application.LogCallback _handler = (condition, stackTrace, type) =>
            {
                if (condition != null && condition.Contains(ChainingLogFragment)) _chainingLogCount++;
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (condition != null && condition.Contains(BenignUtpSocketNoise)) return;
                _errors.Add($"{type}: {condition}");
            };

            bool _prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += _handler;
            try
            {
                _gameManager.HandlePlayerLeft(1); // first ignition source
                _gameManager.HandlePlayerLeft(1); // redundant second source — must be a total no-op
                yield return null;
            }
            finally
            {
                Application.logMessageReceived -= _handler;
                LogAssert.ignoreFailingMessages = _prevIgnore;
            }

            Assert.IsTrue(_leaver.isChained.Value, "The mid-game leaver must be chained by the first call.");
            Assert.AreEqual(1, _chainingLogCount,
                "The chaining path must run EXACTLY ONCE across two calls — the second call is a no-op (no double-chain).");
            Assert.IsTrue(_gameManager.HasClientLeft(1), "The leaver must be recorded in the departed-set.");
            Assert.IsEmpty(_errors, "The redundant leave must not log any server-side error:\n" + string.Join("\n", _errors));
        }

        // ─────────────────────── redundant call, no owned Character: no NRE ───────────────────────

        [UnityTest]
        public IEnumerator HandlePlayerLeft_CalledTwice_LeaverWithNoCharacter_IsNoOp_NoNre()
        {
            var _errors = new List<string>();
            Application.LogCallback _handler = (condition, stackTrace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (condition != null && condition.Contains(BenignUtpSocketNoise)) return;
                _errors.Add($"{type}: {condition}");
            };

            bool _prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += _handler;
            try
            {
                // Id 5 owns no Character — the pipeline records the departure and finds nothing to chain (the
                // "no live character" branch), and the second call short-circuits at the idempotency guard.
                _gameManager.HandlePlayerLeft(5);
                _gameManager.HandlePlayerLeft(5);
                yield return null;
            }
            finally
            {
                Application.logMessageReceived -= _handler;
                LogAssert.ignoreFailingMessages = _prevIgnore;
            }

            Assert.IsTrue(_gameManager.HasClientLeft(5), "Even with no Character, the departure must be recorded once.");
            Assert.IsEmpty(_errors, "A redundant leave for a character-less id must not NRE or log an error:\n" + string.Join("\n", _errors));
        }
    }
}
