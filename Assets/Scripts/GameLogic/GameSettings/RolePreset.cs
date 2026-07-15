using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;

namespace GameLogic.GameSettings
{
    /// <summary>
    /// A designer-authored lobby composition preset for a given player count: a named set of per-role max/forced
    /// values. Applying it fills the whole pool (roles not listed go to max 0). One preset per count may be flagged
    /// <see cref="isClassic"/> — the one-click "balanced game" for a new host (the Discord "recommandation" task).
    /// Content is design-owned (Poyo).
    /// </summary>
    [CreateAssetMenu(fileName = "RolePreset", menuName = "Corruption/Role Preset")]
    public class RolePreset : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public RoleID roleId;
            public int max;
            public int forced;
        }

        public string displayName;
        [TextArea] public string description;
        [Tooltip("The connected-player count this preset targets.")]
        public int playerCount;
        [Tooltip("The one-click 'balanced' preset for this player count. At most one per count should be classic.")]
        public bool isClassic;
        public List<Entry> entries = new();
    }
}
