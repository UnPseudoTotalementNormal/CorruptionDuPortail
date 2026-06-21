using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain;
using Characters.WinningConditions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 2.8 — fast EditMode tests for the extracted win-team aggregation POCO (VictoryEvaluator).
    /// Uses stub IWinningConditions (no NGO host), proving the loop semantics independently of live state.
    /// </summary>
    [Category("VictoryEvaluator")]
    public class VictoryEvaluatorTests
    {
        private sealed class StubCondition : IWinningCondition
        {
            private readonly WinningTeam _team;
            private readonly bool _verdict;

            public StubCondition(WinningTeam team, bool verdict)
            {
                _team = team;
                _verdict = verdict;
            }

            public WinningTeam GetWinningTeam() => _team;
            public bool CheckCondition(GameSnapshot snapshot) => _verdict;
        }

        // The evaluator passes the snapshot through to conditions; the stubs ignore it, so an empty one suffices.
        private static readonly GameSnapshot AnySnapshot = new GameSnapshot(new List<CharacterSnapshot>(), 0, 0);

        private static ConditionsForOwner Owner(ulong id, params IWinningCondition[] conditions) =>
            new ConditionsForOwner(id, conditions);

        [Test]
        public void Evaluate_NoOwners_ReturnsEmpty()
        {
            var result = new VictoryEvaluator().Evaluate(AnySnapshot, new List<ConditionsForOwner>());
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Evaluate_SingleTrueCondition_MapsTeamToOwner()
        {
            var owners = new[] { Owner(7, new StubCondition(WinningTeam.marginal, true)) };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result.ContainsKey(WinningTeam.marginal));
            Assert.That(result[WinningTeam.marginal], Is.EquivalentTo(new ulong[] { 7 }));
        }

        [Test]
        public void Evaluate_FalseCondition_Excluded()
        {
            var owners = new[] { Owner(7, new StubCondition(WinningTeam.marginal, false)) };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Evaluate_TwoOwnersWinningSameTeam_BothInSet()
        {
            var owners = new[]
            {
                Owner(1, new StubCondition(WinningTeam.anomaly, true)),
                Owner(2, new StubCondition(WinningTeam.anomaly, true)),
            };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result[WinningTeam.anomaly], Is.EquivalentTo(new ulong[] { 1, 2 }));
        }

        [Test]
        public void Evaluate_KeysBy_GetWinningTeam_NotOwner()
        {
            // Owner 1 carries a condition that wins for 'chosen' — the SET is keyed by the condition's team.
            var owners = new[] { Owner(1, new StubCondition(WinningTeam.chosen, true)) };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result.Keys, Is.EquivalentTo(new[] { WinningTeam.chosen }));
            Assert.That(result[WinningTeam.chosen], Is.EquivalentTo(new ulong[] { 1 }));
        }

        [Test]
        public void Evaluate_MixedMultiOwnerMultiCondition_AggregatesCorrectly()
        {
            var owners = new[]
            {
                Owner(1, new StubCondition(WinningTeam.marginal, true), new StubCondition(WinningTeam.chosen, false)),
                Owner(2, new StubCondition(WinningTeam.anomaly, true)),
                Owner(3, new StubCondition(WinningTeam.marginal, true)),
            };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result[WinningTeam.marginal], Is.EquivalentTo(new ulong[] { 1, 3 }));
            Assert.That(result[WinningTeam.anomaly], Is.EquivalentTo(new ulong[] { 2 }));
            Assert.That(result.ContainsKey(WinningTeam.chosen), Is.False, "A false condition must not create a team key.");
        }

        [Test]
        public void Evaluate_SameOwnerMultipleTrueTeams_AppearsInEach()
        {
            var owners = new[]
            {
                Owner(5, new StubCondition(WinningTeam.marginal, true), new StubCondition(WinningTeam.anomaly, true)),
            };

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result[WinningTeam.marginal], Is.EquivalentTo(new ulong[] { 5 }));
            Assert.That(result[WinningTeam.anomaly], Is.EquivalentTo(new ulong[] { 5 }));
        }

        // ───────────────── Null / empty-conditions edges (added coverage) ─────────────────

        [Test]
        public void Evaluate_NullOwners_ThrowsNullReferenceException()
        {
            // No null-guard at the loop head (VictoryEvaluator.cs line 34 iterates a null sequence) — pins the
            // CURRENT throw, not a desired graceful-empty behavior (reported as a suspect in the audit, not fixed).
            Assert.Throws<System.NullReferenceException>(() => new VictoryEvaluator().Evaluate(AnySnapshot, null));
        }

        [Test]
        public void Evaluate_OwnerWithZeroConditions_ContributesNothing()
        {
            var owners = new[] { Owner(7) }; // an owner carrying no winning conditions

            var result = new VictoryEvaluator().Evaluate(AnySnapshot, owners);

            Assert.That(result, Is.Empty, "An owner with no conditions must not create any team key.");
        }
    }
}
