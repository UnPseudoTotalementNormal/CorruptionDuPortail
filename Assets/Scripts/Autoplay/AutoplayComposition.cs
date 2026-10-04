#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Characters;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using UnityEngine;
using Unpseudo.Autoplay;

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

        /// <summary>
        /// Guarantees one real copy of every role whose name contains one of <paramref name="_fragments"/>
        /// (max raised to at least 1, forced 1). Returns the fragments that matched no role.
        /// </summary>
        public static IEnumerable<string> ForceRoles(GameSettingsManager _settings, RoleAttributionState _rolePool,
            IEnumerable<string> _fragments, AutoplayJournal _journal)
        {
            foreach (string _fragment in _fragments)
            {
                RoleDataObject _match = _rolePool.roleAttributionDictionary.Keys.FirstOrDefault(_r =>
                    _r.role.roleName.ToString().IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0);
                if (_match == null)
                {
                    yield return _fragment;
                    continue;
                }

                RoleID _id = _match.role.roleID;
                _settings.RequestSetRoleCount(_id, Math.Max(1, _settings.GetRoleCount(_id)));
                _settings.RequestSetForced(_id, 1);
                _journal.Record("composition.force", $"{_match.role.roleName} (from '{_fragment}')");
            }
        }

        [Serializable]
        private sealed class PowerInfo
        {
            public string name;
            public bool passive;
            public bool targeted;
        }

        [Serializable]
        private sealed class RoleInfo
        {
            public string name;
            public string faction;
            public PowerInfo[] powers;
        }

        [Serializable]
        private sealed class RolePool
        {
            public RoleInfo[] roles;
        }

        /// <summary>The authored role pool and each role's powers (for coverage tools such as the power sweep).</summary>
        public static void WriteRolePool(RoleAttributionState _rolePool, string _path)
        {
            var _pool = new RolePool
            {
                roles = _rolePool.roleAttributionDictionary.Keys
                    .Where(_r => _r != null && _r.role != null)
                    .Select(_r => new RoleInfo
                    {
                        name = _r.role.roleName.ToString(),
                        faction = _r.role.factionType.ToString(),
                        powers = PowersOf(_r).ToArray(),
                    })
                    .ToArray(),
            };
            File.WriteAllText(_path, JsonUtility.ToJson(_pool, true));
        }

        private static IEnumerable<PowerInfo> PowersOf(RoleDataObject _roleData)
            => (_roleData.powers ?? new List<Characters.Powers.Power>())
                .Where(_p => _p != null)
                .Select(_p => new PowerInfo { name = _p.powerName.ToString(), passive = _p.isPassive, targeted = _p.needTargetSelection });

    }
}
#endif
