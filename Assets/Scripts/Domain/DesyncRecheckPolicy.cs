using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-00: decides when a digest mismatch is a real desync. NGO delivers RPCs immediately but NetworkVariable
    /// deltas at the end of the tick, so a first comparison can catch a replica one tick behind. A component is
    /// only confirmed when it mismatches on the first check AND again on a fresh re-check; a confirmed component is
    /// reported at most once per state key (one log per component per game state, never a flood).
    /// </summary>
    public sealed class DesyncRecheckPolicy
    {
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

        public bool HasPendingRecheck => _pending.Count > 0;

        /// <summary>First comparison of a round. Returns true when a re-check must be requested.</summary>
        public bool OnFirstCheck(IEnumerable<string> mismatches)
        {
            _pending.Clear();
            foreach (string _component in mismatches)
            {
                _pending.Add(_component);
            }
            return _pending.Count > 0;
        }

        /// <summary>
        /// Re-check comparison. Returns the components that mismatched both times and were not already reported for
        /// <paramref name="stateKey"/>; clears the pending set.
        /// </summary>
        public List<string> OnRecheck(IEnumerable<string> mismatches, string stateKey)
        {
            var _confirmed = new List<string>();
            foreach (string _component in mismatches)
            {
                if (_pending.Contains(_component) && _reported.Add(stateKey + "|" + _component))
                {
                    _confirmed.Add(_component);
                }
            }
            _pending.Clear();
            _confirmed.Sort(StringComparer.Ordinal);
            return _confirmed;
        }
    }
}
