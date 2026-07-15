using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.LobbyRoles
{
    /// <summary>
    /// HARNESS-ONLY <see cref="ILobbyRolesDataSource"/>. Feeds a mock role roster with in-memory max/forced so the
    /// UITK lobby app can be built and Play-tested without a running multiplayer game. Steppers mutate the local
    /// state and raise <see cref="OnChanged"/> (no network). Left/Right arrows tweak the mock connected-player
    /// count so the presets/gating can be exercised. Typed on the REAL max/forced model (not a fiction) so the
    /// harness validates the same contract the game data source will implement. Not used in GameScene.
    /// </summary>
    public class DemoLobbyRolesDataSource : MonoBehaviour, ILobbyRolesDataSource
    {
        private struct Entry
        {
            public RoleID Id;
            public string Name;
            public FactionType Faction;
            public int Max;
            public int Forced;
        }

        [SerializeField] private int _playerCount = 5;

        private readonly List<Entry> _roles = new()
        {
            new Entry { Id = RoleID.MageOcculte,   Name = "Va'ahl, Le Mage Occulte",    Faction = FactionType.anomaly, Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Abyss,         Name = "Abyss, L'Extension du Néant", Faction = FactionType.anomaly, Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Robot,         Name = "Le Robot",                    Faction = FactionType.anomaly, Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Orpheline,     Name = "L'Orpheline",                 Faction = FactionType.anomaly, Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Technomancien, Name = "Le Technomancien",            Faction = FactionType.chosen,  Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Croupiere,     Name = "Luma, la croupière",          Faction = FactionType.chosen,  Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Oracle,        Name = "L'Oracle",                    Faction = FactionType.chosen,  Max = 0, Forced = 0 },
            new Entry { Id = RoleID.Gardien,       Name = "Le Gardien du Portail",       Faction = FactionType.chosen,  Max = 0, Forced = 0 },
        };

        private const int MaxRoleCount = 15;

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

        public IReadOnlyList<LobbyRoleView> GetRoles()
        {
            var list = new List<LobbyRoleView>(_roles.Count);
            foreach (Entry e in _roles)
            {
                list.Add(new LobbyRoleView(e.Id, e.Name, e.Faction, 0, e.Max, e.Forced));
            }
            return list;
        }

        public void RequestSetMax(RoleID id, int max)
        {
            for (int i = 0; i < _roles.Count; i++)
            {
                if (_roles[i].Id != id) continue;
                Entry e = _roles[i];
                e.Max = Mathf.Clamp(max, 0, MaxRoleCount);
                if (e.Forced > e.Max) e.Forced = e.Max; // forced <= max invariant
                _roles[i] = e;
                OnChanged?.Invoke();
                return;
            }
        }

        public void RequestSetForced(RoleID id, int forced)
        {
            for (int i = 0; i < _roles.Count; i++)
            {
                if (_roles[i].Id != id) continue;
                Entry e = _roles[i];
                e.Forced = Mathf.Clamp(forced, 0, e.Max);
                _roles[i] = e;
                OnChanged?.Invoke();
                return;
            }
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

            for (int i = 0; i < _roles.Count; i++) { Entry e = _roles[i]; e.Max = 0; e.Forced = 0; _roles[i] = e; }
            foreach ((RoleID id, int max, int forced) in presets[index].Entries)
            {
                for (int i = 0; i < _roles.Count; i++)
                {
                    if (_roles[i].Id != id) continue;
                    Entry e = _roles[i];
                    e.Max = Mathf.Clamp(max, 0, MaxRoleCount);
                    e.Forced = Mathf.Clamp(forced, 0, e.Max);
                    _roles[i] = e;
                    break;
                }
            }
            OnChanged?.Invoke();
        }
    }
}
