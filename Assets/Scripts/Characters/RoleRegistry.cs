#region

using System.Collections.Generic;
using UnityEngine;

#endregion

namespace Characters
{
    /// <summary>
    /// NET-07 (epic-network-sync-hardening): RoleID → authored <see cref="RoleDataObject"/>, identical on every peer
    /// (the role pool assets ship in every build). A character's role is replicated as its <see cref="RoleID"/> only;
    /// each peer rebuilds the <see cref="Role"/> from this registry, so no RPC (that could be lost, reordered, or
    /// orphaned by a spawn race — the "blank white card") ever carries a role again.
    /// Filled by RoleAttributionState.OnStateCreated on every peer (and by the server when it applies a role).
    /// Assets, not session state: entries never go stale, so no per-session reset is needed.
    /// </summary>
    public static class RoleRegistry
    {
        private static readonly Dictionary<RoleID, RoleDataObject> s_byId = new();

        public static void Register(RoleDataObject _roleDataObject)
        {
            if (_roleDataObject == null || _roleDataObject.role == null)
            {
                return;
            }

            RoleID _id = _roleDataObject.role.roleID;
            if ((int)_id == 0)
            {
                // 0 is the "no role" sentinel of Character.roleId (RoleID declares no 0 value): an id-less role (the
                // synthetic roles of the role-assignment golden harness) can never be replicated, so it is not indexed.
                return;
            }
            if (s_byId.TryGetValue(_id, out RoleDataObject _existing) && _existing != null && _existing != _roleDataObject
                && _existing.role.roleName != _roleDataObject.role.roleName)
            {
                Debug.LogError($"[ROLE] RoleID {_id} is used by two different roles ('{_existing.role.roleName}' and " +
                               $"'{_roleDataObject.role.roleName}') — roles cannot replicate correctly. Fix the RoleDataObject assets.");
                return;
            }
            bool _isNew = !s_byId.ContainsKey(_id);
            s_byId[_id] = _roleDataObject;
            if (_isNew)
            {
                onRegistered?.Invoke(_id);
            }
        }

        /// <summary>A role id became known on this peer. A character replicated before the role pool was loaded (a game
        /// that joins mid-game, fresh after a relaunch) rebuilds its role then.</summary>
        public static event System.Action<RoleID> onRegistered;

        public static bool TryGet(RoleID _id, out RoleDataObject _roleDataObject)
        {
            return s_byId.TryGetValue(_id, out _roleDataObject) && _roleDataObject != null;
        }

        /// <summary>A fresh per-character copy of the authored role (same Clone the server uses at attribution).</summary>
        public static Role CreateRole(RoleID _id)
        {
            return TryGet(_id, out RoleDataObject _roleDataObject) ? (Role)_roleDataObject.role.Clone() : null;
        }
    }
}
