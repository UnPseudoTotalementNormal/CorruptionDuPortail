namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Narrow monotonic time port for the liveness layer (Story B1, arch-liveness-heartbeat §8.1).
    /// Same shape/discipline as <see cref="IRandomProvider"/>: a single member, engine-free, mockable.
    ///
    /// Consumed ONLY by the pump (the scheduler) to measure real elapsed between wakes and run the
    /// stall-guard. The <see cref="LivenessTracker"/> NEVER sees a clock — its decision is pure
    /// integer beat-count, not time (§8.1/§8.2).
    ///
    /// <see cref="NowTicks"/> must be monotonic and unaffected by <c>timeScale</c> / pause — the real
    /// impl (<see cref="StopwatchLivenessClock"/>) uses QPC via <c>Stopwatch.GetTimestamp()</c>, NOT
    /// <c>Time.deltaTime</c> / <c>realtimeSinceStartup</c>. Tests inject a fake that returns scripted ticks.
    /// </summary>
    public interface ILivenessClock
    {
        /// <summary>
        /// A monotonically non-decreasing tick count from an unspecified epoch. Only deltas are meaningful.
        /// UNIT CONTRACT (load-bearing for the pump's stall-guard): the tick unit MUST match
        /// <c>System.Diagnostics.Stopwatch.Frequency</c> (i.e. QPC ticks), because the pump derives its
        /// beat-period threshold as <c>Stopwatch.Frequency * seconds</c> and compares it against
        /// <see cref="NowTicks"/> deltas. A clock with a different tick base would silently break the
        /// stall-guard (evict on normal jitter, or never skip on real stalls). The fake test clock returns
        /// scripted Stopwatch-frequency ticks.
        /// </summary>
        long NowTicks { get; }
    }
}
