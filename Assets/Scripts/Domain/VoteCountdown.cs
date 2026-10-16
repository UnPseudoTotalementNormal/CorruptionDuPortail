using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The rising tension at the end of the vote (board task T19, maquettes compte-a-rebours v1: A3 + A4, B1, C2): the
    /// candle, the room light and the clock follow one curve of the time left. Calm until the last minute, then the
    /// tension creeps in (soft phase, the clock starts ticking quietly), and the final seconds are played in full: the
    /// clock speeds up, the flame panics, the room goes dark around the table.
    /// <para>The final phase lasts <see cref="FinalSeconds"/>, or less when everyone has voted (the timer then drops to a
    /// few seconds at once, and those seconds become the final phase: <paramref name="finalSpan"/> = the time left at
    /// that moment).</para>
    /// Pure: no engine type, testable in EditMode.
    /// </summary>
    public static class VoteCountdown
    {
        /// <summary>The soft phase starts this many seconds before the end.</summary>
        public const float SoftStartSeconds = 60f;

        /// <summary>Length of the final phase when the vote runs to its end.</summary>
        public const float FinalSeconds = 10f;

        /// <summary>Tension reached when the final phase starts (the soft phase climbs to it).</summary>
        public const float SoftTension = 0.4f;

        /// <summary>Interval between two ticks of the clock during the soft phase, and at the start of the final one.</summary>
        public const float CalmTickInterval = 1f;

        /// <summary>Shortest interval, reached right before the end.</summary>
        public const float FastestTickInterval = 0.28f;

        /// <summary>
        /// Tension in [0, 1] for <paramref name="timeLeft"/> seconds left: 0 before the last minute, up to
        /// <see cref="SoftTension"/> at the start of the final phase, 1 at the end.
        /// </summary>
        public static float Tension(float timeLeft, float finalSpan = FinalSeconds)
        {
            finalSpan = ClampSpan(finalSpan);
            if (timeLeft <= 0f)
            {
                return 1f;
            }
            if (timeLeft <= finalSpan)
            {
                float k = 1f - timeLeft / finalSpan;
                return SoftTension + (1f - SoftTension) * EaseIn(k);
            }
            if (timeLeft >= SoftStartSeconds)
            {
                return 0f;
            }
            float soft = (SoftStartSeconds - timeLeft) / (SoftStartSeconds - finalSpan);
            return SoftTension * SmoothStep(soft);
        }

        /// <summary>True when the clock ticks at all (from the start of the soft phase).</summary>
        public static bool IsTicking(float timeLeft) => timeLeft > 0f && timeLeft <= SoftStartSeconds;

        /// <summary>Seconds until the next tick, <paramref name="timeLeft"/> seconds before the end: one a second, then
        /// faster and faster over the final phase.</summary>
        public static float TickInterval(float timeLeft, float finalSpan = FinalSeconds)
        {
            finalSpan = ClampSpan(finalSpan);
            if (timeLeft >= finalSpan)
            {
                return CalmTickInterval;
            }
            float k = 1f - Math.Max(0f, timeLeft) / finalSpan;
            return CalmTickInterval - (CalmTickInterval - FastestTickInterval) * k * k;
        }

        /// <summary>Volume of a tick in [0, 1]: barely audible a minute before the end, loud at the end.</summary>
        public static float TickVolume(float timeLeft, float finalSpan = FinalSeconds)
        {
            finalSpan = ClampSpan(finalSpan);
            if (timeLeft <= finalSpan)
            {
                float k = 1f - Math.Max(0f, timeLeft) / finalSpan;
                return Lerp(0.55f, 1f, k);
            }
            float soft = Clamp01((SoftStartSeconds - timeLeft) / (SoftStartSeconds - finalSpan));
            return Lerp(0.12f, 0.55f, soft);
        }

        /// <summary>
        /// How nervous the flame is during the local player's turn at night (maquettes v3, N2: the flame alone, no
        /// clock): calm for the first two thirds of the turn, then up to 1 at its end.
        /// </summary>
        public static float TurnAgitation(float timeLeft, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }
            float k = 1f - Clamp01(timeLeft / duration);
            return SmoothStep((k - 2f / 3f) * 3f);
        }

        /// <summary>How much of the candle has melted, [0, 1]: the share of the vote's duration already gone.</summary>
        public static float Melt(float timeLeft, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }
            return Clamp01(1f - timeLeft / duration);
        }

        /// <summary>
        /// The final span to use after the timer jumped from <paramref name="before"/> to <paramref name="after"/>
        /// seconds: a big drop into the final phase (everyone voted) makes the time left the new final phase; anything
        /// else keeps <paramref name="current"/>.
        /// </summary>
        public static float SpanAfterJump(float before, float after, float current)
        {
            const float JumpSeconds = 1.5f;
            if (before - after > JumpSeconds && after < current)
            {
                return ClampSpan(after);
            }
            return current;
        }

        private static float ClampSpan(float span) => Math.Max(1f, Math.Min(FinalSeconds, span));

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static float Lerp(float a, float b, float k) => a + (b - a) * Clamp01(k);

        private static float SmoothStep(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float EaseIn(float x)
        {
            x = Clamp01(x);
            return x * x * (2f - x) * 0.5f + x * 0.5f;
        }
    }
}
