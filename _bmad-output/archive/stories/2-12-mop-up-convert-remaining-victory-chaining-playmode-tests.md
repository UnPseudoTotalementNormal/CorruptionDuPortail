# Story 2.12: Mop-up — convert remaining victory/chaining PlayMode tests still coupled to the adapter

Status: review

## Story

As a developer (Poyo),
I want any victory/chaining tests that could not migrate earlier (still coupled to the host adapter) converted where now possible,
so that EditMode coverage is maximized and only genuinely network-observable tests remain in PlayMode.

## Acceptance Criteria

**Given** extractions 2.8–2.11b are complete and most tests already migrated incrementally
**When** the mop-up runs
**Then** the leftover host-coupled victory/chaining verdict tests are converted where now possible, with no big-bang rewrite
**And** tests that are genuinely network-observable (live-NetworkVariable oracles, transition ordering, simulated-client gateway) remain in PlayMode by design
**And** the full EditMode + PlayMode suite is green (NFR6 gate for entering Wave 2 / Epic 3)

## Acceptance reading notes (binding)

1. **Strict "blocked by the 2-phase `GameLoopMachine`" set is empty.** No victory/chaining test drives a state transition — none is *blocked by* the GameLoopMachine specifically. The practical mop-up target (loop intent: "convertir les tests victory/chaining PlayMode encore couplés à l'adapter") is the one remaining **host-coupled victory verdict** test that can now run host-free against the snapshot signature.
2. **Target: `VictoryConditionTests` (PlayMode).** It exercises the legacy no-arg `CheckCondition()` (live `GameManager.instance` pull) over spawned `Character`s — 4 cases on `WAnomalyCorruption` + `WChosenChainedAllAnomaly`. Its **live no-arg path is fully subsumed** by `WinningConditionGoldenMasterTests` (same harness, same no-arg call, strictly more cases A1–A7 / C1–C8). So its verdict-logic intent can move to EditMode against `CheckCondition(GameSnapshot)` with **zero coverage loss** (the live path stays pinned by the untouched goldens + the snapshot↔live equality stays pinned by the Migration differentials).
3. **Conversion, not deletion.** Replace it with EditMode `WinningConditionSnapshotVerdictTests` `[Category("WinningConditionSnapshotVerdict")]` driving `CheckCondition(GameSnapshot)` over hand-built `GameSnapshot`/`CharacterSnapshot` (no host) — net: −1 host-coupled PlayMode class, +1 fast EditMode class covering the snapshot verdict path directly (previously only covered transitively via the PlayMode Migration differential).
4. **Untouched (genuinely network-observable, the safety net):** `WinningConditionGoldenMasterTests`, `WinningConditionHarnessFidelityTests`, the `Migration/*` differentials, `SnapshotDifferentialTests`, `SnapshotOracle/*`, `GameSnapshotBuilderLosslessnessTests`, `VoteTallyGoldenMasterTests`, `ChainingResolverGoldenMasterTests`, `GameLoopTransitionOrderingTests` — these are live-NetworkVariable oracles / transition-ordering / builder-fidelity and stay PlayMode **by design**.
5. **No production change (NFR1).** Test-only mop-up.

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (full EditMode + PlayMode; console clean)
- [ ] **T1 — Add EditMode `WinningConditionSnapshotVerdictTests`** (note 3): the 4 `VictoryConditionTests` cases re-expressed as `CheckCondition(GameSnapshot)` assertions over hand-built snapshots
- [ ] **T2 — Remove `VictoryConditionTests`** (PlayMode) — its live path is subsumed by the goldens (note 2)
- [ ] **T3 — Prove**: full EditMode + full PlayMode green (NFR6 gate); console clean

## Dev Notes

**Created:** `Assets/Scripts/Tests/Editor/WinningConditionSnapshotVerdictTests.cs`.
**Removed:** `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`.
**Must NOT change:** any golden/differential/oracle/losslessness/transition-ordering test (the safety net), any production file.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.12] (lines 381–393)
- [Source: Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs] — the host-coupled class being converted
- [Source: Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs] — the live no-arg oracle that subsumes its coverage
- [Source: Assets/Scripts/Domain/CharacterSnapshot.cs, Assets/Scripts/Domain/GameSnapshot.cs] — the value objects to hand-build

### Previous story intelligence

- Post-2.7 both `CheckCondition()` (live pull) and `CheckCondition(GameSnapshot)` exist; production evaluates via the snapshot signature, goldens pin the live no-arg path, Migration differentials pin snapshot↔live equality.
- This is the LAST Epic 2 story → STOP for the Epic 2→3 checkpoint after merge.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- Strict "blocked by the 2-phase GameLoopMachine" set was empty (no victory/chaining test transitions states). Practical mop-up = the one host-coupled victory verdict test now runnable host-free.
- Converted `VictoryConditionTests` (PlayMode, 4 cases on the legacy no-arg `CheckCondition()` over live Characters) → EditMode `WinningConditionSnapshotVerdictTests` `[Category("WinningConditionSnapshotVerdict")]` driving `CheckCondition(GameSnapshot)` over hand-built snapshots. Zero coverage loss: the live no-arg path stays pinned by `WinningConditionGoldenMasterTests` (more cases), snapshot↔live equality by the Migration differentials.
- Net: −1 host-coupled PlayMode class, +1 fast EditMode class covering the snapshot verdict path directly (previously only transitive via the PlayMode differential).
- All goldens/differentials/oracles/losslessness/transition-ordering tests untouched (genuinely network-observable, stay PlayMode by design).
- NFR6 gate met: **117/117 EditMode + 129/129 PlayMode** green (PlayMode 133→129 = the 4 removed redundant host cases). Test-only (NFR1). **End of Epic 2.**

### File List

- **Added:** `Assets/Scripts/Tests/Editor/WinningConditionSnapshotVerdictTests.cs`
- **Removed:** `Assets/Scripts/Tests/PlayMode/VictoryConditionTests.cs`

### Change Log

- 2026-06-10 — Story 2.12 drafted; convert the host-coupled `VictoryConditionTests` to an EditMode snapshot-verdict test, closing Epic 2's EditMode-maximization gate.
