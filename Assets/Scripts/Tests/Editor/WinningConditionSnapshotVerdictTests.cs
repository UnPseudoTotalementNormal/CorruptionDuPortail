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
    }
}
