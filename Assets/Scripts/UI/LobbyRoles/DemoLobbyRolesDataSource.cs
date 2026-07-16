using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.LobbyRoles
{
    /// <summary>
    /// HARNESS-ONLY <see cref="ILobbyRolesDataSource"/>. Reads the REAL authored role pool (names / factions /
    /// portraits) from a wired <see cref="RoleAttributionState"/> asset, and keeps max/forced in-memory so the
    /// UITK lobby app can be Play-tested without a running multiplayer game — the cards show real portraits via
    /// the controller's PortraitTable. Steppers mutate local state and raise <see cref="OnChanged"/> (no
    /// network). Left/Right arrows tweak the mock connected-player count so presets/gating can be exercised.
    /// Not used in GameScene (that path is <c>GameLobbyRolesDataSource</c>).
    /// </summary>
    public class DemoLobbyRolesDataSource : MonoBehaviour, ILobbyRolesDataSource
    {
        [SerializeField] private int _playerCount = 5;

        [Tooltip("The authored role pool asset — the harness reads real names/factions/portraits from its RoleDataObjects.")]
        [SerializeField] private RoleAttributionState _rolePoolAsset;

        private const int MaxRoleCount = 15;

        private readonly Dictionary<RoleID, (int max, int forced)> _state = new();

        // Harness-only placeholder presets, keyed by player count. Real presets are design-owned (RolePresetDatabase).
        private struct DemoPreset { public string Name; public string Desc; public bool Classic; public (RoleID id, int max, int forced)[] Entries; }

        private static readonly Dictionary<int, DemoPreset[]> Presets = new()
        {
            [5] = new[]
            {
                new DemoPreset { Name = "Classique", Desc = "Compo équilibrée, 1 mage garanti", Classic = true,
                    Entries = new[] { (RoleID.MageOcculte, 1, 1), (RoleID.Robot, 2, 0), (RoleID.Technomancien, 2, 1), (RoleID.Oracle, 1, 0) } },
                new DemoPreset { Name = "Néant montant", Desc = "Anomalies renforcées", Classic = false,
                    Entries = new[] { (RoleID.Abyss, 1, 1), (RoleID.MageOcculte, 1, 0), (RoleID.Robot, 2, 0), (RoleID.Croupiere, 1, 0) } },
            },
            [7] = new[]
            {
                new DemoPreset { Name = "Classique", Desc = "Compo 7 équilibrée", Classic = true,
                    Entries = new[] { (RoleID.MageOcculte, 1, 1), (RoleID.Abyss, 1, 0), (RoleID.Robot, 2, 0), (RoleID.Technomancien, 2, 1), (RoleID.Croupiere, 1, 0), (RoleID.Oracle, 1, 0) } },
                new DemoPreset { Name = "Conclave", Desc = "Élus soudés", Classic = false,
                    Entries = new[] { (RoleID.MageOcculte, 1, 0), (RoleID.Abyss, 1, 0), (RoleID.Technomancien, 2, 1), (RoleID.Croupiere, 2, 1), (RoleID.Oracle, 1, 0), (RoleID.Gardien, 1, 0) } },
            },
        };

        public event Action OnChanged;

        private void Start() => OnChanged?.Invoke();

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame) { _playerCount = Mathf.Max(3, _playerCount - 1); OnChanged?.Invoke(); }
            if (kb.rightArrowKey.wasPressedThisFrame) { _playerCount = Mathf.Min(10, _playerCount + 1); OnChanged?.Invoke(); }
        }

        public int GetPlayerCount() => _playerCount;

        private IEnumerable<RoleDataObject> Pool()
            => _rolePoolAsset != null ? _rolePoolAsset.roleAttributionDictionary.Keys : Enumerable.Empty<RoleDataObject>();

        public IReadOnlyList<LobbyRoleView> GetRoles()
        {
            var list = new List<LobbyRoleView>();
            foreach (RoleDataObject rdo in Pool())
            {
                if (rdo == null) continue;
                Role role = rdo.role;
                (int max, int forced) st = _state.TryGetValue(role.roleID, out var s) ? s : (0, 0);
                list.Add(new LobbyRoleView(role.roleID, role.roleName.ToString(), role.factionType, (int)role.rolePortrait, st.max, st.forced));
            }
            return list;
        }

        // Harness mirrors the production default rule set so the footer gates like the real game.
        private static readonly FactionMinimum[] _minimums =
        {
            new FactionMinimum(FactionType.anomaly, 1),
            new FactionMinimum(FactionType.chosen, 1),
        };

        public IReadOnlyList<FactionMinimum> GetFactionMinimums() => _minimums;

        public Role GetRole(RoleID id)
        {
            foreach (RoleDataObject rdo in Pool())
            {
                if (rdo == null || rdo.role.roleID != id) continue;
                return LobbyRoleDetail.From(rdo);
            }
            return null;
        }

        public void RequestSetMax(RoleID id, int max)
        {
            (int max, int forced) st = _state.TryGetValue(id, out var s) ? s : (0, 0);
            st.max = Mathf.Clamp(max, 0, MaxRoleCount);
            if (st.forced > st.max) st.forced = st.max; // forced <= max invariant
            _state[id] = st;
            OnChanged?.Invoke();
        }

        public void RequestSetForced(RoleID id, int forced)
        {
            (int max, int forced) st = _state.TryGetValue(id, out var s) ? s : (0, 0);
            st.forced = Mathf.Clamp(forced, 0, st.max);
            _state[id] = st;
            OnChanged?.Invoke();
        }

        public IReadOnlyList<LobbyPresetView> GetPresets()
        {
            var list = new List<LobbyPresetView>();
            if (!Presets.TryGetValue(_playerCount, out DemoPreset[] presets)) return list;
            foreach (DemoPreset p in presets) list.Add(new LobbyPresetView(p.Name, p.Desc, p.Classic));
            return list;
        }

        public void ApplyPreset(int index)
        {
            if (!Presets.TryGetValue(_playerCount, out DemoPreset[] presets)) return;
            if (index < 0 || index >= presets.Length) return;

            _state.Clear();
            foreach ((RoleID id, int max, int forced) in presets[index].Entries)
            {
                int m = Mathf.Clamp(max, 0, MaxRoleCount);
                _state[id] = (m, Mathf.Clamp(forced, 0, m));
            }
            OnChanged?.Invoke();
        }

        public int GetActivePresetIndex()
        {
            if (!Presets.TryGetValue(_playerCount, out DemoPreset[] presets)) return -1;
            for (int i = 0; i < presets.Length; i++)
                if (MatchesPreset(presets[i])) return i;
            return -1;
        }

        // A preset is "active" when every pool role's current (max, forced) equals what the preset would set —
        // entry (clamped like ApplyPreset) if listed, else (0, 0). Any divergence → not this preset.
        private bool MatchesPreset(DemoPreset p)
        {
            foreach (RoleDataObject rdo in Pool())
            {
                if (rdo == null) continue;
                RoleID id = rdo.role.roleID;
                (int max, int forced) cur = _state.TryGetValue(id, out var s) ? s : (0, 0);
                int em = 0, ef = 0;
                foreach ((RoleID eid, int emax, int eforced) in p.Entries)
                    if (eid == id) { em = Mathf.Clamp(emax, 0, MaxRoleCount); ef = Mathf.Clamp(eforced, 0, em); break; }
                if (cur.max != em || cur.forced != ef) return false;
            }
            return true;
        }

        public void RequestStart() { /* harness only — no game loop to advance */ }
    }
}
