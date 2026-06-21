using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 2.10 — fast EditMode property tests for the extracted ChainingResolver POCO. Reproduces the Story 1.5
    /// order-varied corpus: the membership SET must be order-INDIFFERENT; the list SEQUENCE must be first-occurrence
    /// insertion order; dedup under interleaving.
    /// </summary>
    [Category("ChainingResolver")]
    public class ChainingResolverTests
    {
        private static List<ulong> Resolve(params ulong[] additions) =>
            new List<ulong>(new ChainingResolver().Resolve(additions));

        // ── MUST be indifferent: membership set invariant across permutations ──

        [Test]
        public void Membership_IsOrderIndifferent_AcrossPermutations()
        {
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Resolve(10, 20, 30));
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Resolve(30, 20, 10));
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Resolve(20, 10, 30));
        }

        // ── MUST matter: the list sequence is first-occurrence order ──

        [Test]
        public void ListSequence_IsFirstOccurrenceOrder_Forward()
        {
            Assert.AreEqual(new List<ulong> { 10, 20, 30 }, Resolve(10, 20, 30));
        }

        [Test]
        public void ListSequence_IsFirstOccurrenceOrder_Reverse()
        {
            Assert.AreEqual(new List<ulong> { 30, 20, 10 }, Resolve(30, 20, 10));
        }

        // ── Dedup ──

        [Test]
        public void Dedup_InterleavedDuplicates_FirstOccurrenceKept()
        {
            Assert.AreEqual(new List<ulong> { 10, 20, 30 }, Resolve(10, 20, 10, 30, 20));
        }

        [Test]
        public void Empty_ReturnsEmpty()
        {
            Assert.That(Resolve(), Is.Empty);
        }

        // ── IsNewMember (the per-addition adapter decision) ──

        [Test]
        public void IsNewMember_AbsentCandidate_True()
        {
            Assert.IsTrue(new ChainingResolver().IsNewMember(new List<ulong> { 1, 2 }, 3));
        }

        [Test]
        public void IsNewMember_PresentCandidate_False()
        {
            Assert.IsFalse(new ChainingResolver().IsNewMember(new List<ulong> { 1, 2 }, 2));
        }

        [Test]
        public void IsNewMember_EmptyMembership_True()
        {
            Assert.IsTrue(new ChainingResolver().IsNewMember(new List<ulong>(), 1));
        }

        // ───────────────── Null inputs / zero-as-candidate edges (added coverage) ─────────────────

        [Test]
        public void Resolve_NullAdditions_ReturnsEmptyList()
        {
            // The null guard (ChainingResolver.cs line 22) returns an empty list, never null.
            Assert.That(new ChainingResolver().Resolve(null), Is.Empty);
        }

        [Test]
        public void IsNewMember_NullMembership_ReturnsTrue()
        {
            // The null guard (ChainingResolver.cs line 38) treats a null membership as "candidate is new".
            Assert.IsTrue(new ChainingResolver().IsNewMember(null, 5));
        }

        [Test]
        public void Resolve_ZeroIsANormalCandidate_NotASentinel()
        {
            // 0 is a valid ulong client id, deduped like any other: [0,1,0,2] → [0,1,2].
            Assert.AreEqual(new List<ulong> { 0, 1, 2 }, Resolve(0, 1, 0, 2));
        }
    }
}
