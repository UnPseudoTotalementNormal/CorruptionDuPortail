namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The scheduler seam for the liveness layer (arch-liveness-heartbeat §8.1/§8.3). The pump is the
    /// ONLY place wall-clock lives: it cadences <see cref="LivenessTracker.Tick"/>. The threshold
    /// decision NEVER lives here — the pump cadences, the tracker decides.
    ///
    /// Prod impl (B2): a UniTask realtime loop launched from CompositionRoot, owning the clock and the
    /// stall-guard (<see cref="LivenessPumpPolicy"/>), cancelled on session teardown. It lives in the
    /// Game assembly (UniTask is not engine-free). <see cref="ManualLivenessPump"/> is the test double.
    /// </summary>
    public interface ILivenessPump
    {
        /// <summary>True once the pump is cadencing ticks; false before <see cref="Start"/> / after <see cref="Stop"/>.</summary>
        bool IsRunning { get; }

        /// <summary>Begin cadencing ticks (prod: launch the realtime loop).</summary>
        void Start();

        /// <summary>Stop cadencing ticks (prod: cancel the loop). Idempotent.</summary>
        void Stop();
    }
}
