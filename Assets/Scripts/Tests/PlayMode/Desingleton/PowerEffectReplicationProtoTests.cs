using System.Collections;
using Characters;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// PROTO (spec-proto-power-effect-2client-replication) — proves the 2-NetworkManager
    /// <see cref="MultiClientGameFixture"/> can carry a power's gameplay effect all the way to a REAL remote
    /// client's replica, not just the host/server.
    ///
    /// WHY: the existing CorruptionTests suite runs StartHost only (host==server, RTT=0), so it proves the
    /// server-side corruption logic but is structurally blind to whether the effect REPLICATES — the exact gap
    /// that hid the CursedVision/Embrace regressions (see memory project_powers_poco_v2_complete).
    ///
    /// FIDELITY: PCorruptingMark's CorruptPlayer effect is realised by CorruptPlayerExecutor, which calls
    /// characterManager.GetCharacter(slot).CorruptPlayerServerRpc(). Driving Character.CorruptPlayerServerRpc()
    /// on the host here IS the terminal server mutation of that decision->dispatch->executor chain. The decision
    /// /dispatch layer above it is already covered host-only by CorruptionTests.PCorruptingMark_CorruptsTarget;
    /// this proto adds ONLY the wire crossing.
    ///
    /// SCOPE: no production code touched, no fixture change. Proto B (spawning a real Power NetworkObject in
    /// the fixture) shipped alongside — see PowerObjectReplicationProtoTests: the per-NM CompositionRoot "wall"
    /// did not survive contact. This proto remains as the minimal wire-crossing pin for the terminal Character
    /// mutation, below the full power pipeline.
    /// </summary>
    public class PowerEffectReplicationProtoTests : MultiClientGameFixture
    {
        [UnityTest]
        public IEnumerator CorruptionEffect_OnTarget_IsObservedOnRemoteClientReplica()
        {
            // A target Character owned by the REAL remote client (server-authoritative: the seat identity is the
            // ownerClientId NetworkVariable, not NGO ownership).
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_targetId);

            // --- Given (host side): the target exists and is not corrupted.
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for clientId {_targetId}.");
            Assert.IsFalse(_hostTarget.isCorrupted.Value, "Baseline: host target must start uncorrupted.");

            // --- Given (client side): resolve the CLIENT NM's own replica of the target.
            // NB: ClientCm.GetCharacter cannot be used here. CharacterManager rebuilds its registry via
            // NetworkBehaviourReference.TryGet WITHOUT an explicit NetworkManager, so NGO resolves it against
            // NetworkManager.Singleton (the host) and hands back the HOST's Character even when called on the
            // client manager. In production each client is its own process (Singleton == its own NM) so that is
            // correct there; only the 2-NM-in-one-process fixture exposes the ambiguity. Resolve the genuine
            // client-side instance through the client NM's own SpawnManager, keyed by the shared NetworkObjectId.
            ulong _netId = _hostTarget.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId),
                5f,
                $"Client NM never spawned its replica of the target (networkObjectId {_netId}).");
            Character _clientTarget = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<Character>();
            Assert.IsNotNull(_clientTarget, "Client target replica missing after wait.");
            Assert.IsFalse(_clientTarget.isCorrupted.Value, "Baseline: client replica must start uncorrupted.");

            // AC3: the two are genuinely distinct objects, so the hinge assertion below is a real wire crossing,
            // not a disguised host read.
            Assert.AreNotSame(_hostTarget, _clientTarget,
                "Host and client Characters must be distinct replicas for the proof to mean anything.");

            // --- When (host): drive the corruption exactly as the power's CorruptPlayerExecutor does.
            _hostTarget.CorruptPlayerServerRpc();

            // --- Then (the wire): the corruption is observed ON THE CLIENT REPLICA after a real tick.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isCorrupted.Value,
                5f,
                "Corruption never crossed the wire to the remote client's replica within 5s.");

            Assert.IsTrue(_clientTarget.isCorrupted.Value,
                "Client replica must observe isCorrupted == true (the effect replicated).");
            Assert.IsTrue(_hostTarget.isCorrupted.Value, "Sanity: host target must also be corrupted.");
        }
    }
}
