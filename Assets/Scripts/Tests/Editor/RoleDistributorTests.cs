using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 3.3 — fast EditMode property tests for the pure <see cref="RoleDistributor"/>. The live
    /// (Unity-seeded) assignment stays pinned by the PlayMode RoleAssignmentGoldenMasterTests; these use a
    /// scripted stub RNG to assert the EXACT selection mechanics (frozen-order indexing, shared depletion,
    /// fake-then-real order, the canBeFake filter, the empty-fake break, no input mutation).
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
            // counts [2,2,1], no fakes, 5 reals, rng always 0 → first available each draw.
            var result = Distributor.Distribute(
                new[] { 2, 2, 1 }, new[] { false, false, false }, fakeCount: 0, realCount: 5,
                new StubRandomProvider(0, 0, 0, 0, 0));

            CollectionAssert.IsEmpty(result.FakeRoleIndices);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 2 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeThenReal_ShareTheSameDepletingCounts()
        {
            // counts [2,2] both fakeable, 2 fakes + 2 reals, rng always 0. Fakes drain role 0; reals fall to role 1.
            var result = Distributor.Distribute(
                new[] { 2, 2 }, new[] { true, true }, fakeCount: 2, realCount: 2,
                new StubRandomProvider(0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 0, 0 }, result.FakeRoleIndices);
            CollectionAssert.AreEqual(new[] { 1, 1 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeLoop_OnlyDrawsFromCanBeFakeSubset()
        {
            // role 0 NOT fakeable, role 1 fakeable. 1 fake + 3 reals, rng always 0.
            var result = Distributor.Distribute(
                new[] { 2, 2 }, new[] { false, true }, fakeCount: 1, realCount: 3,
                new StubRandomProvider(0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 1 }, result.FakeRoleIndices, "Fake must come from the canBeFake subset (role 1).");
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, result.RealRoleIndices);
        }

        [Test]
        public void FakeLoop_BreaksWhenNoFakeableRoleRemains()
        {
            // No role is fakeable → the fake loop breaks immediately, fakes empty; reals proceed.
            var result = Distributor.Distribute(
                new[] { 1, 1 }, new[] { false, false }, fakeCount: 2, realCount: 2,
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
                new[] { 1, 1, 1 }, new[] { false, false, false }, fakeCount: 0, realCount: 3,
                new StubRandomProvider(2, 1, 0));

            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, result.RealRoleIndices);
        }

        [Test]
        public void DoesNotMutateCallerInput()
        {
            var counts = new List<int> { 2, 2, 1 };
            var canBeFake = new List<bool> { true, false, true };

            Distributor.Distribute(counts, canBeFake, fakeCount: 1, realCount: 4, new StubRandomProvider(0, 0, 0, 0, 0));

            CollectionAssert.AreEqual(new[] { 2, 2, 1 }, counts, "Distribute must not mutate the caller's counts (Story 3.2 finding).");
            CollectionAssert.AreEqual(new[] { true, false, true }, canBeFake);
        }

        [Test]
        public void SeededProvider_SameSeed_ReproducesDistribution()
        {
            int[] counts = { 2, 2, 1 };
            bool[] fake = { true, true, false };

            var a = Distributor.Distribute(counts, fake, 2, 3, new SeededRandomProvider(123));
            var b = Distributor.Distribute(counts, fake, 2, 3, new SeededRandomProvider(123));

            CollectionAssert.AreEqual(a.FakeRoleIndices, b.FakeRoleIndices);
            CollectionAssert.AreEqual(a.RealRoleIndices, b.RealRoleIndices);
        }
    }
}
