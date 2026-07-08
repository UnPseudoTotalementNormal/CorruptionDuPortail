using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure seconds-&gt;beats conversion for the liveness threshold (arch-liveness-heartbeat §8.1).
    /// Computed ONCE at wiring; the <see cref="LivenessTracker"/> only ever receives the resulting
    /// <c>int</c> beat count and never sees seconds or a beat period.
    ///
    /// <c>threshold = max(2, ceil(timeoutSeconds / beatPeriodSeconds))</c>:
    /// - <b>ceil</b> so we never declare a peer lost too early (round toward more patience).
    /// - <b>floor of 2</b> because a threshold of 1 makes a single missed beat fatal — flaky even in prod.
    /// </summary>
    public static class LivenessThreshold
    {
        /// <summary>Never let the computed threshold drop below this — a threshold of 1 is a hair-trigger.</summary>
        public const int MinimumThreshold = 2;

        /// <summary>Simulated bots use clientIds at or above this floor; they are never enrolled (§8.2).</summary>
        public const int SimulatedBotClientIdFloor = 100;

        /// <param name="timeoutSeconds">Wall-clock silence before a peer is considered gone (e.g. 5s).</param>
        /// <param name="beatPeriodSeconds">Interval between beats (e.g. 1s at 1 Hz). Must be &gt; 0.</param>
        /// <returns><c>max(2, ceil(timeoutSeconds / beatPeriodSeconds))</c>.</returns>
        public static int FromSeconds(double timeoutSeconds, double beatPeriodSeconds)
        {
            if (beatPeriodSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(beatPeriodSeconds),
                    beatPeriodSeconds,
                    "Beat period must be strictly positive.");
            }

            int beats = (int)Math.Ceiling(timeoutSeconds / beatPeriodSeconds);
            return Math.Max(MinimumThreshold, beats);
        }
    }
}
