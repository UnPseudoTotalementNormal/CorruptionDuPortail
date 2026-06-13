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

namespace Tests.PlayMode.SnapshotOracle
{
    /// <summary>
    /// Story 2.7b — the STANDING ORACLE for the snapshot signature. After the 2.7 cut, production evaluates off the
    /// snapshot; this suite pins CheckCondition(snapshot) against the SAME frozen verdicts Story 1.2/1.5 captured —
    /// directly, NOT against the live pull. This replaces the dual-source (pull-vs-snapshot) differential as the
    /// standing proof, so the net no longer self-confirms via the pull. The dual-source Migration/Differential tests
    /// remain green as a redundant cross-check (the pull is still present) until the pull is finally removed.
    ///
    /// Also: the anti-tautology sentinel is re-pointed onto this oracle, and a kill-test proves the oracle bites on
    /// a deliberately wrong verdict (mirrors the Story 1.0 harness-fidelity pattern).
    /// </summary>
    [Category("SnapshotOracle")]
    public class WinningConditionSnapshotOracleTests
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

        // Test-only mutant: real snapshot logic via base, verdict inverted (for the net-bites kill-test).
        private sealed class InvertingMarginal : WMarginalIsChainedWin
        {
            public override bool CheckCondition(GameSnapshot snapshot) => !base.CheckCondition(snapshot);
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Oracle");
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

        private GameSnapshot Snapshot() => GameSnapshotBuilder.FromLiveState(_gameManager);

        private POmniscience SpawnOmniscience()
        {
            var go = new GameObject("POmniscience_Oracle");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
        }

        // ───────────────── WMarginalIsChainedWin (frozen vectors) ─────────────────

        [UnityTest]
        public IEnumerator Oracle_WMarginal_ChainedOwner_True()
        {
            Character o = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(o);
            o.role = new Role { factionType = FactionType.marginal }; o.isChained.Value = true;

            Assert.IsTrue(new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WMarginal_NotChained_False()
        {
            Character o = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(o);
            o.role = new Role { factionType = FactionType.marginal }; o.isChained.Value = false;

            Assert.IsFalse(new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WMarginal_OwnerAbsent_False() // golden M1 — owner not in the snapshot
        {
            Character other = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(other);
            other.role = new Role { factionType = FactionType.marginal };

            Assert.IsFalse(new WMarginalIsChainedWin { ownerClientId = 999 }.CheckCondition(Snapshot()));
        }

        [Test]
        public void Oracle_WMarginal_FakeOwner_False() // golden M2 — the IsFake short-circuit on the snapshot path
        {
            // Hand-built snapshot pins the !IsFake branch directly (a live fake owner can't be spawned reliably).
            var fakeOwner = new CharacterSnapshot(1, isFake: true, isCorrupted: false, isChained: true, FactionType.marginal, POmniscience.HACKED_CHARACTER_DEFAULT);
            var snapshot = new GameSnapshot(new[] { fakeOwner }, 0, 0);

            Assert.IsFalse(new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snapshot),
                "A fake owner must not win even if chained (the !IsFake term).");
        }

        // ───────────────── WAnomalyCorruption ─────────────────

        [UnityTest]
        public IEnumerator Oracle_WAnomaly_AllCorrupted_True()
        {
            Character a = _characterManager.AddNewCharacter(1);
            Character b = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a, b);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;
            b.role = new Role { factionType = FactionType.chosen }; b.isCorrupted.Value = true;

            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WAnomaly_OneNotCorrupted_False()
        {
            Character a = _characterManager.AddNewCharacter(1);
            Character b = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a, b);
            a.role = new Role { factionType = FactionType.anomaly }; a.isCorrupted.Value = true;
            b.role = new Role { factionType = FactionType.chosen }; b.isCorrupted.Value = false;

            Assert.IsFalse(new WAnomalyCorruption().CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WAnomaly_EmptyPopulation_VacuouslyTrue()
        {
            yield return null;
            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(Snapshot()));
        }

        // ───────────────── WChosenChainedAllAnomaly ─────────────────

        [UnityTest]
        public IEnumerator Oracle_WChosen_ChainedAnomaly_True()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = true;

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WChosen_FreeAnomaly_False()
        {
            Character a = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(a);
            a.role = new Role { factionType = FactionType.anomaly }; a.isChained.Value = false;

            Assert.IsFalse(new WChosenChainedAllAnomaly().CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WChosen_NoAnomaly_VacuouslyTrue()
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.chosen };

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(Snapshot()));
        }

        // ───────────────── WOmniscienceHackedCharacter ─────────────────

        private IEnumerator BuildOmniTrue()
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

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_BaseTrue()
        {
            yield return BuildOmniTrue();
            Assert.IsTrue(new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_HackedNotChosen_False()
        {
            yield return BuildOmniTrue();
            _characterManager.GetCharacter(2).role.factionType = FactionType.anomaly;
            Assert.IsFalse(new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_NoOmniscience_False() // golden O2 — owner has no POmniscience → DEFAULT guard
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.marginal }; // no powers

            Assert.IsFalse(new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_TargetNotFound_False() // golden O4 — hackedId set but absent
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);
            owner.role = new Role { factionType = FactionType.marginal };
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 999;

            Assert.IsFalse(new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_HackedNotChained_False() // golden O5 — first conjunct fails
        {
            yield return BuildOmniTrue();
            _characterManager.GetCharacter(2).isChained.Value = false;
            Assert.IsFalse(new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(Snapshot()));
        }

        [UnityTest]
        public IEnumerator Oracle_WOmniscience_OwnerAbsent_ThrowsNRE()
        {
            Character other = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(other);
            other.role = new Role { factionType = FactionType.marginal };

            GameSnapshot snapshot = Snapshot();
            Assert.Throws<System.NullReferenceException>(
                () => new WOmniscienceHackedCharacter { ownerClientId = 999 }.CheckCondition(snapshot),
                "Owner-absent must throw NRE on the snapshot path (frozen golden O1).");
        }

        // ───────────────── Re-pointed sentinel (anti-tautology) ─────────────────

        [UnityTest]
        public IEnumerator Sentinel_CorruptingReadField_DeviatesFromFrozenVerdict()
        {
            Character o = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(o);
            o.role = new Role { factionType = FactionType.marginal }; o.isChained.Value = true;

            const bool frozenExpected = true; // WMarginal chained owner
            var condition = new WMarginalIsChainedWin { ownerClientId = 1 };
            GameSnapshot clean = Snapshot();
            Assert.AreEqual(frozenExpected, condition.CheckCondition(clean), "Oracle baseline must match the frozen verdict.");

            // Corrupt the IsChained field the condition reads → the snapshot verdict must DEVIATE from the frozen one.
            var corruptedChars = new List<CharacterSnapshot>();
            foreach (var c in clean.Characters)
            {
                corruptedChars.Add(c.OwnerClientId == 1
                    ? new CharacterSnapshot(c.OwnerClientId, c.IsFake, c.IsCorrupted, !c.IsChained, c.FactionType, c.HackedByOmniscienceTarget)
                    : c);
            }
            var corrupted = new GameSnapshot(corruptedChars, clean.Day, clean.CurrentStateIndex);

            // Pin the corrupted verdict concretely (only IsChained was flipped, field-by-field): chained→unchained ⇒ false.
            Assert.IsFalse(condition.CheckCondition(corrupted),
                "Flipping ONLY IsChained must drive the verdict to false — the sentinel pins the exact field, not just any change.");
            Assert.AreNotEqual(frozenExpected, condition.CheckCondition(corrupted),
                "A corrupted snapshot field must make the oracle deviate from its frozen verdict — sentinel is not decorative.");
        }

        // ───────────────── Net-bites kill-test ─────────────────

        [UnityTest]
        public IEnumerator NetBites_InvertedVerdict_FailsTheFrozenGoldenAssertion()
        {
            Character o = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(o);
            o.role = new Role { factionType = FactionType.marginal }; o.isChained.Value = true;

            GameSnapshot snapshot = Snapshot();
            var inverting = new InvertingMarginal { ownerClientId = 1 }; // real logic, verdict inverted → false

            // The standing golden assertion (expected true) MUST fail against a wrong verdict — the net bites.
            Assert.Throws<AssertionException>(
                () => Assert.IsTrue(inverting.CheckCondition(snapshot)),
                "A deliberately inverted verdict must redden the frozen-golden assertion (post-cut net still bites).");
        }
    }
}
