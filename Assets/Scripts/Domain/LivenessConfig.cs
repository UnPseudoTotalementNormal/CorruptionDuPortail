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

        /// <summary>Locked v1 defaults (§8.6): 5s detection window at 1 Hz ⇒ threshold 5.</summary>
        public static LivenessConfig Default => new LivenessConfig(5.0, 1.0);

        /// <summary>The computed integer beat threshold handed to the tracker.</summary>
        public int Threshold => LivenessThreshold.FromSeconds(TimeoutSeconds, BeatPeriodSeconds);
    }
}
