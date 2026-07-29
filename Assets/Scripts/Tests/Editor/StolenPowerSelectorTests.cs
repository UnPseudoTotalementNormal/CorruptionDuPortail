using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode property tests for the pure <see cref="StolenPowerSelector"/> — the random-distinct-pick
    /// kernel behind Ugues' Marque d'Hurluberluges. A scripted stub RNG pins exact draws; a seeded provider
    /// proves determinism. The engine-coupled faction/passive filtering is covered by the PlayMode flow test.
    /// </summary>
    [Category("StolenPowerSelector")]
    public class StolenPowerSelectorTests
    {
        // Returns scripted indices in order (then 0 if it runs dry) — lets a test pin the exact picks.
        private sealed class StubRandomProvider : IRandomProvider
        {
            private readonly Queue<int> _scripted;
            public StubRandomProvider(params int[] values) => _scripted = new Queue<int>(values);
            public int Next(int maxExclusive) => _scripted.Count > 0 ? _scripted.Dequeue() : 0;
        }

        [Test]
        public void PicksThreeDistinct_WhenPoolLargerThanPickCount()
        {
            // pool = [0,1,2,3,4]; scripted draws: index 0 (→0), then index 0 of remaining [1,2,3,4] (→1),
            // then index 2 of remaining [2,3,4] (→4).
            var picks = StolenPowerSelector.SelectDistinct(candidateCount: 5, pickCount: 3,
                new StubRandomProvider(0, 0, 2));

            CollectionAssert.AreEqual(new[] { 0, 1, 4 }, picks);
        }

        [Test]
        public void ReturnsAllCandidates_WhenPoolSmallerThanPickCount()
        {
            var picks = StolenPowerSelector.SelectDistinct(candidateCount: 2, pickCount: 3,
                new StubRandomProvider(0, 0));

            Assert.AreEqual(2, picks.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, picks);
        }

        [Test]
        public void ReturnsEmpty_WhenNoCandidates()
        {
            var picks = StolenPowerSelector.SelectDistinct(candidateCount: 0, pickCount: 3,
                new StubRandomProvider(0));

            CollectionAssert.IsEmpty(picks);
        }

        [Test]
        public void NeverReturnsDuplicates_AndStaysInRange()
        {
            var picks = StolenPowerSelector.SelectDistinct(candidateCount: 8, pickCount: 3,
                new StubRandomProvider(7, 3, 1));

            Assert.AreEqual(3, picks.Count);
            Assert.AreEqual(picks.Count, picks.Distinct().Count(), "picks must be distinct");
            Assert.IsTrue(picks.All(i => i >= 0 && i < 8), "every pick must be in [0, candidateCount)");
        }

        [Test]
        public void IsDeterministic_ForAGivenSeed()
        {
            var a = StolenPowerSelector.SelectDistinct(6, 3, new SeededRandomProvider(1234));
            var b = StolenPowerSelector.SelectDistinct(6, 3, new SeededRandomProvider(1234));

            CollectionAssert.AreEqual(a, b);
        }

        // --- SelectStealable (filter + pick) — Ugues eligibility rule (AC 7) ---

        // ownerIsChosen, ownerIsUgues, isPassive, isCopiedPower
        private static PowerCandidate Eligible() => new PowerCandidate(true, false, false, false);
        private static PowerCandidate NotChosen() => new PowerCandidate(false, false, false, false);
        private static PowerCandidate Passive() => new PowerCandidate(true, false, true, false);
        private static PowerCandidate OwnedByUgues() => new PowerCandidate(true, true, false, false);
        private static PowerCandidate AlreadyCopied() => new PowerCandidate(true, false, false, true);

        [Test]
        public void SelectStealable_ReturnsOriginalIndices_OfEligibleOnly()
        {
            // indices:   0 elig, 1 anomaly, 2 passive, 3 elig, 4 ugues, 5 stolen, 6 elig
            var candidates = new List<PowerCandidate>
            {
                Eligible(), NotChosen(), Passive(), Eligible(), OwnedByUgues(), AlreadyCopied(), Eligible()
            };

            // 3 eligible → all taken; stub draws 0,0,0 walk the eligible subset in order [0,3,6].
            var picks = StolenPowerSelector.SelectStealable(candidates, 3, new StubRandomProvider(0, 0, 0));

            CollectionAssert.AreEqual(new[] { 0, 3, 6 }, picks);
        }

        [Test]
        public void SelectStealable_ExcludesNonChosen_Passive_Self_AndAlreadyCopied()
        {
            var candidates = new List<PowerCandidate>
            {
                NotChosen(), Passive(), OwnedByUgues(), AlreadyCopied(), Eligible()
            };

            var picks = StolenPowerSelector.SelectStealable(candidates, 3, new StubRandomProvider(0));

            CollectionAssert.AreEqual(new[] { 4 }, picks); // only the single eligible power
        }

        [Test]
        public void SelectStealable_CapsAtThree_AmongManyEligible()
        {
            var candidates = new List<PowerCandidate>
            {
                Eligible(), Eligible(), Eligible(), Eligible(), Eligible()
            };

            var picks = StolenPowerSelector.SelectStealable(candidates, 3, new StubRandomProvider(0, 0, 0));

            Assert.AreEqual(3, picks.Count);
            Assert.AreEqual(picks.Count, picks.Distinct().Count(), "picks must be distinct");
            Assert.IsTrue(picks.All(i => i >= 0 && i < candidates.Count));
        }

        [Test]
        public void SelectStealable_ReturnsEmpty_WhenNothingEligible()
        {
            var candidates = new List<PowerCandidate> { NotChosen(), Passive(), OwnedByUgues(), AlreadyCopied() };

            var picks = StolenPowerSelector.SelectStealable(candidates, 3, new StubRandomProvider(0));

            CollectionAssert.IsEmpty(picks);
        }

        // --- Luma « Mélange des cartes » : copie UN pouvoir actif d'un rôle élu absent (pickCount 1).
        // Luma construit ses PowerCandidate avec ownerIsChosen:true / ownerIsUgues:false, donc IsEligible se
        // réduit à (!isPassive && !isCopiedPower). Le tirage réutilise le même kernel avec pickCount 1.

        [Test]
        public void SelectStealable_Luma_PicksExactlyOneActive_SkippingPassives()
        {
            // indices: 0 passif, 1 actif, 2 passif, 3 actif → éligibles [1,3], stub(0) prend le 1er.
            var candidates = new List<PowerCandidate> { Passive(), Eligible(), Passive(), Eligible() };

            var picks = StolenPowerSelector.SelectStealable(candidates, 1, new StubRandomProvider(0));

            CollectionAssert.AreEqual(new[] { 1 }, picks);
        }

        [Test]
        public void SelectStealable_Luma_ReturnsEmpty_WhenRoleHasOnlyPassivePowers()
        {
            var candidates = new List<PowerCandidate> { Passive(), Passive() };

            var picks = StolenPowerSelector.SelectStealable(candidates, 1, new StubRandomProvider(0));

            CollectionAssert.IsEmpty(picks);
        }
    }
}
