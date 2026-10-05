using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Catalog scenario 179 (the F-review's "most dangerous gap"): a client that joins AFTER the host has already
    /// spawned characters receives a pre-populated `networkedCharacters` NetworkList via NGO's initial-sync — the
    /// exact path where NGO #3280 can deliver a same-tick entry twice. The base MultiClientGameFixture connects the
    /// client BEFORE any spawn, so it never exercises this; this hand-rolled 2-NM harness deliberately reorders to
    /// spawn N characters, THEN StartClient. Asserts the [CHARLIST] self-healing projection still yields exactly N
    /// distinct seats on the late-joining client (RebuildCharactersCache, CharacterManager.cs:150-184).
    ///
    /// Standalone (does not derive MultiClientGameFixture) precisely because the connection ORDER is the point.
    /// UnityTransport loopback, host created first keeps NetworkManager.Singleton — same substrate as the proven
    /// OwnerLocalEffectBoundaryTests harness.
    /// </summary>
    public class LateJoinerCharacterListTests
    {
        private const uint GmPrefabHash = 0xC0DE0501u;
        private const uint CmPrefabHash = 0xC0DE0502u;
        private const uint CharacterPrefabHash = 0xC0DE0503u;
        private const ushort LoopbackPort = 7794;

        private GameObject _gmPrefabGo, _cmPrefabGo, _characterPrefabGo;
        private NetworkObject _gmPrefabNo, _cmPrefabNo, _characterPrefabNo;
        private GameState _seededState;

        private GameObject _hostNmGo, _clientNmGo;
        private NetworkManager _hostNm, _clientNm;
        private GameManager _hostGm;
        private CharacterManager _hostCm;

        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gmPrefabGo = MakePrefab("LateJoinGmPrefab", GmPrefabHash, go =>
            {
                var gm = go.AddComponent<GameManager>();
                gm.ignoreGameLoop = true;
                _seededState = ScriptableObject.CreateInstance<DummyGameState>();
                gm.gameStates.Add(_seededState, new GameStateSettings());
            });
            _gmPrefabNo = _gmPrefabGo.GetComponent<NetworkObject>();
            _cmPrefabGo = MakePrefab("LateJoinCmPrefab", CmPrefabHash, go => go.AddComponent<CharacterManager>());
            _cmPrefabNo = _cmPrefabGo.GetComponent<NetworkObject>();
            _characterPrefabGo = MakePrefab("LateJoinCharacterPrefab", CharacterPrefabHash, go => go.AddComponent<Character>());
            _characterPrefabNo = _characterPrefabGo.GetComponent<NetworkObject>();

            _hostNmGo = new GameObject("LateJoinHostNM");
            _hostNm = _hostNmGo.AddComponent<NetworkManager>();
            ConfigureNm(_hostNm, _hostNmGo);
            _clientNmGo = new GameObject("LateJoinClientNM");
            _clientNm = _clientNmGo.AddComponent<NetworkManager>();
            ConfigureNm(_clientNm, _clientNmGo);

            Assert.IsTrue(NetworkManager.Singleton == _hostNm, "Host NM (first) must own the Singleton.");
            ResetManagerStatics();

            Assert.IsTrue(_hostNm.StartHost(), "StartHost failed.");

            _hostGm = _hostNm.SpawnManager.InstantiateAndSpawn(_gmPrefabNo, destroyWithScene: true).GetComponent<GameManager>();
            _hostCm = _hostNm.SpawnManager.InstantiateAndSpawn(_cmPrefabNo, destroyWithScene: true).GetComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_hostGm, "characterManager", _hostCm);
            ReflectionHelper.SetPrivateField(_hostCm, "_characterPrefab", _characterPrefabNo);
            ReflectionHelper.SetPrivateField(_hostCm, "_charactersParent", _hostCm.transform);

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, _hostGm, _hostCm);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_clientNm != null && _clientNm.IsListening) _clientNm.Shutdown();
            if (_hostNm != null && _hostNm.IsListening) _hostNm.Shutdown();
            // Wait WITHOUT asserting — ignoreFailingMessages is process-wide and must be restored before
            // anything here can throw, otherwise every remaining PlayMode test stops failing on error logs.
            bool stoppedListening = false;
            yield return NetworkTestHelper.WaitUntilOrElapsed(
                () => (_clientNm == null || !_clientNm.IsListening) && (_hostNm == null || !_hostNm.IsListening),
                5f, ok => stoppedListening = ok);
            LogAssert.ignoreFailingMessages = false;
            Assert.IsTrue(stoppedListening, "NGO did not stop listening after Shutdown().");

            foreach (var go in new[] { _clientNmGo, _hostNmGo, _gmPrefabGo, _cmPrefabGo, _characterPrefabGo })
                if (go != null) Object.Destroy(go);
            if (_seededState != null) Object.Destroy(_seededState);
            ResetManagerStatics();
            yield return null;
        }

        // Scenario 179: spawn 3 seats on the host BEFORE the client connects, then join and assert the late-joiner
        // projects each seat exactly once (the [CHARLIST] guard absorbs any #3280 double-delivery).
        [UnityTest]
        public IEnumerator LateJoiner_ReceivesEachSeatExactlyOnce_CharlistGuardHolds()
        {
            // --- Populate the roster BEFORE StartClient (the initial-sync path #3280 rides).
            _hostCm.AddNewCharacter(1);
            _hostCm.AddNewCharacter(2);
            _hostCm.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostCm.GetCharacters(false).Count == 3, 5f,
                "Host never projected all 3 pre-spawned characters.");

            // --- NOW the client joins: it receives a pre-populated networkedCharacters via initial-sync.
            // If NGO #3280 actually double-delivers here, the [CHARLIST] guard drops the dup and logs ONE
            // Debug.LogError — the exact scenario this test absorbs. UTF would fail the test on that
            // unexpected error even though every assertion holds, so failing logs are ignored for the
            // sync/settle window only (the explicit assertions below are unaffected by this flag).
            LogAssert.ignoreFailingMessages = true;
            Assert.IsTrue(_clientNm.StartClient(), "StartClient failed.");
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => CharacterManager.For(_clientNm) != null, 10f,
                "Client CharacterManager replica never registered.");
            CharacterManager clientCm = CharacterManager.For(_clientNm);

            // The projection (RebuildCharactersCache) self-heals any duplicate list entry, so the client must
            // settle on exactly 3 projected characters — even if the raw NetworkList momentarily carried a #3280
            // dup. (Count is meaningful even though RebuildCache resolves via Singleton=host: a dup entry resolves
            // to the SAME object and is deduped by reference.)
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => clientCm.GetCharacters(false).Count == 3, 10f, 3,
                "Late-joiner never settled on exactly 3 projected characters (charlist guard failed?).");
            LogAssert.ignoreFailingMessages = false;

            // Now verify the CLIENT's OWN replicas: read the client's raw networkedCharacters and resolve each
            // entry against the CLIENT NM explicitly — GetCharacters/RebuildCache uses TryGet WITHOUT a NM, which
            // resolves against Singleton=host (reference_char_replica_resolution_multi_nm). A #3280 duplicate entry
            // resolves to the same client replica, so the seat set still dedups to exactly {1,2,3}.
            // NET-04: the replicated list is now a full-value snapshot of NetworkObject ids.
            IReadOnlyList<ulong> rawList = clientCm.ReplicatedCharacterObjectIds;
            Assert.IsNotNull(rawList, "Could not read the client CM's replicated character ids.");
            var seats = new HashSet<ulong>();
            foreach (ulong objectId in rawList)
            {
                Assert.IsTrue(_clientNm.SpawnManager.SpawnedObjects.TryGetValue(objectId, out NetworkObject no),
                    "A client list entry did not resolve against the CLIENT NetworkManager.");
                Character c = no.GetComponent<Character>();
                Assert.AreSame(_clientNm, c.NetworkManager,
                    "A resolved roster entry is not a client-side replica — the projection leaked a host object.");
                seats.Add(c.ownerClientId.Value);
            }
            CollectionAssert.AreEquivalent(new ulong[] { 1, 2, 3 }, seats,
                "The late-joiner must hold exactly the three spawned seats, once each.");
        }

        // --- helpers (OwnerLocalEffectBoundaryTests substrate) ---

        private GameObject MakePrefab(string name, uint hash, System.Action<GameObject> addComponents)
        {
            var go = new GameObject(name);
            var no = go.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(no, hash);
            MarkAsNonSceneObject(no);
            addComponents(go);
            return go;
        }

        private void ConfigureNm(NetworkManager nm, GameObject go)
        {
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", LoopbackPort);
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _gmPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _cmPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _characterPrefabGo });
        }

        private static void SetGlobalObjectIdHash(NetworkObject no, uint hash) =>
            typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(no, hash);

        private static void MarkAsNonSceneObject(NetworkObject no) =>
            typeof(NetworkObject).GetProperty("IsSceneObject",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(no, (bool?)false);

        private static void ResetManagerStatics()
        {
            typeof(CharacterManager)
                .GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
            typeof(GameManager)
                .GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
        }
    }
}
