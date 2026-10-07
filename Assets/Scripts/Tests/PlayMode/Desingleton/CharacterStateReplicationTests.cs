using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// 2-NM E2E batch 1 — Character server-state mutations observed on the REAL remote client's replica
    /// (and one CLIENT→SERVER leg). Every test here would be blind (or trivially green) under StartHost
    /// alone: host==server means a server write is observed with no serialization and no tick, and a
    /// ServerRpc invoked on the host object never leaves the process. Uses only the base fixture's
    /// Character prefab — no extra network prefabs, no singletons to reset.
    /// </summary>
    public class CharacterStateReplicationTests : MultiClientGameFixture
    {
        /// <summary>Resolve the CLIENT NM's own replica of a host Character. Never ClientCm.GetCharacter —
        /// the NetworkBehaviourReference registry resolves against NetworkManager.Singleton (the host) in
        /// this 2-NM fixture and would hand back the HOST object.</summary>
        private IEnumerator ResolveClientReplica(Character _hostCharacter, System.Action<Character> _assign)
        {
            ulong _netId = _hostCharacter.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId),
                5f,
                $"Client NM never spawned its replica of Character networkObjectId {_netId}.");
            Character _replica = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<Character>();
            Assert.IsNotNull(_replica, "Client Character replica missing after wait.");
            Assert.AreNotSame(_hostCharacter, _replica, "Host and client Characters must be distinct replicas.");
            _assign(_replica);
        }

        // --- 1. Chaining (server-only mutator) crosses the wire: isChained + isCorrupted on the replica. ---
        [UnityTest]
        public IEnumerator ChainCharacterServer_ChainsAndCorrupts_OnRemoteClientReplica()
        {
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_targetId);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for clientId {_targetId}.");

            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);
            Assert.IsFalse(_clientTarget.isChained.Value, "Baseline: client replica must start unchained.");
            Assert.IsFalse(_clientTarget.isCorrupted.Value, "Baseline: client replica must start uncorrupted.");

            _hostTarget.ChainCharacterServer();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isChained.Value && _clientTarget.isCorrupted.Value,
                5f,
                "isChained/isCorrupted never crossed the wire to the remote client's replica.");
            Assert.IsTrue(_hostTarget.isChained.Value, "Sanity: host target must also be chained.");
        }

        // --- 2. Corruption then heal: the TRUE -> FALSE transition (plus isHealed) observed on the replica.
        // A one-way replication bug (initial-value-only sync, missed dirty flag) is exactly what StartHost
        // can never catch. ---
        [UnityTest]
        public IEnumerator HealAfterCorruption_ClearsCorruptionAndSetsHealed_OnRemoteClientReplica()
        {
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_targetId);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for clientId {_targetId}.");

            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);

            _hostTarget.CorruptPlayerServerRpc();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isCorrupted.Value,
                5f,
                "Corruption never reached the client replica.");

            _hostTarget.HealPlayerServerRpc();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => !_clientTarget.isCorrupted.Value && _clientTarget.isHealed.Value,
                5f,
                "The heal (isCorrupted true->false + isHealed) never reached the client replica.");
            Assert.IsFalse(_hostTarget.isCorrupted.Value, "Sanity: host must be uncorrupted after heal.");
            Assert.IsTrue(_hostTarget.isHealed.Value, "Sanity: host must be healed.");
        }

        // --- 3. CLIENT -> SERVER leg. Invoking a [Rpc(SendTo.Server, RequireOwnership=false)] on the CLIENT
        // replica must serialize, travel the socket to the host, and mutate SERVER state. On the host object
        // this call executes locally (host IS the server) so only the client-replica invocation exercises
        // the client->server wire. A client cannot write a server-write NetworkVariable, so the host
        // observing isCorrupted==true proves the RPC genuinely travelled. ---
        [UnityTest]
        public IEnumerator CorruptPlayerServerRpc_InvokedOnClientReplica_MutatesServerState()
        {
            const ulong TargetSeat = 999UL;
            yield return SpawnRealCharacterForClient(TargetSeat);
            Character _hostTarget = HostCm.GetCharacter(TargetSeat, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for seat {TargetSeat}.");

            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);
            Assert.IsFalse(_hostTarget.isCorrupted.Value, "Baseline: host target must start uncorrupted.");

            // Invoke ON THE CLIENT REPLICA: its NetworkManager is ClientNm, so NGO sends a real
            // client->server RPC over the loopback socket instead of running the body in place.
            _clientTarget.CorruptPlayerServerRpc();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostTarget.isCorrupted.Value,
                5f,
                "The ServerRpc sent from the CLIENT replica never mutated the server-side state.");

            // And the server write re-replicates back down to the client replica.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isCorrupted.Value,
                5f,
                "The server mutation never re-replicated back to the client replica.");
        }

        // --- 3b. SECOND client->server leg, folded in from the former ClientToServerHealTests (it re-paid a full
        // 2-NM boot for one extra RPC). HealPlayerServerRpc is a DISTINCT [Rpc(SendTo.Server, RequireOwnership=false)]
        // entry point from the Corrupt one above and clears two NetworkVariables instead of setting one, so it is
        // worth covering — but it belongs on this fixture's existing boot, not its own. ---
        [UnityTest]
        public IEnumerator HealPlayerServerRpc_InvokedOnClientReplica_ClearsCorruptionOnServer_ThenReReplicates()
        {
            ulong _seat = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_seat);
            Character _hostTarget = HostCm.GetCharacter(_seat, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for seat {_seat}.");

            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);

            // Corrupt on the SERVER first so the heal has something to clear.
            _hostTarget.CorruptPlayerServerRpc();
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _clientTarget.isCorrupted.Value, 5f, 3,
                "Corruption never reached the client replica.");

            // CLIENT drives the heal on its own replica -> serializes to the host.
            _clientTarget.HealPlayerServerRpc();

            // A client cannot write these server-write NVs locally, so the HOST observing the change is the
            // proof the RPC actually travelled the socket.
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _hostTarget.isHealed.Value && !_hostTarget.isCorrupted.Value, 5f, 3,
                "The heal ServerRpc sent from the client never mutated the server state.");

            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _clientTarget.isHealed.Value && !_clientTarget.isCorrupted.Value, 5f, 3,
                "The server heal never re-replicated to the client replica.");
        }

        // --- 4. Seat isolation: a targeted server mutation on seat A must reach ONLY A's replica; seat B's
        // replica stays untouched. A slot/index mixup in replication would be invisible under StartHost
        // (single shared object graph). ---
        [UnityTest]
        public IEnumerator TargetedCorruption_OnSeatA_DoesNotLeakToSeatBReplica()
        {
            ulong _seatA = ClientNm.LocalClientId;
            const ulong SeatB = 999UL;
            yield return SpawnRealCharacterForClient(_seatA);
            Character _hostA = HostCm.GetCharacter(_seatA, false);
            yield return SpawnRealCharacterForClient(SeatB);
            Character _hostB = HostCm.GetCharacter(SeatB, false);
            Assert.IsNotNull(_hostA, "Host has no Character for seat A.");
            Assert.IsNotNull(_hostB, "Host has no Character for seat B.");

            Character _clientA = null;
            Character _clientB = null;
            yield return ResolveClientReplica(_hostA, _c => _clientA = _c);
            yield return ResolveClientReplica(_hostB, _c => _clientB = _c);

            _hostA.CorruptPlayerServerRpc();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientA.isCorrupted.Value,
                5f,
                "Seat A's corruption never reached its client replica.");

            // When A's change has demonstrably arrived, B's replica must still be clean.
            Assert.IsFalse(_clientB.isCorrupted.Value,
                "Seat B's client replica must NOT be corrupted by a mutation targeted at seat A.");
            Assert.IsFalse(_hostB.isCorrupted.Value, "Sanity: host seat B must be untouched.");
        }

        // --- 5. Awakening: the server flips isAwakened AND fires a SendTo.Everyone RPC; both must be
        // observed on the remote replica (NetworkVariable leg + broadcast-RPC leg of the same action).
        // AwakenCharacterServerRpc raises the AWAKEN notification (onCharacterAwakened) — a prior copy-paste
        // had it firing the sleep broadcast instead; the fix (fix(character) 313a1a71) is pinned here. ---
        [UnityTest]
        public IEnumerator AwakenServerRpc_SetsAwakenedAndFiresAwakenBroadcast_OnRemoteClientReplica()
        {
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_targetId);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for clientId {_targetId}.");
            if (_hostTarget.role == null)
            {
                _hostTarget.role = new Role();
            }

            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);

            bool _clientAwakenEventFired = false;
            _clientTarget.onCharacterAwakened += () => _clientAwakenEventFired = true;

            // Invoked on the host object on the server: executes the server body directly (the RPC
            // fan-out to Everyone is the wire crossing under test).
            _hostTarget.AwakenCharacterServerRpc();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isAwakened.Value && _clientAwakenEventFired,
                5f,
                "isAwakened and/or the SendTo.Everyone awaken-notification RPC never reached the remote client replica.");
        }

        // --- 6. The character roster projects EXACTLY once per seat on the remote client: the replicated
        // NetworkList has no duplicates and each entry resolves (against the CLIENT NM) to a genuine
        // client-side replica. This is the [CHARLIST] no-dup guarantee observed from the client seat. ---
        [UnityTest]
        public IEnumerator NetworkedCharacterList_ProjectsEachSeatExactlyOnce_OnRemoteClient()
        {
            ulong _hostSeat = HostNm.LocalClientId;
            ulong _clientSeat = ClientNm.LocalClientId;
            const ulong FakeSeat = 999UL;
            yield return SpawnRealCharacterForClient(_hostSeat);
            yield return SpawnRealCharacterForClient(_clientSeat);
            yield return SpawnRealCharacterForClient(FakeSeat);

            // NET-04: the replicated list is now a full-value snapshot of NetworkObject ids.
            IReadOnlyList<ulong> _clientList = null;
            System.Func<IReadOnlyList<ulong>> _read = () => _clientList = ClientCm.ReplicatedCharacterObjectIds;

            // Stable wait on EXACTLY 3: a duplicate landing one tick after the third entry resets the
            // stability counter, so the no-dup claim is checked over a settled window, not a snapshot.
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _read().Count == 3, 5f, 3,
                () => $"Client networkedCharacters never settled on exactly 3 entries (got {_read().Count}).");

            Assert.AreEqual(3, _read().Count,
                "Client's replicated character list must have EXACTLY one entry per spawned seat (no duplicates).");

            var _ids = new List<ulong>();
            foreach (ulong _objectId in _clientList)
            {
                Assert.IsTrue(ClientNm.SpawnManager.SpawnedObjects.TryGetValue(_objectId, out NetworkObject _replicaObject),
                    "A client list entry did not resolve against the CLIENT NetworkManager.");
                Character _replica = _replicaObject.GetComponent<Character>();
                Assert.AreSame(ClientNm, _replica.NetworkManager,
                    "A resolved roster entry is not a client-side object — the projection leaked a host object.");
                _ids.Add(_replica.ownerClientId.Value);
            }
            CollectionAssert.AllItemsAreUnique(_ids, "Duplicate seat in the client's projected roster.");
            CollectionAssert.AreEquivalent(new[] { _hostSeat, _clientSeat, FakeSeat }, _ids,
                "The client roster must contain exactly the three spawned seats.");

            Assert.AreEqual(3, ClientCm.GetCharacters(false).Count,
                "ClientCm's projected character list must count 3 (no [CHARLIST] duplicate).");
        }

        // --- 7. Heal is ONE-SHOT (Character.cs:187-196): HealPlayerServerRpc clears corruption once and sets
        // isHealed; a re-corrupt afterwards works, but a SECOND heal is a no-op (the isHealed gate), so the target
        // stays corrupted. A server-authority rule whose final state still replicates to the client. ---
        [UnityTest]
        public IEnumerator Heal_IsOneShot_ReCorruptAfterHealStaysCorrupted()
        {
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_targetId);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, $"Host has no Character for clientId {_targetId}.");

            _hostTarget.CorruptPlayerServerRpc();
            Assert.IsTrue(_hostTarget.isCorrupted.Value, "Corrupt should set isCorrupted.");

            _hostTarget.HealPlayerServerRpc();
            Assert.IsFalse(_hostTarget.isCorrupted.Value, "Heal should clear corruption.");
            Assert.IsTrue(_hostTarget.isHealed.Value, "Heal should set isHealed.");

            // Re-corrupt works after a heal.
            _hostTarget.CorruptPlayerServerRpc();
            Assert.IsTrue(_hostTarget.isCorrupted.Value, "Re-corrupt after heal should set isCorrupted again.");

            // A SECOND heal is a no-op (isHealed already true) — the target must STAY corrupted.
            _hostTarget.HealPlayerServerRpc();
            Assert.IsTrue(_hostTarget.isCorrupted.Value,
                "A second heal must be a no-op (heal is one-shot), so the re-corrupted target stays corrupted.");

            // The final corrupted state settles on the real client replica (quiescence, not a single tick).
            Character _clientTarget = null;
            yield return ResolveClientReplica(_hostTarget, _c => _clientTarget = _c);
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _clientTarget.isCorrupted.Value, 5f, 3,
                "The final (re-corrupted, not re-healed) state never settled true on the client replica.");
        }
    }
}
