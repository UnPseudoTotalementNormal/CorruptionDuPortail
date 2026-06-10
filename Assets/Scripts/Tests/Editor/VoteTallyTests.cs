using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 2.9 — fast EditMode tests for the extracted VoteTally POCO, reproducing the Story 1.5 golden corpus
    /// directly over VoteCount lists (no NGO host). Insertion order is the tie-break contract (stable sort).
    /// </summary>
    [Category("VoteTally")]
    public class VoteTallyTests
    {
        private const ulong Skip = 99999; // stand-in for SKIP_VOTE_ID

        private static ulong Resolve(params VoteCount[] votes) =>
            new VoteTally().Resolve(new List<VoteCount>(votes), Skip);

        [Test]
        public void ClearSingleWinner_ReturnsThatPlayer()
        {
            // p1=3, p2=0, skip=0 → unique top → p1
            Assert.AreEqual(1UL, Resolve(new VoteCount(1, 3), new VoteCount(2, 0), new VoteCount(Skip, 0)));
        }

        [Test]
        public void TieForTop_ReturnsSkip()
        {
            // p1=1, p2=1 → two-way tie at the top → skip
            Assert.AreEqual(Skip, Resolve(new VoteCount(1, 1), new VoteCount(2, 1), new VoteCount(Skip, 0)));
        }

        [Test]
        public void AbstentionStrictMax_ReturnsSkip_ViaGuard()
        {
            // skip=3 strict max, but the != skip guard sends it to skip anyway
            Assert.AreEqual(Skip, Resolve(new VoteCount(1, 1), new VoteCount(2, 0), new VoteCount(Skip, 3)));
        }

        [Test]
        public void SingleVoter_ReturnsThatPlayer()
        {
            // p2=1 unique top → p2
            Assert.AreEqual(2UL, Resolve(new VoteCount(1, 0), new VoteCount(2, 1), new VoteCount(Skip, 0)));
        }

        [Test]
        public void ZeroVoters_AllTiedAtZero_ReturnsSkip()
        {
            Assert.AreEqual(Skip, Resolve(new VoteCount(1, 0), new VoteCount(2, 0), new VoteCount(Skip, 0)));
        }

        [Test]
        public void EmptyBuckets_ReturnsSkip_Defensive()
        {
            Assert.AreEqual(Skip, new VoteTally().Resolve(new List<VoteCount>(), Skip));
        }

        [Test]
        public void StableSort_TieOutcomeIndependentOfInsertionOrder()
        {
            // Both orders are a top-tie → skip, regardless of which tied entry is enumerated first.
            Assert.AreEqual(Skip, Resolve(new VoteCount(1, 2), new VoteCount(2, 2)));
            Assert.AreEqual(Skip, Resolve(new VoteCount(2, 2), new VoteCount(1, 2)));
        }

        [Test]
        public void UniqueTop_AmongHigherCounts_ReturnsThatPlayer()
        {
            // p3=5 strict max over p1=2, p2=2 → p3
            Assert.AreEqual(3UL, Resolve(new VoteCount(1, 2), new VoteCount(2, 2), new VoteCount(3, 5), new VoteCount(Skip, 0)));
        }
    }
}
