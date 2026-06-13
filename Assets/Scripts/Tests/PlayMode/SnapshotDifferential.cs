using System.Collections.Generic;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using GameLogic;
using GameLogic.Snapshot;
using NUnit.Framework;

namespace Tests.PlayMode
{
    /// <summary>
    /// SUPERSEDED (Story 2.7b): the standing oracle for the snapshot signature is now
    /// <c>WinningConditionSnapshotOracleTests</c> (snapshot verdict vs the frozen Epic-1 vectors), not this
    /// pull-vs-snapshot differential. This harness + its `Differential_*`/Migration callers are RETAINED as a
    /// redundant cross-check while the legacy pull still exists; they will be removed when the pull is finally
    /// deleted. Kept (not deleted) deliberately — never reduce the net at the net-critical swap.
    ///
    /// Story 2.2 — stateless differential harness for the WinningCondition dual signature.
    ///
    /// NO in-prod canary is shipped: the differential lives only here, in the test harness. There is therefore no
    /// release flag to assert OFF — production never double-evaluates. (If a canary is ever added it must be behind
    /// a flag OFF by default with a release-asserting test — but that is not this story.)
    ///
    /// The snapshot is rebuilt locally on every call and passed by argument — never cached in a static/shared
    /// field — so migrating one condition cannot move state under another (the AC's statelessness requirement).
    /// </summary>
    public static class SnapshotDifferential
    {
        /// <summary>Asserts the legacy pull and the snapshot overload agree for <paramref name="condition"/>.</summary>
        public static void AssertAgrees(WinningCondition condition, GameManager gameManager)
        {
            GameSnapshot snapshot = GameSnapshotBuilder.FromLiveState(gameManager); // local — never cached
            Assert.AreEqual(
                condition.CheckCondition(),
                condition.CheckCondition(snapshot),
                $"Differential disagreement for {condition.GetType().Name}: pull verdict != snapshot verdict.");
        }

        /// <summary>
        /// Per-field mutation-sentinel mechanism (mirrors Story 1.4 Test B): yields one variant of
        /// <paramref name="reference"/> with exactly one field corrupted. Ready to be instantiated per condition
        /// in 2.3–2.6 — corrupting a field a migrated condition reads must flip its snapshot verdict.
        /// </summary>
        public static IEnumerable<(string field, CharacterSnapshot corrupted)> SingleFieldCorruptions(CharacterSnapshot r)
        {
            yield return ("OwnerClientId", new CharacterSnapshot(r.OwnerClientId ^ 0xABCDUL, r.IsFake, r.IsCorrupted, r.IsChained, r.FactionType, r.HackedByOmniscienceTarget));
            yield return ("IsFake", new CharacterSnapshot(r.OwnerClientId, !r.IsFake, r.IsCorrupted, r.IsChained, r.FactionType, r.HackedByOmniscienceTarget));
            yield return ("IsCorrupted", new CharacterSnapshot(r.OwnerClientId, r.IsFake, !r.IsCorrupted, r.IsChained, r.FactionType, r.HackedByOmniscienceTarget));
            yield return ("IsChained", new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, !r.IsChained, r.FactionType, r.HackedByOmniscienceTarget));
            yield return ("FactionType", new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, r.IsChained, NextFaction(r.FactionType), r.HackedByOmniscienceTarget));
            yield return ("HackedByOmniscienceTarget", new CharacterSnapshot(r.OwnerClientId, r.IsFake, r.IsCorrupted, r.IsChained, r.FactionType, r.HackedByOmniscienceTarget ^ 0xABCDUL));
        }

        private static Characters.FactionType NextFaction(Characters.FactionType f) =>
            f == Characters.FactionType.anomaly ? Characters.FactionType.chosen : Characters.FactionType.anomaly;
    }
}
