namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Deterministic test-double pump (arch-liveness-heartbeat §8.1/§8.8). Drives a
    /// <see cref="LivenessTracker"/> directly via explicit beat advancement — NO clock, NO real seconds,
    /// so the entire liveness core is exercised in EditMode with zero <c>WaitForSeconds</c>.
    ///
    /// The prod pump measures real elapsed and applies a stall-guard; here the caller controls the
    /// cadence outright with <see cref="AdvanceBeats"/>, mirroring an unstalled pump (one Tick per beat).
    /// </summary>
    public sealed class ManualLivenessPump : ILivenessPump
    {
        private readonly LivenessTracker _tracker;

        public ManualLivenessPump(LivenessTracker tracker)
        {
            _tracker = tracker;
        }

        public bool IsRunning { get; private set; }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        /// Advance <paramref name="beats"/> beat windows by calling <see cref="LivenessTracker.Tick"/>
        /// once per beat. Negative/zero counts are a no-op. Independent of running state — this is a
        /// test driver, not the prod cadence.
        /// </summary>
        public void AdvanceBeats(int beats)
        {
            for (int i = 0; i < beats; i++)
            {
                _tracker.Tick();
            }
        }
    }
}
