using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using Extensions;
using GameLogic;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

namespace UI.LobbyRoles
{
    /// <summary>
    /// The real (NGO) <see cref="ILobbyRolesDataSource"/> for the tablet lobby app. Resolves the
    /// server-authoritative <see cref="GameSettingsManager"/> and the authored role POOL (the
    /// <see cref="RoleAttributionState"/> dictionary) through the CompositionRoot — the sanctioned host-agnostic
    /// route, mirroring <c>RoleAttributionSettingTab</c>. Reads max/forced from the manager, writes host-only
    /// through it (RequestSetRoleCount = max, RequestSetForced), and raises <see cref="OnChanged"/> on both the
    /// manager's replicated changes and player connect/disconnect. Presentation-only: no game-state mutation.
    /// </summary>
    public class GameLobbyRolesDataSource : MonoBehaviour, ILobbyRolesDataSource
    {
        [Tooltip("Authored preset set (design-owned). Null = no presets offered.")]
        [SerializeField] private RolePresetDatabase _presetDatabase;

        public event Action OnChanged;

        private GameSettingsManager _manager;
        private RoleAttributionState _rolePool;
        private NetworkManager _nm;
        private bool _ready;

        private void Start()
        {
            ResolveWhenReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid ResolveWhenReadyAsync(CancellationToken _token)
        {
            await UniTask.WaitUntil(
                () => NetworkManager.Singleton != null
                      && CompositionRoot.For(NetworkManager.Singleton).GameSettingsManager != null
                      && CompositionRoot.For(NetworkManager.Singleton).GameManager != null,
                cancellationToken: _token);

            if (_token.IsCancellationRequested) return;

            _nm = NetworkManager.Singleton;
            _manager = CompositionRoot.For(_nm).GameSettingsManager;
            _rolePool = (RoleAttributionState)CompositionRoot.For(_nm).GameManager
                .GetGameStates(typeof(RoleAttributionState)).First();

            _manager.OnSettingsChanged += Raise;
            _nm.OnClientConnectedCallback += OnClientChanged;
            _nm.OnClientDisconnectCallback += OnClientChanged;
            _ready = true;
            Raise();
        }

        private void OnDestroy()
        {
            if (_manager != null) _manager.OnSettingsChanged -= Raise;
            if (_nm != null)
            {
                _nm.OnClientConnectedCallback -= OnClientChanged;
                _nm.OnClientDisconnectCallback -= OnClientChanged;
            }
        }

        private void OnClientChanged(ulong _clientId) => Raise();
        private void Raise() => OnChanged?.Invoke();

        public int GetPlayerCount()
        {
            // Lobby is host-configured; the host has the authoritative connected list. (A client-accurate count
            // would read a replicated player holder — refine at GameScene wiring if the tally must be exact on
            // non-host tablets.)
            if (_nm == null) return 0;
            return _nm.IsServer ? _nm.ConnectedClientsIds.Count : _nm.ConnectedClients.Count;
        }

        public IReadOnlyList<LobbyRoleView> GetRoles()
        {
            var list = new List<LobbyRoleView>();
            if (!_ready || _rolePool == null || _manager == null) return list;

            foreach (RoleDataObject _rdo in _rolePool.roleAttributionDictionary.Keys)
            {
                Role _role = _rdo.role;
                list.Add(new LobbyRoleView(
                    _role.roleID,
                    _role.roleName.ToString(),
                    _role.factionType,
                    (int)_role.rolePortrait,
                    _manager.GetRoleCount(_role.roleID),
                    _manager.GetForced(_role.roleID)));
            }
            return list;
        }

        public void RequestSetMax(RoleID id, int max) => _manager?.RequestSetRoleCount(id, max);
        public void RequestSetForced(RoleID id, int forced) => _manager?.RequestSetForced(id, forced);

        public IReadOnlyList<LobbyPresetView> GetPresets()
        {
            var list = new List<LobbyPresetView>();
            if (_presetDatabase == null) return list;
            foreach (RolePreset p in _presetDatabase.ForPlayerCount(GetPlayerCount()))
            {
                list.Add(new LobbyPresetView(p.displayName, p.description, p.isClassic));
            }
            return list;
        }

        public void ApplyPreset(int index)
        {
            if (_presetDatabase == null || _manager == null || _rolePool == null) return;
            List<RolePreset> forCount = _presetDatabase.ForPlayerCount(GetPlayerCount());
            if (index < 0 || index >= forCount.Count) return;
            RolePreset preset = forCount[index];

            // Fill the WHOLE pool: a role not listed in the preset goes to max 0. Set max before forced so the
            // server-side forced <= max clamp lands on the new max (host-only writes; non-host = no-op).
            foreach (RoleDataObject _rdo in _rolePool.roleAttributionDictionary.Keys)
            {
                RoleID _id = _rdo.role.roleID;
                RolePreset.Entry entry = preset.entries.FirstOrDefault(e => e.roleId == _id);
                bool listed = preset.entries.Any(e => e.roleId == _id);
                _manager.RequestSetRoleCount(_id, listed ? entry.max : 0);
                _manager.RequestSetForced(_id, listed ? entry.forced : 0);
            }
        }
    }
}
