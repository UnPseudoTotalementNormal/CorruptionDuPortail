using CorruptionDuPortail.Domain;
using Random = UnityEngine.Random;

namespace GameLogic
{
    /// <summary>
    /// Production <see cref="IRandomProvider"/> backed by <c>UnityEngine.Random</c> (Story 3.1).
    /// <c>Next(maxExclusive)</c> maps to <c>Random.Range(0, maxExclusive)</c> — the exact call the
    /// live role-assignment path uses today (RoleAttributionState.cs:102). Lives in the Game adapter
    /// so the engine reference never crosses into Domain (NFR2).
    ///
    /// Created here as the additive seam; it is wired into the prod role-assignment path in Story 3.3
    /// alongside the <c>RoleDistributor</c> extraction (this story changes no behavior).
    /// </summary>
    public sealed class UnityRandomProvider : IRandomProvider
    {
        public int Next(int maxExclusive)
        {
            return Random.Range(0, maxExclusive);
        }
    }
}
