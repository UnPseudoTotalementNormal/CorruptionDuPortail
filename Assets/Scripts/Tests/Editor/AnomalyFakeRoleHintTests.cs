using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// The anomalies learn as many fake roles as there are anomalies, the same ones for each (GD wording of the Mage
    /// Occulte / Abyss passives).
    /// </summary>
    [Category("Roles")]
    public class AnomalyFakeRoleHintTests
    {
        private sealed class StubRandomProvider : IRandomProvider
        {
            private readonly Queue<int> _scripted;
            public StubRandomProvider(params int[] values) => _scripted = new Queue<int>(values);
            public int Next(int maxExclusive) => _scripted.Count > 0 ? _scripted.Dequeue() : 0;
        }

        [Test]
        public void TwoAnomalies_LearnTheSameTwoFakes()
        {
            var pairs = AnomalyFakeRoleHint.Pick(new ulong[] { 3, 105 }, new ulong[] { 900, 901, 902 }, new StubRandomProvider(2, 0));

            CollectionAssert.AreEqual(new (ulong, ulong)[] { (3, 902), (3, 900), (105, 902), (105, 900) }, pairs);
        }

        [Test]
        public void FewerFakesThanAnomalies_EveryFakeIsKnown()
        {
            var pairs = AnomalyFakeRoleHint.Pick(new ulong[] { 1, 2, 4 }, new ulong[] { 900 }, new StubRandomProvider(0));

            CollectionAssert.AreEqual(new (ulong, ulong)[] { (1, 900), (2, 900), (4, 900) }, pairs);
        }

        [Test]
        public void NoFake_NothingToLearn()
        {
            CollectionAssert.IsEmpty(AnomalyFakeRoleHint.Pick(new ulong[] { 1, 2 }, new ulong[0], new StubRandomProvider()));
        }

        [Test]
        public void NoAnomaly_NobodyLearns()
        {
            CollectionAssert.IsEmpty(AnomalyFakeRoleHint.Pick(new ulong[0], new ulong[] { 900, 901 }, new StubRandomProvider()));
        }
    }
}
