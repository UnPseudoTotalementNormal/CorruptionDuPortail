using System;
using CorruptionDuPortail.Domain;
using Unity.Netcode;
using UnityEngine;

namespace Network.Liveness
{
    /// <summary>
    /// [LIVENESS B2] Orchestrates the liveness layer for ONE role of ONE session (arch-liveness-heartbeat
    /// §8.3/§8.4/§8.7). Owns a pure <see cref="LivenessTracker"/>, wires the <see cref="ILivenessPump"/> to it,
    /// registers itself as the bridge's <see cref="ILivenessSink"/>, and routes the tracker's terminal
    /// decision into the EXISTING leave reactions — never duplicating them:
    /// <list type="bullet">
    /// <item>Server: enroll real clients on connect, remove on disconnect; on <c>PeerLost</c> call the SAME
    /// <see cref="GameLogic.GameManager.HandlePlayerLeft"/> the transport backstop drives (a second, faster
    /// ignition source of one idempotent pipeline).</item>
    /// <item>Client: track the single host peer (keepalive receipts); on <c>PeerLost</c> drive the existing
    /// <see cref="ClientDisconnectHandler"/> host-loss path (which itself honours the graceful
    /// <c>expectedShutdown</c> flag).</item>
    /// </list>
    /// The host is server AND client, but runs ONLY the server role here — it never heartbeat-declares itself
    /// (its own client id is never enrolled). Started/stopped by <see cref="GameLogic.CompositionRoot"/>, bound
    /// to the session lifecycle (do NOT let the pump outlive a return-to-menu — the LobbyState-sub-leak family).
    /// </summary>
    public sealed class LivenessService : ILivenessSink
    {
        private const string LogTag = "[LIVENESS]";

        private readonly NetworkManager _networkManager;
        private readonly bool _isServerRole;
        private readonly LivenessTracker _tracker;
        private readonly ILivenessClock _clock;
        private readonly double _beatPeriodSeconds;
        private readonly System.Action<ulong> _onServerPeerLost;
        private readonly System.Action _onClientHostLost;

        private ILivenessPump _pump;
        private bool _started;

        private LivenessService(
            NetworkManager networkManager,
            bool isServerRole,
            LivenessConfig config,
            ILivenessClock clock,
            System.Action<ulong> onServerPeerLost,
            System.Action onClientHostLost,
            ILivenessPump pumpOverride)
        {
            _networkManager = networkManager;
            _isServerRole = isServerRole;
            _clock = clock;
            _beatPeriodSeconds = config.BeatPeriodSeconds;
            _onServerPeerLost = onServerPeerLost;
            _onClientHostLost = onClientHostLost;
            _pump = pumpOverride; // tests inject a ManualLivenessPump; prod builds a LivenessNetworkPump in Start.

            _tracker = new LivenessTracker(config.Threshold);
            _tracker.PeerLost += HandlePeerLost;
        }

        /// <summary>The liveness tracker for this role (server-client tracker / single-host tracker). For tests/diagnostics.</summary>
        public LivenessTracker Tracker => _tracker;

        /// <summary>The scheduler cadencing the tracker. Exposed so tests can assert / drive it. For tests/diagnostics.</summary>
        public ILivenessPump Pump => _pump;

        /// <summary>
        /// Start the SERVER role: track every real client, feed <paramref name="onServerPeerLost"/> (the
        /// existing HandlePlayerLeft pipeline) on a terminal miss. Bots (≥ 100) and the host's own client are
        /// never enrolled. Pass <paramref name="pumpOverride"/> in tests to avoid the real timed loop.
        /// </summary>
        public static LivenessService StartServer(
            NetworkManager networkManager,
            LivenessConfig config,
            ILivenessClock clock,
            System.Action<ulong> onServerPeerLost,
            ILivenessPump pumpOverride = null)
        {
            var _service = new LivenessService(networkManager, true, config, clock, onServerPeerLost, null, pumpOverride);
            _service.StartInternal();
            return _service;
        }

        /// <summary>
        /// Start the CLIENT role: track exactly one peer (the host) via keepalive receipts, and drive
        /// <paramref name="onClientHostLost"/> (the existing ClientDisconnectHandler host-loss path) on a
        /// terminal miss. Never call this for the host (it is the server — see <see cref="StartServer"/>).
        /// </summary>
        public static LivenessService StartClient(
            NetworkManager networkManager,
            LivenessConfig config,
            ILivenessClock clock,
            System.Action onClientHostLost,
            ILivenessPump pumpOverride = null)
        {
            var _service = new LivenessService(networkManager, false, config, clock, null, onClientHostLost, pumpOverride);
            _service.StartInternal();
            return _service;
        }

        private void StartInternal()
        {
            if (_started)
            {
                return;
            }
            _started = true;

            LivenessNetworkBridge.RegisterSink(_networkManager, this);

            if (_isServerRole)
            {
                // Enroll already-connected real clients (the host's own id and bots are filtered), then keep
                // the tracked set in step with connects/disconnects. HandlePlayerLeft has its OWN transport
                // subscription; RemovePeer here just stops the tracker ticking a peer the transport already saw.
                foreach (ulong _clientId in _networkManager.ConnectedClientsIds)
                {
                    EnrollRealClient(_clientId);
                }
                _networkManager.OnClientConnectedCallback += EnrollRealClient;
                _networkManager.OnClientDisconnectCallback += RemovePeer;
            }
            else
            {
                // The client's one and only peer is the host.
                _tracker.Enroll(NetworkManager.ServerClientId);
            }

            _pump ??= BuildProductionPump();
            _pump.Start();

            Debug.Log($"{LogTag} service started (role={(_isServerRole ? "server" : "client")}, enrolled={_tracker.TrackedCount}).");
        }

        /// <summary>
        /// Tear down: cancel the pump, drop the network subscriptions and the bridge sink. Called on session
        /// teardown / return-to-menu / CompositionRoot destruction. Idempotent + null-safe (NetworkManager may
        /// already be gone during shutdown — never key off it).
        /// </summary>
        public void Stop()
        {
            if (!_started)
            {
                return;
            }
            _started = false;

            _pump?.Stop();

            if (_isServerRole && _networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= EnrollRealClient;
                _networkManager.OnClientDisconnectCallback -= RemovePeer;
            }

            _tracker.PeerLost -= HandlePeerLost;
            LivenessNetworkBridge.UnregisterSink(_networkManager, this);
        }

        private ILivenessPump BuildProductionPump()
        {
            return new LivenessNetworkPump(
                _clock,
                _beatPeriodSeconds,
                isReady: IsCarrierReady,
                sendBeat: SendBeat,
                tick: _tracker.Tick);
        }

        private bool IsCarrierReady()
        {
            LivenessNetworkBridge _bridge = LivenessNetworkBridge.For(_networkManager);
            return _bridge != null && ((ILivenessSender)_bridge).IsReady;
        }

        private void SendBeat()
        {
            LivenessNetworkBridge _bridge = LivenessNetworkBridge.For(_networkManager);
            if (_bridge == null)
            {
                return;
            }
            ILivenessSender _sender = _bridge;
            if (!_sender.IsReady)
            {
                return;
            }

            if (_isServerRole)
            {
                _sender.SendServerKeepalives();
            }
            else
            {
                _sender.SendClientHeartbeat();
            }
        }

        private void EnrollRealClient(ulong _clientId)
        {
            // The host runs the server pump; it never heartbeats itself, so it must never be enrolled or it
            // would evict itself (§ "the host does not heartbeat-declare itself"). Bots (≥ 100) are excluded
            // inside Enroll.
            if (_clientId == _networkManager.LocalClientId)
            {
                return;
            }
            _tracker.Enroll(_clientId);
        }

        private void RemovePeer(ulong _clientId)
        {
            // Graceful / transport disconnect: stop ticking this peer so liveness cannot emit a late PeerLost
            // for a client the transport already reported (no double leave — HandlePlayerLeft is also idempotent).
            _tracker.RemovePeer(_clientId);
        }

        private void HandlePeerLost(ulong _clientId)
        {
            if (_isServerRole)
            {
                Debug.Log($"{LogTag} server declared client {_clientId} lost (no beats) — routing to the leave pipeline.");
                _onServerPeerLost?.Invoke(_clientId);
            }
            else
            {
                Debug.Log($"{LogTag} client declared the host lost (no keepalives) — routing to host-loss handling.");
                _onClientHostLost?.Invoke();
            }
        }

        // --- ILivenessSink (fed by the bridge on RPC receipt) -----------------------------------------

        public void OnClientHeartbeat(ulong _senderClientId)
        {
            // Server side only: reset that client's miss counter. No-op on an unknown/lost/bot id (tracker rule).
            if (_isServerRole)
            {
                _tracker.RecordBeat(_senderClientId);
            }
        }

        public void OnServerKeepalive(ulong _hostClientId)
        {
            // Client side only: reset the host peer's miss counter.
            if (!_isServerRole)
            {
                _tracker.RecordBeat(_hostClientId);
            }
        }
    }
}
