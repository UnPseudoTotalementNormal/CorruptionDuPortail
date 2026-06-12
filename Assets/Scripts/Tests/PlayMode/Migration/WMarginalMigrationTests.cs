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
    /// Story 2.3 — migration proof for WMarginalIsChainedWin (the regime reused by 2.4–2.6).
    ///
    /// FIELD-READ TRACE (code-verified against WMarginalIsChainedWin.cs:16,17,22 snapshot override):
    ///   reads exactly OwnerClientId (match key), IsFake, IsChained — all 3 covered by Story 2.1 losslessness tests.
    /// Full-matrix differential (chained / not-chained / absent) proves pull == snapshot.
    /// Per-field mutation-sentinel proves each read field flips the verdict, and a non-read field is inert.
    /// </summary>
    [Category("Migration")]
    public class WMarginalMigrationTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Marg");
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

        // --- full-matrix differential ---

        [UnityTest]
        public IEnumerator Differential_OwnerChained_Agrees()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.isChained.Value = true;

            SnapshotDifferential.AssertAgrees(new WMarginalIsChainedWin { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_OwnerNotChained_Agrees()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.isChained.Value = false;

            SnapshotDifferential.AssertAgrees(new WMarginalIsChainedWin { ownerClientId = 1 }, _gameManager);
        }

        [UnityTest]
        public IEnumerator Differential_OwnerAbsent_Agrees()
        {
            Character other = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(other);
            other.role = new Role { factionType = FactionType.marginal };

            // Condition owner id 999 matches no character → both pull and snapshot return false.
            SnapshotDifferential.AssertAgrees(new WMarginalIsChainedWin { ownerClientId = 999 }, _gameManager);
        }

        // --- per-condition mutation-sentinel ---

        private GameSnapshot ReplaceOwner(GameSnapshot snapshot, ulong ownerId, CharacterSnapshot replacement)
        {
            var list = new List<CharacterSnapshot>();
            foreach (var c in snapshot.Characters)
            {
                list.Add(c.OwnerClientId == ownerId ? replacement : c);
            }
            return new GameSnapshot(list, snapshot.Day, snapshot.CurrentStateIndex);
        }

        [UnityTest]
        public IEnumerator Sentinel_EachReadField_FlipsVerdict_NonReadField_Inert()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.isChained.Value = true; // live pull verdict = true

            var condition = new WMarginalIsChainedWin { ownerClientId = 1 };
            bool liveVerdict = condition.CheckCondition();
            Assert.IsTrue(liveVerdict, "Owner chained → live verdict true.");

            GameSnapshot clean = GameSnapshotBuilder.FromLiveState(_gameManager);
            CharacterSnapshot ownerSnap = null;
            foreach (var c in clean.Characters)
            {
                if (c.OwnerClientId == 1) ownerSnap = c;
            }
            Assert.IsNotNull(ownerSnap);

            var readFields = new HashSet<string> { "OwnerClientId", "IsFake", "IsChained" };
            bool sawInert = false;

            foreach (var (field, corrupted) in SnapshotDifferential.SingleFieldCorruptions(ownerSnap))
            {
                GameSnapshot mutated = field == "OwnerClientId"
                    ? new GameSnapshot(WithReplaced(clean, ownerSnap, corrupted), clean.Day, clean.CurrentStateIndex)
                    : ReplaceOwner(clean, 1, corrupted);

                bool mutatedVerdict = condition.CheckCondition(mutated);

                if (readFields.Contains(field))
                {
                    Assert.AreNotEqual(liveVerdict, mutatedVerdict,
                        $"Corrupting read field '{field}' must flip the snapshot verdict (sentinel bites).");
                }
                else
                {
                    Assert.AreEqual(liveVerdict, mutatedVerdict,
                        $"Corrupting non-read field '{field}' must NOT change the verdict (sentinel is specific).");
                    sawInert = true;
                }
            }

            Assert.IsTrue(sawInert, "At least one non-read field must be proven inert.");
        }

        // For OwnerClientId corruption the replacement no longer matches id 1; replace by reference identity instead.
        private static List<CharacterSnapshot> WithReplaced(GameSnapshot snapshot, CharacterSnapshot original, CharacterSnapshot replacement)
        {
            var list = new List<CharacterSnapshot>();
            foreach (var c in snapshot.Characters)
            {
                list.Add(ReferenceEquals(c, original) ? replacement : c);
            }
            return list;
        }
    }
}
