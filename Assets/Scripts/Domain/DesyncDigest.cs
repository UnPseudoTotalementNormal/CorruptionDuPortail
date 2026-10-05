using System;
using System.Collections.Generic;
using System.Text;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-00 (epic-network-sync-hardening): a canonical, engine-free projection of the PUBLIC replicated state —
    /// what every peer is supposed to hold identically. Built per peer by an adapter, then hashed per named
    /// component so a mismatch says WHERE replicas diverged. Private per-viewer knowledge never goes in here.
    /// </summary>
    public sealed class PublicStateProjection
    {
        private readonly SortedDictionary<string, List<string>> _components = new(StringComparer.Ordinal);

        /// <summary>
        /// Sets a component's canonical lines. <paramref name="ordered"/> = true keeps the given order because the
        /// order itself is replicated state (roster / character order drives card order and seats); false sorts the
        /// lines ordinally so an unordered set hashes the same regardless of iteration order.
        /// </summary>
        public void SetComponent(string name, IEnumerable<string> lines, bool ordered)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Component name is required.", nameof(name));
            var _lines = new List<string>(lines ?? Array.Empty<string>());
            if (!ordered)
            {
                _lines.Sort(StringComparer.Ordinal);
            }
            _components[name] = _lines;
        }

        public IEnumerable<string> ComponentNames => _components.Keys;

        public IReadOnlyList<string> GetLines(string name)
        {
            return _components.TryGetValue(name, out var _lines) ? _lines : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }

    /// <summary>
    /// NET-00: per-component 64-bit FNV-1a hash over the UTF-8 canonical text of a <see cref="PublicStateProjection"/>.
    /// Non-adversarial tripwire — proves THAT and WHERE two replicas diverged, not an integrity check.
    /// </summary>
    public static class DesyncDigest
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static Dictionary<string, ulong> ComputeComponentHashes(PublicStateProjection projection)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            var _hashes = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (string _name in projection.ComponentNames)
            {
                _hashes[_name] = Hash(Join(projection.GetLines(_name)));
            }
            return _hashes;
        }

        /// <summary>Components whose hash differs, or that exist on only one side. Sorted for stable logs.</summary>
        public static List<string> Mismatches(IReadOnlyDictionary<string, ulong> server, IReadOnlyDictionary<string, ulong> client)
        {
            var _result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var _pair in server)
            {
                if (!client.TryGetValue(_pair.Key, out ulong _clientHash) || _clientHash != _pair.Value)
                {
                    _result.Add(_pair.Key);
                }
            }
            foreach (var _pair in client)
            {
                if (!server.ContainsKey(_pair.Key))
                {
                    _result.Add(_pair.Key);
                }
            }
            return new List<string>(_result);
        }

        /// <summary>Human-readable dump of one component, for the [DESYNC] log.</summary>
        public static string Describe(PublicStateProjection projection, string component)
        {
            IReadOnlyList<string> _lines = projection.GetLines(component);
            return _lines.Count == 0 ? "  (empty)" : "  " + string.Join("\n  ", _lines);
        }

        private static string Join(IReadOnlyList<string> lines) => string.Join("\n", lines);

        private static ulong Hash(string text)
        {
            ulong _hash = FnvOffset;
            foreach (byte _b in Encoding.UTF8.GetBytes(text))
            {
                _hash ^= _b;
                _hash *= FnvPrime;
            }
            return _hash;
        }
    }
}
