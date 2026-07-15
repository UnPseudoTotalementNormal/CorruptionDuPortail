using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameLogic.GameSettings
{
    /// <summary>
    /// The authored set of lobby <see cref="RolePreset"/>s. Queried by the tablet lobby app to offer a one-click
    /// "classique" and a player-count-filtered list of alternatives. Content is design-owned (Poyo). Wire the
    /// asset on the game data source.
    /// </summary>
    [CreateAssetMenu(fileName = "RolePresetDatabase", menuName = "Corruption/Role Preset Database")]
    public class RolePresetDatabase : ScriptableObject
    {
        public List<RolePreset> presets = new();

        /// <summary>Presets targeting exactly this player count, in authored order.</summary>
        public List<RolePreset> ForPlayerCount(int playerCount)
        {
            return presets.Where(p => p != null && p.playerCount == playerCount).ToList();
        }

        /// <summary>The classic preset for this player count (first flagged; else the first of that count; else null).</summary>
        public RolePreset Classic(int playerCount)
        {
            List<RolePreset> forCount = ForPlayerCount(playerCount);
            return forCount.FirstOrDefault(p => p.isClassic) ?? forCount.FirstOrDefault();
        }
    }
}
