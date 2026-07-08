using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story B1 — the seconds-&gt;beats conversion (arch-liveness-heartbeat §8.8 case 9): ceil rounding
    /// (never declare lost too early) and the floor of 2 (a threshold of 1 is a hair-trigger).
    /// </summary>
    [Category("Liveness")]
    public class LivenessThresholdTests
    {
        // 9 (ceil)
        [Test]
        public void ThresholdConversion_SecondsToBeats_RoundsAsCeil()
        {
            // 5s / 1s = 5 exactly.
            Assert.AreEqual(5, LivenessThreshold.FromSeconds(5.0, 1.0));
            // 5.1s / 1s = 5.1 -> ceil 6 (round toward more patience, never lose early).
            Assert.AreEqual(6, LivenessThreshold.FromSeconds(5.1, 1.0));
            // 6s / 2.5s = 2.4 -> ceil 3.
            Assert.AreEqual(3, LivenessThreshold.FromSeconds(6.0, 2.5));
            // Fractional period: 3s / 0.5s = 6 exactly.
            Assert.AreEqual(6, LivenessThreshold.FromSeconds(3.0, 0.5));
        }

        // 9 (floor of 2)
        [Test]
        public void ThresholdConversion_FloorsAtTwo()
        {
            // 1s / 1s = 1 -> floored to 2.
            Assert.AreEqual(2, LivenessThreshold.FromSeconds(1.0, 1.0));
            // 0.3s / 1s = ceil 1 -> floored to 2.
            Assert.AreEqual(2, LivenessThreshold.FromSeconds(0.3, 1.0));
            // Zero timeout -> ceil 0 -> floored to 2.
            Assert.AreEqual(2, LivenessThreshold.FromSeconds(0.0, 1.0));
        }

        [Test]
        public void ThresholdConversion_NonPositivePeriod_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LivenessThreshold.FromSeconds(5.0, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LivenessThreshold.FromSeconds(5.0, -1.0));
        }

        [Test]
        public void LivenessConfig_Default_YieldsThresholdFive()
        {
            // Locked v1 defaults (§8.6): 5s @ 1 Hz -> threshold 5. The tracker only ever sees this int.
            var config = LivenessConfig.Default;
            Assert.AreEqual(5, config.Threshold);
        }
    }
}
