using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>One frame of the night parchment: how far it is unrolled, how far it has burnt, whether its rollers show.</summary>
    public readonly struct ParchmentBannerFrame
    {
        /// <summary>0 = rolled up, 1 = fully unrolled (opens from the centre outwards).</summary>
        public readonly double Unroll;

        /// <summary>Burn front, 0 = intact, <see cref="ParchmentBannerTimeline.BurnEnd"/> = nothing left.</summary>
        public readonly double Burn;

        /// <summary>The wooden rollers at both ends: gone as soon as the sheet catches fire.</summary>
        public readonly bool RollersVisible;

        /// <summary>Burnt out: the banner can hide.</summary>
        public readonly bool Done;

        public ParchmentBannerFrame(double unroll, double burn, bool rollersVisible, bool done)
        {
            Unroll = unroll;
            Burn = burn;
            RollersVisible = rollersVisible;
            Done = done;
        }
    }

    /// <summary>
    /// Timing of the night parchment (board task T21, mockups tour-de-nuit v5 C3 + v4 T2, 2026-10-16): when the local
    /// player's role wakes, "Vous vous éveillez" unrolls across the table, stays, then catches fire from a corner and
    /// burns away in about a second; "Vous vous rendormez" does the same when the turn ends. Pure: the presenter feeds
    /// it the seconds since the banner started and how long the sheet stays whole before it burns.
    /// </summary>
    public static class ParchmentBannerTimeline
    {
        public const double UnrollSeconds = 0.45;
        public const double BurnSeconds = 1.0;

        /// <summary>
        /// Last burn front value: the shader's field (a corner gradient mixed with noise) stays under 1, and the glow
        /// that hugs the burnt edge reaches a little behind the front, so a little past 1 leaves nothing.
        /// </summary>
        public const double BurnEnd = 1.04;

        /// <param name="elapsed">Seconds since the banner started.</param>
        /// <param name="wholeSeconds">Seconds from the start until it catches fire (unrolling included).</param>
        public static ParchmentBannerFrame Evaluate(double elapsed, double wholeSeconds)
        {
            if (elapsed <= 0) return new ParchmentBannerFrame(0, 0, true, false);
            double unroll = elapsed >= UnrollSeconds ? 1 : EaseOutCubic(elapsed / UnrollSeconds);
            double fireStart = Math.Max(wholeSeconds, UnrollSeconds);
            if (elapsed < fireStart) return new ParchmentBannerFrame(unroll, 0, true, false);
            double burning = (elapsed - fireStart) / BurnSeconds;
            if (burning >= 1) return new ParchmentBannerFrame(1, BurnEnd, false, true);
            return new ParchmentBannerFrame(1, burning * BurnEnd, false, false);
        }

        private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);
    }
}
