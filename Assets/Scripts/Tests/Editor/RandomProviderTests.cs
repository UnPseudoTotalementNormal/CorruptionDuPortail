using CorruptionDuPortail.Domain;
using GameLogic;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 3.1 — contract tests for the <see cref="IRandomProvider"/> port and its two impls:
    /// the engine-free deterministic <see cref="SeededRandomProvider"/> (Domain) and the production
    /// <see cref="UnityRandomProvider"/> (adapter). Both honour the <c>[0, maxExclusive)</c> contract;
    /// only the seeded one is reproducible (production randomness is, by design, not).
    /// </summary>
    [Category("RandomProvider")]
    public class RandomProviderTests
    {
        private const int Seed = 12345;
        private const int Samples = 1000;

        // ───────────────────────────── Seeded determinism ─────────────────────────────

        [Test]
        public void Seeded_SameSeed_ProducesIdenticalSequence()
        {
            var a = new SeededRandomProvider(Seed);
            var b = new SeededRandomProvider(Seed);

            for (int i = 0; i < Samples; i++)
            {
                Assert.AreEqual(a.Next(100), b.Next(100),
                    $"SeededRandomProvider must be reproducible for a given seed (mismatch at draw {i}).");
            }
        }

        [Test]
        public void Seeded_DifferentSeed_DivergesOverASample()
        {
            var a = new SeededRandomProvider(Seed);
            var b = new SeededRandomProvider(Seed + 1);

            bool diverged = false;
            for (int i = 0; i < Samples && !diverged; i++)
            {
                if (a.Next(1000) != b.Next(1000))
                {
                    diverged = true;
                }
            }

            Assert.IsTrue(diverged, "Different seeds should produce different sequences over a 1000-draw sample.");
        }

        // ───────────────────────────── Range contract ─────────────────────────────

        [Test]
        public void Seeded_Next_StaysWithinBounds()
        {
            var provider = new SeededRandomProvider(Seed);
            AssertInBounds(provider);
        }

        [Test]
        public void Unity_Next_StaysWithinBounds()
        {
            // UnityEngine.Random is available in EditMode; this exercises the adapter impl directly.
            var provider = new UnityRandomProvider();
            AssertInBounds(provider);
        }

        [Test]
        public void Next_One_AlwaysReturnsZero()
        {
            var seeded = new SeededRandomProvider(Seed);
            var unity = new UnityRandomProvider();

            for (int i = 0; i < Samples; i++)
            {
                Assert.AreEqual(0, seeded.Next(1), "Next(1) is always 0 (single-element pool).");
                Assert.AreEqual(0, unity.Next(1), "Next(1) is always 0 (single-element pool).");
            }
        }

        [Test]
        public void Next_Zero_ReturnsZero()
        {
            // Edge documented in the story: both PRNGs return 0 for Next(0). Mirrors the live code's
            // (unreached) empty-pool behaviour — captured, not special-cased.
            Assert.AreEqual(0, new SeededRandomProvider(Seed).Next(0));
            Assert.AreEqual(0, new UnityRandomProvider().Next(0));
        }

        private static void AssertInBounds(IRandomProvider provider)
        {
            for (int max = 1; max <= 10; max++)
            {
                for (int i = 0; i < Samples; i++)
                {
                    int value = provider.Next(max);
                    Assert.GreaterOrEqual(value, 0, $"Next({max}) must be >= 0.");
                    Assert.Less(value, max, $"Next({max}) must be < {max} (max-exclusive contract).");
                }
            }
        }
    }
}
