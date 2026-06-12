using System.Collections;
using Characters;
using Characters.WinningConditions;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    /// <summary>
    /// HARNESS-FIDELITY GATE (Story 1.0 — first blocking gate of Epic 1).
    ///
    /// Proves the existing <c>StartHost()</c> PlayMode harness (mirrored from
    /// <see cref="VictoryConditionTests"/>) actually traverses the PRODUCTION
    /// <see cref="WinningCondition.CheckCondition"/> code path before any golden
    /// master (Story 1.2 / 1.5) is captured on it.
    ///
    /// Each test does two things on one live NGO state with a KNOWN verdict:
    ///   1. Positive control — assert the real condition returns the known verdict
    ///      (if it fails, the setup never reaches production logic → harness broken).
    ///   2. Kill assertion — assert the golden-style assertion against a test-only
    ///      MUTANT (which runs <c>base.CheckCondition()</c> — real logic — and only
    ///      inverts the verdict) throws <see cref="AssertionException"/> (the net bites).
    ///
    /// DOCUMENTED GATE: if any test here ever goes RED, the harness does NOT
    /// faithfully traverse the condition under test, and Epic 1 golden capture
    /// (Stories 1.2 / 1.5) is BLOCKED until the harness is fixed.
    /// </summary>
    [Category("HarnessFidelity")]
    public class WinningConditionHarnessFidelityTests
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

        // Test-only mutants: traverse the REAL production logic via base.CheckCondition(),
        // invert only the verdict. No production file is modified.
        private sealed class MutantAnomalyCorruption : WAnomalyCorruption
        {
            public override bool CheckCondition() => !base.CheckCondition();
        }

        private sealed class MutantChosenChainedAllAnomaly : WChosenChainedAllAnomaly
        {
            public override bool CheckCondition() => !base.CheckCondition();
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Fidelity");
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

            // Load-bearing: domain reload may be disabled, statics survive across
            // PlayMode sessions — reset or a later test inherits a stale singleton.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Harness_TraversesProductionVerdict_AndCatchesInversion_ForWAnomalyCorruption()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            // Known verdict: all corrupted → WAnomalyCorruption MUST be true.
            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = true;

            // Positive control: harness actually reaches production logic.
            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
                "Harness did not traverse production WAnomalyCorruption — golden capture is unsafe.");

            // Kill assertion: the golden-style assertion bites on an inverted verdict.
            Assert.Throws<AssertionException>(
                () => Assert.IsTrue(new MutantAnomalyCorruption().CheckCondition()),
                "Harness assertion did NOT catch an inverted verdict — harness is hollow, Epic 1 blocked.");
        }

        [UnityTest]
        public IEnumerator Harness_TraversesProductionVerdict_AndCatchesInversion_ForWChosenChainedAllAnomaly()
        {
            Character anomaly = _characterManager.AddNewCharacter(1);
            Character chosen = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly, chosen);

            anomaly.role = new Role { factionType = FactionType.anomaly };
            chosen.role = new Role { factionType = FactionType.chosen };

            // Known verdict: every anomaly chained → WChosenChainedAllAnomaly MUST be true.
            anomaly.isChained.Value = true;
            chosen.isChained.Value = false;

            // Positive control: harness actually reaches production logic.
            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "Harness did not traverse production WChosenChainedAllAnomaly — golden capture is unsafe.");

            // Kill assertion: the golden-style assertion bites on an inverted verdict.
            Assert.Throws<AssertionException>(
                () => Assert.IsTrue(new MutantChosenChainedAllAnomaly().CheckCondition()),
                "Harness assertion did NOT catch an inverted verdict — harness is hollow, Epic 1 blocked.");
        }
    }
}
