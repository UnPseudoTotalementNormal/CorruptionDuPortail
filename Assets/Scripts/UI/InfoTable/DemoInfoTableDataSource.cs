using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.InfoTable
{
    /// <summary>
    /// HARNESS-ONLY <see cref="IInfoTableDataSource"/>. Feeds the mockup player/role set so the UITK InfoTable
    /// can be built and Play-tested in <c>TabletOnlySpike</c> without a running multiplayer game. Space rebuilds
    /// the grid; R reveals the first player as the first role (exercises the lock path). Not used in GameScene.
    /// </summary>
    public class DemoInfoTableDataSource : MonoBehaviour, IInfoTableDataSource
    {
        [SerializeField] private string[] _players = { "afzaf", "Simulated 1", "Simulated 2" };
        [SerializeField] private string[] _roles = { "Dr Gloubi", "L'Orpheline", "Va'ahl, Le Mage Occulte" };

        public event Action OnRebuildRequested;
        public event Action OnRevealChanged;

        private readonly Dictionary<ulong, string> _revealed = new();

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
            if (kb.rKey.wasPressedThisFrame && _players.Length > 0 && _roles.Length > 0)
            {
                _revealed[0] = _roles[0];
                OnRevealChanged?.Invoke();
            }
        }

        public IReadOnlyList<InfoTablePlayer> GetPlayers()
        {
            var list = new List<InfoTablePlayer>();
            for (int i = 0; i < _players.Length; i++)
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
                list.Add(new InfoTableRole(r, 1));
            }
            return list;
        }

        public string GetRevealedRoleName(ulong clientId)
            => _revealed.TryGetValue(clientId, out string role) ? role : null;
    }
}
