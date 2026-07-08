using System.Collections.Generic;
using Characters;
using Unity.Netcode;
using UnityEngine;

namespace Network.Liveness
{
    /// <summary>
    /// [LIVENESS B2] The transport-agnostic beat carrier (arch-liveness-heartbeat §8.5). A DUMB NGO RPC pipe
    /// placed on the SAME GameObject as the GameManager <see cref="NetworkObject"/> — session-long and
    /// scene-placed (the lobby is a state, not a scene), so its lifecycle is guaranteed and it never has to
    /// be "remembered to spawn" (the OnNetworkSpawn-never-fires trap that bit vote-skip).
    ///
    /// Contains NO liveness logic: it forwards received beats to the <see cref="ILivenessSink"/> registered
    /// for its NetworkManager (by <see cref="LivenessService"/>) and exposes <see cref="ILivenessSender"/> so
    /// the pump can drive sends. Multiple NetworkBehaviours on one NetworkObject have NO guaranteed
    /// inter-<c>OnNetworkSpawn</c> order, so this bridge depends on NOTHING at spawn except registering itself
    /// in its own static registry (+ a guard log so a future spawn bug screams instead of dead-ending).
    /// </summary>
    public class LivenessNetworkBridge : NetworkBehaviour, ILivenessSender
    {
        private const string LogTag = "[LIVENESS]";

        // Simulated-bot floor (mirrors LivenessThreshold.SimulatedBotClientIdFloor / GetSafeRpcTarget): a bot
        // has no real socket and is alive by definition, so the server never keepalives it.
        private const ulong SimulatedBotClientIdFloor = 100;

        // Per-NetworkManager registry so the pump can resolve the live bridge for its NM (null-safe, no-op
        // while unspawned). Mirrors the proven GameManager.For / CharacterManager.For pattern.
        private static readonly Dictionary<NetworkManager, LivenessNetworkBridge> s_byNetworkManager = new();

        // Per-NetworkManager sink: where received beats are routed. Owned by LivenessService (register/
        // unregister), NOT by the bridge — the carrier stays decoupled from the trackers.
        private static readonly Dictionary<NetworkManager, ILivenessSink> s_sinks = new();

        /// <summary>The live bridge registered for <paramref name="networkManager"/>, or null if none is spawned.</summary>
        public static LivenessNetworkBridge For(NetworkManager networkManager)
        {
            if (networkManager != null && s_byNetworkManager.TryGetValue(networkManager, out var _bridge) && _bridge != null)
            {
                return _bridge;
            }
            return null;
        }

        /// <summary>LivenessService registers its sink for the NM so received RPCs reach the right tracker.</summary>
        public static void RegisterSink(NetworkManager networkManager, ILivenessSink sink)
        {
            if (networkManager != null && sink != null)
            {
                s_sinks[networkManager] = sink;
            }
        }

        /// <summary>Idempotent, value-checked sink removal (only drops the entry if it is still this sink).</summary>
        public static void UnregisterSink(NetworkManager networkManager, ILivenessSink sink)
        {
            if (networkManager != null && s_sinks.TryGetValue(networkManager, out var _current) && _current == sink)
            {
                s_sinks.Remove(networkManager);
            }
        }

        /// <summary>
        /// [LEAVE][PHASE 4] Explicit per-session static reset (domain reload is disabled). Threaded through
        /// <see cref="GameLogic.CompositionRoot.ResetSessionStatics"/>, which the graceful ShutOffGame and the
        /// client host-loss return-to-menu paths already call. Idempotent + null-safe.
        /// </summary>
        public static void ResetSessionStatics()
        {
            s_byNetworkManager.Clear();
            s_sinks.Clear();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Depend on NOTHING here except claiming our own registry slot — inter-NetworkBehaviour spawn
            // order on this NetworkObject is not guaranteed. The guard log makes a future spawn regression loud.
            s_byNetworkManager[NetworkManager] = this;
            Debug.Log($"{LogTag} spawned=true (isServer={IsServer}, isClient={IsClient}).");
        }

        public override void OnNetworkDespawn()
        {
            UnregisterFromRegistry();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            // Safety net for teardown orderings where OnNetworkDespawn does not run (NetworkManager may be
            // null during shutdown — never key off it, value-scan instead).
            UnregisterFromRegistry();
            base.OnDestroy();
        }

        private void UnregisterFromRegistry()
        {
            NetworkManager _key = null;
            foreach (var _pair in s_byNetworkManager)
            {
                if (_pair.Value == this)
                {
                    _key = _pair.Key;
                    break;
                }
            }
            if (_key != null)
            {
                s_byNetworkManager.Remove(_key);
            }
        }

        // --- ILivenessSender (drive from the pump) ---------------------------------------------------

        public bool IsReady => IsSpawned && NetworkManager != null;

        public void SendClientHeartbeat()
        {
            if (!IsReady)
            {
                return;
            }
            HeartbeatServerRpc();
        }

        public void SendServerKeepalives()
        {
            if (!IsReady || !IsServer)
            {
                return;
            }

            // Per-recipient send with GetSafeRpcTarget, EXPLICITLY skipping simulated bots (≥ 100) and the
            // host's own client (the server does not keepalive itself into the void — §8.5).
            foreach (ulong _clientId in NetworkManager.ConnectedClientsIds)
            {
                if (_clientId >= SimulatedBotClientIdFloor || _clientId == NetworkManager.LocalClientId)
                {
                    continue;
                }
                KeepaliveClientRpc(SafeTarget(_clientId));
            }
        }

        // Route the keepalive through the canonical GetSafeRpcTarget when a CharacterManager is resolvable
        // (the project's mandated wrapper — keeps the bot-debug flow honest), with a self-contained fallback
        // that mirrors its semantics for graphs without a CharacterManager (e.g. a bare seam-test substrate).
        private RpcParams SafeTarget(ulong _clientId)
        {
            CharacterManager _characterManager = CharacterManager.For(NetworkManager);
            if (_characterManager != null)
            {
                return _characterManager.GetSafeRpcTarget(_clientId);
            }

            ulong _routed = _clientId >= SimulatedBotClientIdFloor ? 0UL : _clientId;
            return new RpcParams
            {
                Send = new RpcSendParams { Target = NetworkManager.RpcTarget.Single(_routed, RpcTargetUse.Temp) }
            };
        }

        // --- RPCs ------------------------------------------------------------------------------------

        // RequireOwnership = false is LOAD-BEARING (§8.5): the scene-placed bridge is server-owned, so a
        // non-owner client's beat would be silently dropped otherwise and the server would think every client
        // is dead. The sender is read from the authoritative Receive.SenderClientId, never a client argument.
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void HeartbeatServerRpc(RpcParams _rpcParams = default)
        {
            ulong _senderClientId = _rpcParams.Receive.SenderClientId;
            ResolveSink()?.OnClientHeartbeat(_senderClientId);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void KeepaliveClientRpc(RpcParams _rpcParams = default)
        {
            // Received on a real client — record a beat for the single host peer (ServerClientId).
            ResolveSink()?.OnServerKeepalive(NetworkManager.ServerClientId);
        }

        private ILivenessSink ResolveSink()
        {
            NetworkManager _networkManager = NetworkManager;
            if (_networkManager != null && s_sinks.TryGetValue(_networkManager, out var _sink))
            {
                return _sink;
            }
            return null;
        }
    }
}
