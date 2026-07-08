namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure stall-guard policy for the pump (arch-liveness-heartbeat §8.1). Kept OUT of the tracker so the
    /// tracker stays a clean integer beat-count; extracted as a pure function so it is unit-tested without
    /// a real clock.
    ///
    /// The prod pump wakes on a realtime cadence and measures real elapsed (in <see cref="ILivenessClock"/>
    /// ticks) since its last wake. Normal elapsed ⇒ call <c>tracker.Tick()</c> once. Abnormally large
    /// elapsed (the pump itself was starved: GC / scene-load / <c>timeScale=0</c> / editor focus-loss)
    /// ⇒ SKIP the tick — the stall must NOT be counted as a missed beat for everyone, or a single host
    /// hitch would evict the whole match.
    /// </summary>
    public static class LivenessPumpPolicy
    {
        /// <summary>
        /// A wake whose elapsed exceeds <see cref="StallFactor"/>x the expected beat period is treated as a
        /// starved pump (it overslept far enough to have missed a beat window) and its tick is skipped.
        /// Conservative: normal scheduler jitter stays under 2x, real stalls (multi-second GC / scene load)
        /// blow well past it.
        /// </summary>
        public const long StallFactor = 2;

        /// <summary>
        /// True when the pump was starved for this wake and its <c>Tick()</c> must be skipped.
        /// </summary>
        /// <param name="elapsedTicks">Clock ticks elapsed since the pump's previous wake.</param>
        /// <param name="beatPeriodTicks">Expected clock ticks per beat window (must be &gt; 0).</param>
        public static bool ShouldSkipTick(long elapsedTicks, long beatPeriodTicks)
        {
            if (beatPeriodTicks <= 0)
            {
                return false; // Misconfigured period — do not silently swallow ticks.
            }

            return elapsedTicks > beatPeriodTicks * StallFactor;
        }
    }
}
