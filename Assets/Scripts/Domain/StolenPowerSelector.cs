using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, engine-free selection kernel for Ugues' "Marque d'Hurluberluges" power (story
    /// role-ugues-marque-hurluberluges). Given a flat count of eligible candidate powers, it draws up to
    /// <paramref name="pickCount"/> DISTINCT candidate indices via the injected <see cref="IRandomProvider"/>.
    ///
    /// The engine-coupled filtering (chosen faction, non-passive, exclude Ugues himself) happens in the
    /// adapter (the power) BEFORE calling here — this kernel only owns the "pick N distinct at random, capped
    /// at what exists" mechanic so it can be deterministically EditMode-tested like <see cref="RoleDistributor"/>.
    /// </summary>
    public static class StolenPowerSelector
    {
        /// <summary>
        /// Returns up to <paramref name="pickCount"/> distinct indices in <c>[0, candidateCount)</c>, in draw
        /// order. If <paramref name="candidateCount"/> &lt;= <paramref name="pickCount"/> every index is
        /// returned (still shuffled by draw order). Never returns duplicates. Does not mutate external state.
        /// </summary>
        public static List<int> SelectDistinct(int candidateCount, int pickCount, IRandomProvider rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
            if (pickCount < 0) throw new ArgumentOutOfRangeException(nameof(pickCount));

            int take = Math.Min(pickCount, candidateCount);
            var result = new List<int>(take);
            if (take == 0) return result;

            // Partial Fisher-Yates draw from a local pool of every candidate index — removal guarantees
            // distinctness and rng.Next(pool.Count) stays inside [0, remaining) each step.
            var pool = new List<int>(candidateCount);
            for (int i = 0; i < candidateCount; i++) pool.Add(i);

            for (int i = 0; i < take; i++)
            {
                int j = rng.Next(pool.Count);
                result.Add(pool[j]);
                pool.RemoveAt(j);
            }
            return result;
        }

        /// <summary>
        /// Filters <paramref name="candidates"/> to the ones Ugues may steal (see <see cref="PowerCandidate.IsEligible"/>)
        /// then draws up to <paramref name="pickCount"/> distinct of them via <paramref name="rng"/>. Returns the
        /// ORIGINAL indices (into <paramref name="candidates"/>) of the chosen powers, in draw order — so the caller
        /// can map them straight back to its parallel live-power list. Empty if nothing is eligible.
        /// </summary>
        public static List<int> SelectStealable(IReadOnlyList<PowerCandidate> candidates, int pickCount, IRandomProvider rng)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var eligible = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].IsEligible)
                {
                    eligible.Add(i);
                }
            }

            var picked = SelectDistinct(eligible.Count, pickCount, rng);
            var result = new List<int>(picked.Count);
            foreach (int _localIndex in picked)
            {
                result.Add(eligible[_localIndex]);
            }
            return result;
        }
    }

    /// <summary>
    /// Engine-free descriptor of one candidate power for <see cref="StolenPowerSelector.SelectStealable"/>. The
    /// adapter (PMarqueHurluberluges) builds one per live power so the eligibility rule stays pure and testable:
    /// a power is stealable iff its owner is a CHOSEN-faction character other than Ugues, and the power is an
    /// active (non-passive) power that is not itself a copy (of any provenance — a copy is never re-copiable).
    /// </summary>
    public readonly struct PowerCandidate
    {
        public readonly bool OwnerIsChosen;
        public readonly bool OwnerIsUgues;
        public readonly bool IsPassive;
        public readonly bool IsCopiedPower;

        public PowerCandidate(bool ownerIsChosen, bool ownerIsUgues, bool isPassive, bool isCopiedPower)
        {
            OwnerIsChosen = ownerIsChosen;
            OwnerIsUgues = ownerIsUgues;
            IsPassive = isPassive;
            IsCopiedPower = isCopiedPower;
        }

        public bool IsEligible => OwnerIsChosen && !OwnerIsUgues && !IsPassive && !IsCopiedPower;
    }
}
