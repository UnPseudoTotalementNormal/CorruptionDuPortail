#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// How an autoplay bot makes its choices. A policy only ever picks among options the game itself already declared
    /// legal (validators, usability checks) — it plays legal moves, it never encodes game design.
    /// </summary>
    public interface IAutoplayPolicy
    {
        /// <summary>Picks one element of a non-empty list. <paramref name="_decision"/> names the decision for logs.</summary>
        T Choose<T>(IReadOnlyList<T> _options, string _decision);

        /// <summary>True with probability <paramref name="_probability"/> (0..1).</summary>
        bool Roll(double _probability, string _decision);
    }

    /// <summary>Uniform choice among legal options, reproducible from its seed.</summary>
    public sealed class RandomValidPolicy : IAutoplayPolicy
    {
        private readonly Random random;

        public int Seed { get; }

        public RandomValidPolicy(int _seed)
        {
            Seed = _seed;
            random = new Random(_seed);
        }

        public T Choose<T>(IReadOnlyList<T> _options, string _decision)
        {
            if (_options == null || _options.Count == 0)
            {
                throw new ArgumentException($"Autoplay decision '{_decision}' has no option to choose from.");
            }

            return _options[random.Next(_options.Count)];
        }

        public bool Roll(double _probability, string _decision) => random.NextDouble() < _probability;
    }
}
#endif
