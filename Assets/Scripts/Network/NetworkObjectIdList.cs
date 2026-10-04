#region

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// NET-04 (epic-network-sync-hardening): an ordered list of spawned NetworkObject ids, replicated as ONE immutable
    /// full value through a <c>NetworkVariable</c>. Replaces <c>NetworkList&lt;NetworkBehaviourReference&gt;</c> for
    /// collections mutated around joins and leaves (characters, avatars): NGO's NetworkList double-delivers a same-tick
    /// <c>Add</c> to a synchronizing client (#3280) and removes by INDEX, so one duplicate made a later lobby leave
    /// remove the WRONG character/avatar on that client — and since the list order drives card order and avatar
    /// seats, a diverged replica also ordered cards and seats differently. A NetworkVariable synchronizes a joining
    /// client with the previously sent value and applies the pending change on top, so replicas cannot diverge.
    ///
    /// RULES: never mutate in place; every operation returns a NEW list for the server to assign. Ids are unique
    /// (add-if-absent), in insertion order; removal keeps the relative order of the others.
    /// </summary>
    public sealed class NetworkObjectIdList : INetworkSerializable, IEquatable<NetworkObjectIdList>
    {
        private const int MaxEntries = 256;

        private ulong[] _ids = Array.Empty<ulong>();

        public NetworkObjectIdList() { }

        private NetworkObjectIdList(ulong[] _source)
        {
            _ids = _source;
        }

        public IReadOnlyList<ulong> Ids => _ids;

        public int Count => _ids.Length;

        public bool Contains(ulong _id) => Array.IndexOf(_ids, _id) >= 0;

        public NetworkObjectIdList WithAdded(ulong _id)
        {
            if (Contains(_id))
            {
                return this;
            }
            if (_ids.Length >= MaxEntries)
            {
                Debug.LogError($"[CHARLIST] NetworkObjectIdList overflow: refusing id {_id} ({_ids.Length} entries).");
                return this;
            }
            var _next = new ulong[_ids.Length + 1];
            Array.Copy(_ids, _next, _ids.Length);
            _next[_ids.Length] = _id;
            return new NetworkObjectIdList(_next);
        }

        public NetworkObjectIdList WithRemoved(ulong _id)
        {
            if (!Contains(_id))
            {
                return this;
            }
            var _next = new List<ulong>(_ids.Length);
            foreach (ulong _existing in _ids)
            {
                if (_existing != _id)
                {
                    _next.Add(_existing);
                }
            }
            return new NetworkObjectIdList(_next.ToArray());
        }

        /// <summary>Test seam: builds a list from raw ids WITHOUT the uniqueness rule (simulates a corrupt source).</summary>
        public static NetworkObjectIdList FromRawForTests(params ulong[] _ids) => new((ulong[])_ids.Clone());

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            int _count = _ids.Length;
            _serializer.SerializeValue(ref _count);
            if (_serializer.IsReader)
            {
                _ids = new ulong[Mathf.Clamp(_count, 0, MaxEntries)];
            }
            for (int _i = 0; _i < _ids.Length; _i++)
            {
                _serializer.SerializeValue(ref _ids[_i]);
            }
        }

        public bool Equals(NetworkObjectIdList _other)
        {
            if (ReferenceEquals(this, _other)) return true;
            if (_other == null || _other._ids.Length != _ids.Length) return false;
            for (int _i = 0; _i < _ids.Length; _i++)
            {
                if (_ids[_i] != _other._ids[_i]) return false;
            }
            return true;
        }

        public override bool Equals(object _obj) => _obj is NetworkObjectIdList _other && Equals(_other);

        public override int GetHashCode()
        {
            int _hash = _ids.Length;
            foreach (ulong _id in _ids)
            {
                _hash = HashCode.Combine(_hash, _id);
            }
            return _hash;
        }
    }
}
