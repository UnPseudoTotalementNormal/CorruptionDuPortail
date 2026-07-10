using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.InfoTable
{
    /// <summary>
    /// HARNESS-ONLY <see cref="IInfoTableDataSource"/>. Feeds the mockup player/role set so the UITK InfoTable
    /// can be built and Play-tested in <c>TabletOnlySpike</c> without a running multiplayer game. Space rebuilds
    /// the grid; R reveals the first player as the first role (exercises the lock path). The Add/Remove methods
    /// let a harness control panel grow/shrink the grid live. Not used in GameScene.
    /// </summary>
    public class DemoInfoTableDataSource : MonoBehaviour, IInfoTableDataSource
    {
        [SerializeField] private List<string> _players = new() { "afzaf", "Simulated 1", "Simulated 2" };
        [SerializeField] private List<string> _roles = new() { "Dr Gloubi", "L'Orpheline", "Va'ahl, Le Mage Occulte" };

        // Pool of extra role names to cycle through when adding roles, so new columns look varied before falling
        // back to a numbered name.
        private static readonly string[] RolePool =
        {
            "Le Corrompu", "La Sentinelle", "L'Oracle", "Le Passeur", "L'Ombre", "Le Gardien du Portail"
        };

        public event Action OnRebuildRequested;
        public event Action OnRevealChanged;

        private readonly Dictionary<ulong, string> _revealed = new();
        private int _roleCounter;

        private void Start() => OnRebuildRequested?.Invoke();

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame)
            {
                _revealed.Clear();
                OnRebuildRequested?.Invoke();
            }
            if (kb.rKey.wasPressedThisFrame && _players.Count > 0 && _roles.Count > 0)
            {
                _revealed[0] = _roles[0];
                OnRevealChanged?.Invoke();
            }
        }

        // ---- Harness mutation API (called by SpikeInfoTableControls) ----

        public void AddPlayer()
        {
            _players.Add($"Joueur {_players.Count + 1}");
            RebuildFresh();
        }

        public void RemovePlayer()
        {
            if (_players.Count == 0) return;
            _players.RemoveAt(_players.Count - 1);
            RebuildFresh();
        }

        public void AddRole()
        {
            string name = _roleCounter < RolePool.Length ? RolePool[_roleCounter] : $"Rôle {_roles.Count + 1}";
            _roleCounter++;
            _roles.Add(name);
            RebuildFresh();
        }

        public void RemoveRole()
        {
            if (_roles.Count == 0) return;
            _roles.RemoveAt(_roles.Count - 1);
            RebuildFresh();
        }

        public int PlayerCount => _players.Count;
        public int RoleCount => _roles.Count;

        // Reveals key on player index, so any structural change clears them to avoid stale locks.
        private void RebuildFresh()
        {
            _revealed.Clear();
            OnRebuildRequested?.Invoke();
        }

        public IReadOnlyList<InfoTablePlayer> GetPlayers()
        {
            var list = new List<InfoTablePlayer>();
            for (int i = 0; i < _players.Count; i++)
            {
                list.Add(new InfoTablePlayer((ulong)i, _players[i]));
            }
            return list;
        }

        public IReadOnlyList<InfoTableRole> GetRoles()
        {
            var list = new List<InfoTableRole>();
            foreach (string r in _roles)
            {
                list.Add(new InfoTableRole(r, 1, GuessFaction(r)));
            }
            return list;
        }

        // Harness-only: the demo roles are bare strings with no real Role/faction, so fake a faction by name so the
        // board can show the 3 "found" colours (anomaly=scarlet / marginal=orange / chosen=green). Not game logic.
        private static FactionType GuessFaction(string roleName)
        {
            if (roleName.IndexOf("Mage Occulte", StringComparison.OrdinalIgnoreCase) >= 0) return FactionType.anomaly;
            if (roleName.IndexOf("Robot", StringComparison.OrdinalIgnoreCase) >= 0) return FactionType.marginal;
            return FactionType.chosen;
        }

        public string GetRevealedRoleName(ulong clientId)
            => _revealed.TryGetValue(clientId, out string role) ? role : null;
    }
}
