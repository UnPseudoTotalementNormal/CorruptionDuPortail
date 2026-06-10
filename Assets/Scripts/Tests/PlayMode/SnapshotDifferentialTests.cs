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

namespace Tests.PlayMode
{
    /// <summary>
    /// Story 2.2 — proves the dual-signature plumbing: the snapshot overload (still delegating to the pull)
    /// agrees with the pull across the 4 real conditions, and the per-field mutation-sentinel MECHANISM bites
    /// when a condition actually reads the snapshot (proven with a fake snapshot-reading condition).
    /// </summary>
    [Category("Differential")]
    public class SnapshotDifferentialTests
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

        // Fake condition that ACTUALLY reads the snapshot — used to prove the sentinel mechanism bites.
        private class FakeOwnerChainReader : WinningCondition
        {
            public override WinningTeam GetWinningTeam() => WinningTeam.marginal;
            public override bool CheckCondition() => false; // pull baseline (unused in the meta-test)
            public override bool CheckCondition(GameSnapshot snapshot)
            {
                foreach (var c in snapshot.Characters)
                {
                    if (c.OwnerClientId == ownerClientId)
                    {
                        return c.IsChained;
                    }
                }
                return false;
            }
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Diff");
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

        private POmniscience SpawnOmniscience()
        {
            var go = new GameObject("POmniscience_Diff");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
        }

        // --- T4: the differential agrees for the 4 real conditions (delegating overload) ---

        [UnityTest]
        public IEnumerator Differential_AllFourConditions_Agree()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            c1.role = new Role { factionType = FactionType.marginal };
            c1.isChained.Value = true;
            c2.role = new Role { factionType = FactionType.anomaly };
            c2.isCorrupted.Value = true;

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);
            c3.role = new Role { factionType = FactionType.chosen };
            c3.role.powers.Add(omni);

            SnapshotDifferential.AssertAgrees(new WMarginalIsChainedWin { ownerClientId = 1 }, _gameManager);
            SnapshotDifferential.AssertAgrees(new WAnomalyCorruption(), _gameManager);
            SnapshotDifferential.AssertAgrees(new WChosenChainedAllAnomaly(), _gameManager);
            SnapshotDifferential.AssertAgrees(new WOmniscienceHackedCharacter { ownerClientId = 3 }, _gameManager);
        }

        // --- T5: the sentinel mechanism bites when a condition reads the corrupted field ---

        [UnityTest]
        public IEnumerator MutationSentinel_BitesWhenConditionReadsCorruptedField()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.isChained.Value = true;

            var reader = new FakeOwnerChainReader { ownerClientId = 1 };
            GameSnapshot snapshot = GameSnapshotBuilder.FromLiveState(_gameManager);

            bool referenceVerdict = reader.CheckCondition(snapshot);
            Assert.IsTrue(referenceVerdict, "Owner is chained → fake reader returns true on the clean snapshot.");

            // Corrupt only IsChained on the owner's CharacterSnapshot and re-evaluate.
            var corruptedChars = new List<CharacterSnapshot>();
            foreach (var c in snapshot.Characters)
            {
                if (c.OwnerClientId == 1)
                {
                    foreach (var (field, corrupted) in SnapshotDifferential.SingleFieldCorruptions(c))
                    {
                        if (field == "IsChained")
                        {
                            corruptedChars.Add(corrupted);
                        }
                    }
                }
                else
                {
                    corruptedChars.Add(c);
                }
            }

            var corruptedSnapshot = new GameSnapshot(corruptedChars, snapshot.Day, snapshot.CurrentStateIndex);
            bool corruptedVerdict = reader.CheckCondition(corruptedSnapshot);

            Assert.AreNotEqual(referenceVerdict, corruptedVerdict,
                "Corrupting the IsChained field the condition reads must flip its snapshot verdict — the sentinel bites.");
        }
    }
}
