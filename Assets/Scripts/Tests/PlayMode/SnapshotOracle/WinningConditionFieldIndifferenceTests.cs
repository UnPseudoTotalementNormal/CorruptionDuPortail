using Characters;
using Characters.Powers;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.PlayMode.SnapshotOracle
{
    /// <summary>
    /// EditMode-pure boundary coverage (catalog E) for the win conditions, asserted directly on a hand-built
    /// GameSnapshot — no NGO, no setup. The oracle suite pins the true/false VERDICTS via live characters; these
    /// pin the FIELD-INDIFFERENCE contract: which fields each condition deliberately ignores (a regression that
    /// coupled victory to heal/faction/chaining would slip past the verdict tests but fail here). Pure [Test]
    /// methods in a setup-free class so no NetworkManager is spawned.
    /// </summary>
    [Category("PowerDecision")]
    public class WinningConditionFieldIndifferenceTests
    {
        private const ulong Default = POmniscience.HACKED_CHARACTER_DEFAULT;

        private static GameSnapshot Snap(params CharacterSnapshot[] characters) => new GameSnapshot(characters, 0, 0);

        // WAnomalyCorruption reads only IsFake (filter) + IsCorrupted — it ignores FactionType and IsChained.
        [Test]
        public void WAnomaly_AllCorrupted_IgnoresFactionAndChained_True()
        {
            var snap = Snap(
                new CharacterSnapshot(1, isFake: false, isCorrupted: true, isChained: false, FactionType.chosen, Default),
                new CharacterSnapshot(2, isFake: false, isCorrupted: true, isChained: true, FactionType.anomaly, Default),
                new CharacterSnapshot(3, isFake: false, isCorrupted: true, isChained: false, FactionType.marginal, Default));
            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(snap),
                "All non-fake corrupted → anomaly win, regardless of faction/chained.");
        }

        // A fake un-corrupted character must NOT break WAnomaly (fakes are filtered out).
        [Test]
        public void WAnomaly_FakeUncorrupted_IsSkipped_True()
        {
            var snap = Snap(
                new CharacterSnapshot(1, isFake: true, isCorrupted: false, isChained: false, FactionType.anomaly, Default),
                new CharacterSnapshot(2, isFake: false, isCorrupted: true, isChained: false, FactionType.anomaly, Default));
            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(snap),
                "A fake un-corrupted character must be skipped, so the real corrupted population still wins.");
        }

        // WChosenChainedAllAnomaly reads only IsFake + FactionType + IsChained — it ignores IsCorrupted, and the
        // chained state of non-anomalies.
        [Test]
        public void WChosen_AnomaliesChained_IgnoresCorruptedAndNonAnomalyChained_True()
        {
            var snap = Snap(
                new CharacterSnapshot(1, isFake: false, isCorrupted: false, isChained: true, FactionType.anomaly, Default),
                new CharacterSnapshot(2, isFake: false, isCorrupted: false, isChained: false, FactionType.chosen, Default),
                new CharacterSnapshot(3, isFake: false, isCorrupted: false, isChained: false, FactionType.marginal, Default));
            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(snap),
                "Every anomaly chained → chosen win, regardless of corruption or non-anomaly chaining.");
        }

        // A fake un-chained anomaly must NOT break WChosen (fakes are filtered out).
        [Test]
        public void WChosen_FakeUnchainedAnomaly_IsSkipped_True()
        {
            var snap = Snap(
                new CharacterSnapshot(1, isFake: true, isCorrupted: false, isChained: false, FactionType.anomaly, Default),
                new CharacterSnapshot(2, isFake: false, isCorrupted: false, isChained: true, FactionType.anomaly, Default));
            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(snap),
                "A fake un-chained anomaly must be skipped, so the real chained anomalies still win.");
        }

        // WMarginalIsChainedWin reads only OwnerClientId (key) + IsFake + IsChained — it ignores FactionType and
        // IsCorrupted of the owner.
        [Test]
        public void WMarginal_OwnerChained_IgnoresFactionAndCorrupted_True()
        {
            var snap = Snap(
                new CharacterSnapshot(1, isFake: false, isCorrupted: false, isChained: true, FactionType.chosen, Default));
            Assert.IsTrue(new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snap),
                "A chained owner wins regardless of faction or corruption.");
        }
    }
}
