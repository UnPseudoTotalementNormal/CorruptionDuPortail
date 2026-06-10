using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Characters;
using Characters.Powers;
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
    /// Story 2.1 — losslessness battery + synchrony proof for <see cref="GameSnapshotBuilder.FromLiveState"/>.
    /// Per inventoried field, set a known live value, build the snapshot, assert it round-trips. Includes the
    /// dedicated hackedCharacterClientId-from-live-POmniscience test and the simulated-bot identity test.
    /// Reuses the VictoryConditionTests host harness. Builder is read-only — these tests also implicitly prove
    /// it fires no RPC / mutates no state (the host stays consistent).
    /// </summary>
    [Category("SnapshotBuilder")]
    public class GameSnapshotBuilderLosslessnessTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Snapshot");
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

        // --- helpers ---

        private POmniscience SpawnOmniscience()
        {
            var go = new GameObject("POmniscience_Snapshot");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
        }

        private CharacterSnapshot SnapshotOf(ulong ownerClientId)
        {
            var snapshot = GameSnapshotBuilder.FromLiveState(_gameManager);
            foreach (var c in snapshot.Characters)
            {
                if (c.OwnerClientId == ownerClientId)
                {
                    return c;
                }
            }
            Assert.Fail($"No CharacterSnapshot for ownerClientId {ownerClientId}.");
            return null;
        }

        // --- synchrony proof (AC line 206) ---

        [Test]
        public void FromLiveState_IsSynchronous_NotAsync_ReturnsGameSnapshot()
        {
            MethodInfo method = typeof(GameSnapshotBuilder).GetMethod(
                nameof(GameSnapshotBuilder.FromLiveState), BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "FromLiveState must exist as a public static method.");
            Assert.AreEqual(typeof(GameSnapshot), method.ReturnType,
                "FromLiveState must return GameSnapshot synchronously — never UniTask/Task (no await before capture).");
            Assert.IsNull(method.GetCustomAttribute<AsyncStateMachineAttribute>(),
                "FromLiveState must not be async — a snapshot torn across an await is not a faithful capture.");
        }

        // --- per-field losslessness ---

        [UnityTest]
        public IEnumerator Lossless_OwnerClientId_And_IsFakeFalse_ForRealCharacter()
        {
            Character c = _characterManager.AddNewCharacter(5);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.marginal };

            var snap = SnapshotOf(5);
            Assert.AreEqual(5UL, snap.OwnerClientId);
            Assert.IsFalse(snap.IsFake, "A real (non-sentinel) clientId must map to IsFake == false.");
        }

        [UnityTest]
        public IEnumerator Lossless_IsCorrupted()
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.anomaly };
            c.isCorrupted.Value = true;

            Assert.IsTrue(SnapshotOf(1).IsCorrupted);
        }

        [UnityTest]
        public IEnumerator Lossless_IsChained()
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.anomaly };
            c.isChained.Value = true;

            Assert.IsTrue(SnapshotOf(1).IsChained);
        }

        [UnityTest]
        public IEnumerator Lossless_FactionType()
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.chosen };

            Assert.AreEqual(FactionType.chosen, SnapshotOf(1).FactionType);
        }

        // --- the hackedCharacterClientId trap (AC line 207) ---

        [UnityTest]
        public IEnumerator Lossless_HackTarget_ReadFromLivePOmniscience()
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);
            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role { factionType = FactionType.marginal };
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 777;

            Assert.AreEqual(777UL, SnapshotOf(1).HackedByOmniscienceTarget,
                "Hack target must come from the live POmniscience instance, not a serialized Role.");
        }

        [UnityTest]
        public IEnumerator HackTarget_DefaultsWhenNoPOmniscience()
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);
            c.role = new Role { factionType = FactionType.chosen }; // no powers

            Assert.AreEqual(POmniscience.HACKED_CHARACTER_DEFAULT, SnapshotOf(1).HackedByOmniscienceTarget);
        }

        // --- simulated-bot identity preserved verbatim (AC line 208) ---

        [UnityTest]
        public IEnumerator Lossless_SimulatedBotIdentity_ClientIdAbove100_MapsVerbatim()
        {
            const ulong botId = 123; // simulated-bot range (>= 100), still a real (non-fake) character
            Character bot = _characterManager.AddNewCharacter(botId);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(bot);
            bot.role = new Role { factionType = FactionType.anomaly };

            var snap = SnapshotOf(botId);
            Assert.AreEqual(botId, snap.OwnerClientId, "Bot clientId must map verbatim — not normalized.");
            Assert.AreEqual(bot.isFake, snap.IsFake, "Snapshot IsFake must equal the runtime IsFakeClientId verdict.");
        }

        // --- standalone differential over a golden-style scenario (AC line 209) ---

        [UnityTest]
        public IEnumerator Differential_GoldenScenario_AllFieldsMatchLive()
        {
            Character corrupted = _characterManager.AddNewCharacter(1);
            Character chainedAnomaly = _characterManager.AddNewCharacter(2);
            Character omniOwner = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(corrupted, chainedAnomaly, omniOwner);

            corrupted.role = new Role { factionType = FactionType.anomaly };
            corrupted.isCorrupted.Value = true;

            chainedAnomaly.role = new Role { factionType = FactionType.anomaly };
            chainedAnomaly.isChained.Value = true;

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);
            omniOwner.role = new Role { factionType = FactionType.chosen };
            omniOwner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;

            foreach (var live in _characterManager.GetCharacters(false))
            {
                var snap = SnapshotOf(live.ownerClientId.Value);
                Assert.AreEqual(live.ownerClientId.Value, snap.OwnerClientId);
                Assert.AreEqual(live.isFake, snap.IsFake);
                Assert.AreEqual(live.isCorrupted.Value, snap.IsCorrupted);
                Assert.AreEqual(live.isChained.Value, snap.IsChained);
                Assert.AreEqual(live.role.factionType, snap.FactionType);
            }
        }
    }
}
