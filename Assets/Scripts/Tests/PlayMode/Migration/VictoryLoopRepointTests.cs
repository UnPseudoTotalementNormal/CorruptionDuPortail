using System.Collections;
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
    /// Story 2.7 — proves the re-pointed victory loop is behavior-preserving. The loop now evaluates each
    /// condition via CheckCondition(snapshot); this test reconstructs the loop's per-(character, condition)
    /// evaluation BOTH ways over a multi-character multi-condition scenario and asserts they agree — so the
    /// aggregate the production loop builds is identical to the legacy pull-based aggregate. The loop's
    /// aggregation logic itself is unchanged, so per-condition equivalence ⇒ loop equivalence.
    /// </summary>
    [Category("Migration")]
    public class VictoryLoopRepointTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Repoint");
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
            var go = new GameObject("POmniscience_Repoint");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
        }

        [UnityTest]
        public IEnumerator RepointedLoop_PerConditionVerdicts_MatchLegacyPull()
        {
            // char 1 — marginal, chained → WMarginalIsChainedWin
            Character c1 = _characterManager.AddNewCharacter(1);
            // char 2 — anomaly, chained, corrupted → WAnomalyCorruption + WChosenChainedAllAnomaly
            Character c2 = _characterManager.AddNewCharacter(2);
            // char 3 — chosen, POmniscience targeting char 2 → WOmniscienceHackedCharacter
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            c1.role = new Role { factionType = FactionType.marginal };
            c1.isChained.Value = true;
            c1.role.winningConditions.Add(new WMarginalIsChainedWin { ownerClientId = 1 });

            c2.role = new Role { factionType = FactionType.anomaly };
            c2.isChained.Value = true;
            c2.isCorrupted.Value = true;
            c2.role.winningConditions.Add(new WAnomalyCorruption { ownerClientId = 2 });
            c2.role.winningConditions.Add(new WChosenChainedAllAnomaly { ownerClientId = 2 });

            c3.role = new Role { factionType = FactionType.chosen };
            c3.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;
            c3.role.winningConditions.Add(new WOmniscienceHackedCharacter { ownerClientId = 3 });

            // The snapshot the re-pointed loop builds once.
            GameSnapshot snapshot = GameSnapshotBuilder.FromLiveState(_gameManager);

            // Reproduce the loop's per-(character, condition) evaluation BOTH ways and assert agreement.
            int compared = 0;
            foreach (var character in _characterManager.GetCharacters(false))
            {
                if (character.isFake)
                {
                    continue;
                }

                foreach (var condition in character.role.winningConditions)
                {
                    bool pull = condition.CheckCondition();
                    bool snap = condition.CheckCondition(snapshot);
                    Assert.AreEqual(pull, snap,
                        $"Re-point disagreement on {condition.GetType().Name} (owner {character.ownerClientId.Value}).");
                    compared++;
                }
            }

            Assert.AreEqual(4, compared, "All four conditions across the scenario must have been compared.");
        }
    }
}
