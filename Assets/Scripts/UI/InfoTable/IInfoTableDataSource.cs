using System;
using System.Collections.Generic;

namespace UI.InfoTable
{
    /// <summary>One player row in the deduction grid (a non-fake character).</summary>
    public readonly struct InfoTablePlayer
    {
        public readonly ulong ClientId;
        public readonly string Pseudo;

        public InfoTablePlayer(ulong clientId, string pseudo)
        {
            ClientId = clientId;
            Pseudo = pseudo;
        }
    }

    /// <summary>
    /// One role column. Identity is <see cref="RoleName"/>; <see cref="Capacity"/> is how many characters
    /// (fakes included) hold that role — the over-capacity conflict threshold.
    /// </summary>
    public readonly struct InfoTableRole
    {
        public readonly string RoleName;
        public readonly int Capacity;

        public InfoTableRole(string roleName, int capacity)
        {
            RoleName = roleName;
            Capacity = capacity;
        }
    }

    /// <summary>
    /// Presentation-only data seam for the UITK InfoTable. Keeps the POCO model + controller free of NGO/engine
    /// types so the grid runs in the standalone harness (<c>DemoInfoTableDataSource</c>) and is unit-testable.
    /// The real game is wired through <c>GameInfoTableDataSource</c>, the only implementer that touches NGO.
    /// </summary>
    public interface IInfoTableDataSource
    {
        /// <summary>Raised when the grid should be rebuilt from scratch (game started / roles attributed).</summary>
        event Action OnRebuildRequested;

        /// <summary>Raised when a reveal level changed and rows should re-scan their lock state.</summary>
        event Action OnRevealChanged;

        /// <summary>Player rows, in display order, fakes excluded.</summary>
        IReadOnlyList<InfoTablePlayer> GetPlayers();

        /// <summary>Role columns with capacities, counted across ALL characters incl. fakes, in display order.</summary>
        IReadOnlyList<InfoTableRole> GetRoles();

        /// <summary>The revealed role name for a client, or <c>null</c> when not revealed. Locks a row when set.</summary>
        string GetRevealedRoleName(ulong clientId);
    }
}
