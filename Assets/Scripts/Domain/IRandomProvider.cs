namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Seedable randomness port the core depends on instead of <c>UnityEngine.Random</c> (Story 3.1).
    /// Keeps the Domain engine-free (NFR2) and makes role distribution reproducible (Epic 3).
    ///
    /// Contract pinned by the live call site (RoleAttributionState.GiveRandomRole — Random.Range(0, count),
    /// Unity's int overload is max-EXCLUSIVE): <see cref="Next"/> returns a value in <c>[0, maxExclusive)</c>.
    /// </summary>
    public interface IRandomProvider
    {
        /// <summary>A non-negative random integer in <c>[0, maxExclusive)</c>. <c>Next(0)</c> returns 0.</summary>
        int Next(int maxExclusive);
    }
}
