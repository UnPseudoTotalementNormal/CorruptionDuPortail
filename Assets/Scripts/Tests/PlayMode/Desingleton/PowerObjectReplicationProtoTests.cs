using System.Collections;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using NUnit.Framework;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// PROTO B (spec-proto-power-effect-2client-replication) — proves a REAL Power NetworkObject
    /// (PCorruptingMark) can be spawned inside the 2-NetworkManager <see cref="MultiClientGameFixture"/>
    /// and that its FULL production pipeline (server RPC → pure decision → dispatcher → effect executors)
    /// carries the corruption all the way to the REAL remote client's replica. Extends Proto A
    /// (PowerEffectReplicationProtoTests), which drove Character.CorruptPlayerServerRpc directly with no
    /// Power object in the loop.
    ///
    /// THE WALL THAT WASN'T: Power.OnNetworkSpawn resolves its managers via
    /// CompositionRoot.For(NetworkManager) and Assert-requires a non-null CharacterManager — on the HOST
    /// and on the CLIENT replica. No CompositionRoot *instance* is needed for that resolution:
    /// CompositionRoot.For(nm) returns a value resolver delegating to the per-NM manager registries
    /// (CharacterManager.For(nm)), and the fixture's HostCm/ClientCm are already registered there (the
    /// resolution design recorded in CompositionRoot's class doc). This test asserts the crux explicitly:
    /// each replica resolved ITS OWN NM's CharacterManager. Every other field Power resolves at spawn is
    /// null-tolerant by design.
    ///
    /// EXTRA REAL DEPENDENCY: CorruptingMarkDecision emits a NewTargeting effect whose executor
    /// dereferences the RoleTargetSystem singleton (not de-singletonised), so this fixture registers and
    /// spawns a real RoleTargetSystem in both NMs. Its targeting data replicating to the client
    /// (SendTo.Everyone leg) is asserted as a bonus proof that the REAL executor ran.
    /// </summary>
    public class PowerObjectReplicationProtoTests : MultiClientGameFixture
    {
        // Unique, non-zero prefab hashes — disjoint from the fixture's own 0xC0DE01xx block.
        private const uint PowerPrefabHash = 0xC0DE0201u;
        private const uint RtsPrefabHash = 0xC0DE0202u;

        private GameObject _powerPrefabGo;
        private NetworkObject _powerPrefabNo;
        private GameObject _rtsPrefabGo;
        private NetworkObject _rtsPrefabNo;

        private PCorruptingMark _hostPower;
        private RoleTargetSystem _hostRts;

        protected override void BuildExtraNetworkPrefabs(List<GameObject> _templates)
        {
            // A real PCorruptingMark prefab. Active template so the instantiated clones run Awake
            // normally (authoredIsPassive snapshot); excluded from StartHost's scene sweep.
            _powerPrefabGo = new GameObject("ProtoPowerPrefab");
            _powerPrefabNo = _powerPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_powerPrefabNo, PowerPrefabHash);
            MarkAsNonSceneObject(_powerPrefabNo);
            _powerPrefabGo.AddComponent<PCorruptingMark>();
            _templates.Add(_powerPrefabGo);

            // A real RoleTargetSystem prefab — the NewTargeting executor's singleton dependency. Its
            // Start() resolves GameManager.For(NetworkManager) (answers in both NMs) and subscribes to
            // AwakeningStates only (none among the fixture's DummyGameStates — a no-op here).
            _rtsPrefabGo = new GameObject("ProtoRtsPrefab");
            _rtsPrefabNo = _rtsPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_rtsPrefabNo, RtsPrefabHash);
            MarkAsNonSceneObject(_rtsPrefabNo);
            _rtsPrefabGo.AddComponent<RoleTargetSystem>();
            _templates.Add(_rtsPrefabGo);
        }

        [UnityTearDown]
        public IEnumerator ProtoTearDown()
        {
            // Derived TearDown runs BEFORE the base fixture's: despawn the proto's own spawned objects
            // while the NMs are still up, so the base manager-despawn + shutdown sequence is untouched.
            if (HostNm != null && HostNm.IsListening)
            {
                if (_hostPower != null && _hostPower.IsSpawned) _hostPower.NetworkObject.Despawn(true);
                if (_hostRts != null && _hostRts.IsSpawned) _hostRts.NetworkObject.Despawn(true);
            }
            yield return null;

            // RoleTargetSystem clears its own instance on despawn; null it defensively anyway in case the
            // test failed before/mid spawn (domain reload is disabled — statics survive across tests).
            RoleTargetSystem.instance = null;
            _hostPower = null;
            _hostRts = null;
        }

        [UnityTest]
        public IEnumerator SpawnedPCorruptingMark_FullPipeline_CorruptsTargetOnRemoteClientReplica()
        {
            ulong _ownerId = HostNm.LocalClientId;
            ulong _targetId = ClientNm.LocalClientId;
            Assert.AreNotEqual(_ownerId, _targetId, "Owner (host) and target (remote client) must be distinct seats.");

            // --- Given: real seats — owner on the host, target on the REAL remote client.
            yield return SpawnRealCharacterForClient(_ownerId);
            yield return SpawnRealCharacterForClient(_targetId);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for target clientId {_targetId}.");

            // --- Given: a real RoleTargetSystem (the NewTargeting executor dereferences the singleton).
            _hostRts = HostNm.SpawnManager.InstantiateAndSpawn(_rtsPrefabNo, destroyWithScene: true)
                .GetComponent<RoleTargetSystem>();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostRts, 5f);
            Assert.AreSame(_hostRts, RoleTargetSystem.instance,
                "The host RTS must claim the singleton (the client replica must not overwrite it).");
            ulong _rtsNetId = _hostRts.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_rtsNetId),
                5f,
                "Client NM never spawned its RoleTargetSystem replica.");
            RoleTargetSystem _clientRts = ClientNm.SpawnManager.SpawnedObjects[_rtsNetId].GetComponent<RoleTargetSystem>();

            // --- THE WALL: spawn the REAL Power. OnNetworkSpawn runs on the host AND on the client
            // replica, each asserting CompositionRoot.For(its own NM).CharacterManager != null. If the
            // client-side resolution failed, the replica would never register here (AssertionException
            // in OnNetworkSpawn) and the wait below would fail the test.
            _hostPower = HostNm.SpawnManager.InstantiateAndSpawn(_powerPrefabNo, destroyWithScene: true)
                .GetComponent<PCorruptingMark>();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostPower, 5f);
            Assert.AreEqual(_ownerId, _hostPower.ownerClientId.Value,
                "Server OnNetworkSpawn must set ownerClientId from idHolderServer (default 0 = the host seat).");
            ulong _powerNetId = _hostPower.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_powerNetId),
                5f,
                "Client NM never spawned its Power replica — Power.OnNetworkSpawn (the CompositionRoot resolution) likely failed on the client.");
            PCorruptingMark _clientPower = ClientNm.SpawnManager.SpawnedObjects[_powerNetId].GetComponent<PCorruptingMark>();
            Assert.IsNotNull(_clientPower, "Client Power replica missing after wait.");
            Assert.AreNotSame(_hostPower, _clientPower,
                "Host and client Powers must be distinct replicas for the proof to mean anything.");

            // The crux of Proto B: each replica resolved ITS OWN NetworkManager's CharacterManager via
            // CompositionRoot.For(nm) — no scene-placed root needed, the per-NM registries answered.
            Assert.AreSame(HostCm, ReflectionHelper.GetPrivateField(_hostPower, "characterManager"),
                "Host power must resolve the HOST NM's CharacterManager.");
            Assert.AreSame(ClientCm, ReflectionHelper.GetPrivateField(_clientPower, "characterManager"),
                "Client power replica must resolve the CLIENT NM's CharacterManager — the per-NM CompositionRoot resolution.");

            // --- Given: the CLIENT NM's own replica of the target (never ClientCm.GetCharacter — the
            // NetworkBehaviourReference registry resolves against Singleton=host in this 2-NM fixture).
            ulong _targetNetId = _hostTarget.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_targetNetId),
                5f,
                $"Client NM never spawned its replica of the target (networkObjectId {_targetNetId}).");
            Character _clientTarget = ClientNm.SpawnManager.SpawnedObjects[_targetNetId].GetComponent<Character>();
            Assert.AreNotSame(_hostTarget, _clientTarget, "Target replicas must be distinct objects.");
            Assert.IsFalse(_hostTarget.isCorrupted.Value, "Baseline: host target must start uncorrupted.");
            Assert.IsFalse(_clientTarget.isCorrupted.Value, "Baseline: client target replica must start uncorrupted.");

            // --- When: drive the REAL pipeline — the same entry point production's picker uses
            // (OnCardClickedRpc → CorruptingMarkDecision → dispatcher → NewTargeting/StoreLastCorrupted/
            // CorruptionSucceeded/CorruptPlayer executors).
            ReflectionHelper.InvokePrivateMethod(_hostPower, "OnCardClickedRpc", _targetId);

            // --- Then: the corruption is observed ON THE CLIENT REPLICA after a real tick.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isCorrupted.Value,
                5f,
                "Corruption never crossed the wire to the remote client's replica within 5s.");
            Assert.IsTrue(_hostTarget.isCorrupted.Value, "Sanity: host target must also be corrupted.");

            // --- Then (bonus): the NewTargeting executor's SendTo.Everyone leg reached the client RTS
            // replica too — the full effect fan-out, not just the corruption NetworkVariable.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRts.currentTargetingDataList.Count > 0,
                5f,
                "The targeting data never reached the client's RoleTargetSystem replica.");
            Assert.IsTrue(
                _clientRts.currentTargetingDataList.Exists(_d => _d.targeterId == _ownerId && _d.targetId == _targetId),
                "The client RTS replica must record the owner→target targeting produced by the NewTargeting executor.");
        }
    }
}
