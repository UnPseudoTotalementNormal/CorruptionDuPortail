using System.Collections;
using System.Reflection;
using Characters;
using GameLogic;
using Network.Action;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Story 5.0e — coexistence gate. Boots TWO NetworkManagers in one process
    /// (host + one real in-process client over UnityTransport loopback) and proves
    /// the de-singletonisation of 5.0a–5.0d is sufficient: the second client's
    /// GameManager/CharacterManager replicas coexist with the host's instead of
    /// clobbering the static `instance` façade.
    ///
    /// Substrate choice (story Dev Notes): Option A — hand-rolled dual NetworkManager.
    /// No Packages/manifest.json change, no cross-package dependency on NGO's own
    /// RuntimeTests. NGO's NetworkManager.Singleton is claimed in OnEnable only when
    /// it is still null (NetworkManager.cs:1024), so the FIRST NM created (the host)
    /// keeps the Singleton and the second (the client) never overwrites it — which is
    /// exactly the façade invariant this gate asserts. Option B was therefore not needed.
    ///
    /// DOCUMENTED EXCEPTION to the "never call StartHost()/StartClient() directly in a
    /// test — route through NetworkTestHelper" project rule: this probe tests the
    /// multi-NetworkManager substrate itself, so it must drive the NMs by hand. Do not
    /// "fix" this back onto the bot-flow harness.
    ///
    /// Production transport is Facepunch (Steam); it is deliberately NOT used here.
    /// UnityTransport loopback only.
    /// </summary>
    public class CoexistenceGateTests
    {
        // Distinct, non-zero prefab hashes. Runtime-created NetworkObjects have a
        // GlobalObjectIdHash of 0; two prefabs sharing 0 collide on the registry's
        // source key (NetworkPrefabs.cs:303), so we force unique values by reflection.
        private const uint GmPrefabHash = 0xC0DE0001u;
        private const uint CmPrefabHash = 0xC0DE0002u;
        private const ushort LoopbackPort = 7787;

        private GameObject _gmPrefabGo;
        private GameObject _cmPrefabGo;

        private GameObject _hostNmGo;
        private NetworkManager _hostNm;
        private GameObject _clientNmGo;
        private NetworkManager _clientNm;

        private GameManager _hostGm;
        private CharacterManager _hostCm;
        private GameManager _clientGm;
        private CharacterManager _clientCm;

        // Minimal GameState so GameManager.OnNetworkSpawn -> GetGameState(0) does not
        // throw on an empty dictionary (it calls OnStartStateServer/Client immediately).
        // No stateUIPrefab => OnStateCreated never touches StatesCanvas.Instance.
        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // --- Build the two network prefab templates (shared by both NMs so their
            // hashes match by construction). They are GameManager/CharacterManager
            // components, so their Awake claims the static instance the moment they
            // are added; we null those statics again before spawning so the REAL
            // spawned host instance exercises the claim-if-free path honestly.
            _gmPrefabGo = new GameObject("CoexistGmPrefab");
            var _gmPrefabNo = _gmPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_gmPrefabNo, GmPrefabHash);
            MarkAsNonSceneObject(_gmPrefabNo);
            var _gmPrefabComp = _gmPrefabGo.AddComponent<GameManager>();
            _gmPrefabComp.ignoreGameLoop = true;
            _gmPrefabComp.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());

            _cmPrefabGo = new GameObject("CoexistCmPrefab");
            var _cmPrefabNo = _cmPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_cmPrefabNo, CmPrefabHash);
            MarkAsNonSceneObject(_cmPrefabNo);
            _cmPrefabGo.AddComponent<CharacterManager>();

            // --- Host NM FIRST: its OnEnable claims NetworkManager.Singleton. ---
            _hostNmGo = new GameObject("CoexistHostNM");
            _hostNm = _hostNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(_hostNm, _hostNmGo, _gmPrefabNo, _cmPrefabNo);

            // --- Client NM SECOND: Singleton is already set, so it stays the host. ---
            _clientNmGo = new GameObject("CoexistClientNM");
            _clientNm = _clientNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(_clientNm, _clientNmGo, _gmPrefabNo, _cmPrefabNo);

            Assert.IsTrue(NetworkManager.Singleton == _hostNm,
                "Host NM (created first) must own NetworkManager.Singleton.");

            // Drop the instance claims the prefab templates' Awake made, so the host's
            // spawned instance claims a free façade like production's scene object does.
            ResetManagerStatics();

            Assert.IsTrue(_hostNm.StartHost(), "NGO StartHost() failed — host did not start.");

            // Spawn the managers on the host (registered network prefabs -> replicate
            // to late-joining clients on connect).
            _hostGm = _hostNm.SpawnManager.InstantiateAndSpawn(_gmPrefabNo, destroyWithScene: true)
                .GetComponent<GameManager>();
            _hostCm = _hostNm.SpawnManager.InstantiateAndSpawn(_cmPrefabNo, destroyWithScene: true)
                .GetComponent<CharacterManager>();

            // Production wiring: GameManager.OnPlayerDisconnectedServer reads characterManager.
            ReflectionHelper.SetPrivateField(_hostGm, "characterManager", _hostCm);

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, _hostGm, _hostCm);

            Assert.IsTrue(_clientNm.StartClient(), "NGO StartClient() failed — client did not start.");

            // Wait for replication by polling For(clientNm), frame by frame, with a cap.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => GameManager.For(_clientNm) != null && CharacterManager.For(_clientNm) != null,
                10f,
                "Client replicas of GameManager/CharacterManager never registered (replication did not complete).");

            _clientGm = GameManager.For(_clientNm);
            _clientCm = CharacterManager.For(_clientNm);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Tearing down two UnityTransport loopback sockets in-process logs a benign
            // "[Error] All socket receive requests were marked as failed" from UTP when
            // one socket closes while the other has a pending receive job. It is shutdown
            // noise (0–2 occurrences, non-deterministic), unrelated to the assertions that
            // already ran in the test body — ignore failing log messages for the teardown
            // window only. Explicit assertions below are unaffected by this flag.
            LogAssert.ignoreFailingMessages = true;

            // Despawn the host-owned managers FIRST so GameManager unsubscribes
            // OnClientDisconnectCallback before the shutdown disconnect sequence runs.
            // Otherwise OnPlayerDisconnectedServer fires and NREs on the absent
            // BoardManager (its body is out of this probe's scope).
            if (_hostNm != null && _hostNm.IsListening)
            {
                if (_hostGm != null && _hostGm.IsSpawned) _hostGm.NetworkObject.Despawn(true);
                if (_hostCm != null && _hostCm.IsSpawned) _hostCm.NetworkObject.Despawn(true);
            }
            yield return null;

            // Tear down BOTH NetworkManagers so the probe never destabilises the rest
            // of the PlayMode suite (a UDP port left bound would fail the next test).
            if (_clientNm != null && _clientNm.IsListening)
            {
                _clientNm.Shutdown();
            }
            if (_hostNm != null && _hostNm.IsListening)
            {
                _hostNm.Shutdown();
            }

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => (_clientNm == null || !_clientNm.IsListening) && (_hostNm == null || !_hostNm.IsListening),
                5f,
                "NGO did not stop listening within 5s after Shutdown().");

            Object.Destroy(_clientNmGo);
            Object.Destroy(_hostNmGo);
            Object.Destroy(_gmPrefabGo);
            Object.Destroy(_cmPrefabGo);

            // Statics must be clean for the next test (domain reload is disabled).
            ResetManagerStatics();

            yield return null;

            // Regression net: the registry self-cleans via OnNetworkDespawn/OnDestroy
            // and Singleton clears when its owner is destroyed (NetworkManager.cs:1655).
            Assert.IsTrue(NetworkManager.Singleton == null,
                "NetworkManager.Singleton must be null after both NMs are torn down.");
            Assert.IsNull(GameManager.For(_hostNm), "GameManager registry leaked a host entry after teardown.");
            Assert.IsNull(CharacterManager.For(_hostNm), "CharacterManager registry leaked a host entry after teardown.");
        }

        // AC 1 + 2: both managers replicated to the second client and NOT destroyed.
        [UnityTest]
        public IEnumerator BothManagers_ReplicateToSecondClient_WithoutDestroyingReplicas()
        {
            Assert.IsNotNull(_clientGm, "Client GameManager replica was destroyed or never registered.");
            Assert.IsNotNull(_clientCm, "Client CharacterManager replica was destroyed or never registered.");
            Assert.IsTrue(_clientGm.IsSpawned, "Client GameManager replica is not spawned/alive.");
            Assert.IsTrue(_clientCm.IsSpawned, "Client CharacterManager replica is not spawned/alive.");

            // Distinct objects from the host's: a real second-client replica, not the
            // host instance leaking through the façade.
            Assert.AreNotSame(_hostGm, _clientGm, "Client GameManager must be its own replica, not the host's.");
            Assert.AreNotSame(_hostCm, _clientCm, "Client CharacterManager must be its own replica, not the host's.");
            yield return null;
        }

        // AC 3: the `instance` façades still point at the host's objects throughout.
        [UnityTest]
        public IEnumerator InstanceFacades_StayOnTheHost()
        {
            Assert.AreSame(_hostGm, GameManager.instance, "GameManager.instance must stay on the host.");
            Assert.AreSame(_hostCm, CharacterManager.instance, "CharacterManager.instance must stay on the host.");
            Assert.AreSame(GameManager.instance, GameManager.For(_hostNm), "Façade and For(host) must agree.");
            Assert.AreSame(CharacterManager.instance, CharacterManager.For(_hostNm), "Façade and For(host) must agree.");
            yield return null;
        }

        // AC 4: For(nm) resolves the right per-NetworkManager instance.
        [UnityTest]
        public IEnumerator For_ResolvesPerNetworkManagerInstances()
        {
            Assert.AreSame(_hostGm, GameManager.For(_hostNm), "For(host) must resolve the host's GameManager.");
            Assert.AreSame(_clientGm, GameManager.For(_clientNm), "For(client) must resolve the client's GameManager.");
            Assert.AreNotSame(GameManager.For(_hostNm), GameManager.For(_clientNm), "Per-NM GameManagers must differ.");

            Assert.AreSame(_hostCm, CharacterManager.For(_hostNm), "For(host) must resolve the host's CharacterManager.");
            Assert.AreSame(_clientCm, CharacterManager.For(_clientNm), "For(client) must resolve the client's CharacterManager.");
            Assert.AreNotSame(CharacterManager.For(_hostNm), CharacterManager.For(_clientNm), "Per-NM CharacterManagers must differ.");
            yield return null;
        }

        // AC 5: a NetworkBehaviour-bound NetworkAction routes through its own NM's
        // CustomMessagingManager and does not hijack the host's handler; the UNBOUND
        // path is characterized (it always resolves to Singleton = host).
        [UnityTest]
        public IEnumerator BoundNetworkAction_DoesNotHijackHostHandler_AndUnboundLandsOnSingleton()
        {
            // Same messageID string on both sides; the bound ctor appends the (replicated,
            // identical) NetworkObjectId + NetworkBehaviourId, so the wire keys match while
            // each action binds to its own NetworkManager's CustomMessagingManager.
            var _hostAction = new NetworkAction("coexistProbe", _hostGm);
            var _clientAction = new NetworkAction("coexistProbe", _clientGm);

            int _hostFired = 0;
            int _clientFired = 0;
            System.Action _hostListener = () => _hostFired++;
            System.Action _clientListener = () => _clientFired++;
            _hostAction += _hostListener;
            _clientAction += _clientListener;

            // Server-side invoke: broadcast to all incl. host loopback.
            _hostAction.Invoke();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostFired >= 1 && _clientFired >= 1,
                5f,
                "Bound NetworkAction did not reach both the host and the client listener.");

            Assert.AreEqual(1, _hostFired, "Host listener must fire exactly once for one invoke.");
            Assert.AreEqual(1, _clientFired, "Client listener must fire exactly once for one invoke.");

            // Registering the client's action on clientNm.CustomMessagingManager must NOT
            // have replaced the host's handler on hostNm.CustomMessagingManager: invoke
            // again, the host listener must still fire (per-NM isolation holds).
            _hostAction.Invoke();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostFired >= 2 && _clientFired >= 2,
                5f,
                "Second invoke did not reach both listeners — a handler was hijacked across NMs.");

            Assert.AreEqual(2, _hostFired, "Host handler was replaced by the client's registration.");
            Assert.AreEqual(2, _clientFired, "Client handler stopped receiving after re-invoke.");

            // --- Characterization of the UNBOUND field-init path (answers 5.0b's deferred
            // question). An unbound NetworkAction has no boundNetworkManager, so its
            // Manager getter falls back to NetworkManager.Singleton — which is the HOST.
            // It therefore registers/fires on the host and is unreachable for the second
            // client's NM. GameManager.onGameStarted/onNewDayPassed are exactly this kind.
            var _unbound = new NetworkAction("coexistGlobalProbe", false);
            int _unboundFired = 0;
            System.Action _unboundListener = () => _unboundFired++;
            _unbound += _unboundListener;

            _unbound.Invoke(); // server (host) path: SendNamedMessageToAll on the host NM
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _unboundFired >= 1,
                5f,
                "Unbound NetworkAction did not fire on the Singleton (host) — characterization assumption broken.");

            Assert.AreEqual(1, _unboundFired,
                "Unbound NetworkAction resolves to Singleton=host: it fires on the host only. " +
                "The full 5.0 fixture's second client cannot rely on unbound actions.");

            _hostAction.Unregister();
            _clientAction.Unregister();
            _unbound.Unregister();
            yield return null;
        }

        // --- helpers ---

        private static void ConfigureNetworkManager(NetworkManager _nm, GameObject _go, NetworkObject _gmPrefab, NetworkObject _cmPrefab)
        {
            var _transport = _go.AddComponent<UnityTransport>();
            _transport.SetConnectionData("127.0.0.1", LoopbackPort);
            _nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _transport,
                EnableSceneManagement = false,
            };
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _gmPrefab.gameObject });
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _cmPrefab.gameObject });
        }

        private static void SetGlobalObjectIdHash(NetworkObject _networkObject, uint _hash)
        {
            // GlobalObjectIdHash is an internal field set during editor validation; a
            // runtime-created NetworkObject has 0. Force a unique value so replication
            // can key on it and so the two prefabs do not collide on hash 0.
            FieldInfo _field = typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _field.SetValue(_networkObject, _hash);
        }

        private static void MarkAsNonSceneObject(NetworkObject _networkObject)
        {
            // The templates are active GameObjects with a NetworkObject, so StartHost's
            // in-scene sweep (NetworkSpawnManager.ServerSpawnSceneObjectsOnStartSweep,
            // which spawns anything with IsSceneObject null or true) would auto-spawn them
            // as scene objects — registering the TEMPLATE for the host and destroying our
            // explicit InstantiateAndSpawn clones as duplicates. Setting IsSceneObject=false
            // excludes them from the sweep while keeping them active (so the instantiated
            // clones run Awake normally).
            PropertyInfo _prop = typeof(NetworkObject).GetProperty("IsSceneObject",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _prop.SetValue(_networkObject, (bool?)false);
        }

        private static void ResetManagerStatics()
        {
            // GameManager.instance is a static auto-property with a private setter;
            // CharacterManager.instance is a static field. ReflectionHelper.SetPrivateField
            // cannot reach statics (it resolves obj.GetType() = RuntimeType), so set them
            // directly here.
            typeof(GameManager)
                .GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
            typeof(CharacterManager)
                .GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
        }
    }
}
