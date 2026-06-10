using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure chaining-membership resolution (Story 2.10 — extracted from ChainingManager.AddCharacterToChainingList).
    /// Dedup by membership, first-occurrence insertion order preserved. Decision-only (NFR4): no NGO, no NetworkList,
    /// no RPC — the adapter applies the decision to its replicated list.
    ///
    /// Ordering contract (Story 1.5): the membership SET is order-INDIFFERENT (must hold — it drives
    /// Character.isChained); the list SEQUENCE is first-occurrence order (observable, though no winning condition
    /// reads list position).
    /// </summary>
    public sealed class ChainingResolver
    {
        /// <summary>Batch resolution: dedup, first-occurrence insertion order.</summary>
        public IReadOnlyList<ulong> Resolve(IEnumerable<ulong> additionsInOrder)
        {
            var membership = new List<ulong>();
            if (additionsInOrder == null)
            {
                return membership;
            }

            foreach (var id in additionsInOrder)
            {
                if (IsNewMember(membership, id))
                {
                    membership.Add(id);
                }
            }
            return membership;
        }

        /// <summary>The per-addition decision the live adapter applies: append iff not already a member.</summary>
        public bool IsNewMember(IReadOnlyList<ulong> currentMembership, ulong candidate)
        {
            if (currentMembership == null)
            {
                return true;
            }

            for (int i = 0; i < currentMembership.Count; i++)
            {
                if (currentMembership[i] == candidate)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
