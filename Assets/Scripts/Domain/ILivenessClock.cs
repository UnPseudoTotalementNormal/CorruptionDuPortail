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
        /// <summary>A monotonically non-decreasing tick count from an unspecified epoch. Only deltas are meaningful.</summary>
        long NowTicks { get; }
    }
}
