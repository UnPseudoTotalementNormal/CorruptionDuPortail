# Story 2.7b: Swap the differential oracle to the frozen golden + re-point the sentinel

Status: done

<!-- REVIEW-REQUIRED: swaps the golden oracle — error here corrupts the safety net itself. gds-code-review before merge. -->

## Story

As a developer (Poyo),
I want the now-orphaned dual-source differential replaced by the frozen Phase-0 goldens as the oracle,
so that the safety net keeps comparing against a real reference instead of self-confirming after the cut.

## Acceptance Criteria

**Given** the cut of 2.7 re-pointed production to the snapshot path (the differential's pull side is now orphaned)
**When** this story lands
**Then** the dual-source differential is retired (superseded as the standing proof) and the frozen golden vectors (Epic 1) become the standing oracle for the **snapshot** signature
**And** the anti-tautology mutation-sentinel is **re-pointed** at this new oracle so it does not become decorative
**And** a check proves the post-cut suite still fails on a deliberate verdict mutation (the net still bites)

### Acceptance reading notes (binding — NET-CRITICAL, never reduce coverage)

1. **Net-safety principle: ADD the new oracle before retiring the old proof; never leave the suite with less coverage at any commit.** The sprint note warns "error here corrupts the safety net itself". So: (a) establish the snapshot-signature golden oracle, (b) re-point the sentinel onto it and prove it bites, (c) only then mark the dual-source differential as superseded. Do NOT delete the pull or the Epic-1 goldens in this story — they remain as an independent cross-check. "Retire" = demote from *the* standing proof to redundant, not "delete the net".

2. **Snapshot-signature golden oracle (the new standing reference).** Create `WinningConditionSnapshotOracleTests` — a boundary corpus that builds the live state, takes `GameSnapshotBuilder.FromLiveState`, and asserts `condition.CheckCondition(snapshot) == <frozen expected verdict>` directly (NOT `== pull`). The expected verdicts are the SAME frozen vectors Story 1.2/1.5 pinned (the verdict does not change; only the call target and the comparison reference do). Cover each condition's branches: WMarginal (chained/not/absent), WAnomalyCorruption (all-corrupted/one-not/empty-vacuous), WChosenChainedAllAnomaly (chained-anomaly/free-anomaly/no-anomaly-vacuous), WOmniscience (base-true + the false branches + O1 owner-absent throw). Tag `[Category("SnapshotOracle")]`.

3. **Re-point the anti-tautology sentinel onto the goldens-oracle.** The 1.4/2.2 sentinel previously keyed on the dual-source differential. Now: a test that deliberately corrupts one snapshot field and asserts that **a snapshot-oracle golden goes red** (i.e., `CheckCondition(snapshot)` deviates from the frozen expected verdict). This proves the oracle is not self-confirming. Tag `[Category("SnapshotOracle")]`.

4. **Prove the net still bites (post-cut).** A meta-test proving a deliberate VERDICT mutation reddens the suite: e.g. a test-only condition that inverts its snapshot verdict, asserted against a frozen expected value, must FAIL (use `Assert.Throws<AssertionException>` around the golden-style assertion, mirroring the Story 1.0 harness-fidelity kill-test pattern). This proves the goldens-on-snapshot oracle genuinely catches a wrong verdict.

5. **Retire the dual-source differential (demote, document).** Mark `SnapshotDifferential.AssertAgrees` (and the `Differential_*` Migration tests that compare pull vs snapshot) as SUPERSEDED by the snapshot-oracle in a header comment: they remain green (the pull is still present) as a redundant cross-check and will be removed when the legacy pull is finally deleted. Do NOT delete them in this net-critical story — keeping them is strictly safer.

6. Shippable per commit; full regression green; goldens (Epic 1) untouched. REVIEW-REQUIRED → `gds-code-review` before merge.

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (full PlayMode + EditMode)
- [ ] **T1 — Snapshot-signature golden oracle** `WinningConditionSnapshotOracleTests` `[Category("SnapshotOracle")]` (note 2): per-condition boundary corpus asserting `CheckCondition(snapshot) == frozen verdict`
- [ ] **T2 — Re-pointed sentinel** (note 3): corrupt a snapshot field → a snapshot-oracle golden deviates from its frozen verdict
- [ ] **T3 — Net-bites kill-test** (note 4): an inverting test-only condition makes a golden-style assertion throw `AssertionException`
- [ ] **T4 — Demote the dual-source differential** (note 5): header comment on `SnapshotDifferential` marking it superseded; no deletion
- [ ] **T5 — Prove**: `run_tests category: SnapshotOracle` green; full regression green; console clean
- [ ] **T6 — `gds-code-review`**; address findings; PR + squash-merge

## Dev Notes

**Created:** `Assets/Scripts/Tests/PlayMode/SnapshotOracle/WinningConditionSnapshotOracleTests.cs` (oracle + sentinel + net-bites).
**Modified:** `Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs` (header comment: superseded by the snapshot-oracle; retained as redundant cross-check).
**Must NOT change / delete:** the Epic-1 golden files, the pull `CheckCondition()`, the Migration differential tests (kept as cross-check).

### Why this is safe

- The snapshot-oracle pins `CheckCondition(snapshot)` against the SAME frozen verdicts as Epic-1 — independent of the pull. So the net no longer self-confirms via the pull.
- Coverage only INCREASES in this story (new oracle + sentinel + kill-test added; nothing removed). The dual-source differential stays green as a bonus cross-check.
- The kill-test proves the new oracle bites on a wrong verdict (anti-tautology).

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.7b] (lines 296–308)
- [Source: Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs] — the frozen Epic-1 vectors (the expected verdicts to reuse)
- [Source: Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs] — the dual-source differential being superseded
- [Source: Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs] — `FromLiveState`
- [Source: Story 1.0 harness-fidelity kill-test] — the `Assert.Throws<AssertionException>` net-bites pattern to mirror

### Previous story intelligence

- 2.7 re-pointed prod to the snapshot and retained the pull as the differential oracle "until 2.7b". 2.7b establishes the goldens-on-snapshot oracle so the pull is no longer the reference (full pull deletion is a later cleanup, out of scope here to keep the net intact).
- Reuse the host-harness + POmniscience spawn pattern from the Epic-1 goldens / Story 2.1.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Established `WinningConditionSnapshotOracleTests` `[Category("SnapshotOracle")]` as the standing oracle: pins `CheckCondition(snapshot)` against the frozen Epic-1 verdicts directly. Re-pointed sentinel (corrupt IsChained → false, field-by-field) + net-bites kill-test (inverting condition → `AssertionException`).
- Demoted `SnapshotDifferential` to a redundant cross-check via header comment — NOT deleted. Pull + Epic-1 goldens + Migration tests all retained. Diff is purely additive (net only increases).
- **Post-review hardening (gds-code-review):** added oracle cases for the under-pinned branches the Edge Case Hunter flagged — WMarginal owner-absent (M1) + fake-owner (M2, hand-built snapshot for the !IsFake term), WOmniscience no-omni (O2) + target-not-found (O4) + hacked-not-chained (O5). Tightened the sentinel to pin a concrete `IsFalse`. Oracle 13→18 cases.
- **Verification:** `SnapshotOracle` 18/18; full regression 83/83 PlayMode + 16/16 EditMode green.

### File List

- **Added:** `Assets/Scripts/Tests/PlayMode/SnapshotOracle/WinningConditionSnapshotOracleTests.cs`
- **Modified:** `Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs` (superseded-but-retained header comment)

### Change Log

- 2026-06-10 — Swapped the snapshot-signature oracle to the frozen Epic-1 vectors; re-pointed the sentinel; proved the net still bites; demoted (not deleted) the dual-source differential. gds-code-review PASS (+ oracle-coverage hardening). Story 2.7b → done.

### Senior Developer Review (AI)

**Outcome: Approve (after addressing one coverage finding).** Three adversarial layers via gds-code-review on `story/2-7b`.

- **Edge Case Hunter (project access) — verdicts all CORRECT, kill-test + sentinel VALID, no coverage reduction.** Material Medium finding: the new oracle pinned only ~11/26 golden rows — WMarginal owner-absent/fake and WOmniscience DEFAULT/not-found/not-chained branches were unpinned on the snapshot path (still covered by the retained pull goldens + differential, but the oracle itself was thin). **Addressed:** added 5 oracle cases (M1, M2, O2, O4, O5); oracle now 18 cases.
- **Acceptance Auditor — PASS on all 3 ACs + the net-safety constraint.** Purely additive (0 deletions); "retire" correctly interpreted as demote-via-comment; pull, Epic-1 goldens, and Migration tests all retained; verdicts match the frozen vectors.
- **Blind Hunter (no project access) — flagged the kill-test as "tautological" and the NRE as "golden-mastering a bug":** OVERRULED by the Edge Case Hunter, which verified (with code access) the kill-test runs real base logic and genuinely throws, and that the O1 NRE faithfully mirrors the deliberately-captured Story 1.2 golden (changing it would deviate from the frozen oracle). The Blind Hunter self-disclaimed "confirm against the actual diff". The `!=` sentinel concern was addressed by also pinning a concrete `IsFalse`.
