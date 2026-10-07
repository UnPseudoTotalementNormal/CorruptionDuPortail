using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>NET-10: the four knowledge fields a viewer can hold about a target (CharacterInfoReveal).</summary>
    public enum KnowledgeField
    {
        RoleRevealed = 0,
        CorruptRevealed = 1,
        ForceCorruptOnRoleRevealed = 2,
        Hacked = 3,
    }

    /// <summary>One target's levels in a viewer's slice (RevealLevel values: 0 False, 10 Personal, 20 Public).</summary>
    public readonly struct KnowledgeRow
    {
        public readonly ulong Target;
        public readonly int[] Levels;

        public KnowledgeRow(ulong target, int[] levels)
        {
            Target = target;
            Levels = levels;
        }
    }

    /// <summary>
    /// NET-10 (epic-network-sync-hardening): the SERVER's authoritative record of what every viewer knows. Knowledge
    /// used to exist only on each client, built by reveal RPCs: nothing could detect or repair a missed, duplicated
    /// or misordered reveal. Every reveal now writes here first; each viewer receives its FULL slice (versioned) and
    /// replaces its local view with it, so a client can never accumulate a different history than the server.
    /// Personal levels are monotonic (only the hacked marker can be cleared); public levels live in a shared layer
    /// merged into every viewer's slice (so a viewer that appears later still gets them).
    /// </summary>
    public sealed class KnowledgeLedger
    {
        public const int FieldCount = 4;
        public const int Public = 20;

        private readonly Dictionary<ulong, Dictionary<ulong, int[]>> _personal = new();
        private readonly Dictionary<ulong, int[]> _public = new();
        private readonly Dictionary<ulong, int> _versions = new();

        public IEnumerable<ulong> KnownViewers => _personal.Keys;

        /// <summary>Raises a viewer's level for (target, field). Returns true when the viewer's slice changed.</summary>
        public bool Raise(ulong viewer, ulong target, KnowledgeField field, int level)
        {
            int _before = Effective(viewer, target, field);
            int[] _levels = Row(Personal(viewer), target);
            if (level > _levels[(int)field])
            {
                _levels[(int)field] = level;
            }
            return Effective(viewer, target, field) != _before;
        }

        /// <summary>Raises the shared public layer (every viewer). Returns true when it changed.</summary>
        public bool RaisePublic(ulong target, KnowledgeField field, int level)
        {
            int[] _levels = Row(_public, target);
            if (level <= _levels[(int)field])
            {
                return false;
            }
            _levels[(int)field] = level;
            return true;
        }

        /// <summary>Clears a viewer's PERSONAL level (the hack marker expiry). Returns true when the slice changed.</summary>
        public bool Clear(ulong viewer, ulong target, KnowledgeField field)
        {
            int _before = Effective(viewer, target, field);
            if (_personal.TryGetValue(viewer, out var _rows) && _rows.TryGetValue(target, out int[] _levels))
            {
                _levels[(int)field] = 0;
            }
            return Effective(viewer, target, field) != _before;
        }

        /// <summary>New game: forget everything (versions keep increasing so no stale push can win afterwards).</summary>
        public void Reset()
        {
            _personal.Clear();
            _public.Clear();
        }

        public int Effective(ulong viewer, ulong target, KnowledgeField field)
        {
            int _personalLevel = _personal.TryGetValue(viewer, out var _rows) && _rows.TryGetValue(target, out int[] _p)
                ? _p[(int)field]
                : 0;
            int _publicLevel = _public.TryGetValue(target, out int[] _pub) ? _pub[(int)field] : 0;
            return Math.Max(_personalLevel, _publicLevel);
        }

        /// <summary>The viewer's full, merged slice, stamped with a fresh monotonically increasing version.</summary>
        public List<KnowledgeRow> SliceFor(ulong viewer, out int version)
        {
            _versions.TryGetValue(viewer, out int _current);
            version = _current + 1;
            _versions[viewer] = version;

            var _targets = new SortedSet<ulong>(_public.Keys);
            if (_personal.TryGetValue(viewer, out var _rows))
            {
                _targets.UnionWith(_rows.Keys);
            }

            var _slice = new List<KnowledgeRow>(_targets.Count);
            foreach (ulong _target in _targets)
            {
                var _levels = new int[FieldCount];
                for (int _f = 0; _f < FieldCount; _f++)
                {
                    _levels[_f] = Effective(viewer, _target, (KnowledgeField)_f);
                }
                _slice.Add(new KnowledgeRow(_target, _levels));
            }
            return _slice;
        }

        private Dictionary<ulong, int[]> Personal(ulong viewer)
        {
            if (!_personal.TryGetValue(viewer, out var _rows))
            {
                _rows = new Dictionary<ulong, int[]>();
                _personal[viewer] = _rows;
            }
            return _rows;
        }

        private static int[] Row(Dictionary<ulong, int[]> rows, ulong target)
        {
            if (!rows.TryGetValue(target, out int[] _levels))
            {
                _levels = new int[FieldCount];
                rows[target] = _levels;
            }
            return _levels;
        }
    }
}
