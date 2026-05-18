using System.Collections;
using Characters;
using Characters.WinningConditions;
using GameLogic;
using AudioSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace Tests.PlayMode
{
    public class VictoryConditionTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;

        private GameObject _dummyCharPrefab;

        private class DummyGameState : GameState 
        {
            public override void StateUpdateClient() {}
            public override void StateUpdateServer() {}
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
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Victory");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            _gameManager.characterManager = _characterManager;
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsTrue_WhenAllCharactersAreCorrupted()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = true;

            var condition = new WAnomalyCorruption();
            Assert.IsTrue(condition.CheckCondition(), "WAnomalyCorruption should return true when all are corrupted");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsFalse_WhenOneCharacterIsNotCorrupted()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = false;

            var condition = new WAnomalyCorruption();
            Assert.IsFalse(condition.CheckCondition(), "WAnomalyCorruption should return false if at least one is NOT corrupted");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenAllAnomaliesAreChained()
        {
            Character anomaly = _characterManager.AddNewCharacter(1);
            Character chosen = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly, chosen);

            anomaly.role = new Role { factionType = FactionType.anomaly };
            chosen.role = new Role { factionType = FactionType.chosen };

            anomaly.isChained.Value = true; // All anomalies chained
            chosen.isChained.Value = false;

            var condition = new WChosenChainedAllAnomaly();
            Assert.IsTrue(condition.CheckCondition(), "WChosenChainedAllAnomaly should return true when all anomalies are chained");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsFalse_WhenOneAnomalyIsNotChained()
        {
            Character anomaly1 = _characterManager.AddNewCharacter(1);
            Character anomaly2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly1, anomaly2);

            anomaly1.role = new Role { factionType = FactionType.anomaly };
            anomaly2.role = new Role { factionType = FactionType.anomaly };

            anomaly1.isChained.Value = true;
            anomaly2.isChained.Value = false; // This anomaly is free

            var condition = new WChosenChainedAllAnomaly();
            Assert.IsFalse(condition.CheckCondition(), "WChosenChainedAllAnomaly should return false if one anomaly is NOT chained");
        }
    }
}
