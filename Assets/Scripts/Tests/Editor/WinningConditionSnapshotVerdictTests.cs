using Characters;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 2.12 — fast EditMode verdict tests for the snapshot signature
    /// (<see cref="WinningCondition.CheckCondition(GameSnapshot)"/>), host-free.
    ///
    /// Converted from the former host-coupled PlayMode <c>VictoryConditionTests</c>: that
    /// class drove the legacy no-arg <c>CheckCondition()</c> over live spawned Characters,
    /// a path now fully pinned by <c>WinningConditionGoldenMasterTests</c> (live oracle,
    /// strictly more cases). These re-express its 4 cases against an immutable hand-built
    /// <see cref="GameSnapshot"/> — covering the snapshot verdict path directly in EditMode
    /// (previously only covered transitively by the PlayMode Migration differentials).
    ///
    /// The live no-arg path, the snapshot↔live equality, and the builder fidelity stay
    /// pinned by the untouched PlayMode goldens / differentials / losslessness battery.
    /// </summary>
    [Category("WinningConditionSnapshotVerdict")]
    public class WinningConditionSnapshotVerdictTests
    {
        // A non-fake character snapshot with the fields each condition reads; unread fields default.
        private static CharacterSnapshot Char(
            ulong ownerClientId,
            bool isCorrupted = false,
            bool isChained = false,
            FactionType factionType = FactionType.marginal,
            bool isFake = false) =>
            new CharacterSnapshot(ownerClientId, isFake, isCorrupted, isChained, factionType, hackedByOmniscienceTarget: 0);

        private static GameSnapshot Snapshot(params CharacterSnapshot[] characters) =>
            new GameSnapshot(characters, day: 0, currentStateIndex: 0);

        // ───────────────────────────── WAnomalyCorruption ─────────────────────────────

        [Test]
        public void WAnomalyCorruption_ReturnsTrue_WhenAllCharactersAreCorrupted()
        {
            var snapshot = Snapshot(Char(1, isCorrupted: true), Char(2, isCorrupted: true));

            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(snapshot),
                "WAnomalyCorruption should return true when all non-fake characters are corrupted.");
        }

        [Test]
        public void WAnomalyCorruption_ReturnsFalse_WhenOneCharacterIsNotCorrupted()
        {
            var snapshot = Snapshot(Char(1, isCorrupted: true), Char(2, isCorrupted: false));

            Assert.IsFalse(new WAnomalyCorruption().CheckCondition(snapshot),
                "WAnomalyCorruption should return false if at least one non-fake character is NOT corrupted.");
        }

        // ────────────────────────── WChosenChainedAllAnomaly ──────────────────────────

        [Test]
        public void WChosenChainedAllAnomaly_ReturnsTrue_WhenAllAnomaliesAreChained()
        {
            // The chosen member is continue'd (non-anomaly); the only anomaly is chained → true.
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),
                Char(2, isChained: false, factionType: FactionType.chosen));

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(snapshot),
                "WChosenChainedAllAnomaly should return true when all anomalies are chained (chosen continue'd).");
        }

        [Test]
        public void WChosenChainedAllAnomaly_ReturnsFalse_WhenOneAnomalyIsNotChained()
        {
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),
                Char(2, isChained: false, factionType: FactionType.anomaly));

            Assert.IsFalse(new WChosenChainedAllAnomaly().CheckCondition(snapshot),
                "WChosenChainedAllAnomaly should return false if one anomaly is NOT chained.");
        }

        // ─────────────────────── Field indifference (catalog E) ───────────────────────
        // Folded in from the former PlayMode Tests.PlayMode.SnapshotOracle.WinningConditionFieldIndifferenceTests:
        // it was a pure [Test] class with no NGO sitting in the PlayMode assembly, duplicating this file's
        // GameSnapshot/CheckCondition construct.
        //
        // These pin which fields each condition deliberately IGNORES. Note the shape: asserting one true verdict
        // does NOT prove indifference (and survives a `CheckCondition => true` mutant). Indifference means the
        // verdict is UNCHANGED when the ignored field flips, so each test evaluates BOTH values of that field and
        // compares — and the false-verdict tests above keep the always-true mutant dead.

        // WAnomalyCorruption reads only IsFake (filter) + IsCorrupted — FactionType and IsChained must not matter.
        [Test]
        public void WAnomalyCorruption_IsIndifferentTo_FactionAndChained()
        {
            var condition = new WAnomalyCorruption();

            foreach (var faction in new[] { FactionType.chosen, FactionType.anomaly, FactionType.marginal })
            {
                bool chainedVerdict = condition.CheckCondition(Snapshot(
                    Char(1, isCorrupted: true, isChained: true, factionType: faction)));
                bool unchainedVerdict = condition.CheckCondition(Snapshot(
                    Char(1, isCorrupted: true, isChained: false, factionType: faction)));

                Assert.AreEqual(chainedVerdict, unchainedVerdict,
                    $"WAnomalyCorruption must ignore IsChained (faction {faction}).");
                Assert.IsTrue(chainedVerdict,
                    $"All non-fake corrupted → anomaly win, whatever the faction ({faction}) or chaining.");
            }
        }

        // A fake un-corrupted character is filtered out, so it must not flip the verdict.
        [Test]
        public void WAnomalyCorruption_IsIndifferentTo_FakeCharacters()
        {
            var condition = new WAnomalyCorruption();
            var withoutFake = Snapshot(Char(2, isCorrupted: true, factionType: FactionType.anomaly));
            var withFake = Snapshot(
                Char(1, isCorrupted: false, factionType: FactionType.anomaly, isFake: true),
                Char(2, isCorrupted: true, factionType: FactionType.anomaly));

            Assert.AreEqual(condition.CheckCondition(withoutFake), condition.CheckCondition(withFake),
                "A fake un-corrupted character must be filtered out, leaving the verdict unchanged.");
            Assert.IsTrue(condition.CheckCondition(withFake),
                "The real corrupted population still wins despite the fake.");
        }

        // WChosenChainedAllAnomaly reads only IsFake + FactionType + IsChained — IsCorrupted, and the chained
        // state of non-anomalies, must not matter.
        [Test]
        public void WChosenChainedAllAnomaly_IsIndifferentTo_CorruptedAndNonAnomalyChained()
        {
            var condition = new WChosenChainedAllAnomaly();

            foreach (bool corrupted in new[] { false, true })
            foreach (bool chosenChained in new[] { false, true })
            {
                bool verdict = condition.CheckCondition(Snapshot(
                    Char(1, isCorrupted: corrupted, isChained: true, factionType: FactionType.anomaly),
                    Char(2, isCorrupted: corrupted, isChained: chosenChained, factionType: FactionType.chosen),
                    Char(3, isCorrupted: corrupted, isChained: chosenChained, factionType: FactionType.marginal)));

                Assert.IsTrue(verdict,
                    $"Every anomaly chained → chosen win (corrupted={corrupted}, non-anomaly chained={chosenChained}).");
            }
        }

        [Test]
        public void WChosenChainedAllAnomaly_IsIndifferentTo_FakeAnomalies()
        {
            var condition = new WChosenChainedAllAnomaly();
            var withoutFake = Snapshot(Char(2, isChained: true, factionType: FactionType.anomaly));
            var withFake = Snapshot(
                Char(1, isChained: false, factionType: FactionType.anomaly, isFake: true),
                Char(2, isChained: true, factionType: FactionType.anomaly));

            Assert.AreEqual(condition.CheckCondition(withoutFake), condition.CheckCondition(withFake),
                "A fake un-chained anomaly must be filtered out, leaving the verdict unchanged.");
            Assert.IsTrue(condition.CheckCondition(withFake),
                "The real chained anomalies still win despite the fake.");
        }

        // WMarginalIsChainedWin keys on OwnerClientId + IsFake + IsChained — the owner's faction and corruption
        // must not matter.
        [Test]
        public void WMarginalIsChainedWin_IsIndifferentTo_OwnerFactionAndCorrupted()
        {
            foreach (var faction in new[] { FactionType.chosen, FactionType.anomaly, FactionType.marginal })
            foreach (bool corrupted in new[] { false, true })
            {
                var condition = new WMarginalIsChainedWin { ownerClientId = 1 };

                Assert.IsTrue(
                    condition.CheckCondition(Snapshot(Char(1, isCorrupted: corrupted, isChained: true, factionType: faction))),
                    $"A chained owner wins whatever the faction ({faction}) or corruption ({corrupted}).");
                // Negative control on the field it DOES read — without this the loop above would pass on an
                // always-true mutant.
                Assert.IsFalse(
                    condition.CheckCondition(Snapshot(Char(1, isCorrupted: corrupted, isChained: false, factionType: faction))),
                    $"An UNCHAINED owner must not win (faction {faction}, corrupted {corrupted}).");
            }
        }

        // ───────────────────────────── PortalTakedownRule (Mage Occulte) ─────────────────────────────
        // GD bug 2026-11-24: chaining the Mage first took the portal down at once, then again when Abyss got chained.
        // The portal comes down once, when EVERY anomaly is chained, whatever the chaining order.

        [Test]
        public void Portal_DoesNotPlay_WhenTheMageIsChainedFirst_AndAbyssIsStillFree()
        {
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),   // Mage Occulte, chained first
                Char(2, isChained: false, factionType: FactionType.anomaly),  // Abyss, still free
                Char(3, factionType: FactionType.chosen));

            PortalTakedownRule.Decision decision = PortalTakedownRule.Decide(snapshot, carrierChained: true, alreadyPlayed: false);

            Assert.IsFalse(decision.Activate, "The Mage alone chained must not take the portal down.");
            Assert.AreEqual("anomalies-free", decision.Reason);
            CollectionAssert.AreEqual(new ulong[] { 2 }, decision.FreeAnomalies);
        }

        [Test]
        public void Portal_Plays_OnceEveryAnomalyIsChained_WhateverTheOrder()
        {
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),
                Char(2, isChained: true, factionType: FactionType.anomaly),
                Char(3, factionType: FactionType.chosen));

            PortalTakedownRule.Decision decision = PortalTakedownRule.Decide(snapshot, carrierChained: true, alreadyPlayed: false);

            Assert.IsTrue(decision.Activate);
            Assert.AreEqual("all-anomalies-chained", decision.Reason);
            CollectionAssert.IsEmpty(decision.FreeAnomalies);
        }

        [Test]
        public void Portal_DoesNotPlayTwice()
        {
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),
                Char(2, isChained: true, factionType: FactionType.anomaly));

            PortalTakedownRule.Decision decision = PortalTakedownRule.Decide(snapshot, carrierChained: true, alreadyPlayed: true);

            Assert.IsFalse(decision.Activate, "The portal is taken down once per game.");
            Assert.AreEqual("already-played", decision.Reason);
        }

        [Test]
        public void Portal_DoesNotPlay_WithoutAChainedCarrier()
        {
            // Every anomaly chained but no portal carrier (no Mage in the game): nothing to take down.
            var snapshot = Snapshot(Char(2, isChained: true, factionType: FactionType.anomaly));

            Assert.IsFalse(PortalTakedownRule.Decide(snapshot, carrierChained: false, alreadyPlayed: false).Activate);
        }

        [Test]
        public void Portal_IgnoresFakeAnomalies_AndNonAnomalies()
        {
            // Same filter as WChosenChainedAllAnomaly: a free fake anomaly or a free chosen does not hold the portal back.
            var snapshot = Snapshot(
                Char(1, isChained: true, factionType: FactionType.anomaly),
                Char(2, isChained: false, factionType: FactionType.anomaly, isFake: true),
                Char(3, isChained: false, factionType: FactionType.chosen),
                Char(4, isChained: false, factionType: FactionType.marginal));

            Assert.IsTrue(PortalTakedownRule.Decide(snapshot, carrierChained: true, alreadyPlayed: false).Activate);
            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(snapshot),
                "The portal opens exactly when the chosen win it precedes would.");
        }
    }
}
