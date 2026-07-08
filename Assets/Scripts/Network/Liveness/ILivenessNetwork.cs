namespace Network.Liveness
{
    /// <summary>
    /// The send seam the liveness pump uses to emit beats (arch-liveness-heartbeat §8.3/§8.5). The
    /// production impl is <see cref="LivenessNetworkBridge"/> (a dumb NGO RPC pipe); tests inject a fake so
    /// the pump is exercised with no real network. The pump depends on THIS, never on the concrete bridge.
    /// </summary>
    public interface ILivenessSender
    {
        /// <summary>
        /// True once the RPC carrier is spawned and the session is up. The pump no-ops cleanly while this is
        /// false (the bridge is not spawned yet — cf. the OnNetworkSpawn ordering trap the design avoids).
        /// </summary>
        bool IsReady { get; }

        /// <summary>Client → server: send this client's Alive beat (HeartbeatServerRpc, RequireOwnership=false).</summary>
        void SendClientHeartbeat();

        /// <summary>
        /// Server → every REAL client: send the keepalive. Simulated bots (clientId ≥ 100) are skipped — they
        /// have no socket and are alive by definition (§8.5); the host never keepalives itself.
        /// </summary>
        void SendServerKeepalives();
    }

    /// <summary>
    /// Where the bridge routes a received beat (arch-liveness-heartbeat §8.4/§8.5). Registered per
    /// <see cref="Unity.Netcode.NetworkManager"/> by <see cref="LivenessService"/> so the dumb RPC carrier
    /// stays fully decoupled from the trackers (the bridge knows nothing about liveness decisions).
    /// </summary>
    public interface ILivenessSink
    {
        /// <summary>
        /// Server side: a client's HeartbeatServerRpc arrived. The sender id is the authoritative
        /// <c>ServerRpcParams.Receive.SenderClientId</c>, never a client-supplied argument.
        /// </summary>
        void OnClientHeartbeat(ulong senderClientId);

        /// <summary>Client side: the server keepalive arrived; record a beat for the single host peer.</summary>
        void OnServerKeepalive(ulong hostClientId);
    }
}
