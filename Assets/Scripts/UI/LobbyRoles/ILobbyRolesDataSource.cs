using System;
using System.Collections.Generic;
using Characters;

namespace UI.LobbyRoles
{
    /// <summary>
    /// One role card in the lobby attribution grid: identity + faction (for grouping/tint) + the two editable
    /// integers of the max/forced model. <see cref="Max"/> is the pool cap; <see cref="Forced"/> is the
    /// guaranteed minimum of real assignments (forced &lt;= max). A role's fakeable capacity is max - forced.
    /// </summary>
    public readonly struct LobbyRoleView
    {
        public readonly RoleID Id;
        public readonly string Name;
        public readonly FactionType Faction;
        public readonly int PortraitId; // CharacterPortraits enum as int; the controller resolves the sprite via PortraitTable
        public readonly int Max;
        public readonly int Forced;

        public LobbyRoleView(RoleID id, string name, FactionType faction, int portraitId, int max, int forced)
        {
            Id = id;
            Name = name;
            Faction = faction;
            PortraitId = portraitId;
            Max = max;
            Forced = forced;
        }
    }

    /// <summary>A preset offered for the current player count (name + one-liner + whether it's the classic).</summary>
    public readonly struct LobbyPresetView
    {
        public readonly string Name;
        public readonly string Description;
        public readonly bool IsClassic;

        public LobbyPresetView(string name, string description, bool isClassic)
        {
            Name = name;
            Description = description;
            IsClassic = isClassic;
        }
    }

    /// <summary>
    /// Presentation-only data seam for the UITK lobby role-attribution app. Keeps the controller free of NGO so
    /// the grid runs in the standalone Play harness (<c>DemoLobbyRolesDataSource</c>) and is testable. The real
    /// game wires <c>GameLobbyRolesDataSource</c> (the only implementer that touches the server-authoritative
    /// <c>GameSettingsManager</c> + the connected-player list). Mirrors the InfoTable seam pattern.
    ///
    /// Writes are host-only requests: on a non-host they are no-ops (the manager mirrors are read-only), exactly
    /// like the uGUI slider path. Which characters receive the roles is decided server-side at game start.
    /// </summary>
    public interface ILobbyRolesDataSource
    {
        /// <summary>Raised when the settings or the player count changed and the grid + tally should refresh.</summary>
        event Action OnChanged;

        /// <summary>Connected-player count (read-only — the tablet never edits it).</summary>
        int GetPlayerCount();

        /// <summary>All roles in the authored pool order, with their current max/forced.</summary>
        IReadOnlyList<LobbyRoleView> GetRoles();

        /// <summary>The full authored <see cref="Role"/> (with its powers) for a role, for the detail overlay. Null if unknown.</summary>
        Role GetRole(RoleID id);

        /// <summary>Host-only: propose a new pool cap (max) for a role. Re-clamps forced &lt;= max server-side.</summary>
        void RequestSetMax(RoleID id, int max);

        /// <summary>Host-only: propose a new guaranteed minimum (forced) for a role. Clamped to [0, max].</summary>
        void RequestSetForced(RoleID id, int forced);

        /// <summary>Presets available for the CURRENT player count, in display order (classic first by convention).</summary>
        IReadOnlyList<LobbyPresetView> GetPresets();

        /// <summary>Host-only: apply the preset at <paramref name="index"/> of <see cref="GetPresets"/> (fills the whole pool).</summary>
        void ApplyPreset(int index);

        /// <summary>
        /// Index (into <see cref="GetPresets"/>) of the preset whose composition EXACTLY matches the current pool,
        /// or -1 if none — i.e. the pool was hand-edited ("Personnalisé"). Lets the UI show the active preset and
        /// clear that badge the moment a stepper diverges from it. Pure comparison, no state tracking.
        /// </summary>
        int GetActivePresetIndex();

        /// <summary>Host-only: request the game to start. The server re-validates the composition gate before advancing.</summary>
        void RequestStart();
    }

    /// <summary>
    /// Builds a detail-ready <see cref="Role"/> for the RoleCard overlay from an authored <see cref="RoleDataObject"/>.
    /// A fresh Role (its own powers list) whose powers = the authored <c>RoleDataObject.powers</c> — because
    /// <c>Role.powers</c> is readonly and the authored <c>role.powers</c> is empty pre-game (powers are added to a
    /// character's role only at distribution). Never mutates the ScriptableObject.
    /// </summary>
    public static class LobbyRoleDetail
    {
        public static Role From(RoleDataObject rdo)
        {
            if (rdo == null) return null;
            Role src = rdo.role;
            var detail = new Role
            {
                roleName = src.roleName,
                factionType = src.factionType,
                roleID = src.roleID,
                rolePortrait = src.rolePortrait,
                roleDifficulty = src.roleDifficulty,
            };
            if (rdo.powers != null) detail.powers.AddRange(rdo.powers);
            return detail;
        }
    }
}
