namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Follows the vote timer for the vote candle (board task T19), frame by frame, on any peer: the host reads the
    /// timer it runs, a client gets it once a second and counts down in between. Knows how long the vote has really
    /// lasted (the candle melts from that), when everyone has voted (the timer falls to a few seconds: those become the
    /// final phase, but the candle does not burn down at once) and when the clock ticks.
    /// </summary>
    public sealed class VoteClock
    {
        private float duration;
        private float lastRaw;
        private float nextTickAt;

        /// <summary>Seconds left, extrapolated between two updates.</summary>
        public float TimeLeft { get; private set; }

        /// <summary>Seconds the vote has really lasted.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Length of the final phase: <see cref="VoteCountdown.FinalSeconds"/>, or the seconds left when
        /// everyone voted.</summary>
        public float FinalSpan { get; private set; } = VoteCountdown.FinalSeconds;

        public float Tension => VoteCountdown.Tension(TimeLeft, FinalSpan);

        public float Melt => VoteCountdown.Melt(duration - Elapsed, duration);

        /// <summary>
        /// The vote opens. <paramref name="raw"/> is this peer's copy of the timer, which on a client may still hold the
        /// last vote's value: the clock starts from a whole vote and follows the timer once it changes.
        /// </summary>
        public void Start(float voteDuration, float raw)
        {
            duration = voteDuration;
            lastRaw = raw;
            TimeLeft = voteDuration;
            Elapsed = 0f;
            FinalSpan = VoteCountdown.FinalSeconds;
            nextTickAt = VoteCountdown.SoftStartSeconds;
        }

        /// <summary>One frame of <paramref name="dt"/> seconds with this peer's copy of the timer. Returns true when the
        /// clock ticks this frame.</summary>
        public bool Advance(float raw, float dt)
        {
            if (raw != lastRaw)
            {
                float span = VoteCountdown.SpanAfterJump(TimeLeft, raw, FinalSpan);
                if (span < FinalSpan)
                {
                    nextTickAt = raw; // everyone voted: the clock races at once
                }
                else if (FinalSpan >= VoteCountdown.FinalSeconds)
                {
                    // An ordinary update, or a rejoin in the middle of the vote: the real time gone. Not once everyone
                    // has voted: the timer then says a few seconds left, the vote has not lasted any longer.
                    Elapsed = System.Math.Max(Elapsed, duration - raw);
                }
                FinalSpan = span;
                TimeLeft = raw;
                lastRaw = raw;
            }
            else
            {
                TimeLeft = System.Math.Max(0f, TimeLeft - dt);
            }
            Elapsed += dt;

            if (VoteCountdown.IsTicking(TimeLeft) && TimeLeft <= nextTickAt)
            {
                nextTickAt = TimeLeft - VoteCountdown.TickInterval(TimeLeft, FinalSpan);
                return true;
            }
            return false;
        }
    }
}
