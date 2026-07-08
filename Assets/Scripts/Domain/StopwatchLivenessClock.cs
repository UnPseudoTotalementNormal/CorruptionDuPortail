using System.Diagnostics;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Production <see cref="ILivenessClock"/> backed by <see cref="Stopwatch.GetTimestamp"/> (QPC):
    /// high-resolution, monotonic, and unaffected by <c>timeScale</c>, pause, or wall-clock changes
    /// (arch-liveness-heartbeat §8.1). Engine-free — lives in Domain so the whole liveness core stays
    /// testable without UnityEngine.
    ///
    /// Returned ticks are Stopwatch ticks (unit = <see cref="Stopwatch.Frequency"/> per second), NOT
    /// <see cref="System.TimeSpan"/> ticks. The pump measures elapsed deltas in this same unit, so the
    /// absolute epoch is irrelevant.
    /// </summary>
    public sealed class StopwatchLivenessClock : ILivenessClock
    {
        public long NowTicks => Stopwatch.GetTimestamp();
    }
}
