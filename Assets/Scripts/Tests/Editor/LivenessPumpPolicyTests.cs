using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story B1 — the pump stall-guard as a pure policy (arch-liveness-heartbeat §8.1/§8.8): a starved
    /// pump (abnormally large elapsed: GC / scene-load / timeScale=0 / focus-loss) must SKIP its tick so a
    /// single host hitch never counts as a missed beat for the whole match.
    /// </summary>
    [Category("Liveness")]
    public class LivenessPumpPolicyTests
    {
        private const long Period = 1000; // arbitrary clock-tick unit for one beat window

        // Pump_AbnormalElapsed_SkipsTick (stall-guard)
        [Test]
        public void Pump_AbnormalElapsed_SkipsTick()
        {
            // Overslept far past the beat window (e.g. a multi-second GC / scene-load stall).
            Assert.IsTrue(LivenessPumpPolicy.ShouldSkipTick(Period * 10, Period));
            // Just past the stall factor boundary.
            Assert.IsTrue(LivenessPumpPolicy.ShouldSkipTick(Period * LivenessPumpPolicy.StallFactor + 1, Period));
        }

        [Test]
        public void Pump_NormalElapsed_DoesNotSkipTick()
        {
            // A wake at roughly the beat period is normal — tick.
            Assert.IsFalse(LivenessPumpPolicy.ShouldSkipTick(Period, Period));
            // Mild jitter under the stall factor is still normal.
            Assert.IsFalse(LivenessPumpPolicy.ShouldSkipTick(Period + (Period / 2), Period));
            // Exactly at the factor boundary is not yet a stall (strict >).
            Assert.IsFalse(LivenessPumpPolicy.ShouldSkipTick(Period * LivenessPumpPolicy.StallFactor, Period));
        }

        [Test]
        public void Pump_NonPositivePeriod_DoesNotSkip()
        {
            // Misconfigured period must not silently swallow every tick.
            Assert.IsFalse(LivenessPumpPolicy.ShouldSkipTick(Period * 100, 0));
        }
    }
}
