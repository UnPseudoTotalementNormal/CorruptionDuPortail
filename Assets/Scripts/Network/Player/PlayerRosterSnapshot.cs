#region

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network.Player
{
    /// <summary>
    /// NET-01 (epic-network-sync-hardening): the player roster as ONE immutable, full-value snapshot, replicated
    /// through a <c>NetworkVariable</c> instead of a <c>NetworkList</c>.
    ///
    /// WHY: NGO's <c>NetworkList</c> (2.12) double-delivers an <c>Add</c> to a client synchronizing in the same tick
    /// (no <c>WriteFieldSynchronization</c> override, NGO #3280) and applies <c>Value</c>/<c>RemoveAt</c> deltas by
    /// INDEX. One duplicate on a client + one later index write (a ready toggle, a leave) overwrote or removed another
    /// player's row on that client only → missing pseudos (playtest 2026-10-04). A <c>NetworkVariable</c> synchronizes
    /// a joining client with the previously sent value and then applies the pending change on top
    /// (<c>NetworkVariable.WriteFieldSynchronization</c>), and every change carries the whole roster, so a replica can
    /// never diverge.
    ///
    /// RULES: never mutate a snapshot in place — every operation returns a NEW snapshot that the server assigns to the
    /// NetworkVariable (value-equality change detection). Rows are unique by <see cref="PlayerInfo.playerClientId"/>
    /// (upsert semantics), in first-join order.
    /// </summary>
    public sealed class PlayerRosterSnapshot : INetworkSerializable, IEquatable<PlayerRosterSnapshot>
    {
        // Generous hard cap (real players + simulated bots + headroom); the game itself caps at GameValues.MAX_PLAYERS.
        public const int MaxEntries = 64;

        private PlayerInfo[] _entries = Array.Empty<PlayerInfo>();

        public PlayerRosterSnapshot() { }

        private PlayerRosterSnapshot(PlayerInfo[] _source)
        {
            _entries = _source;
        }

        public IReadOnlyList<PlayerInfo> Entries => _entries;

        public int Count => _entries.Length;

        public bool TryGet(ulong _clientId, out PlayerInfo _info)
        {
            int _index = IndexOf(_clientId);
            _info = _index >= 0 ? _entries[_index] : default;
            return _index >= 0;
        }

        /// <summary>Adds the row, or replaces the existing row with the same clientId. The existing ready flag is kept
        /// (it is owned by <see cref="WithReady"/> only).</summary>
        public PlayerRosterSnapshot WithUpsert(PlayerInfo _info)
        {
            int _index = IndexOf(_info.playerClientId);
            if (_index >= 0)
            {
                _info.isReady = _entries[_index].isReady;
                _info.hasLeft = _entries[_index].hasLeft;
                return _entries[_index].Equals(_info) ? this : Replace(_index, _info);
            }

            if (_entries.Length >= MaxEntries)
            {
                Debug.LogError($"[ROSTER] overflow: refusing clientId={_info.playerClientId}, roster already holds {_entries.Length} rows.");
                return this;
            }

            var _next = new PlayerInfo[_entries.Length + 1];
            Array.Copy(_entries, _next, _entries.Length);
            _next[_entries.Length] = _info;
            return new PlayerRosterSnapshot(_next);
        }

        /// <summary>Replaces an EXISTING row only (unknown clientId = no-op), keeping its ready flag.</summary>
        public PlayerRosterSnapshot WithUpdate(PlayerInfo _info)
        {
            int _index = IndexOf(_info.playerClientId);
            if (_index < 0)
            {
                return this;
            }
            _info.isReady = _entries[_index].isReady;
            _info.hasLeft = _entries[_index].hasLeft;
            return _entries[_index].Equals(_info) ? this : Replace(_index, _info);
        }

        /// <summary>Flips ONLY the ready flag of one row. Unknown clientId or unchanged value = same snapshot.</summary>
        public PlayerRosterSnapshot WithReady(ulong _clientId, bool _ready)
        {
            int _index = IndexOf(_clientId);
            if (_index < 0 || _entries[_index].isReady == _ready)
            {
                return this;
            }
            PlayerInfo _info = _entries[_index];
            _info.isReady = _ready;
            return Replace(_index, _info);
        }

        /// <summary>NET-03: flags a row as "left mid-game" without removing it. Unknown clientId = same snapshot.</summary>
        public PlayerRosterSnapshot WithLeft(ulong _clientId)
        {
            int _index = IndexOf(_clientId);
            if (_index < 0 || _entries[_index].hasLeft)
            {
                return this;
            }
            PlayerInfo _info = _entries[_index];
            _info.hasLeft = true;
            return Replace(_index, _info);
        }

        /// <summary>Rejoin 02: the player of that seat is back (row no longer flagged "left").</summary>
        public PlayerRosterSnapshot WithBack(ulong _clientId)
        {
            int _index = IndexOf(_clientId);
            if (_index < 0 || !_entries[_index].hasLeft)
            {
                return this;
            }
            PlayerInfo _info = _entries[_index];
            _info.hasLeft = false;
            return Replace(_index, _info);
        }

        /// <summary>Removes the row of that clientId (every row, should a corrupt source ever hold two).</summary>
        public PlayerRosterSnapshot WithRemoved(ulong _clientId)
        {
            if (IndexOf(_clientId) < 0)
            {
                return this;
            }
            var _next = new List<PlayerInfo>(_entries.Length);
            foreach (PlayerInfo _info in _entries)
            {
                if (_info.playerClientId != _clientId)
                {
                    _next.Add(_info);
                }
            }
            return new PlayerRosterSnapshot(_next.ToArray());
        }

        private int IndexOf(ulong _clientId)
        {
            for (int _i = 0; _i < _entries.Length; _i++)
            {
                if (_entries[_i].playerClientId == _clientId)
                {
                    return _i;
                }
            }
            return -1;
        }

        private PlayerRosterSnapshot Replace(int _index, PlayerInfo _info)
        {
            var _next = (PlayerInfo[])_entries.Clone();
            _next[_index] = _info;
            return new PlayerRosterSnapshot(_next);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            int _count = _entries.Length;
            _serializer.SerializeValue(ref _count);
            if (_serializer.IsReader)
            {
                _entries = new PlayerInfo[Mathf.Clamp(_count, 0, MaxEntries)];
            }
            for (int _i = 0; _i < _entries.Length; _i++)
            {
                _serializer.SerializeValue(ref _entries[_i]);
            }
        }

        public bool Equals(PlayerRosterSnapshot _other)
        {
            if (ReferenceEquals(this, _other)) return true;
            if (_other == null || _other._entries.Length != _entries.Length) return false;
            for (int _i = 0; _i < _entries.Length; _i++)
            {
                if (!_entries[_i].Equals(_other._entries[_i])) return false;
            }
            return true;
        }

        public override bool Equals(object _obj) => _obj is PlayerRosterSnapshot _other && Equals(_other);

        public override int GetHashCode()
        {
            int _hash = _entries.Length;
            foreach (PlayerInfo _info in _entries)
            {
                _hash = HashCode.Combine(_hash, _info);
            }
            return _hash;
        }
    }
}
