using System.Collections.Generic;
using Characters;
using MessageSystem;
using NUnit.Framework;

namespace Tests.Editor
{
    public class TurnStatCalculatorTests
    {
        private static CharacterFactionState Chosen(bool _corrupted = false, bool _fake = false)
            => new(FactionType.chosen, _fake, _corrupted);

        private static CharacterFactionState Anomaly(bool _corrupted = false, bool _fake = false)
            => new(FactionType.anomaly, _fake, _corrupted);

        [Test]
        public void Compute_NonAnomalyTotal_ExcludesAnomaliesAndFakes()
        {
            var _chars = new List<CharacterFactionState>
            {
                Chosen(),
                Chosen(),
                Anomaly(),                 // excluded: anomaly
                Chosen(_fake: true),       // excluded: fake
            };
            TurnStat _stat = TurnStatCalculator.Compute(3, _chars, false, 0);
            Assert.AreEqual(2, _stat.nonAnomalyTotal);
        }

        [Test]
        public void Compute_CorruptedCount_OnlyCorruptedNonAnomalyReals()
        {
            var _chars = new List<CharacterFactionState>
            {
                Chosen(_corrupted: true),
                Chosen(_corrupted: false),
                Anomaly(_corrupted: true),              // excluded: anomaly
                Chosen(_corrupted: true, _fake: true),  // excluded: fake
            };
            TurnStat _stat = TurnStatCalculator.Compute(1, _chars, false, 0);
            Assert.AreEqual(1, _stat.corruptedCount);
            Assert.AreEqual(2, _stat.nonAnomalyTotal);
        }

        [Test]
        public void Compute_NoRobot_SetsSentinelIgnoringTargeterCount()
        {
            TurnStat _stat = TurnStatCalculator.Compute(2, new List<CharacterFactionState>(), false, 5);
            Assert.AreEqual(-1, _stat.robotTargetCount);
            Assert.IsFalse(_stat.hasRobot);
        }

        [Test]
        public void Compute_WithRobot_UsesTargeterCount()
        {
            TurnStat _stat = TurnStatCalculator.Compute(2, new List<CharacterFactionState>(), true, 3);
            Assert.AreEqual(3, _stat.robotTargetCount);
            Assert.IsTrue(_stat.hasRobot);
        }

        [Test]
        public void Compute_StampsDay()
        {
            TurnStat _stat = TurnStatCalculator.Compute(7, new List<CharacterFactionState>(), true, 0);
            Assert.AreEqual(7, _stat.day);
        }

        [Test]
        public void DedupByDay_LastWinsPerDay_PreservesFirstSeenOrder()
        {
            var _stats = new List<TurnStat>
            {
                new(1, 1, 6, 0),
                new(2, 2, 6, 1),
                new(2, 2, 6, 1), // duplicate day 2 (late-joiner NetworkList replay)
            };
            List<TurnStat> _result = TurnStatCalculator.DedupByDay(_stats);
            Assert.AreEqual(2, _result.Count);
            Assert.AreEqual(1, _result[0].day);
            Assert.AreEqual(2, _result[1].day);
        }

        [Test]
        public void DedupByDay_LaterEntryOverwritesEarlierSameDay()
        {
            var _stats = new List<TurnStat>
            {
                new(1, 1, 6, 0),
                new(1, 3, 6, 2), // same day, updated values win
            };
            List<TurnStat> _result = TurnStatCalculator.DedupByDay(_stats);
            Assert.AreEqual(1, _result.Count);
            Assert.AreEqual(3, _result[0].corruptedCount);
            Assert.AreEqual(2, _result[0].robotTargetCount);
        }
    }
}
