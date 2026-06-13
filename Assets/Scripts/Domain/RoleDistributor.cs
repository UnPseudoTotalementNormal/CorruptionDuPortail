using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The role-distribution decision (Story 3.3). Each value is an index into the frozen role-pool
    /// order; the adapter maps it back to a RoleDataObject and applies the side effects. Decision-only (NFR4).
    /// </summary>
    public readonly struct RoleDistribution
    {
        /// <summary>Frozen-order role index drawn for each fake character, in creation order.</summary>
        public IReadOnlyList<int> FakeRoleIndices { get; }

        /// <summary>Frozen-order role index drawn for each real character, in processing order.</summary>
        public IReadOnlyList<int> RealRoleIndices { get; }

        public RoleDistribution(IReadOnlyList<int> fakeRoleIndices, IReadOnlyList<int> realRoleIndices)
        {
            FakeRoleIndices = fakeRoleIndices;
            RealRoleIndices = realRoleIndices;
        }
    }

    /// <summary>
    /// Pure, deterministic role distribution (Story 3.3 — extracted from RoleAttributionState.OnStartStateServer).
    /// Reproduces the two-loop selection exactly: a fake loop (draws from the canBeFake subset) then a real loop,
    /// both drawing from the SAME depleting per-role counts via <see cref="IRandomProvider"/> in order. Indexes
    /// into the caller's frozen pool order (Story 1.1) — no Dictionary.Keys drift. Decision-only (NFR4): no NGO,
    /// no RoleDataObject, no Random. Works on an internal copy of the counts — it never mutates the caller's input
    /// (closes the Story 3.2 shared-mutation finding).
    /// </summary>
    public sealed class RoleDistributor
    {
        /// <param name="initialCounts">roleToAttribute per role, in frozen pool order (a 0 means never available).</param>
        /// <param name="canBeFake">canBeFake per role, same order.</param>
        /// <param name="fakeCount">number of fake characters to assign first (drawn from the canBeFake subset).</param>
        /// <param name="realCount">number of real characters to assign after the fakes.</param>
        public RoleDistribution Distribute(
            IReadOnlyList<int> initialCounts,
            IReadOnlyList<bool> canBeFake,
            int fakeCount,
            int realCount,
            IRandomProvider rng)
        {
            int k = initialCounts.Count;
            var remaining = new int[k];
            for (int i = 0; i < k; i++)
            {
                remaining[i] = initialCounts[i];
            }

            var fakeIndices = new List<int>();
            for (int f = 0; f < fakeCount; f++)
            {
                List<int> available = AvailableFake(remaining, canBeFake);
                if (available.Count == 0) // mirrors the live `if (_fakeRoles.Count == 0) break;`
                {
                    break;
                }

                int pick = available[rng.Next(available.Count)];
                fakeIndices.Add(pick);
                remaining[pick] -= 1;
            }

            var realIndices = new List<int>();
            for (int r = 0; r < realCount; r++)
            {
                // No empty-guard here — mirrors the live real loop, which indexes available[Range(0,0)]
                // and throws if the pool is exhausted. Behaviour preserved as-is.
                List<int> available = Available(remaining);
                int pick = available[rng.Next(available.Count)];
                realIndices.Add(pick);
                remaining[pick] -= 1;
            }

            return new RoleDistribution(fakeIndices, realIndices);
        }

        // Frozen order filtered to roles with remaining count > 0 (= the live _rolesToAttribute membership).
        private static List<int> Available(int[] remaining)
        {
            var list = new List<int>();
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0)
                {
                    list.Add(i);
                }
            }
            return list;
        }

        // The canBeFake subset still available (= the live _fakeRoles ∩ remaining).
        private static List<int> AvailableFake(int[] remaining, IReadOnlyList<bool> canBeFake)
        {
            var list = new List<int>();
            for (int i = 0; i < remaining.Length; i++)
            {
                if (canBeFake[i] && remaining[i] > 0)
                {
                    list.Add(i);
                }
            }
            return list;
        }
    }
}
