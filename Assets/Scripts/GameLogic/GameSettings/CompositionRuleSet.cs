using System.Collections.Generic;
using Characters;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace GameLogic.GameSettings
{
    /// <summary>
    /// Authoring shell for the lobby composition rules — the SINGLE source of the per-faction minimums shared by
    /// the server start gate (via <c>RoleAttributionState.ValidateComposition</c>), the live distribution, and the
    /// client footer mirror. Editing this asset is the whole "add / remove / retune a faction rule" surface; the
    /// evaluation itself lives in the pure <see cref="CompositionValidator"/> / <see cref="RoleDistributor"/>.
    /// Default = anomaly ≥ 1 AND chosen ("élu") ≥ 1.
    /// </summary>
    [CreateAssetMenu(fileName = "CompositionRuleSet", menuName = "GameSettings/CompositionRuleSet")]
    public class CompositionRuleSet : ScriptableObject
    {
        [System.Serializable]
        public struct FactionFloor
        {
            public FactionType faction;
            [Min(0)] public int min;
        }

        [SerializeField]
        [Tooltip("Minimum real players guaranteed per faction. Add/remove an entry to change the faction rules.")]
        private List<FactionFloor> factionMinimums = new()
        {
            new FactionFloor { faction = FactionType.anomaly, min = 1 },
            new FactionFloor { faction = FactionType.chosen, min = 1 },
        };

        /// <summary>
        /// Projects the authored floors onto the pure Domain minimums (engine-free, NFR4). Duplicate faction
        /// entries are coalesced (the strongest floor wins) so the validator and the distributor — which both
        /// consume this list — can never disagree on an authoring slip like two anomaly rows.
        /// </summary>
        public IReadOnlyList<FactionMinimum> ToMinimums()
        {
            var byFaction = new Dictionary<FactionType, int>();
            var order = new List<FactionType>();
            foreach (FactionFloor floor in factionMinimums)
            {
                if (!byFaction.TryGetValue(floor.faction, out int cur))
                {
                    order.Add(floor.faction);
                }
                byFaction[floor.faction] = Mathf.Max(cur, floor.min);
            }

            var list = new List<FactionMinimum>(order.Count);
            foreach (FactionType faction in order)
            {
                list.Add(new FactionMinimum(faction, byFaction[faction]));
            }
            return list;
        }
    }
}
