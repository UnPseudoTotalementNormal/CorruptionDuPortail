using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 3.3 — fast EditMode property tests for the pure <see cref="RoleDistributor"/>. The live
    /// (Unity-seeded) assignment stays pinned by the PlayMode RoleAssignmentGoldenMasterTests; these use a
    /// scripted stub RNG to assert the EXACT selection mechanics (frozen-order indexing, shared depletion,
    /// fake-then-real order, the fakeable = max − forced filter, the empty-fake break, no input mutation) plus
    /// the max/forced reservation invariant: forced copies are excluded from the fake pool, so each role gets
    /// at least `forced` reals while which characters receive them stays RNG-random.
    /// </summary>
    [Category("RoleDistributor")]
    public class RoleDistributorTests
    {
        // Returns scripted indices in order (then 0 if it runs dry) — lets a test pin the exact picks.
        private sealed class StubRandomProvider : IRandomProvider
        {
            private readonly Queue<int> _scripted;
            public StubRandomProvider(params int[] values) => _scripted = new Queue<int>(values);
            public int Next(int maxExclusive) => _scripted.Count > 0 ? _scripted.Dequeue() : 0;
        }

        private static readonly RoleDistributor Distributor = new();

        [Test]
        public void RealLoop_AlwaysFirstAvailable_ExhaustsInFrozenOrder()
        {
            // counts [2,2,1], forced == max everywhere (nothing fakeable), no fakes, 5 reals, rng always 0.
            var result = Distributor.Distribute(
                new[] { 2, 2, 1 }, new[] { 2, 2, 1 }, fakeCount: 0, realCount: 5,
                new StubRandomProvider(0, 0, 0, 0, 0));

            CollectionAssert.IsEmpty(result.FakeRoleIndices);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 2 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeThenReal_ShareTheSameDepletingCounts()
        {
            // counts [2,2] fully fakeable (forced 0), 2 fakes + 2 reals, rng always 0. Fakes drain role 0; reals fall to role 1.
            var result = Distributor.Distribute(
                new[] { 2, 2 }, new[] { 0, 0 }, fakeCount: 2, realCount: 2,
                new StubRandomProvider(0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 0, 0 }, result.FakeRoleIndices);
            CollectionAssert.AreEqual(new[] { 1, 1 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeLoop_OnlyDrawsFromFakeableSubset()
        {
            // role 0 fully forced (forced==max, not fakeable), role 1 fakeable. 1 fake + 3 reals, rng always 0.
            var result = Distributor.Distribute(
                new[] { 2, 2 }, new[] { 2, 0 }, fakeCount: 1, realCount: 3,
                new StubRandomProvider(0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 1 }, result.FakeRoleIndices, "Fake must come from the fakeable subset (role 1).");
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeLoop_BreaksWhenNoFakeableRoleRemains()
        {
            // No role is fakeable (forced==max) → the fake loop breaks immediately, fakes empty; reals proceed.
            var result = Distributor.Distribute(
                new[] { 1, 1 }, new[] { 1, 1 }, fakeCount: 2, realCount: 2,
                new StubRandomProvider(0, 0));

            CollectionAssert.IsEmpty(result.FakeRoleIndices);
            CollectionAssert.AreEqual(new[] { 0, 1 }, result.RealRoleIndices);
        }

        [Test]
        public void RngIndexSelectsAcrossAvailable_NotRawRoleIndex()
        {
            // counts [1,1,1], 3 reals. rng picks the LAST available each time (Count-1).
            // r0: avail[0,1,2] pick idx2=role2; r1: avail[0,1] pick idx1=role1; r2: avail[0] pick idx0=role0.
            var result = Distributor.Distribute(
                new[] { 1, 1, 1 }, new[] { 1, 1, 1 }, fakeCount: 0, realCount: 3,
                new StubRandomProvider(2, 1, 0));

            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, result.RealRoleIndices);
        }

        [Test]
        public void DoesNotMutateCallerInput()
        {
            var counts = new List<int> { 2, 2, 1 };
            var forced = new List<int> { 0, 2, 0 };

            Distributor.Distribute(counts, forced, fakeCount: 1, realCount: 4, new StubRandomProvider(0, 0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 2, 2, 1 }, counts, "Distribute must not mutate the caller's counts (Story 3.2 finding).");
            CollectionAssert.AreEqual(new[] { 0, 2, 0 }, forced, "Distribute must not mutate the caller's forced list.");
        }

        [Test]
        public void SeededProvider_SameSeed_ReproducesDistribution()
        {
            int[] counts = { 2, 2, 1 };
            int[] forced = { 0, 0, 1 };

            var a = Distributor.Distribute(counts, forced, 2, 3, new SeededRandomProvider(123));
            var b = Distributor.Distribute(counts, forced, 2, 3, new SeededRandomProvider(123));

            CollectionAssert.AreEqual(a.FakeRoleIndices, b.FakeRoleIndices);
            CollectionAssert.AreEqual(a.RealRoleIndices, b.RealRoleIndices);
        }

        // ───────────────── max/forced reservation invariant (C2) ─────────────────

        [Test]
        public void ForcedRole_NeverFaked_AndItsReserveSurvivesToTheRealLoop()
        {
            // role 0: max 1, forced 1 → 0 fakeable copies. role 1: max 2, forced 0 → fakeable. 1 fake + 2 reals.
            // Even with rng preferring index 0, the fake CANNOT take role 0 (excluded from the fakeable subset),
            // so role 0's single copy is guaranteed to land on a real character.
            var result = Distributor.Distribute(
                new[] { 1, 2 }, new[] { 1, 0 }, fakeCount: 1, realCount: 2,
                new StubRandomProvider(0, 0, 0));

            CollectionAssert.AreEqual(new[] { 1 }, result.FakeRoleIndices, "The fake must be role 1 — role 0 is fully forced.");
            CollectionAssert.Contains(result.RealRoleIndices, 0, "role 0's forced copy must be dealt to a real character.");
        }

        [Test]
        public void ForcedEqualsMax_RoleIsFullyMandatory_AllCopiesReal()
        {
            // role 0: max 2, forced 2 (fully mandatory). role 1: max 1, forced 0 (fakeable). 1 fake + 2 reals.
            var result = Distributor.Distribute(
                new[] { 2, 1 }, new[] { 2, 0 }, fakeCount: 1, realCount: 2,
                new StubRandomProvider(0, 0, 0));

            CollectionAssert.AreEqual(new[] { 1 }, result.FakeRoleIndices, "Only role 1 can be faked.");
            CollectionAssert.AreEqual(new[] { 0, 0 }, result.RealRoleIndices, "Both forced copies of role 0 are real.");
        }

        [Test]
        public void PartialForced_ReservesExactlyForced_RestOfPoolIsFakeable()
        {
            // Single role, max 3, forced 1 → 1 guaranteed real, 2 fakeable copies. 2 fakes + 1 real.
            var result = Distributor.Distribute(
                new[] { 3 }, new[] { 1 }, fakeCount: 2, realCount: 1,
                new StubRandomProvider(0, 0, 0));

            CollectionAssert.AreEqual(new[] { 0, 0 }, result.FakeRoleIndices, "The 2 non-reserved copies (max − forced) become fakes.");
            CollectionAssert.AreEqual(new[] { 0 }, result.RealRoleIndices, "Exactly the 1 forced copy survives as a real.");
        }

        // ───────────────── Pool-exhaustion / zero-count edges ─────────────────

        [Test]
        public void RealLoop_PoolExhausted_ThrowsArgumentOutOfRangeException()
        {
            // counts [1,1,1] → only 3 reals can be drawn; the 4th draw hits an empty available list and indexes
            // available[rng.Next(0)] = available[0] on an empty List<int>. The real loop has NO empty-guard
            // (RoleDistributor real loop, "behaviour preserved as-is"), so it throws — pinned here.
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                Distributor.Distribute(
                    new[] { 1, 1, 1 }, new[] { 1, 1, 1 }, fakeCount: 0, realCount: 4,
                    new StubRandomProvider(0, 0, 0, 0)));
        }

        [Test]
        public void ZeroRealCount_ProducesEmptyRealIndices_FakesUnaffected()
        {
            // realCount = 0 → the real loop runs zero times; the fake loop still draws its single fake.
            var result = Distributor.Distribute(
                new[] { 2, 2 }, new[] { 0, 0 }, fakeCount: 1, realCount: 0,
                new StubRandomProvider(0));

            CollectionAssert.AreEqual(new[] { 0 }, result.FakeRoleIndices);
            CollectionAssert.IsEmpty(result.RealRoleIndices);
        }
    }
}
