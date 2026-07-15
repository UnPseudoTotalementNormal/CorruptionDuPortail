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
    /// Reproduces the two-loop selection: a fake loop (draws from the fakeable subset = max − forced) then a real loop,
    /// both drawing from the SAME depleting per-role counts via <see cref="IRandomProvider"/> in order. Indexes
    /// into the caller's frozen pool order (Story 1.1) — no Dictionary.Keys drift. Decision-only (NFR4): no NGO,
    /// no RoleDataObject, no Random. Works on an internal copy of the counts — it never mutates the caller's input
    /// (closes the Story 3.2 shared-mutation finding).
    /// </summary>
    public sealed class RoleDistributor
    {
        /// <param name="initialCounts">max (pool cap) per role, in frozen pool order (a 0 means never available).</param>
        /// <param name="forced">guaranteed minimum reals per role, same order (forced ≤ max). A role's fakeable
        /// capacity is max − forced: the forced copies are excluded from the fake pool, so the real loop (which
        /// consumes every remaining slot) always places at least `forced` reals of that role. Which characters
        /// receive them stays RNG-random. forced == 0 ⇒ whole pool fakeable (≡ old canBeFake=true); forced == max
        /// ⇒ nothing fakeable (≡ old canBeFake=false) — so under the canBeFake→forced migration the fake-eligible
        /// set is byte-identical to the old bool filter and the golden masters are unchanged.</param>
        /// <param name="fakeCount">number of fake characters to assign first (drawn from the fakeable subset).</param>
        /// <param name="realCount">number of real characters to assign after the fakes.</param>
        public RoleDistribution Distribute(
            IReadOnlyList<int> initialCounts,
            IReadOnlyList<int> forced,
            int fakeCount,
            int realCount,
            IRandomProvider rng)
        {
            int k = initialCounts.Count;
            var remaining = new int[k];
            var fakeable = new int[k]; // copies eligible to become a fake = max − forced (forced copies stay real)
            for (int i = 0; i < k; i++)
            {
                remaining[i] = initialCounts[i];
                int _forced = i < forced.Count ? forced[i] : 0;
                int _fakeable = initialCounts[i] - _forced;
                fakeable[i] = _fakeable > 0 ? _fakeable : 0;
            }

            var fakeIndices = new List<int>();
            for (int f = 0; f < fakeCount; f++)
            {
                List<int> available = AvailableFake(fakeable);
                if (available.Count == 0) // mirrors the live `if (_fakeRoles.Count == 0) break;`
                {
                    break;
                }

                int pick = available[rng.Next(available.Count)];
                fakeIndices.Add(pick);
                fakeable[pick] -= 1; // one fewer fakeable copy
                remaining[pick] -= 1; // and one fewer pool slot left for the real loop
            }

            var realIndices = new List<int>();
            for (int r = 0; r < realCount; r++)
            {
                // No empty-guard here — mirrors the live real loop, which indexes available[Range(0,0)]
                // and throws if the pool is exhausted. Behaviour preserved as-is. Because the fake loop can
                // only touch the max−forced fakeable copies, every role's `forced` copies survive into here.
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

        // The still-fakeable subset: roles with an unreserved (max − forced) copy left. Forced copies are never
        // in `fakeable`, so they can never be drawn as a fake.
        private static List<int> AvailableFake(int[] fakeable)
        {
            var list = new List<int>();
            for (int i = 0; i < fakeable.Length; i++)
            {
                if (fakeable[i] > 0)
                {
                    list.Add(i);
                }
            }
            return list;
        }
    }
}
