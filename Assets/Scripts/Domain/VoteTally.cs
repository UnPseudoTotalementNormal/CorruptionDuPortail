using System.Collections.Generic;
using System.Linq;

namespace CorruptionDuPortail.Domain
{
    /// <summary>One candidate (a player id, or the skip-vote id) and how many votes it received.</summary>
    public readonly struct VoteCount
    {
        public ulong Candidate { get; }
        public int Count { get; }

        public VoteCount(ulong candidate, int count)
        {
            Candidate = candidate;
            Count = count;
        }
    }

    /// <summary>
    /// Pure vote-count → outcome resolution (Story 2.9 — extracted from VoteState.OnEndStateServer). Decision-only
    /// (NFR4): returns the winning client-id, or <paramref name="skipVoteId"/> on a tie / when skip has the top
    /// count. No state mutation, no RPC — the adapter applies the result.
    ///
    /// Insertion order is the tie-break contract (Story 1.1 §3b C): <see cref="System.Linq.Enumerable.OrderByDescending"/>
    /// is a STABLE sort, so candidates passed in the live dictionary's enumeration order resolve bit-for-bit with
    /// the legacy tally. (The tie case routes to skip regardless of which tied entry is first, so the OUTCOME is
    /// order-independent there — but the algorithm is mirrored exactly.)
    /// </summary>
    public sealed class VoteTally
    {
        public ulong Resolve(IReadOnlyList<VoteCount> votesInInsertionOrder, ulong skipVoteId)
        {
            if (votesInInsertionOrder == null || votesInInsertionOrder.Count == 0)
            {
                return skipVoteId;
            }

            var ordered = votesInInsertionOrder.OrderByDescending(v => v.Count).ToList();
            int topCount = ordered[0].Count;
            int numTopTied = ordered.Count(v => v.Count == topCount);

            if (numTopTied == 1 && ordered[0].Candidate != skipVoteId)
            {
                return ordered[0].Candidate;
            }

            return skipVoteId;
        }
    }
}
