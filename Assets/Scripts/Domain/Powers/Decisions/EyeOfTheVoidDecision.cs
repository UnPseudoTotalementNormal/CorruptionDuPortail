using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PEyeOfTheVoid — passive: on game start, discover the shared anomaly chat for every anomaly-faction
    /// player, and reveal each anomaly's role to the others (GD wording: "les Anomalies se connaissent et
    /// communiquent entre elles"). Fake characters (unassigned roles, no player) are skipped: nobody to talk to,
    /// and revealing one would give away that its role is fake. Granting twice (Mage + Abyss both hold it) is
    /// harmless: chat membership and reveals are idempotent. Config-as-public-field (<see cref="AnomalyChatId"/>) is serialized by Unity when this POCO
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
            var anomalies = new List<int>();
            foreach (var slot in ctx.Roster.Slots)
            {
                if (ctx.Roster.FactionOf(slot) == FactionType.anomaly && !ctx.Roster.IsFake(slot))
                {
                    anomalies.Add(slot);
                    effects.Add(new DiscoverChat(AnomalyChatId, "", PowerEffectAudience.Specific(slot)));
                }
            }
            foreach (var viewer in anomalies)
            {
                foreach (var other in anomalies)
                {
                    if (other == viewer) continue;
                    effects.Add(new RevealInfo(other, RevealField.RoleRevealed, RevealVisibility.Personal, viewer, false));
                }
            }
            return effects.Count == 0 ? PowerOutcome.AcceptEmpty() : PowerOutcome.Accept(effects);
        }
    }
}
