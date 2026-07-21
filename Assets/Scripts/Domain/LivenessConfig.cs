namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Plain, engine-free settings holder for the liveness numbers (arch-liveness-heartbeat §8.1, item 4).
    /// Kept as a struct in Domain (not a ScriptableObject) so B1 stays pure and needs no Game-assembly
    /// wiring; B2 may front it with an authoring ScriptableObject that feeds these same plain values in.
    ///
    /// Only <see cref="Threshold"/> (a plain <c>int</c>) ever crosses into <see cref="LivenessTracker"/> —
    /// seconds/period stay on this side of the seam.
    /// </summary>
    public struct LivenessConfig
    {
        /// <summary>Wall-clock silence before a peer is considered gone.</summary>
        public readonly double TimeoutSeconds;

        /// <summary>Interval between beats (1s at the locked 1 Hz rate).</summary>
        public readonly double BeatPeriodSeconds;

        public LivenessConfig(double timeoutSeconds, double beatPeriodSeconds)
        {
            TimeoutSeconds = timeoutSeconds;
            BeatPeriodSeconds = beatPeriodSeconds;
        }

        /// <summary>
        /// v2 defaults (investigation vpn-instant-disconnect): 15s detection window at 1 Hz ⇒ threshold 15.
        ///
        /// The v1 window was 5s, which made this layer the STRICTEST kill in the whole stack — stricter than
        /// the transport it is supposed to pre-empt (UnityTransport DisconnectTimeoutMS, now back at the Unity
        /// default of 30s) and stricter than every comparable engine (Unity Relay TTL 10s, Photon 10s,
        /// Mirror/KCP 10s, Unreal ConnectionTimeout 60s). Because the beats ride application-level RPCs while
        /// the transport keeps its own 500ms heartbeat alive independently, a client on a jittery path (VPN,
        /// hotspot, congested Wi-Fi) got evicted here while both the transport AND Relay still considered the
        /// connection perfectly healthy — and a server-side eviction is an irreversible CHAIN.
        ///
        /// 15s keeps the layer doing its job (it still fires before the 30s transport timeout, so it remains
        /// the FAST half-dead detector) while sitting above the industry's low-water mark of 10s.
        /// </summary>
        public static LivenessConfig Default => new LivenessConfig(15.0, 1.0);

        /// <summary>The computed integer beat threshold handed to the tracker.</summary>
        public int Threshold => LivenessThreshold.FromSeconds(TimeoutSeconds, BeatPeriodSeconds);
    }
}
