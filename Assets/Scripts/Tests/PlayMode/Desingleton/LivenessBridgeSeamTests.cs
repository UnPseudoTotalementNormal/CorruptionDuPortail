using System.Collections;
using System.Collections.Generic;
using Network.Liveness;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// [LIVENESS B2] The ONE host-driven PlayMode seam (arch-liveness-heartbeat §8.8): a client
    /// <c>HeartbeatServerRpc</c> reaches the server bridge and calls the sink with the AUTHORITATIVE sender id
    /// (<c>ServerRpcParams.Receive.SenderClientId</c>) — the only "real" thing here is the RPC round-trip over
    /// the 2-NM loopback. No timed pump runs (the beat is driven directly, the ManualLivenessPump role); the
    /// tracker/decision layers are covered exhaustively in EditMode. Asserted FROM THE HOST because IsOwner /
    /// LocalClientId are unreliable on client replicas in this fixture (project memory). Poll-frames, no
    /// WaitForSeconds.
    /// </summary>
    [Category("Desingleton")]
    public class LivenessBridgeSeamTests : MultiClientGameFixture
    {
        // Minimal capturing sink so the assertion reads the exact id the bridge routed (a real LivenessTracker
        // would also work, but capturing the raw call is the tightest proof of the RPC → sink → sender-id seam).
        private sealed class CapturingSink : ILivenessSink
        {
            public readonly List<ulong> ClientHeartbeats = new();
            public readonly List<ulong> ServerKeepalives = new();
            public void OnClientHeartbeat(ulong senderClientId) => ClientHeartbeats.Add(senderClientId);
            public void OnServerKeepalive(ulong hostClientId) => ServerKeepalives.Add(hostClientId);
        }

        [UnityTest]
        public IEnumerator ClientHeartbeat_ReachesServerBridge_RecordsAuthoritativeSenderId()
        {
            yield return SpawnBridge();
            Assert.IsNotNull(HostBridge, "Host bridge was not spawned.");
            Assert.IsNotNull(ClientBridge, "Client bridge replica never resolved.");

            // [LIVENESS B2 hardening] The bridge is server-SPAWNED, so its client replica must NOT be owned by
            // the client. RequireOwnership=false on HeartbeatServerRpc is the whole reason a non-owner client's
            // beat reaches the server; a fixture regression that made the client the owner would otherwise pass
            // this seam while silently voiding the bug the layer fixes. IsOwner==false on a server-owned replica
            // is the RELIABLE direction in this multi-NM fixture (the flaky direction is expect-true).
            Assert.IsFalse(ClientBridge.IsOwner,
                "The client replica of the server-spawned bridge must not be its owner (RequireOwnership=false is load-bearing).");

            ulong _clientId = ClientNm.LocalClientId;
            Assert.AreNotEqual(0UL, _clientId, "The real client must have a non-host clientId before it beats.");

            var _sink = new CapturingSink();
            LivenessNetworkBridge.RegisterSink(HostNm, _sink);
            try
            {
                // Drive the beat from the CLIENT replica (the pump's SendClientHeartbeat, invoked directly).
                ((ILivenessSender)ClientBridge).SendClientHeartbeat();

                // Assert on the HOST: the server bridge received the RPC and routed the AUTHORITATIVE sender id.
                yield return NetworkTestHelper.WaitUntilOrTimeout(
                    () => _sink.ClientHeartbeats.Contains(_clientId),
                    5f,
                    "The client's HeartbeatServerRpc never reached the server bridge sink with the sender id.");

                Assert.Contains(_clientId, _sink.ClientHeartbeats,
                    "HeartbeatServerRpc must call the sink with ServerRpcParams.Receive.SenderClientId (the real client id).");
            }
            finally
            {
                LivenessNetworkBridge.UnregisterSink(HostNm, _sink);
            }
        }

        /// <summary>
        /// [LIVENESS B2] The MIRROR direction (arch-liveness-heartbeat §8.4/§8.5): the server → client keepalive
        /// — the half-dead-HOST case the whole layer exists for (a frozen host whose socket the transport still
        /// sees as alive). The HOST calls <c>SendServerKeepalives</c>; the beat reaches the real client bridge
        /// and its sink records a beat for the single host peer (<see cref="NetworkManager.ServerClientId"/>).
        /// Host-driven (the host sends), poll-frames, no WaitForSeconds; the sink is registered for the CLIENT NM
        /// because the keepalive is RECEIVED client-side (mirror of the client → server test above).
        /// </summary>
        [UnityTest]
        public IEnumerator ServerKeepalive_ReachesClientBridge_RecordsHostPeer()
        {
            yield return SpawnBridge();
            Assert.IsNotNull(HostBridge, "Host bridge was not spawned.");
            Assert.IsNotNull(ClientBridge, "Client bridge replica never resolved.");

            var _sink = new CapturingSink();
            LivenessNetworkBridge.RegisterSink(ClientNm, _sink);
            try
            {
                // Drive from the HOST: keepalive every REAL client. Simulated bots (>= 100) and the host's own
                // id are skipped inside SendServerKeepalives — the real client is the only recipient here.
                ((ILivenessSender)HostBridge).SendServerKeepalives();

                // The client records the beat for its single host peer == ServerClientId (never a client arg).
                yield return NetworkTestHelper.WaitUntilOrTimeout(
                    () => _sink.ServerKeepalives.Contains(NetworkManager.ServerClientId),
                    5f,
                    "The host's KeepaliveClientRpc never reached the client bridge sink for the host peer.");

                Assert.Contains(NetworkManager.ServerClientId, _sink.ServerKeepalives,
                    "KeepaliveClientRpc must call the client sink with ServerClientId (the single host peer).");
            }
            finally
            {
                LivenessNetworkBridge.UnregisterSink(ClientNm, _sink);
            }
        }
    }
}
