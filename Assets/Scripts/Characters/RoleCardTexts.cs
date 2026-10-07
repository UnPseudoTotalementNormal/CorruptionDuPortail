#region

using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

#endregion

namespace Characters
{
    /// <summary>The passive lines of one role, written by the game designer for the role card.</summary>
    [Serializable]
    public class RoleCardPassives
    {
        [TextArea(1, 4)] public List<string> lines = new();
    }

    /// <summary>
    /// Design-owned wording of each role's "Passif" block on the role card, keyed by RoleID. The designer writes the
    /// passives per role (a faction rule like "les Anomalies se connaissent" has no power of its own), while power
    /// descriptions stay per power. A role without an entry falls back to its passive powers' descriptions.
    /// Presentation only, never replicated.
    /// </summary>
    [CreateAssetMenu(fileName = "RoleCardTexts", menuName = "Corruption/Role Card Texts")]
    public class RoleCardTexts : ScriptableObject
    {
        [SerializeField] private SerializedDictionary<RoleID, RoleCardPassives> passives = new();

        public bool TryGetPassives(RoleID role, out IReadOnlyList<string> lines)
        {
            lines = passives.TryGetValue(role, out RoleCardPassives entry) && entry != null && entry.lines.Count > 0
                ? entry.lines
                : null;
            return lines != null;
        }
    }
}
