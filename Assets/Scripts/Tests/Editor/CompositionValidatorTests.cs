using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Fast EditMode tests for the pure <see cref="CompositionValidator"/> (the lobby start-gate rules) and the
    /// gate↔distributor contract: every composition the gate ACCEPTS must yield a distribution with at least the
    /// required reals per faction and no exception. Covers the two Poyo bugs — forced monopolising every real
    /// (rejected) and a random draw dropping a faction (accepted, then guaranteed by the distributor).
    /// </summary>
    [Category("CompositionValidator")]
    public class CompositionValidatorTests
    {
        private static readonly RoleDistributor Distributor = new();

        // anomaly ≥ 1 AND chosen ("élu") ≥ 1 — the production rule set.
        private static readonly IReadOnlyList<FactionMinimum> Mins = new[]
        {
            new FactionMinimum(FactionType.anomaly, 1),
            new FactionMinimum(FactionType.chosen, 1),
        };

        private static CompositionSnapshot Snap(int players, params (FactionType faction, int max, int forced)[] roles)
        {
            var list = new List<RoleComposition>();
            foreach (var r in roles)
            {
                list.Add(new RoleComposition(r.faction, r.max, r.forced));
            }
            return new CompositionSnapshot(players, list);
        }

        // ───────────────── the two Poyo bugs ─────────────────

        [Test]
        public void ForcedMonopolisesReals_GateRejects()
        {
            // 5 players: anomaly max5/forced5 eats every real; chosen max5/forced0 → the chosen top-up pushes the
            // guaranteed reals to 6 > 5. The gate must refuse (this is the "0 élu" bug).
            var v = CompositionValidator.Validate(Snap(5, (FactionType.anomaly, 5, 5), (FactionType.chosen, 5, 0)), Mins);

            Assert.IsFalse(v.IsValid);
            CompositionFailure fit = v.Failures.First(f => f.RuleId == "guaranteed-fit");
            Assert.AreEqual(FactionType.anomaly, fit.OffendingFaction, "the over-forced faction must be named (AC1)");
            StringAssert.Contains("Anomalie", fit.Reason);
        }

        [Test]
        public void RngCouldDropAFaction_GateAccepts()
        {
            // 5 players, anomaly max5/forced0 + chosen max5/forced0: the raw RNG draw COULD produce 0 of a faction,
            // but the gate accepts because the distributor reserves 1 of each (proven by the contract test below).
            var v = CompositionValidator.Validate(Snap(5, (FactionType.anomaly, 5, 0), (FactionType.chosen, 5, 0)), Mins);

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }

        // ───────────────── individual rules ─────────────────

        [Test]
        public void RequiredFactionAbsent_GateRejects_WithFactionReason()
        {
            var v = CompositionValidator.Validate(Snap(5, (FactionType.anomaly, 5, 0)), Mins); // no chosen role at all

            Assert.IsFalse(v.IsValid);
            CompositionFailure fail = v.Failures.First(f => f.RuleId == "faction-capacity");
            Assert.AreEqual(FactionType.chosen, fail.OffendingFaction);
            StringAssert.Contains("Élu", fail.Reason);
        }

        [Test]
        public void CoverageShortfall_GateRejects()
        {
            var v = CompositionValidator.Validate(Snap(5, (FactionType.anomaly, 2, 0), (FactionType.chosen, 2, 0)), Mins);

            Assert.IsFalse(v.IsValid);
            Assert.IsTrue(v.Failures.Any(f => f.RuleId == "coverage"));
        }

        [Test]
        public void MinimumAlreadyMetByForced_AddsNoExtraCost()
        {
            // Each faction's forced already satisfies its minimum → guaranteed == Σforced == 2 ≤ 2 players.
            var v = CompositionValidator.Validate(Snap(2, (FactionType.anomaly, 3, 1), (FactionType.chosen, 3, 1)), Mins);

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }

        [Test]
        public void EmptyMinimums_OnlyScalarRulesApply()
        {
            // No faction rules → a pool with zero chosen roles is fine (only coverage/guaranteed-fit run).
            var v = CompositionValidator.Validate(Snap(3, (FactionType.anomaly, 5, 0)), System.Array.Empty<FactionMinimum>());

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }

        [Test]
        public void ClampedForced_MaxBelowForced_CountsAsMaxNotForced()
        {
            // anomaly max1/forced3 (bad SO): guaranteed for it clamps to 1. With chosen max2/forced1, players2:
            // guaranteed = 1(anomaly clamp) + 1(chosen forced) = 2 ≤ 2 → valid; both factions covered by forced.
            var v = CompositionValidator.Validate(Snap(2, (FactionType.anomaly, 1, 3), (FactionType.chosen, 2, 1)), Mins);

            Assert.IsTrue(v.IsValid, v.FirstReason);
        }

        // ───────────────── gate ↔ distributor contract (the highest-value test) ─────────────────

        [Test]
        public void EveryGateAcceptedConfig_DistributorGuaranteesEachFaction_NoThrow()
        {
            (int[] counts, int[] forced, FactionType[] factions, int players, string name)[] configs =
            {
                (new[] { 5, 5 }, new[] { 0, 0 }, new[] { FactionType.anomaly, FactionType.chosen }, 5, "5v5 no-forced"),
                (new[] { 3, 3, 2 }, new[] { 0, 0, 0 }, new[] { FactionType.anomaly, FactionType.chosen, FactionType.marginal }, 4, "3 factions"),
                (new[] { 4, 1 }, new[] { 0, 0 }, new[] { FactionType.anomaly, FactionType.chosen }, 3, "chosen scarce (max1)"),
                (new[] { 2, 2, 2 }, new[] { 1, 0, 0 }, new[] { FactionType.anomaly, FactionType.anomaly, FactionType.chosen }, 4, "two anomaly roles + partial forced"),
                (new[] { 6, 3 }, new[] { 0, 0 }, new[] { FactionType.anomaly, FactionType.chosen }, 5, "surplus pool"),
            };

            int[] seeds = { 1, 7, 42, 99, 2026 };

            foreach (var c in configs)
            {
                int total = c.counts.Sum();
                var snap = Snap(c.players, c.counts.Select((m, i) => (c.factions[i], m, c.forced[i])).ToArray());
                CompositionValidation v = CompositionValidator.Validate(snap, Mins);
                Assert.IsTrue(v.IsValid, $"config '{c.name}' should be gate-accepted but was rejected: {v.FirstReason}");

                int fakeCount = total - c.players; // live path: surplus roles become decoys
                int realCount = c.players;

                foreach (int seed in seeds)
                {
                    RoleDistribution dist = default;
                    Assert.DoesNotThrow(
                        () => dist = Distributor.Distribute(c.counts, c.forced, c.factions, Mins, fakeCount, realCount, new SeededRandomProvider(seed)),
                        $"config '{c.name}' seed {seed} threw");

                    Assert.AreEqual(realCount, dist.RealRoleIndices.Count, $"config '{c.name}' seed {seed}: wrong real count");

                    foreach (FactionMinimum fm in Mins)
                    {
                        int reals = dist.RealRoleIndices.Count(idx => c.factions[idx] == fm.Faction);
                        Assert.GreaterOrEqual(reals, fm.Min,
                            $"config '{c.name}' seed {seed}: faction {fm.Faction} got {reals} reals, expected ≥ {fm.Min}");
                    }
                }
            }
        }
    }
}
