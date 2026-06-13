using System.Collections.Generic;
using Characters.WinningConditions;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// One non-fake owner and the winning conditions attached to it. The adapter pre-filters fakes when building
    /// these, so <see cref="VictoryEvaluator"/> never recomputes <c>isFake</c> (Story 2.8 — set at the mapping source).
    /// </summary>
    public readonly struct ConditionsForOwner
    {
        public ulong OwnerClientId { get; }
        public IEnumerable<IWinningCondition> Conditions { get; }

        public ConditionsForOwner(ulong ownerClientId, IEnumerable<IWinningCondition> conditions)
        {
            OwnerClientId = ownerClientId;
            Conditions = conditions;
        }
    }

    /// <summary>
    /// Pure win-team aggregation (Story 2.8 — the loop extracted from VictoryConditionCheckState). For each owner's
    /// conditions, evaluates <see cref="IWinningCondition.CheckCondition(GameSnapshot)"/> off the immutable snapshot
    /// and maps each satisfied condition's <see cref="WinningTeam"/> to the owning client ids.
    /// Decision-only (NFR4): it returns the mapping and triggers no state transition / RPC — the adapter applies it.
    /// </summary>
    public sealed class VictoryEvaluator
    {
        public Dictionary<WinningTeam, HashSet<ulong>> Evaluate(GameSnapshot snapshot, IEnumerable<ConditionsForOwner> owners)
        {
            var winningTeams = new Dictionary<WinningTeam, HashSet<ulong>>();

            foreach (var owner in owners)
            {
                foreach (var condition in owner.Conditions)
                {
                    if (!condition.CheckCondition(snapshot))
                    {
                        continue;
                    }

                    WinningTeam team = condition.GetWinningTeam();
                    if (!winningTeams.ContainsKey(team))
                    {
                        winningTeams[team] = new HashSet<ulong>();
                    }
                    winningTeams[team].Add(owner.OwnerClientId);
                }
            }

            return winningTeams;
        }
    }
}
