using System;
using System.Collections.Generic;

namespace UI.InfoTable
{
    /// <summary>Per-cell deduction state. <see cref="None"/> = the uGUI "no toggle on" state.</summary>
    public enum CellState
    {
        None,
        Sure,
        Maybe,
        SurelyNot
    }

    /// <summary>
    /// Pure C# model of the Players×Roles deduction matrix. Behaviour ported verbatim from the uGUI
    /// <c>InfoTablePlayerRoleHandler</c> + <c>InfoTableSystem.CheckGlobalConflicts</c>:
    /// <list type="bullet">
    /// <item>local conflict = more than one <see cref="CellState.Sure"/> in a player row;</item>
    /// <item>global conflict = more players marked Sure for a role than that role's capacity;</item>
    /// <item>reveal-lock = a revealed row forced to correct=Sure / others=SurelyNot and made non-interactive.</item>
    /// </list>
    /// No Unity dependency — unit-testable and harness-runnable.
    /// </summary>
    public sealed class InfoTableModel
    {
        private IReadOnlyList<InfoTablePlayer> _players = Array.Empty<InfoTablePlayer>();
        private IReadOnlyList<InfoTableRole> _roles = Array.Empty<InfoTableRole>();
        private CellState[,] _cells = new CellState[0, 0];
        private bool[] _locked = Array.Empty<bool>();
        private ConflictType[] _conflict = Array.Empty<ConflictType>();

        /// <summary>Raised after any interaction that mutated cell/conflict/lock state (NOT on <see cref="Build"/>).</summary>
        public event Action OnChanged;

        public IReadOnlyList<InfoTablePlayer> Players => _players;
        public IReadOnlyList<InfoTableRole> Roles => _roles;
        public int PlayerCount => _players.Count;
        public int RoleCount => _roles.Count;

        /// <summary>Structural reset to a fresh empty matrix for the given players/roles. Does not raise OnChanged.</summary>
        public void Build(IReadOnlyList<InfoTablePlayer> players, IReadOnlyList<InfoTableRole> roles)
        {
            _players = players ?? Array.Empty<InfoTablePlayer>();
            _roles = roles ?? Array.Empty<InfoTableRole>();
            _cells = new CellState[_players.Count, _roles.Count];
            _locked = new bool[_players.Count];
            _conflict = new ConflictType[_players.Count];
        }

        public CellState GetCell(int playerIndex, int roleIndex) => _cells[playerIndex, roleIndex];
        public bool IsLocked(int playerIndex) => _locked[playerIndex];
        public ConflictType GetConflict(int playerIndex) => _conflict[playerIndex];

        /// <summary>A Sure cell renders "in conflict" when its row is in any conflict state (mirrors UpdateCheckerConflicts).</summary>
        public bool IsCellInConflict(int playerIndex, int roleIndex)
            => _cells[playerIndex, roleIndex] == CellState.Sure && _conflict[playerIndex] != ConflictType.None;

        /// <summary>How many players are marked <see cref="CellState.Sure"/> for a role (its column) — the capacity fill.</summary>
        public int GetSureCountForRole(int roleIndex)
        {
            if (roleIndex < 0 || roleIndex >= _roles.Count) return 0;
            int count = 0;
            for (int pi = 0; pi < _players.Count; pi++)
            {
                if (_cells[pi, roleIndex] == CellState.Sure) count++;
            }
            return count;
        }

        /// <summary>True when more players are marked Sure for a role than its capacity (the over-capacity signal).</summary>
        public bool IsRoleOverCapacity(int roleIndex)
            => roleIndex >= 0 && roleIndex < _roles.Count && GetSureCountForRole(roleIndex) > _roles[roleIndex].Capacity;

        /// <summary>True when any player row is currently in a conflict state (drives the footer alert banner).</summary>
        public bool HasAnyConflict()
        {
            for (int pi = 0; pi < _players.Count; pi++)
            {
                if (_conflict[pi] != ConflictType.None) return true;
            }
            return false;
        }

        /// <summary>User clicked a swatch: set that state, or clear to None when re-clicking the active state. Locked rows ignore.</summary>
        public void SetCell(int playerIndex, int roleIndex, CellState state)
        {
            if (playerIndex < 0 || playerIndex >= _players.Count) return;
            if (roleIndex < 0 || roleIndex >= _roles.Count) return;
            if (_locked[playerIndex]) return;

            _cells[playerIndex, roleIndex] = _cells[playerIndex, roleIndex] == state ? CellState.None : state;
            Recompute();
        }

        /// <summary>Lock a revealed row: correct column Sure, others SurelyNot, non-interactive, no conflict.</summary>
        public void LockRowToRole(int playerIndex, string revealedRoleName)
        {
            if (playerIndex < 0 || playerIndex >= _players.Count) return;

            // Find the FIRST column matching the revealed role. If none matches, do NOT seal the row into an
            // impossible all-SurelyNot state (it would lock the player as "not any role", permanently
            // uneditable) — leave it interactive. In-game the column always exists (headers are built from the
            // same characters), so a no-match only arises from an out-of-band source; matching only the first
            // column also keeps a duplicate-named column set from being forced Sure twice.
            int matchIndex = -1;
            for (int ri = 0; ri < _roles.Count; ri++)
            {
                if (_roles[ri].RoleName == revealedRoleName) { matchIndex = ri; break; }
            }
            if (matchIndex < 0) return;

            for (int ri = 0; ri < _roles.Count; ri++)
            {
                _cells[playerIndex, ri] = ri == matchIndex ? CellState.Sure : CellState.SurelyNot;
            }
            _locked[playerIndex] = true;
            _conflict[playerIndex] = ConflictType.None;
            Recompute();
        }

        private void Recompute()
        {
            // Local conflict: >1 Sure in a row (locked rows never conflict).
            for (int pi = 0; pi < _players.Count; pi++)
            {
                if (_locked[pi])
                {
                    _conflict[pi] = ConflictType.None;
                    continue;
                }

                int sure = 0;
                for (int ri = 0; ri < _roles.Count; ri++)
                {
                    if (_cells[pi, ri] == CellState.Sure) sure++;
                }
                _conflict[pi] = sure > 1 ? ConflictType.PlayerMultipleRoles : ConflictType.None;
            }

            // Global conflict: per role, more Sure rows than capacity. Local conflict + locked take precedence.
            for (int ri = 0; ri < _roles.Count; ri++)
            {
                int sureRows = 0;
                for (int pi = 0; pi < _players.Count; pi++)
                {
                    if (_cells[pi, ri] == CellState.Sure) sureRows++;
                }
                if (sureRows <= _roles[ri].Capacity) continue;

                for (int pi = 0; pi < _players.Count; pi++)
                {
                    if (_locked[pi]) continue;
                    if (_cells[pi, ri] != CellState.Sure) continue;
                    if (_conflict[pi] == ConflictType.PlayerMultipleRoles) continue;
                    _conflict[pi] = ConflictType.RoleOverCapacity;
                }
            }

            OnChanged?.Invoke();
        }
    }
}
