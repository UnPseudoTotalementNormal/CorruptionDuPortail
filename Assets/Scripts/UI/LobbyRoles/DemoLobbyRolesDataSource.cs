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
    }
}
