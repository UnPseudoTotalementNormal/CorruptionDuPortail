#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using Characters;
using GameLogic.GameSettings;
using GameLogic.GameStates;

namespace Autoplay
{
    /// <summary>Lobby composition for autoplay runs: the same host-side writes the lobby tablet's preset button does.</summary>
    public static class AutoplayComposition
    {
        /// <summary>
        /// Mirrors GameLobbyRolesDataSource.ApplyPreset: fills the WHOLE pool (unlisted roles go to max 0), max before
        /// forced so the server-side forced &lt;= max clamp lands on the new max. Server/host only.
        /// </summary>
        public static void ApplyPreset(GameSettingsManager _settings, RoleAttributionState _rolePool, RolePreset _preset)
        {
            foreach (RoleDataObject _roleData in _rolePool.roleAttributionDictionary.Keys)
            {
                RoleID _id = _roleData.role.roleID;
                bool _listed = _preset.entries.Any(_e => _e.roleId == _id);
                RolePreset.Entry _entry = _preset.entries.FirstOrDefault(_e => _e.roleId == _id);
                _settings.RequestSetRoleCount(_id, _listed ? _entry.max : 0);
                _settings.RequestSetForced(_id, _listed ? _entry.forced : 0);
            }
        }
    }
}
#endif
