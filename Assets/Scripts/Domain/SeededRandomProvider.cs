using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Deterministic, engine-free <see cref="IRandomProvider"/> backed by <see cref="System.Random"/> (Story 3.1).
    /// For a given seed it produces the same sequence across runs and machines — the reproducible source the
    /// Epic 3 role-assignment goldens (3.2) and the extracted <c>RoleDistributor</c> tests (3.3) consume.
    ///
    /// NOTE: its sequence is NOT identical to <c>UnityRandomProvider</c> for the same seed (different PRNGs).
    /// That is intentional — production uses the Unity-backed impl; goldens use this one.
    /// </summary>
    public sealed class SeededRandomProvider : IRandomProvider
    {
        private readonly Random _random;

        public SeededRandomProvider(int seed)
        {
            _random = new Random(seed);
        }

        public int Next(int maxExclusive)
        {
            return _random.Next(maxExclusive);
        }
    }
}
