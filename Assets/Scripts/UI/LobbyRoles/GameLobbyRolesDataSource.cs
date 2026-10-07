using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Characters;
using CorruptionDuPortail.Domain;
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
        private Network.LobbyPlayerInfoHolder _lobby;
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
                      && CompositionRoot.For(NetworkManager.Singleton).GameManager != null
                      && CompositionRoot.For(NetworkManager.Singleton).LobbyPlayerInfoHolder != null
                      && CompositionRoot.For(NetworkManager.Singleton).LobbyPlayerInfoHolder.playerInfos != null,
                cancellationToken: _token);

            if (_token.IsCancellationRequested) return;

            _nm = NetworkManager.Singleton;
            _manager = CompositionRoot.For(_nm).GameSettingsManager;
            _lobby = CompositionRoot.For(_nm).LobbyPlayerInfoHolder;
            _rolePool = (RoleAttributionState)CompositionRoot.For(_nm).GameManager
                .GetGameStates(typeof(RoleAttributionState)).First();

            // Count + live-refresh source is the replicated player census (mirrors ConnectedPlayerPanel), NOT
            // NGO ConnectedClientsIds: simulated bots (clientId >= 100) enter playerInfos via AddDebugPlayer but
            // never register as NGO clients, so ConnectedClientsIds undercounts them and never fires connect.
            _manager.OnSettingsChanged += Raise;
            _lobby.onRosterChanged += Raise;
            _ready = true;
            Raise();
        }

        private void OnDestroy()
        {
            if (_manager != null) _manager.OnSettingsChanged -= Raise;
            if (_lobby != null)
                _lobby.onRosterChanged -= Raise;
        }

        private void Raise() => OnChanged?.Invoke();

        public int GetPlayerCount()
        {
            // Replicated census (includes simulated bots, correct on host AND non-host tablets). See the
            // OnListChanged wiring above for why NGO ConnectedClientsIds is the wrong source here.
            // Unity-safe null check (NOT `?.`): a destroyed holder is not C#-null, so `?.` would deref it
            // during teardown and throw on the disposed NetworkList — matches the OnDestroy guard style.
            if (_lobby == null || _lobby.playerInfos == null) return 0;
            return _lobby.playerInfos.Count;
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

        public IReadOnlyList<FactionMinimum> GetFactionMinimums()
            => _rolePool != null ? _rolePool.GetFactionMinimums() : System.Array.Empty<FactionMinimum>();

        public Role GetRole(RoleID id)
        {
            if (_rolePool == null) return null;
            foreach (RoleDataObject rdo in _rolePool.roleAttributionDictionary.Keys)
            {
                if (rdo == null || rdo.role.roleID != id) continue;
                return LobbyRoleDetail.From(rdo);
            }
            return null;
        }

        public bool IsHost() => _nm != null && _nm.IsServer;

        public bool GetLocalReady()
        {
            if (_nm == null || _lobby == null) return false;
            return _lobby.GetPlayerInfo(_nm.LocalClientId).isReady;
        }

        public void RequestSetReady(bool ready)
        {
            // SendTo.Server + sender-trusted: readies the LOCAL player only (works on host and non-host).
            if (_lobby == null) return;
            _lobby.RequestSetReady(ready);
        }

        public int GetReadyCount() => _lobby != null ? _lobby.ReadyCount() : 0;

        public void RequestForceStart()
        {
            // Host-only DEV control ("Démarrage forcé"): skip the all-ready condition, keep the composition gate
            // (LobbyState.ForceStart re-validates it server-side). The normal start is now auto-on-all-ready.
            if (_nm == null || !_nm.IsServer) return;
            var lobbyState = CompositionRoot.For(_nm).GameManager
                .GetGameStates(typeof(LobbyState)).FirstOrDefault() as LobbyState;
            if (lobbyState == null)
            {
                Debug.LogError("GameLobbyRolesDataSource: no LobbyState resolved — cannot force-start the game.");
                return;
            }
            lobbyState.ForceStart();
        }

        public void RequestSetMax(RoleID id, int max) => _manager?.RequestSetRoleCount(id, max);
        public void RequestSetForced(RoleID id, int forced) => _manager?.RequestSetForced(id, forced);

        public int GetActivePresetIndex()
        {
            if (!_ready || _presetDatabase == null || _manager == null || _rolePool == null) return -1;
            List<RolePreset> forCount = _presetDatabase.ForPlayerCount(GetPlayerCount());
            for (int i = 0; i < forCount.Count; i++)
                if (MatchesPreset(forCount[i])) return i;
            return -1;
        }

        // A preset is "active" when the manager's current (count, forced) for every pool role equals what the
        // preset would set — entry if listed, else 0/0. Any divergence (a stepper touched) → not this preset.
        private bool MatchesPreset(RolePreset preset)
        {
            foreach (RoleDataObject _rdo in _rolePool.roleAttributionDictionary.Keys)
            {
                RoleID _id = _rdo.role.roleID;
                bool listed = preset.entries.Any(e => e.roleId == _id);
                RolePreset.Entry entry = preset.entries.FirstOrDefault(e => e.roleId == _id);
                int em = listed ? entry.max : 0;
                int ef = listed ? entry.forced : 0;
                if (_manager.GetRoleCount(_id) != em || _manager.GetForced(_id) != ef) return false;
            }
            return true;
        }

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
