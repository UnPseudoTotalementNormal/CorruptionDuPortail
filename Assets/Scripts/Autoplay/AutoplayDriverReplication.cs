#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CorruptionDuPortail.Domain;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

namespace Autoplay
{
    /// <summary>
    /// Desync hunt beyond the in-game tripwire's projection: a generic dump of EVERY replicated value this peer holds
    /// (each NetworkVariable / NetworkList field of each NetworkBehaviour of each spawned NetworkObject, read by
    /// reflection), keyed by NetworkObjectId (identical on every peer). Journaled once per settled phase as
    /// <c>state.repl</c>, one line per value; <c>tools/autoplay/compare_replication.py</c> diffs host and clients phase
    /// by phase. Values that move on their own (avatar look angles, awake flags mid-phase) are left out.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        // Moves continuously or by local timing only: never comparable from a one-shot sample.
        private static readonly HashSet<string> ReplicationVolatile = new(StringComparer.Ordinal)
        {
            "PlayerAvatar.SeatedYaw", "PlayerAvatar.SeatedPitch",
        };

        private static readonly Dictionary<Type, List<FieldInfo>> s_replicatedFields = new();

        private static List<FieldInfo> ReplicatedFieldsOf(Type _type)
        {
            if (s_replicatedFields.TryGetValue(_type, out List<FieldInfo> _cached))
            {
                return _cached;
            }
            var _fields = new List<FieldInfo>();
            for (Type _t = _type; _t != null && _t != typeof(NetworkBehaviour); _t = _t.BaseType)
            {
                foreach (FieldInfo _f in _t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (typeof(NetworkVariableBase).IsAssignableFrom(_f.FieldType))
                    {
                        _fields.Add(_f);
                    }
                }
            }
            _fields.Sort((_a, _b) => string.CompareOrdinal(_a.Name, _b.Name));
            s_replicatedFields[_type] = _fields;
            return _fields;
        }

        private static string DescribeReplicated(object _value)
        {
            if (_value == null)
            {
                return "null";
            }
            Type _type = _value.GetType();
            if (_type.IsGenericType && _type.GetGenericTypeDefinition() == typeof(NetworkVariable<>))
            {
                return DescribePlain(_type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public).GetValue(_value), 0);
            }
            if (_type.IsGenericType && _type.GetGenericTypeDefinition() == typeof(NetworkList<>))
            {
                // NetworkList<T> enumerates through its own struct enumerator only (IEnumerable throws): Count + indexer.
                int _count = (int)_type.GetProperty("Count").GetValue(_value);
                PropertyInfo _item = _type.GetProperty("Item");
                var _items = new List<string>(_count);
                for (int _i = 0; _i < _count; _i++)
                {
                    _items.Add(DescribePlain(_item.GetValue(_value, new object[] { _i }), 0));
                }
                return "[" + string.Join(",", _items) + "]";
            }
            return DescribePlain(_value, 0);
        }

        private static string DescribePlain(object _value, int _depth)
        {
            switch (_value)
            {
                case null: return "null";
                case string _s: return _s;
                case float _f: return _f.ToString("0.###", CultureInfo.InvariantCulture);
                case double _d: return _d.ToString("0.###", CultureInfo.InvariantCulture);
            }
            Type _type = _value.GetType();
            if (_type.IsPrimitive || _type.IsEnum || _type.Name.StartsWith("FixedString", StringComparison.Ordinal))
            {
                return Convert.ToString(_value, CultureInfo.InvariantCulture); // FixedString*: ToString is the text
            }
            if (_depth >= 4)
            {
                return _value.ToString();
            }
            if (_value is IEnumerable _enumerable)
            {
                var _items = new List<string>();
                foreach (object _item in _enumerable)
                {
                    _items.Add(DescribePlain(_item, _depth + 1));
                }
                return "[" + string.Join(",", _items) + "]";
            }
            var _sb = new StringBuilder("{");
            foreach (FieldInfo _f in _type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .OrderBy(_f => _f.Name, StringComparer.Ordinal))
            {
                _sb.Append(_f.Name).Append('=').Append(DescribePlain(_f.GetValue(_value), _depth + 1)).Append(' ');
            }
            return _sb.Append('}').ToString();
        }

        /// <summary>Every replicated value this peer holds, one "objectId:Type.field=value" line each, sorted.</summary>
        private List<string> ReplicatedStateLines(bool _volatilePhase)
        {
            var _lines = new List<string>();
            if (networkManager == null || networkManager.SpawnManager == null)
            {
                return _lines;
            }
            foreach (NetworkObject _object in networkManager.SpawnManager.SpawnedObjectsList.OrderBy(_o => _o.NetworkObjectId))
            {
                if (_object == null)
                {
                    continue;
                }
                foreach (NetworkBehaviour _behaviour in _object.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    if (_behaviour == null || _behaviour.NetworkObject != _object)
                    {
                        continue; // a nested NetworkObject's behaviours are listed under their own id
                    }
                    string _typeName = _behaviour.GetType().Name;
                    foreach (FieldInfo _field in ReplicatedFieldsOf(_behaviour.GetType()))
                    {
                        string _key = _typeName + "." + _field.Name;
                        if (ReplicationVolatile.Contains(_key) || (_volatilePhase && _key == "Character.isAwakened"))
                        {
                            continue;
                        }
                        string _text;
                        try
                        {
                            _text = DescribeReplicated(_field.GetValue(_behaviour));
                        }
                        catch (Exception _e)
                        {
                            _text = "<" + _e.GetType().Name + ">";
                        }
                        _lines.Add(string.Concat(_object.NetworkObjectId.ToString(CultureInfo.InvariantCulture), ":", _key, "=", _text));
                    }
                }
            }
            return _lines;
        }

        private void RecordReplicatedState(string _phase, bool _volatilePhase)
        {
            List<string> _lines = ReplicatedStateLines(_volatilePhase);
            Journal.Record("state.repl", $"{_phase} | {_lines.Count} | {string.Join(" ;; ", _lines)}");
            RecordViewerStates(_phase);
        }

        // Per-player state sent by RPC (not replicated): what each player knows about the others and the icons he
        // sees. The host journals the SERVER truth for every real player (knowledge ledger, icon table); a client
        // journals what it applied for its own seat. compare_replication.py pairs them by seat ("state.view").
        private void RecordViewerStates(string _phase)
        {
            if (revealer == null || characterManager == null)
            {
                return;
            }
            List<ulong> _targets = characterManager.GetCharacters(false).Where(_c => _c && !_c.isFake)
                .Select(_c => _c.ownerClientId.Value).OrderBy(_id => _id).ToList();
            PlayerIconManager _icons = FindAnyObjectByType<PlayerIconManager>();
            if (networkManager.IsServer)
            {
                object _ledger = typeof(GameInfoRevealer).GetField("_ledger", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(revealer);
                MethodInfo _sliceFor = _ledger?.GetType().GetMethod("SliceFor");
                foreach (ulong _viewer in _targets.Where(_id => _id != NetworkManager.ServerClientId && _id < 100))
                {
                    var _knowledge = new List<string>();
                    if (_sliceFor != null)
                    {
                        object[] _args = { _viewer, 0 };
                        foreach (KnowledgeRow _row in (IEnumerable)_sliceFor.Invoke(_ledger, _args))
                        {
                            if (_row.Target != _viewer && _row.Levels.Any(_l => _l > 0))
                            {
                                _knowledge.Add($"{_row.Target}:{string.Join(",", _row.Levels)}");
                            }
                        }
                    }
                    IEnumerable<string> _iconLines = _icons != null
                        ? _icons.GetServerMarkersFor(_viewer).Select(_m => $"{_m.MarkedClientId}:{_m.IconId}")
                        : Enumerable.Empty<string>();
                    JournalView(_phase, _viewer, _knowledge, _iconLines);
                }
                return;
            }
            ulong _seat = LocalSeat;
            var _mine = new List<string>();
            // Every character, fakes included: the server ledger lists them too.
            foreach (ulong _target in characterManager.GetCharacters(false).Where(_c => _c).Select(_c => _c.ownerClientId.Value)
                         .Where(_t => _t != _seat).OrderBy(_t => _t))
            {
                CharacterInfoReveal _info = revealer.GetCharacterInfo(_target, _seat);
                int[] _levels = { (int)_info.isRoleRevealed, (int)_info.isCorruptRevealed, (int)_info.forceCorruptOnRoleRevealed, (int)_info.isHacked, (int)_info.isFakeRevealed };
                if (_levels.Any(_l => _l > 0))
                {
                    _mine.Add($"{_target}:{string.Join(",", _levels)}");
                }
            }
            IEnumerable<string> _mineIcons = _icons != null
                ? _icons.GetLocalIcons().Select(_e => $"{_e.MarkedClientId}:{_e.IconId}")
                : Enumerable.Empty<string>();
            JournalView(_phase, _seat, _mine, _mineIcons);
        }

        /// <summary>Watchdog diagnostic: what this peer's bots and the game are busy with right now.</summary>
        public string DescribeWait()
        {
            var _parts = new List<string>();
            if (characterManager != null)
            {
                string _awake = string.Join(",", characterManager.GetCharacters(false)
                    .Where(_c => _c && _c.isAwakened.Value).Select(_c => _c.ownerClientId.Value >= 1844674407370955161UL ? "fake" : _c.ownerClientId.Value.ToString(CultureInfo.InvariantCulture)));
                _parts.Add($"awake=[{_awake}]");
            }
            string _acting = string.Join(",", turns.Where(_t => _t.Value.active != null)
                .Select(_t => string.Format(CultureInfo.InvariantCulture, "{0}:{1}({2:0}s)", _t.Key, _t.Value.active.powerName,
                    Time.realtimeSinceStartup - _t.Value.activeSince)));
            _parts.Add($"acting=[{_acting}]");
            if (inputBusy)
            {
                _parts.Add("input-busy");
            }
            if (UI.BoardUI.CardPickerManager.instance != null && UI.BoardUI.CardPickerManager.instance.IsPickerActive)
            {
                _parts.Add($"picker={(UI.BoardUI.CardPickerManager.instance.IsRolePicker ? "role" : "character")}");
            }
            switch (CurrentState)
            {
                case GameLogic.GameStates.AwakeningState _awakening:
                    _parts.Add(string.Format(CultureInfo.InvariantCulture, "night-timer={0:0}/{1:0}s", _awakening.currentAwakeningTimer, _awakening.currentAwakeningMaxTime));
                    break;
                case GameLogic.GameStates.VoteState _vote:
                    _parts.Add(string.Format(CultureInfo.InvariantCulture, "vote-timer={0:0}s voted={1}", _vote.voteTimer, votedThisState.Count));
                    break;
            }
            if (networkManager != null)
            {
                _parts.Add(networkManager.IsServer
                    ? $"clients={networkManager.ConnectedClientsIds.Count - 1}"
                    : $"connected={networkManager.IsConnectedClient}");
            }
            return string.Join(" ", _parts);
        }

        private void JournalView(string _phase, ulong _viewer, IEnumerable<string> _knowledge, IEnumerable<string> _iconLines)
        {
            string _k = string.Join(",", _knowledge.OrderBy(_s => _s, StringComparer.Ordinal).Select(_s => "[" + _s + "]"));
            string _i = string.Join(",", _iconLines.OrderBy(_s => _s, StringComparer.Ordinal));
            Journal.Record("state.view", $"{_phase} | v={_viewer} | knowledge={_k} ;; icons={_i}");
        }
    }
}
#endif
