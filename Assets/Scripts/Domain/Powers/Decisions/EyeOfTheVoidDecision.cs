using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PEyeOfTheVoid — passive: on game start, discover the shared anomaly chat for every anomaly-faction
    /// player. Config-as-public-field (<see cref="AnomalyChatId"/>) is serialized by Unity when this POCO
    /// is [SerializeReference]'d on the prefab — no [SerializeField], so the Domain purity wall holds.
    /// Empty override name (the 2-arg DiscoverChat overload).
    /// </summary>
    public sealed class EyeOfTheVoidDecision : IPowerDecision
    {
        /// <summary>Adapter-supplied chat window id (e.g. (int)ChatWindowIDs.AnomalyOnly). Set on the prefab.</summary>
        public int AnomalyChatId;

        public PowerId Id => PowerId.EyeOfTheVoid;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            foreach (var slot in ctx.Roster.Slots)
            {
                if (ctx.Roster.FactionOf(slot) == FactionType.anomaly)
                {
                    effects.Add(new DiscoverChat(AnomalyChatId, "", PowerEffectAudience.Specific(slot)));
                }
            }
            return effects.Count == 0 ? PowerOutcome.AcceptEmpty() : PowerOutcome.Accept(effects);
        }
    }
}
