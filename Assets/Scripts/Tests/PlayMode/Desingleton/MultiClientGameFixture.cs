using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using GameLogic;
using Network.Liveness;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Story 5.0 — reusable multi-client PlayMode fixture. Boots a host plus ONE
    /// real in-process remote client over a UnityTransport loopback transport (two
    /// <see cref="NetworkManager"/>s in one process), replicates a minimal
    /// GameManager/CharacterManager to the client, and records — ON THE REMOTE
    /// CLIENT — the ordered sequence of <c>currentGameStateIndex.OnValueChanged</c>
    /// observations.
    ///
    /// WHY THIS EXISTS: the rest of the PlayMode suite runs StartHost only, so
    /// host==server and RTT=0 — a server write to a NetworkVariable is observed
    /// in-process with NO serialization and NO tick. That harness is structurally
    /// blind to replication skew. Story 5.3 (GameLoopMachine index ownership, the
    /// highest-risk story of the refactor: silent multiplayer desync) cannot be
    /// safely gated without a real second client that watches the index travel the
    /// wire. This fixture is that tool; 5.3 derives from it and widens the recorder
    /// into a parameterized client-trace suite.
    ///
    /// Substrate (proven by story 5.0e's CoexistenceGateTests): Option A —
    /// hand-rolled dual NetworkManager, no Packages/manifest.json change, no
    /// dependency on NGO's own RuntimeTests. NetworkManager.Singleton is claimed in
    /// OnEnable only when still null (NetworkManager.cs:1024), so the host (created
    /// FIRST) keeps the Singleton and the client never overwrites it — which is the
    /// `instance` façade invariant the de-singletonisation guarantees.
    ///
    /// DOCUMENTED EXCEPTION to the "never call StartHost()/StartClient() directly in
    /// a test — route through NetworkTestHelper" project rule: this fixture drives
    /// the multi-NetworkManager substrate by hand on purpose. Do not "fix" it back
    /// onto the bot-flow harness. Production transport is Facepunch (Steam); it is
    /// deliberately NOT used here — UnityTransport loopback only.
    ///
    /// Derive a test class from this and add [UnityTest] methods; the host, the real
    /// client, the resolved replicas, <see cref="RemoteIndexTrace"/>, and
    /// <see cref="SetServerIndex"/> are all available after [UnitySetUp] runs.
    /// </summary>
    public abstract class MultiClientGameFixture
    {
        // Distinct, non-zero prefab hashes. Runtime-created NetworkObjects have a
        // GlobalObjectIdHash of 0; two prefabs sharing 0 collide on the registry's
        // source key (NetworkPrefabs.cs:303), so we force unique values by reflection.
        private const uint GmPrefabHash = 0xC0DE0101u;
        private const uint CmPrefabHash = 0xC0DE0102u;
        // Phase 0 (epic-player-leave-stability): a real, owned Character so the mid-game
        // disconnect path (GameManager.OnPlayerDisconnectedServer) has something to act on.
        private const uint CharacterPrefabHash = 0xC0DE0103u;
        // [LIVENESS B2] A LivenessNetworkBridge prefab so the beat-RPC seam can be exercised over the real
        // 2-NM loopback. Registered in both NMs but INERT unless a test calls SpawnBridge (mirrors the
        // Character prefab precedent above — the other fixtures never spawn it, so behaviour is unchanged).
        private const uint BridgePrefabHash = 0xC0DE0104u;
        private const ushort LoopbackPort = 7788;

        /// <summary>Number of DummyGameStates seeded into the GameManager prefab.
        /// Three lets the index move 0->1->2 (SwitchGameState/index reads bound-check
        /// against gameStates.Count).</summary>
        protected const int SeededStateCount = 3;

        /// <summary>The clientId a simulated bot uses. Anything >= 100 is intercepted
        /// by the host (GetSafeRpcTarget routes it to client 0). See
        /// <see cref="AssertSimulatedBotIsIntercepted"/>.</summary>
        protected const ulong SimulatedBotClientId = 100;

        private GameObject _gmPrefabGo;
        private GameObject _cmPrefabGo;
        private NetworkObject _gmPrefabNo;
        private NetworkObject _cmPrefabNo;
        // Phase 0 (epic-player-leave-stability): template + registered prefab for a real Character.
        private GameObject _characterPrefabGo;
        private NetworkObject _characterPrefabNo;
        // [LIVENESS B2] template + registered prefab for the beat carrier (spawned only by SpawnBridge).
        private GameObject _bridgePrefabGo;
        private NetworkObject _bridgePrefabNo;

        // Proto B (spec-proto-power-effect-2client-replication): extra prefab templates contributed by a
        // derived fixture via BuildExtraNetworkPrefabs. Registered in BOTH NMs before StartHost/StartClient
        // (NGO's AddNetworkPrefab throws after start when ForceSamePrefabs is on, the default here) and
        // destroyed in teardown. Empty for every existing fixture — behaviour unchanged.
        private readonly List<GameObject> _extraPrefabTemplates = new();

        /// <summary>
        /// Extension hook for derived fixtures that need ADDITIONAL registered network prefabs (e.g. a real
        /// Power NetworkObject). Called during SetUp BEFORE the NetworkManagers are created; add fully-built,
        /// active template GameObjects (with a NetworkObject carrying a unique GlobalObjectIdHash via
        /// <see cref="SetGlobalObjectIdHash"/> and excluded from the scene sweep via
        /// <see cref="MarkAsNonSceneObject"/>) to <paramref name="_templates"/>. The base fixture registers
        /// them in both NMs and destroys them in TearDown.
        /// </summary>
        protected virtual void BuildExtraNetworkPrefabs(List<GameObject> _templates) { }

        /// <summary>The host's spawned <see cref="LivenessNetworkBridge"/> (null until <see cref="SpawnBridge"/> runs).</summary>
        protected LivenessNetworkBridge HostBridge { get; private set; }

        /// <summary>The real client's replica of the bridge (null until <see cref="SpawnBridge"/> runs).</summary>
        protected LivenessNetworkBridge ClientBridge { get; private set; }

        /// <summary>The last Character spawned via <see cref="SpawnRealCharacterForClient"/>.
        /// Held so a test can observe its NetworkVariables after a disconnect fakifies/mutates it
        /// (the object is server-owned, so it is not destroyed when its client leaves).</summary>
        protected Characters.Character LastSpawnedCharacter { get; private set; }

        private GameObject _hostNmGo;
        private GameObject _clientNmGo;

        protected NetworkManager HostNm { get; private set; }
        protected NetworkManager ClientNm { get; private set; }

        protected GameManager HostGm { get; private set; }
        protected CharacterManager HostCm { get; private set; }
        protected GameManager ClientGm { get; private set; }
        protected CharacterManager ClientCm { get; private set; }

        // Ordered, append-only trace of currentGameStateIndex.OnValueChanged newValues
        // observed ON THE REMOTE CLIENT replica. This is the load-bearing observation
        // StartHost can never produce.
        private readonly List<int> _remoteIndexTrace = new();
        protected IReadOnlyList<int> RemoteIndexTrace => _remoteIndexTrace;

        /// <summary>True iff the client's OnValueChanged fired for the index value (0)
        /// carried in the spawn payload, BEFORE any explicit server write. NGO version
        /// behaviour — observed, not assumed (see Dev Notes / Task 3). Read it to know
        /// whether an expected trace starts with [0, ...] or skips it.</summary>
        protected bool SpawnPayloadFiredInitialValue { get; private set; }

        private NetworkVariable<int>.OnValueChangedDelegate _remoteIndexHandler;

        // The DummyGameState SOs seeded into the prefab (shared by reference with the
        // host clone + client replica, since Object.Instantiate does not clone referenced
        // ScriptableObjects). Tracked so teardown can destroy them — with domain reload
        // disabled they would otherwise leak across the whole PlayMode session.
        private readonly List<GameState> _seededStates = new();

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
            // Seating a client hands it a rejoin token, saved in PlayerPrefs: keep the tests' session under their own
            // key and start from none, so no test (nor the Editor's play mode) inherits another one's token.
            Network.RejoinSessionStore.KeySuffix = "-playmode-tests";
            Network.RejoinSessionStore.Clear();

            // --- Build the two network-prefab templates (shared by both NMs so their
            // hashes match by construction). Adding a GameManager/CharacterManager
            // component runs its Awake, which claims the static `instance`; we null
            // those statics again before spawning so the REAL spawned host instance
            // exercises the claim-if-free path honestly.
            _gmPrefabGo = new GameObject("FixtureGmPrefab");
            _gmPrefabNo = _gmPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_gmPrefabNo, GmPrefabHash);
            MarkAsNonSceneObject(_gmPrefabNo);
            var _gmPrefabComp = _gmPrefabGo.AddComponent<GameManager>();
            _gmPrefabComp.ignoreGameLoop = true;
            // Seed SeededStateCount states so the index can legally move 0..N-1.
            // Object.Instantiate copies the SerializedDictionary into both the host
            // clone and the replicated client instance.
            for (int i = 0; i < SeededStateCount; i++)
            {
                var _state = ScriptableObject.CreateInstance<DummyGameState>();
                _seededStates.Add(_state);
                _gmPrefabComp.gameStates.Add(_state, new GameStateSettings());
            }

            _cmPrefabGo = new GameObject("FixtureCmPrefab");
            _cmPrefabNo = _cmPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_cmPrefabNo, CmPrefabHash);
            MarkAsNonSceneObject(_cmPrefabNo);
            _cmPrefabGo.AddComponent<CharacterManager>();

            // Phase 0 (epic-player-leave-stability): a Character prefab template so
            // CharacterManager.AddNewCharacter can spawn a genuine owned Character. Registered
            // in BOTH NMs (below) so the replica reaches the late-joining client. Nothing spawns
            // it unless a test calls SpawnRealCharacterForClient — inert for the other fixtures.
            _characterPrefabGo = new GameObject("FixtureCharacterPrefab");
            _characterPrefabNo = _characterPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_characterPrefabNo, CharacterPrefabHash);
            MarkAsNonSceneObject(_characterPrefabNo);
            _characterPrefabGo.AddComponent<Characters.Character>();

            // [LIVENESS B2] Bridge prefab template (inert unless SpawnBridge is called).
            _bridgePrefabGo = new GameObject("FixtureBridgePrefab");
            _bridgePrefabNo = _bridgePrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_bridgePrefabNo, BridgePrefabHash);
            MarkAsNonSceneObject(_bridgePrefabNo);
            _bridgePrefabGo.AddComponent<LivenessNetworkBridge>();

            // Proto B extension hook: derived fixtures contribute extra prefab templates BEFORE the NMs
            // exist, so both NMs register them pre-start (ForceSamePrefabs forbids post-start additions).
            _extraPrefabTemplates.Clear();
            BuildExtraNetworkPrefabs(_extraPrefabTemplates);

            // --- Host NM FIRST: its OnEnable claims NetworkManager.Singleton. ---
            _hostNmGo = new GameObject("FixtureHostNM");
            HostNm = _hostNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(HostNm, _hostNmGo, _gmPrefabNo, _cmPrefabNo, _characterPrefabNo, _bridgePrefabNo, _extraPrefabTemplates);

            // --- Client NM SECOND: Singleton already set, so it stays the host. ---
            _clientNmGo = new GameObject("FixtureClientNM");
            ClientNm = _clientNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(ClientNm, _clientNmGo, _gmPrefabNo, _cmPrefabNo, _characterPrefabNo, _bridgePrefabNo, _extraPrefabTemplates);

            Assert.IsTrue(NetworkManager.Singleton == HostNm,
                "Host NM (created first) must own NetworkManager.Singleton.");

            // Drop the instance claims the prefab templates' Awake made, so the host's
            // spawned instance claims a free façade like production's scene object does.
            ResetManagerStatics();

            Assert.IsTrue(HostNm.StartHost(), "NGO StartHost() failed — host did not start.");

            // Spawn the managers on the host (registered network prefabs -> replicate
            // to the late-joining client on connect).
            HostGm = HostNm.SpawnManager.InstantiateAndSpawn(_gmPrefabNo, destroyWithScene: true)
                .GetComponent<GameManager>();
            HostCm = HostNm.SpawnManager.InstantiateAndSpawn(_cmPrefabNo, destroyWithScene: true)
                .GetComponent<CharacterManager>();

            // Production wiring: GameManager.OnPlayerDisconnectedServer reads characterManager.
            ReflectionHelper.SetPrivateField(HostGm, "characterManager", HostCm);

            // Phase 0 (epic-player-leave-stability): wire the CharacterManager's spawn prefab +
            // parent so AddNewCharacter can spawn a real owned Character. Only the host needs this
            // (spawns are server-only); the client just receives the replicated NetworkObject. The
            // parent is HostCm's own (spawned) NetworkObject transform.
            ReflectionHelper.SetPrivateField(HostCm, "_characterPrefab", _characterPrefabNo);
            ReflectionHelper.SetPrivateField(HostCm, "_charactersParent", HostCm.transform);

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, HostGm, HostCm);

            Assert.IsTrue(ClientNm.StartClient(), "NGO StartClient() failed — client did not start.");

            // Wait for replication by polling For(clientNm), frame by frame, with a cap.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => GameManager.For(ClientNm) != null && CharacterManager.For(ClientNm) != null,
                10f,
                "Client replicas of GameManager/CharacterManager never registered (replication did not complete).");

            ClientGm = GameManager.For(ClientNm);
            ClientCm = CharacterManager.For(ClientNm);

            // --- Subscribe the remote-client index recorder the moment the replica
            // resolves. AC 4: record the ordered OnValueChanged trace on the CLIENT.
            _remoteIndexHandler = (_oldValue, _newValue) => _remoteIndexTrace.Add(_newValue);
            ClientGm.currentGameStateIndex.OnValueChanged += _remoteIndexHandler;

            // Characterize the spawn-payload initial-value firing (Task 3). The client
            // late-joined with index 0 already in the spawn payload. Give NGO a couple
            // of frames to deliver any initial OnValueChanged, then record whether it
            // fired BEFORE any explicit server write. Observed, never assumed.
            yield return null;
            yield return null;
            SpawnPayloadFiredInitialValue = _remoteIndexTrace.Count > 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Network.RejoinSessionStore.Clear();
            Network.RejoinSessionStore.KeySuffix = string.Empty;

            // Tearing down two UnityTransport loopback sockets in-process logs a benign
            // "[Error] All socket receive requests were marked as failed" from UTP when
            // one socket closes while the other has a pending receive job. It is shutdown
            // noise (0–2 occurrences, non-deterministic), unrelated to the assertions
            // that already ran in the test body — ignore failing log messages for the
            // teardown window only. Explicit assertions are unaffected by this flag.
            LogAssert.ignoreFailingMessages = true;

            if (ClientGm != null && _remoteIndexHandler != null)
            {
                // Unsubscribe the recorder (NGO rule: drop NetworkVariable.OnValueChanged
                // before despawn/destroy).
                ClientGm.currentGameStateIndex.OnValueChanged -= _remoteIndexHandler;
            }
            _remoteIndexHandler = null;

            // Despawn the host-owned managers FIRST so GameManager unsubscribes
            // OnClientDisconnectCallback before the shutdown disconnect sequence runs.
            // Otherwise OnPlayerDisconnectedServer fires and NREs on the absent
            // BoardManager (its body is out of this fixture's scope).
            if (HostNm != null && HostNm.IsListening)
            {
                if (HostGm != null && HostGm.IsSpawned) HostGm.NetworkObject.Despawn(true);
                if (HostCm != null && HostCm.IsSpawned) HostCm.NetworkObject.Despawn(true);
            }
            yield return null;

            // Tear down BOTH NetworkManagers so the fixture never destabilises the rest
            // of the PlayMode suite (a UDP port left bound would fail the next test).
            if (ClientNm != null && ClientNm.IsListening) ClientNm.Shutdown();
            if (HostNm != null && HostNm.IsListening) HostNm.Shutdown();

            // Wait WITHOUT asserting: ignoreFailingMessages is a process-wide static, so it must be restored
            // before anything in this teardown can throw. An Assert.Fail here would leave it stuck at true and
            // silently suppress unexpected-error-log failures for every REMAINING PlayMode test in the session.
            bool _stoppedListening = false;
            yield return NetworkTestHelper.WaitUntilOrElapsed(
                () => (ClientNm == null || !ClientNm.IsListening) && (HostNm == null || !HostNm.IsListening),
                5f,
                ok => _stoppedListening = ok);

            // The UTP socket-close noise is now past — stop ignoring failing logs so the
            // explicit regression assertions below (and the NEXT test) are not silently
            // suppressed by this static flag leaking across tests.
            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(_stoppedListening, "NGO did not stop listening within 5s after Shutdown().");

            Object.Destroy(_clientNmGo);
            Object.Destroy(_hostNmGo);
            Object.Destroy(_gmPrefabGo);
            Object.Destroy(_cmPrefabGo);
            Object.Destroy(_characterPrefabGo);
            if (_bridgePrefabGo != null) Object.Destroy(_bridgePrefabGo);
            // Proto B: destroy the derived-fixture templates (domain reload is disabled — they would
            // otherwise leak across the whole PlayMode session).
            foreach (var _extra in _extraPrefabTemplates)
            {
                if (_extra != null) Object.Destroy(_extra);
            }
            _extraPrefabTemplates.Clear();
            // [LIVENESS B2] Clear the bridge static registries so no entry leaks across PlayMode tests
            // (domain reload is disabled). Idempotent + null-safe.
            LivenessNetworkBridge.ResetSessionStatics();

            // Destroy the seeded DummyGameState SOs (shared by reference with every
            // replica's gameStates) — domain reload is disabled, so they would otherwise
            // accumulate for the whole PlayMode session.
            foreach (var _state in _seededStates)
            {
                if (_state != null) Object.Destroy(_state);
            }
            _seededStates.Clear();

            // Statics must be clean for the next test (domain reload is disabled).
            ResetManagerStatics();
            // NOTE: intentionally NOT calling TestStaticReset.ResetAll() here. Several existing PlayMode tests
            // pass only because a prior test LEAKS a singleton they never wire themselves. A blanket ResetAll in a
            // shared fixture teardown clears those leaks and breaks them (Amelia's ordering warning: fix the
            // leak-dependent tests FIRST, then wire ResetAll). ResetAll stays an opt-in tool for NEW batches that
            // wire their own singletons; the fixture-reset gate (FixtureResetTests) proves it works in isolation.
            //
            // BLAST RADIUS, MEASURED 2026-07-20 (dry run, reverted) — exactly 3 failures out of 244:
            //   CorruptionTests.PCardsShuffling_CorrectGuessRevealsAndRecordsTarget          (NullReferenceException)
            //   OwnerLocalEffectBoundaryTests.CursedVisionCastByClient_RevealsOnClientRevealer_NotHost
            //   OwnerLocalEffectBoundaryTests.EmbraceCastByClient_RevealsOnClientRevealer_NotHost
            // So wiring ResetAll is a small job, not a rewrite. Details + next steps: deferred-work.md,
            // "audit des tests PlayMode post-PR#91".
            //
            // TRAP if you re-measure this: enabling ResetAll HERE ONLY yields 244/244 green, and that green is a
            // FALSE NEGATIVE. Execution order puts Tests.PlayMode.CorruptionTests at index 29 and the first
            // Tests.PlayMode.Desingleton.* at index 38, so this teardown never runs before CorruptionTests. To get
            // the real number you must force a reset BEFORE index 29 (the dry run temporarily called ResetAll() in
            // CorruptionTests' own [UnitySetUp], simulating "the previous fixture cleaned everything").

            yield return null;

            // Regression net: the registry self-cleans via OnNetworkDespawn/OnDestroy
            // and Singleton clears when its owner is destroyed (NetworkManager.cs:1655).
            Assert.IsTrue(NetworkManager.Singleton == null,
                "NetworkManager.Singleton must be null after both NMs are torn down.");
            Assert.IsNull(GameManager.For(HostNm), "GameManager registry leaked a host entry after teardown.");
            Assert.IsNull(CharacterManager.For(HostNm), "CharacterManager registry leaked a host entry after teardown.");
        }

        // --- fixture API for derived tests ---

        /// <summary>
        /// Server-side index driver. The server owns currentGameStateIndex, so writing
        /// its Value here serializes and replicates to the real client, firing
        /// OnValueChanged on the client's replica after a real tick. Minimal, behaviour-
        /// honest driver for 5.0; story 5.3 replaces it with the real SwitchGameState
        /// state-machine path.
        /// </summary>
        protected void SetServerIndex(int _index)
        {
            Assert.IsNotNull(HostGm, "Host GameManager is not spawned.");
            Assert.IsTrue(HostNm != null && HostNm.IsServer, "SetServerIndex must run on the server (host).");
            Assert.IsTrue(_index >= 0 && _index < SeededStateCount,
                $"Index {_index} out of range [0, {SeededStateCount}).");
            HostGm.currentGameStateIndex.Value = _index;
        }

        /// <summary>
        /// Yields until the remote client's recorded trace reaches <paramref name="count"/>
        /// entries, or fails after the timeout. Use after SetServerIndex writes so the
        /// real tick has time to deliver the OnValueChanged.
        /// </summary>
        protected IEnumerator WaitForRemoteTraceCount(int _count, float _timeoutSeconds = 5f)
        {
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _remoteIndexTrace.Count >= _count,
                _timeoutSeconds,
                () => $"Remote client index trace never reached {_count} entries (got {_remoteIndexTrace.Count}).");
        }

        /// <summary>
        /// Bounded-wait resolution of the CLIENT NM's OWN replica of a host-spawned object.
        /// <see cref="SpawnRealCharacterForClient"/> only waits for the HOST projection plus two frames,
        /// so indexing <c>ClientNm.SpawnManager.SpawnedObjects[...]</c> directly after it races the
        /// replication (an editor hitch past two frames = KeyNotFoundException). Always resolve through
        /// this wait instead.
        /// </summary>
        protected IEnumerator WaitForClientReplica<T>(NetworkBehaviour _hostObject, System.Action<T> _assign, float _timeoutSeconds = 5f)
            where T : NetworkBehaviour
        {
            ulong _netId = _hostObject.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId),
                _timeoutSeconds,
                $"Client NM never spawned its replica of {typeof(T).Name} (networkObjectId {_netId}).");
            T _replica = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<T>();
            Assert.IsNotNull(_replica, $"Client {typeof(T).Name} replica missing after wait.");
            Assert.AreNotSame(_hostObject, _replica, $"{typeof(T).Name} replicas must be distinct objects.");
            _assign(_replica);
        }

        /// <summary>
        /// Phase 0 (epic-player-leave-stability): spawn a REAL, server-owned Character whose
        /// <see cref="Characters.Character.ownerClientId"/> NetworkVariable equals
        /// <paramref name="_ownerClientId"/> (production is server-authoritative — the seat identity
        /// is a NetworkVariable, not NGO ownership). This gives the mid-game disconnect handler
        /// (GameManager.OnPlayerDisconnectedServer) a seat to act on. Optionally assigns
        /// <paramref name="_role"/> so "roles exist" before the leave. The spawned Character is
        /// exposed via <see cref="LastSpawnedCharacter"/>. Waits (bounded) for the host to project
        /// it, then a couple of frames for replication to the real client.
        /// </summary>
        protected IEnumerator SpawnRealCharacterForClient(ulong _ownerClientId, Role _role = null)
        {
            Assert.IsNotNull(HostCm, "Host CharacterManager must be spawned before spawning a Character.");
            Assert.IsTrue(HostNm != null && HostNm.IsServer, "SpawnRealCharacterForClient must run on the server (host).");

            Characters.Character _character = HostCm.AddNewCharacter(_ownerClientId);
            Assert.IsNotNull(_character,
                $"AddNewCharacter returned null for clientId {_ownerClientId} (duplicate id, or missing prefab/parent wiring).");
            if (_role != null)
            {
                _character.role = _role;
            }
            LastSpawnedCharacter = _character;

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_character, 5f);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => HostCm.GetCharacter(_ownerClientId, false) != null,
                5f,
                $"Host CharacterManager never projected the spawned Character for clientId {_ownerClientId}.");
            // Let the new NetworkObject replicate to the real client.
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Optional simulated-bot proof (AC 3), opt-in and OFF by default — the real
        /// client is the load-bearing addition. Proves the intercepted-dispatch routing:
        /// GetSafeRpcTarget(clientId >= 100) routes to the host (client 0) instead of the
        /// absent bot client, while a real clientId routes to itself. This is the smallest
        /// honest proof of the bot path without spawning a full bot Character (the full
        /// bot-character spawn is the deferred remainder — see Dev Notes / issue #50).
        /// GetSafeRpcTarget / IsLocalOrSimulated / the >= 100 gateway are exercised
        /// verbatim here (NFR5).
        /// </summary>
        protected void AssertSimulatedBotIsIntercepted()
        {
            // Read the clientId the target actually routes to. Comparing the RpcTarget
            // objects themselves is NOT a valid proof: RpcTarget.Single(_, Persistent)
            // allocates a fresh DirectSendRpcTarget per call with no Equals override, so
            // ANY two distinct clientIds compare unequal — the redirect would "pass" even
            // if the >=100 branch were deleted. We must inspect the routed clientId.
            ulong _botRouted = ResolveTargetClientId(HostCm.GetSafeRpcTarget(SimulatedBotClientId));
            ulong _realRouted = ResolveTargetClientId(HostCm.GetSafeRpcTarget(7));

            Assert.AreEqual(0UL, _botRouted,
                "Simulated bot (clientId >= 100) must be redirected to the host (client 0) — the interception did not happen.");
            Assert.AreEqual(7UL, _realRouted,
                "A real clientId must route to itself (no redirect).");
        }

        // Resolves the clientId the RpcParams' target actually routes to. GetSafeRpcTarget
        // returns NetworkManager.RpcTarget.Single(id, Persistent). When id is a *remote*
        // client it is a DirectSendRpcTarget carrying an `internal ulong ClientId` field
        // (NGO DirectSendRpcTarget.cs:7). When id equals the caller's OWN clientId, NGO
        // collapses Single() to a LocalSendRpcTarget (no ClientId field) — which is exactly
        // what the bot redirect produces: clientId >= 100 -> Single(0), and on the host
        // (LocalClientId == 0) that IS the local target. So a missing ClientId field means
        // "routed to the host itself" = HostNm.LocalClientId. This is the genuine proof of
        // interception: the bot lands on the host, the real client routes to itself.
        private ulong ResolveTargetClientId(RpcParams _params)
        {
            var _target = _params.Send.Target;
            Assert.IsNotNull(_target, "RpcParams.Send.Target was null.");
            FieldInfo _field = _target.GetType().GetField("ClientId",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_field != null)
            {
                return (ulong)_field.GetValue(_target);
            }
            // No explicit ClientId => a local target => routed to the host's own clientId.
            return HostNm.LocalClientId;
        }

        // --- helpers (lifted from 5.0e CoexistenceGateTests, the proven substrate) ---

        private static void ConfigureNetworkManager(NetworkManager _nm, GameObject _go, NetworkObject _gmPrefab, NetworkObject _cmPrefab, NetworkObject _characterPrefab, NetworkObject _bridgePrefab, IReadOnlyList<GameObject> _extraPrefabs)
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
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _characterPrefab.gameObject });
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _bridgePrefab.gameObject });
            // Proto B: derived-fixture templates, registered in BOTH NMs so their replicas can reach the
            // real client (hashes are the templates' own — unique by the hook's contract).
            foreach (var _extra in _extraPrefabs)
            {
                _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _extra });
            }
        }

        /// <summary>
        /// [LIVENESS B2] Spawn the beat carrier on the host and wait for its replica to reach the real client.
        /// Exposes <see cref="HostBridge"/> / <see cref="ClientBridge"/>. Opt-in — nothing spawns it otherwise.
        /// </summary>
        protected IEnumerator SpawnBridge()
        {
            Assert.IsTrue(HostNm != null && HostNm.IsServer, "SpawnBridge must run on the server (host).");

            HostBridge = HostNm.SpawnManager.InstantiateAndSpawn(_bridgePrefabNo, destroyWithScene: true)
                .GetComponent<LivenessNetworkBridge>();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(HostBridge, 5f);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => LivenessNetworkBridge.For(ClientNm) != null,
                10f,
                "Client replica of the LivenessNetworkBridge never registered (replication did not complete).");

            ClientBridge = LivenessNetworkBridge.For(ClientNm);
        }

        protected static void SetGlobalObjectIdHash(NetworkObject _networkObject, uint _hash)
        {
            // GlobalObjectIdHash is an internal field set during editor validation; a
            // runtime-created NetworkObject has 0. Force a unique value so replication
            // can key on it and so the two prefabs do not collide on hash 0.
            FieldInfo _field = typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _field.SetValue(_networkObject, _hash);
        }

        protected static void MarkAsNonSceneObject(NetworkObject _networkObject)
        {
            // The templates are active GameObjects with a NetworkObject, so StartHost's
            // in-scene sweep (NetworkSpawnManager.ServerSpawnSceneObjectsOnStartSweep,
            // which spawns anything with IsSceneObject null or true) would auto-spawn
            // them as scene objects — registering the TEMPLATE for the host and
            // destroying our explicit InstantiateAndSpawn clones as duplicates. Setting
            // IsSceneObject=false excludes them from the sweep while keeping them active
            // (so the instantiated clones run Awake normally).
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
