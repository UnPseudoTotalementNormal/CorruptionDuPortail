using System.Collections;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
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
    /// Story 2.6 — migration proof for WOmniscienceHackedCharacter (hardest: 4-way conjunction + double lookup
    /// + the golden O1 no-owner-guard NRE). Four conjunction-term sentinels prove no term is silently dead.
    /// Field-read trace: owner HackedByOmniscienceTarget; hacked IsChained + FactionType; OwnerClientId key —
    /// all covered by Story 2.1 losslessness (incl. the dedicated live-POmniscience test).
    /// </summary>
    [Category("Migration")]
    public class WOmniscienceMigrationTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Omni");
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
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
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

        private POmniscience SpawnOmniscience()
        {
            var go = new GameObject("POmniscience_Omni");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
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

        private CharacterSnapshot Owner(GameSnapshot snapshot, ulong ownerId)
        {
            foreach (var c in snapshot.Characters)
            {
                if (c.OwnerClientId == ownerId) return c;
            }
            Assert.Fail($"No snapshot for owner {ownerId}");
            return null;
        }

        private static CharacterSnapshot WithHack(CharacterSnapshot r, ulong hackTarget) =>
            new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, r.IsChained, r.FactionType, hackTarget);

        private static CharacterSnapshot WithChained(CharacterSnapshot r, bool isChained) =>
            new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, isChained, r.FactionType, r.HackedByOmniscienceTarget);

        private static CharacterSnapshot WithFaction(CharacterSnapshot r, FactionType faction) =>
            new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, r.IsChained, faction, r.HackedByOmniscienceTarget);

        /// <summary>Builds the base TRUE scenario: owner(1) + POmniscience targeting char(2), char(2) chained+chosen.</summary>
        private IEnumerator BuildTrueScenario()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            Character hacked = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, hacked);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role { factionType = FactionType.marginal };
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;

            hacked.role = new Role { factionType = FactionType.chosen };
            hacked.isChained.Value = true;
        }

        // --- full-matrix differential ---

        [UnityTest]
        public IEnumerator Differential_BaseTrue_Agrees()
        {
            yield return BuildTrueScenario();
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition(), "Sanity: base scenario is true.");
            SnapshotDifferential.AssertAgrees(condition, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_TargetDefault_Agrees_False()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.role.powers.Add(omni); // hackedCharacterClientId left DEFAULT

            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_NoOmniscience_Agrees_False()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal }; // no powers

            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_TargetNotFound_Agrees_False()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 999; // no such character (not-found branch)

            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_HackedNotChained_Agrees_False()
        {
            yield return BuildTrueScenario();
            _characterManager.GetCharacter(2).isChained.Value = false;

            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_HackedNotChosen_Agrees_False()
        {
            yield return BuildTrueScenario();
            _characterManager.GetCharacter(2).role.factionType = FactionType.anomaly;

            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 1 }, _gameManager);
        }

        // --- O1: owner not found → BOTH throw NRE (golden parity) ---

        [UnityTest]
        public IEnumerator O1_OwnerAbsent_BothThrowNullReference()
        {
            Character other = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(other);
            other.role = new Role { factionType = FactionType.marginal };

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 999 }; // absent
            GameSnapshot snapshot = GameSnapshotBuilder.FromLiveState(_gameManager);

            Assert.Throws<System.NullReferenceException>(() => condition.CheckCondition(), "Pull throws when owner absent (golden O1).");
            Assert.Throws<System.NullReferenceException>(() => condition.CheckCondition(snapshot), "Snapshot override must mirror the O1 NRE.");
        }

        // --- four conjunction-term sentinels (corrupt the base TRUE scenario) ---

        [UnityTest]
        public IEnumerator Sentinel_Term1_TargetWasSet_Bites()
        {
            yield return BuildTrueScenario();
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition());

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 1, WithHack(Owner(clean, 1), POmniscience.HACKED_CHARACTER_DEFAULT));
            Assert.IsFalse(condition.CheckCondition(mutated), "Term 1: target → DEFAULT must turn the verdict false.");
        }

        [UnityTest]
        public IEnumerator Sentinel_Term2_TargetExists_Bites_NotFoundBranch()
        {
            yield return BuildTrueScenario();
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition());

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 1, WithHack(Owner(clean, 1), 888)); // absent id → not-found branch
            Assert.IsFalse(condition.CheckCondition(mutated), "Term 2: target → absent id (not-found) must turn the verdict false.");
        }

        [UnityTest]
        public IEnumerator Sentinel_Term3_TargetChained_Bites()
        {
            yield return BuildTrueScenario();
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition());

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 2, WithChained(Owner(clean, 2), false));
            Assert.IsFalse(condition.CheckCondition(mutated), "Term 3: hacked not chained must turn the verdict false.");
        }

        [UnityTest]
        public IEnumerator Sentinel_Term4_TargetChosen_Bites()
        {
            yield return BuildTrueScenario();
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition());

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            var mutated = ReplaceByOwner(clean, 2, WithFaction(Owner(clean, 2), FactionType.anomaly));
            Assert.IsFalse(condition.CheckCondition(mutated), "Term 4: hacked not chosen must turn the verdict false.");
        }
    }
}
