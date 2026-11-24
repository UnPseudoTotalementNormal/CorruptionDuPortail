using System.Collections.Generic;
using Characters;
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

    /// <summary>
    /// When the portal carrier (Va'ahl, le Mage Occulte) takes down the portal: the anomalies' last chance before the
    /// chosen win. GD rule (playtest 2026-11-20): only once EVERY anomaly is chained, never as soon as the carrier alone
    /// is chained, and once per game. Anomalies are read exactly like <c>WChosenChainedAllAnomaly</c> (the chosen win
    /// this step comes just before): non-fake characters of the anomaly faction. Lives here with the victory decision it
    /// precedes. Decides, never applies (the adapter is <c>TakeDownThePortalState</c>).
    /// </summary>
    public static class PortalTakedownRule
    {
        public readonly struct Decision
        {
            public bool Activate { get; }

            /// <summary>Short stable reason for the playtest log: "all-anomalies-chained", "no-carrier",
            /// "already-played", "anomalies-free".</summary>
            public string Reason { get; }

            /// <summary>Anomalies not chained yet (empty when the portal plays).</summary>
            public IReadOnlyList<ulong> FreeAnomalies { get; }

            public Decision(bool activate, string reason, IReadOnlyList<ulong> freeAnomalies)
            {
                Activate = activate;
                Reason = reason;
                FreeAnomalies = freeAnomalies;
            }
        }

        /// <param name="snapshot">Live picture of the game (same source as the victory check).</param>
        /// <param name="carrierChained">A character carrying the take-down-the-portal power has been chained.</param>
        /// <param name="alreadyPlayed">The portal step already played this game.</param>
        public static Decision Decide(GameSnapshot snapshot, bool carrierChained, bool alreadyPlayed)
        {
            var free = new List<ulong>();
            if (snapshot != null)
            {
                foreach (CharacterSnapshot character in snapshot.Characters)
                {
                    if (character == null || character.IsFake || character.FactionType != FactionType.anomaly)
                    {
                        continue;
                    }

                    if (!character.IsChained)
                    {
                        free.Add(character.OwnerClientId);
                    }
                }
            }

            if (!carrierChained)
            {
                return new Decision(false, "no-carrier", free);
            }

            if (alreadyPlayed)
            {
                return new Decision(false, "already-played", free);
            }

            if (free.Count > 0)
            {
                return new Decision(false, "anomalies-free", free);
            }

            return new Decision(true, "all-anomalies-chained", free);
        }
    }
}
