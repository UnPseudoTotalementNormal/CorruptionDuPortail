using System.Collections.Generic;
using Characters;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// [LEAVE] Phase 2 (epic-player-leave-stability) — fast EditMode proof of the DECISION core that
    /// <c>VictoryConditionCheckState.TryResolveVictoryNow</c> keys on when the leave pipeline chains a mid-game
    /// leaver. The adapter (TryResolveVictoryNow) builds a <see cref="GameSnapshot"/> + <see cref="ConditionsForOwner"/>
    /// list exactly like this and ends the game iff <see cref="VictoryEvaluator.Evaluate"/> returns any team.
    /// This pins AC1 (last un-chained anomaly leaves → chosen win) and AC5 (a leave that completes no win-condition
    /// → no winner → continue) at the pure level, with no NGO host.
    ///
    /// The live adapter's actual state transition into GameEndingState (its RPC fan-out / gameInfoRevealer /
    /// boardManager scene wiring) is a full-scene integration deferred to Phase 5; here we prove the boundary the
    /// adapter's <c>if (winners.Count == 0) return false; else jump+return true</c> branch reads.
    /// </summary>
    [Category("VictoryEvaluator")]
    public class LeaveVictoryResolverDecisionTests
    {
        private const ulong ChosenOwnerId = 3;
        private const ulong AnomalyOwnerId = 2;

        // Mirrors TryResolveVictoryNow's owner mapping: one non-fake owner → its role.winningConditions.
        private static List<ConditionsForOwner> OwnersFor(params (ulong id, IWinningCondition[] conditions)[] entries)
        {
            var owners = new List<ConditionsForOwner>();
            foreach (var entry in entries)
            {
                owners.Add(new ConditionsForOwner(entry.id, entry.conditions));
            }
            return owners;
        }

        private static CharacterSnapshot Char(ulong id, FactionType faction, bool isChained) =>
            new CharacterSnapshot(id, isFake: false, isCorrupted: false, isChained: isChained,
                factionType: faction, hackedByOmniscienceTarget: 0);

        [Test]
        public void LastAnomalyChained_WChosenChainedAllAnomaly_YieldsChosenWinner_ResolverWouldEndGame()
        {
            // AC1: the anomaly is now chained (the leaver just got chained), so ALL anomalies are chained.
            var snapshot = new GameSnapshot(new[]
            {
                Char(ChosenOwnerId, FactionType.chosen, isChained: false),
                Char(AnomalyOwnerId, FactionType.anomaly, isChained: true),
            }, day: 0, currentStateIndex: 0);

            var owners = OwnersFor(
                (ChosenOwnerId, new IWinningCondition[] { new WChosenChainedAllAnomaly { ownerClientId = ChosenOwnerId } }),
                (AnomalyOwnerId, new IWinningCondition[0]));

            var winners = new VictoryEvaluator().Evaluate(snapshot, owners);

            // winners.Count > 0 ⇒ TryResolveVictoryNow jumps to GameEndingState and returns true.
            Assert.That(winners.Count, Is.GreaterThan(0), "A fully-chained anomaly set must produce a chosen win.");
            Assert.That(winners.ContainsKey(WinningTeam.chosen), Is.True);
            Assert.That(winners[WinningTeam.chosen], Is.EquivalentTo(new[] { ChosenOwnerId }));
        }

        [Test]
        public void AnomalyStillUnchained_WChosenChainedAllAnomaly_NoWinner_ResolverWouldContinue()
        {
            // AC5: an anomaly remains un-chained, so no win-condition completes.
            var snapshot = new GameSnapshot(new[]
            {
                Char(ChosenOwnerId, FactionType.chosen, isChained: false),
                Char(AnomalyOwnerId, FactionType.anomaly, isChained: false),
            }, day: 0, currentStateIndex: 0);

            var owners = OwnersFor(
                (ChosenOwnerId, new IWinningCondition[] { new WChosenChainedAllAnomaly { ownerClientId = ChosenOwnerId } }),
                (AnomalyOwnerId, new IWinningCondition[0]));

            var winners = new VictoryEvaluator().Evaluate(snapshot, owners);

            // winners.Count == 0 ⇒ TryResolveVictoryNow returns false and performs NO transition.
            Assert.That(winners, Is.Empty, "An un-chained anomaly must leave the chosen win-condition unsatisfied.");
        }
    }
}
