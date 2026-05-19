using System;
using System.Collections.Generic;

namespace GameLogic.Validation
{
    /// <summary>
    /// A simple validator pattern to chain rules.
    /// Usage:
    /// var validator = new Validator<int>();
    /// validator.AddRule(x => x > 0);
    /// bool isValid = validator.Evaluate(5);
    /// </summary>
    public class Validator<T>
    {
        private readonly List<Func<T, bool>> rules = new();

        /// <summary>
        /// Adds a rule that must return true for the validation to pass.
        /// </summary>
        public void AddRule(Func<T, bool> _rule)
        {
            rules.Add(_rule);
        }

        /// <summary>
        /// Returns true if all rules are satisfied.
        /// </summary>
        public bool Evaluate(T _context)
        {
            foreach (var _rule in rules)
            {
                if (!_rule(_context))
                {
                    return false;
                }
            }
            return true;
        }
        
        /// <summary>
        /// Clear all rules.
        /// </summary>
        public void Clear()
        {
            rules.Clear();
        }
    }
}
