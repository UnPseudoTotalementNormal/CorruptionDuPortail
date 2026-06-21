#region

using System;
using System.Collections.Generic;
using Characters;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace GameLogic.GameSettings
{
    /// <summary>
    /// Server-authoritative owner of the lobby role-attribution settings (how many of each role to
    /// attribute + canBeFake), replicated to every client through a <see cref="NetworkList{T}"/>. Replaces
    /// the previous spaghetti where the settings lived on a per-client <see cref="RoleAttributionState"/> SO
    /// clone and were synced by hand-rolled RPCs embedded in runtime-Instantiated UI NetworkBehaviours
    /// (fragile NGO identity — they were never Spawned). This is a scene-placed NetworkObject (Spawned with
    /// the host), resolved by consumers through the CompositionRoot (lane-A field + Services accessor) and
    /// pushed lane-B into the GameStates by SetupGameStates (mirrors boardManager).
    ///
    /// Editing is host-authoritative by default (preserves the pre-refactor de-facto behaviour: only the
    /// host's edits stuck — a non-host slider reverted on the server refresh). Set
    /// <see cref="_allowClientEditing"/> to let every client propose changes through the server RPC.
    /// </summary>
    public class GameSettingsManager : NetworkBehaviour
    {
        // Authored defaults: the original RoleAttributionState SO asset (designer-tuned counts + the role
        // pool). The server seeds the replicated list from it once at spawn; authoring stays on
        // RoleAttributionState (spec Ask-First default). Read-only reference to the asset — never mutated.
        [SerializeField] private RoleAttributionState _authoredSettingsSource;

        // Ask-First (spec): host-only editing today. Flip on (and interact-enable non-host sliders) to let
        // every client propose changes through SubmitRoleCountServerRpc.
        [SerializeField] private bool _allowClientEditing;

        // Server clamp upper bound. Mirrors the authored slider's max (RoleAttributionRoleSettingObject
        // prefab `m_MaxValue: 15`) so the clamp is a no-op for legitimate slider input and the achievable
        // range stays exactly what it was before this refactor (behaviour-preserving — the old code had NO
        // server clamp). NOTE for Poyo: the slider max (15) disagrees with the [Range(0,10)] on
        // RoleAttributionSetting.roleToAttribute — pick one and align them (this const + the prefab slider).
        private const int MaxRoleCount = 15;

        // Server-authoritative, replicated. Keyed by RoleID for lookups; seeded in the authored role-pool
        // order so a positional consumer stays stable. Field initializer per the NetworkList convention
        // (see LobbyPlayerInfoHolder.playerInfos).
        private readonly NetworkList<RoleSettingEntry> _settings = new();

        /// <summary>Fires on every peer (host included) whenever the replicated settings change.</summary>
        public event Action OnSettingsChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _settings.OnListChanged += OnSettingsListChanged;

            if (IsServer)
            {
                SeedFromAuthoredDefaults();
            }
        }

        public override void OnNetworkDespawn()
        {
            _settings.OnListChanged -= OnSettingsListChanged;
            base.OnNetworkDespawn();
        }

        private void OnSettingsListChanged(NetworkListEvent<RoleSettingEntry> _change)
        {
            OnSettingsChanged?.Invoke();
        }

        private void SeedFromAuthoredDefaults()
        {
            // Idempotent: domain reload is disabled, so a NetworkList surviving a prior Play session must not
            // be double-seeded. Only seed when empty.
            if (_settings.Count > 0)
            {
                return;
            }

            // Fail loud (not stripped in a release player, unlike UnityEngine.Assertions): a null source would
            // otherwise NPE in the loop below. The CompositionRoot ref to this manager is SceneWiringGuard-
            // covered, but the asset reference itself is only validated here.
            if (_authoredSettingsSource == null)
            {
                Debug.LogError("GameSettingsManager._authoredSettingsSource is not wired — wire the RoleAttributionState SO asset (authored defaults). Role settings stay empty.");
                return;
            }

            foreach (KeyValuePair<RoleDataObject, RoleAttributionSetting> _pair in _authoredSettingsSource.roleAttributionDictionary)
            {
                RoleID _roleId = _pair.Key.role.roleID;
                // The lookups (GetRoleCount/GetCanBeFake) key by RoleID and return the FIRST match while the
                // total sums every entry — so a duplicate (or unset → default) RoleID would shadow a role and
                // double-count. Fail loud on that authoring mistake instead of mis-distributing silently.
                if (ContainsRole(_roleId))
                {
                    Debug.LogError($"GameSettingsManager: duplicate RoleID '{_roleId}' in the authored settings — skipping it. Every RoleDataObject must carry a unique RoleID.");
                    continue;
                }

                _settings.Add(new RoleSettingEntry
                {
                    roleId = _roleId,
                    count = _pair.Value.roleToAttribute,
                    canBeFake = _pair.Value.canBeFake
                });
            }
        }

        private bool ContainsRole(RoleID _roleId)
        {
            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i].roleId == _roleId)
                {
                    return true;
                }
            }
            return false;
        }

        // ----- read API (distribution + views) -----

        public int GetRoleCount(RoleID _roleId)
        {
            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i].roleId == _roleId)
                {
                    return _settings[i].count;
                }
            }
            return 0;
        }

        public bool GetCanBeFake(RoleID _roleId)
        {
            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i].roleId == _roleId)
                {
                    return _settings[i].canBeFake;
                }
            }
            return false;
        }

        public int GetTotalRolesToAttribute()
        {
            int _total = 0;
            for (int i = 0; i < _settings.Count; i++)
            {
                _total += _settings[i].count;
            }
            return _total;
        }

        // ----- write API (views) -----

        /// <summary>
        /// Propose a new count for a role. The host writes the server-authoritative list directly; a non-host
        /// only reaches the server when <see cref="_allowClientEditing"/> is on (host-only by default — the
        /// non-host sliders are read-only mirrors, so this is a no-op for them today).
        /// </summary>
        public void RequestSetRoleCount(RoleID _roleId, int _count)
        {
            if (IsServer)
            {
                ApplyRoleCountServer(_roleId, _count);
                return;
            }

            if (!_allowClientEditing)
            {
                return;
            }
            SubmitRoleCountServerRpc(_roleId, _count);
        }

        [Rpc(SendTo.Server)]
        private void SubmitRoleCountServerRpc(RoleID _roleId, int _count)
        {
            // Server-side gate (defence in depth): ignore client proposals unless editing is opened up.
            if (!_allowClientEditing)
            {
                return;
            }
            ApplyRoleCountServer(_roleId, _count);
        }

        private void ApplyRoleCountServer(RoleID _roleId, int _count)
        {
            if (!IsServer)
            {
                return;
            }

            int _clamped = Mathf.Clamp(_count, 0, MaxRoleCount);
            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i].roleId != _roleId)
                {
                    continue;
                }
                if (_settings[i].count == _clamped)
                {
                    return; // idempotent — skip no-op replication
                }

                RoleSettingEntry _entry = _settings[i];
                _entry.count = _clamped;
                _settings[i] = _entry;
                return;
            }
        }

        // Replicated DTO: an unmanaged, IEquatable value type — the NetworkList<T> constraint (unmanaged +
        // IEquatable) is satisfied because every field is unmanaged (RoleID enum / int / bool). Mirrors the
        // PlayerInfo shape; the manual NetworkSerialize stays (NGO cannot auto-generate it).
        private struct RoleSettingEntry : INetworkSerializable, IEquatable<RoleSettingEntry>
        {
            public RoleID roleId;
            public int count;
            public bool canBeFake;

            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                _serializer.SerializeValue(ref roleId);
                _serializer.SerializeValue(ref count);
                _serializer.SerializeValue(ref canBeFake);
            }

            public bool Equals(RoleSettingEntry _other)
            {
                return roleId == _other.roleId && count == _other.count && canBeFake == _other.canBeFake;
            }

            public override bool Equals(object _obj) => _obj is RoleSettingEntry _other && Equals(_other);

            public override int GetHashCode() => HashCode.Combine((int)roleId, count, canBeFake);
        }
    }
}
