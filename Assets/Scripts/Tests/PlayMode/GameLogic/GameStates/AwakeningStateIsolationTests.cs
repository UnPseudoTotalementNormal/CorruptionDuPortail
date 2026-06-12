using System.Collections;
using Characters;
using Characters.WinningConditions;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// DETERMINISM QUARANTINE PROOF (Story 1.1 §3b B — AwakeningState frame-timed RNG).
    ///
    /// AwakeningState's per-frame <c>Random.Range</c> (AwakeningState.cs ~250) is
    /// framerate-dependent and therefore unreproducible. It is QUARANTINED (left in
    /// PlayMode, flagged, ungoldened) rather than fixed in Phase 0. This test proves
    /// the quarantine is *isolated*, not just annotated: it asserts the verdict of
    /// each of the 4 WinningConditions is INVARIANT to <c>Character.isAwakened</c>,
    /// so no Story 1.2 golden can ever depend on awakening state.
    ///
    /// DOCUMENTED GATE: if any assertion here goes RED, a WinningCondition has started
    /// reading awakening state and the §3b B quarantine is BREACHED — Story 1.2 golden
    /// capture is no longer safe until the condition is re-isolated.
    /// </summary>
    [Category("Determinism")]
    public class AwakeningStateIsolationTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Awakening");
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

        [UnityTest]
        public IEnumerator AllWinningConditions_VerdictsAreInvariant_WhenAwakeningStateToggles()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            // A live state with determinate (non-trivial) verdicts for each condition.
            c1.role = new Role { factionType = FactionType.anomaly };
            c2.role = new Role { factionType = FactionType.chosen };
            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = true;   // WAnomalyCorruption → true
            c1.isChained.Value = true;     // WChosenChainedAllAnomaly (all anomalies chained) → true; WMarginal(owner=1) → true
            c2.isChained.Value = false;

            var _conditions = new WinningCondition[]
            {
                new WAnomalyCorruption(),
                new WChosenChainedAllAnomaly(),
                new WMarginalIsChainedWin { ownerClientId = 1 },
                new WOmniscienceHackedCharacter { ownerClientId = 1 }, // no POmniscience power → false
            };

            // Baseline verdicts with awakening OFF (default).
            Assert.IsFalse(c1.isAwakened.Value);
            Assert.IsFalse(c2.isAwakened.Value);
            bool[] _baseline = new bool[_conditions.Length];
            for (int _i = 0; _i < _conditions.Length; _i++)
            {
                _baseline[_i] = _conditions[_i].CheckCondition();
            }

            // Flip awakening ON for every character — verdicts MUST NOT change.
            c1.isAwakened.Value = true;
            c2.isAwakened.Value = true;
            for (int _i = 0; _i < _conditions.Length; _i++)
            {
                Assert.AreEqual(_baseline[_i], _conditions[_i].CheckCondition(),
                    $"{_conditions[_i].GetType().Name} verdict changed when isAwakened was set TRUE — " +
                    "§3b B quarantine breached: a WinningCondition now reads awakening state. " +
                    "Story 1.2 golden capture is unsafe until re-isolated.");
            }

            // Flip back OFF — still invariant (covers both transition directions).
            c1.isAwakened.Value = false;
            c2.isAwakened.Value = false;
            for (int _i = 0; _i < _conditions.Length; _i++)
            {
                Assert.AreEqual(_baseline[_i], _conditions[_i].CheckCondition(),
                    $"{_conditions[_i].GetType().Name} verdict changed when isAwakened was reset FALSE — " +
                    "§3b B quarantine breached.");
            }
        }
    }
}
