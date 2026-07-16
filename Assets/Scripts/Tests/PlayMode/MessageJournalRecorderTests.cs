using System.Collections;
using Characters;
using GameLogic;
using MessageSystem;
using NUnit.Framework;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    // Regression guard for the "empty journal at night" bug: the per-turn recorder must APPEND a TurnStat
    // when driven from the awakening recap (MessageManager.RecordCurrentTurnStat), not stay empty because it
    // was tied to a fragile spawn-time state subscription. Also pins Poyo's no-Robot rule.
    public class MessageJournalRecorderTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _revealerGo;
        private GameInfoRevealer _revealer;
        private GameObject _compositionRootGo;
        private GameObject _rtsGo;
        private RoleTargetSystem _roleTargetSystem;
        private GameObject _messageManagerGo;
        private MessageManager _messageManager;
        private GameObject _dummyCharPrefab;

        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Journal");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            _gameManager.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();

            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            _revealerGo = new GameObject("GameInfoRevealer");
            _revealerGo.AddComponent<NetworkObject>();
            _revealer = _revealerGo.AddComponent<GameInfoRevealer>();
            _revealer.GetComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_gameManager, "gameInfoRevealer", _revealer);
            ReflectionHelper.SetPrivateField(_revealer, "characterManager", _characterManager);
            ReflectionHelper.SetPrivateField(_revealer, "gameManager", _gameManager);

            _compositionRootGo = NetworkTestHelper.RegisterCompositionRoot(_gameManager, _characterManager, _revealer);

            _rtsGo = new GameObject("RoleTargetSystem");
            _roleTargetSystem = _rtsGo.AddComponent<RoleTargetSystem>();
            _rtsGo.AddComponent<NetworkObject>().Spawn();

            // MessageManager asserts its gameManager in Awake, so wire it while inactive, then activate + spawn
            // (OnNetworkSpawn resolves CharacterQuery + RoleTargetSystem from the now-registered composition root).
            _messageManagerGo = new GameObject("MessageManager");
            _messageManagerGo.SetActive(false);
            _messageManagerGo.AddComponent<NetworkObject>();
            _messageManager = _messageManagerGo.AddComponent<MessageManager>();
            ReflectionHelper.SetPrivateField(_messageManager, "gameManager", _gameManager);
            ReflectionHelper.SetPrivateField(_messageManager, "characterManager", _characterManager);
            ReflectionHelper.SetPrivateField(_messageManager, "roleTargetSystem", _roleTargetSystem);
            _messageManagerGo.SetActive(true);
            _messageManager.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _messageManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(MessageManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_revealerGo);
            Object.Destroy(_compositionRootGo);
            Object.Destroy(_rtsGo);
            Object.Destroy(_messageManagerGo);
            Object.Destroy(GameObject.Find("CharactersParent"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecordCurrentTurnStat_WithRobot_AppendsStatWithCounts()
        {
            Character corrupted = _characterManager.AddNewCharacter(1);
            Character clean = _characterManager.AddNewCharacter(2);
            Character robot = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(corrupted, clean, robot);

            corrupted.role = new Role { factionType = FactionType.chosen };
            clean.role = new Role { factionType = FactionType.chosen };
            robot.role = new Role { factionType = FactionType.anomaly, roleID = RoleID.Robot };
            corrupted.isCorrupted.Value = true;

            _roleTargetSystem.NewTargeting(2, robot.ownerClientId.Value);
            yield return null;

            Assert.AreEqual(0, _messageManager.turnStats.Count, "precondition: no stat recorded yet");

            _messageManager.RecordCurrentTurnStat();
            yield return null;

            Assert.AreEqual(1, _messageManager.turnStats.Count, "recorder must append exactly one TurnStat");
            TurnStat _stat = _messageManager.turnStats[0];
            Assert.AreEqual(1, _stat.day, "day should be currentDay (1)");
            Assert.AreEqual(2, _stat.nonAnomalyTotal, "two non-anomaly reals (chosen)");
            Assert.AreEqual(1, _stat.corruptedCount, "one corrupted non-anomaly real");
            Assert.IsTrue(_stat.hasRobot, "a Robot is present");
            Assert.AreEqual(1, _stat.robotTargetCount, "one player targeted the Robot");
        }

        [UnityTest]
        public IEnumerator RecordCurrentTurnStat_NoRobot_OmitsRobotClauseViaSentinel()
        {
            Character corrupted = _characterManager.AddNewCharacter(1);
            Character clean = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(corrupted, clean);

            corrupted.role = new Role { factionType = FactionType.chosen };
            clean.role = new Role { factionType = FactionType.chosen };
            corrupted.isCorrupted.Value = true;

            _messageManager.RecordCurrentTurnStat();
            yield return null;

            Assert.AreEqual(1, _messageManager.turnStats.Count, "recorder must append a stat even with no Robot");
            TurnStat _stat = _messageManager.turnStats[0];
            Assert.AreEqual(2, _stat.nonAnomalyTotal);
            Assert.AreEqual(1, _stat.corruptedCount);
            Assert.IsFalse(_stat.hasRobot, "no Robot in the game");
            Assert.AreEqual(-1, _stat.robotTargetCount, "sentinel: robot clause omitted");
        }
    }
}
