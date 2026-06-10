using System.Collections;
using System.Collections.Generic;
using Characters;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using GameLogic;
using GameLogic.Snapshot;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Migration
{
    /// <summary>
    /// Story 2.5 — migration proof for WChosenChainedAllAnomaly.
    /// FIELD-READ TRACE: reads exactly IsFake (filter), FactionType, IsChained — all covered by Story 2.1
    /// losslessness; no newly discovered field (2.1 inventory not reopened). Chaining read backed by Story 1.5.
    /// </summary>
    [Category("Migration")]
    public class WChosenMigrationTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Chosen");
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

        private GameSnapshot ReplaceByOwner(GameSnapshot snapshot, ulong ownerId, CharacterSnapshot replacement)
        {
            var list = new List<CharacterSnapshot>();
            foreach (var c in snapshot.Characters)
            {
                list.Add(c.OwnerClientId == ownerId ? replacement : c);
            }
            return new GameSnapshot(list, snapshot.Day, snapshot.CurrentStateIndex);
        }

        private static CharacterSnapshot With(CharacterSnapshot r, bool? isFake = null, bool? isChained = null, bool? isCorrupted = null, FactionType? faction = null) =>
            new CharacterSnapshot(r.OwnerClientId, isFake ?? r.IsFake, isCorrupted ?? r.IsCorrupted, isChained ?? r.IsChained, faction ?? r.FactionType, r.HackedByOmniscienceTarget);

        private CharacterSnapshot Owner(GameSnapshot snapshot, ulong ownerId)
        {
            foreach (var c in snapshot.Characters)
            {
                if (c.OwnerClientId == ownerId) return c;
            }
            Assert.Fail($"No snapshot for owner {ownerId}");
            return null;
        }

        // --- full-matrix differential ---

        [UnityTest]
        public IEnumerator Differential_SingleChainedAnomaly_Agrees_True()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = true;

            SnapshotDifferential.AssertAgrees(new WChosenChainedAllAnomaly(), _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_SingleFreeAnomaly_Agrees_False()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = false;

            SnapshotDifferential.AssertAgrees(new WChosenChainedAllAnomaly(), _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_NoAnomaly_Agrees_VacuouslyTrue()
        {
            Character chosen = _characterManager.AddNewCharacter(1);
            Character marginal = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(chosen, marginal);
            chosen.role = new Role { factionType = FactionType.chosen };
            marginal.role = new Role { factionType = FactionType.marginal };

            SnapshotDifferential.AssertAgrees(new WChosenChainedAllAnomaly(), _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_EmptyPopulation_Agrees_VacuouslyTrue()
        {
            yield return null;
            SnapshotDifferential.AssertAgrees(new WChosenChainedAllAnomaly(), _gameManager);
        }

        // --- per-field mutation-sentinel ---

        [UnityTest]
        public IEnumerator Sentinel_IsChained_Bites()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = true;

            var condition = new WChosenChainedAllAnomaly();
            bool live = condition.CheckCondition();
            Assert.IsTrue(live);

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 1, With(Owner(clean, 1), isChained: false));
            Assert.AreNotEqual(live, condition.CheckCondition(mutated), "Corrupting IsChained must flip the verdict.");
        }

        [UnityTest]
        public IEnumerator Sentinel_FactionType_Bites()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = false; // free anomaly → false

            var condition = new WChosenChainedAllAnomaly();
            bool live = condition.CheckCondition();
            Assert.IsFalse(live);

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            // Make it non-anomaly → it is continue'd → verdict true.
            var mutated = ReplaceByOwner(clean, 1, With(Owner(clean, 1), faction: FactionType.chosen));
            Assert.AreNotEqual(live, condition.CheckCondition(mutated), "Corrupting FactionType (anomaly→chosen) must flip the verdict.");
        }

        [UnityTest]
        public IEnumerator Sentinel_IsFake_Bites()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = false; // false

            var condition = new WChosenChainedAllAnomaly();
            bool live = condition.CheckCondition();
            Assert.IsFalse(live);

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 1, With(Owner(clean, 1), isFake: true));
            Assert.AreNotEqual(live, condition.CheckCondition(mutated), "Corrupting IsFake (excluding the free anomaly) must flip the verdict.");
        }

        [UnityTest]
        public IEnumerator Sentinel_IsCorrupted_IsInert()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = true;

            var condition = new WChosenChainedAllAnomaly();
            bool live = condition.CheckCondition();

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 1, With(Owner(clean, 1), isCorrupted: true));
            Assert.AreEqual(live, condition.CheckCondition(mutated), "IsCorrupted is not read → inert.");
        }
    }
}
