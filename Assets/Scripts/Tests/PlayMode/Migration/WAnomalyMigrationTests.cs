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
    /// Story 2.4 — migration proof for WAnomalyCorruption.
    /// FIELD-READ TRACE (WAnomalyCorruption.cs snapshot override): reads exactly IsFake (filter) and IsCorrupted,
    /// both covered by Story 2.1 losslessness. Population-wide (no OwnerClientId read → OwnerClientId is inert).
    /// </summary>
    [Category("Migration")]
    public class WAnomalyMigrationTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Anom");
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

        private static CharacterSnapshot With(CharacterSnapshot r, bool? isFake = null, bool? isCorrupted = null, ulong? ownerId = null) =>
            new CharacterSnapshot(ownerId ?? r.OwnerClientId, isFake ?? r.IsFake, isCorrupted ?? r.IsCorrupted, r.IsChained, r.FactionType, r.HackedByOmniscienceTarget);

        // --- full-matrix differential ---

        [UnityTest]
        public IEnumerator Differential_AllCorrupted_Agrees_True()
        {
            Character a = _characterManager.AddNewCharacter(1);
            Character b = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a, b);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;
            b.role = new Role { factionType = FactionType.chosen }; b.isCorrupted.Value = true;

            var condition = new WAnomalyCorruption();
            Assert.IsTrue(condition.CheckCondition(), "Sanity: all corrupted → true.");
            SnapshotDifferential.AssertAgrees(condition, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_OneNotCorrupted_Agrees_False()
        {
            Character a = _characterManager.AddNewCharacter(1);
            Character b = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a, b);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;
            b.role = new Role { factionType = FactionType.chosen }; b.isCorrupted.Value = false;

            SnapshotDifferential.AssertAgrees(new WAnomalyCorruption(), _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_EmptyPopulation_Agrees_VacuouslyTrue()
        {
            yield return null; // no characters spawned
            var condition = new WAnomalyCorruption();
            Assert.IsTrue(condition.CheckCondition(), "Vacuously true over empty non-fake population.");
            SnapshotDifferential.AssertAgrees(condition, _gameManager);
        }

        // --- per-field mutation-sentinel (targeted scenarios) ---

        [UnityTest]
        public IEnumerator Sentinel_IsCorrupted_Bites()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;

            var condition = new WAnomalyCorruption();
            bool live = condition.CheckCondition();
            Assert.IsTrue(live);

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var corrupted = ReplaceByOwner(clean, 1, With(FindOwner(clean, 1), isCorrupted: false));
            Assert.AreNotEqual(live, condition.CheckCondition(corrupted), "Corrupting IsCorrupted must flip the verdict.");
        }

        [UnityTest]
        public IEnumerator Sentinel_IsFake_Bites()
        {
            Character a = _characterManager.AddNewCharacter(1);
            Character b = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a, b);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;
            b.role = new Role { factionType = FactionType.chosen }; b.isCorrupted.Value = false; // verdict false

            var condition = new WAnomalyCorruption();
            bool live = condition.CheckCondition();
            Assert.IsFalse(live);

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            // Excluding the only non-corrupted character (B) by making it fake → verdict becomes true.
            var corrupted = ReplaceByOwner(clean, 2, With(FindOwner(clean, 2), isFake: true));
            Assert.AreNotEqual(live, condition.CheckCondition(corrupted), "Corrupting IsFake (excluding the non-corrupted char) must flip the verdict.");
        }

        [UnityTest]
        public IEnumerator Sentinel_OwnerClientId_IsInert()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;

            var condition = new WAnomalyCorruption();
            bool live = condition.CheckCondition();

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var changed = ReplaceByOwner(clean, 1, With(FindOwner(clean, 1), ownerId: 555));
            Assert.AreEqual(live, condition.CheckCondition(changed), "OwnerClientId is not read → inert.");
        }

        private static CharacterSnapshot FindOwner(GameSnapshot snapshot, ulong ownerId)
        {
            foreach (var c in snapshot.Characters)
            {
                if (c.OwnerClientId == ownerId) return c;
            }
            Assert.Fail($"No snapshot for owner {ownerId}");
            return null;
        }
    }
}
