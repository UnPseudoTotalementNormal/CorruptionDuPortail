# Story 2.2: Dual-signature `CheckCondition(snapshot)` + stateless differential harness

Status: done

## Story

As a developer (Poyo),
I want both `CheckCondition()` and `CheckCondition(snapshot)` to coexist behind a test-only differential harness,
so that each condition can be migrated and proven equivalent without changing prod behavior or cost.

## Acceptance Criteria

**Given** the builder of 2.1
**When** the dual signature is introduced
**Then** `virtual bool CheckCondition(GameSnapshot s) => CheckCondition();` is added to `WinningCondition` (default delegates to the old pull)
**And** the differential harness (`Assert.AreEqual(condition.CheckCondition(), condition.CheckCondition(snapshot))`) is **stateless**: the snapshot is immutable, passed by argument, never cached in a shared/`static` field — so migrating one condition cannot move state under another
**And** the differential runs **in the test harness only**; if any in-prod canary exists it is behind a flag **OFF by default**, with a test asserting the flag is OFF in release builds (no double-evaluation shipped to players)
**And** the harness exposes a **per-field-parameterized mutation-sentinel** mechanism (from Story 1.4) ready to be instantiated per condition in 2.3–2.6

### Acceptance reading notes (binding)

1. **The only production change is ONE method** on `WinningCondition` (`WinningCondition.cs`): `public virtual bool CheckCondition(GameSnapshot snapshot) => CheckCondition();`. It needs `using CorruptionDuPortail.Domain;` (Game already references Domain). The default **delegates to the old pull**, so prod behavior and cost are byte-identical and every existing golden stays green. No evaluator overrides it yet (that is 2.3–2.6).

2. **No in-prod canary is shipped.** We add zero double-evaluation to production — the differential lives only in the test harness. The AC's "flag OFF in release" is satisfied vacuously (nothing to flag). Record this decision in the harness header so a future reader does not look for a missing flag. (If a canary is ever added, it must be behind a flag OFF by default with a release-asserting test — but not in this story.)

3. **Stateless differential harness (the reusable utility).** Create a test-only static helper `SnapshotDifferential` in `Tests.PlayMode`:
   - `AssertAgrees(WinningCondition condition, GameManager gameManager)` — builds the snapshot via `GameSnapshotBuilder.FromLiveState(gameManager)` **locally** (never cached in a static/shared field), then `Assert.AreEqual(condition.CheckCondition(), condition.CheckCondition(snapshot))`.
   - The snapshot is a method-local immutable value passed by argument — assert by construction (and by a code comment) that nothing is stored statically. A second call must rebuild from scratch.

4. **Per-field-parameterized mutation-sentinel MECHANISM (ready, not yet biting).** Provide a reusable helper that, given a `CharacterSnapshot`, yields one variant per field with exactly that field corrupted (mirroring Story 1.4 Test B). Because the 2.2 overload still delegates to the pull (ignores the snapshot), corrupting a snapshot field cannot change a *delegating* condition's verdict yet — that is expected. To prove the **mechanism itself bites** (so it is not decorative when 2.3–2.6 use it), include a meta-test using a **fake test-only condition that actually reads the snapshot**: corrupting the field it reads must flip its `CheckCondition(snapshot)` verdict. This proves the sentinel is wired correctly before the real migrations depend on it.

5. **Behavior preservation.** Full Epic 1 + 2.1 suite green after the one-line addition (the delegation is a no-op for behavior). New category `[Category("Differential")]` (PlayMode), independently runnable.

## Tasks / Subtasks

- [x] **T0 — Baseline green**
- [x] **T1 — Production dual signature** added to `WinningCondition` (`using CorruptionDuPortail.Domain;`); no evaluator overrides it yet; clean compile
- [x] **T2 — `SnapshotDifferential.AssertAgrees`** — builds snapshot locally per call, asserts pull == snapshot verdict; stateless
- [x] **T3 — Mutation-sentinel mechanism** — `SnapshotDifferential.SingleFieldCorruptions` (one variant per field)
- [x] **T4 — Differential tests** — all 4 real conditions agree (delegating)
- [x] **T5 — Sentinel meta-test** — `FakeOwnerChainReader` flips when `IsChained` corrupted → mechanism bites
- [x] **T6 — Prove** — `Differential` 2/2; regression 51/51 PlayMode; console clean

## Dev Notes

### What this story touches (and must NOT)

**Modified (production, one method):** `Assets/Scripts/Characters/WinningConditions/WinningCondition.cs`.
**Created:** `Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs` (helper), `Assets/Scripts/Tests/PlayMode/SnapshotDifferentialTests.cs` (differential + sentinel meta-test).
**Must NOT change:** the 4 evaluators (no overrides yet), `GameSnapshotBuilder`, `VictoryConditionCheckState`, any asmdef.

### Why delegation keeps prod identical

`CheckCondition(GameSnapshot) => CheckCondition()` ignores its argument and runs the existing pull. So in prod (which never calls the overload yet) nothing changes; in tests, the overload returns the same verdict as the pull → the differential trivially agrees. Migrations (2.3–2.6) replace the override body per condition to actually read the snapshot, at which point the differential becomes a real equivalence proof and the sentinel starts biting.

### Testing standards summary

- `SnapshotDifferential` is a pure test utility (no `[Test]`); lives in `Tests.PlayMode` (references `Game` + `CorruptionDuPortail.Domain`).
- The differential builds the snapshot **per call**, never caches it — statelessness is the AC.
- `[Category("Differential")]`, independently runnable; joins `SnapshotBuilder`/`GoldenMaster`/etc.

### Project Context Rules

- Server-authority/RPC patterns not newly exercised (overload is a pure read delegating to the existing pull).
- `WinningCondition` stays in `Game` (it is `INetworkSerializable`); the overload referencing the Domain `GameSnapshot` is fine — `Game` references `Domain`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.2] (lines 211–224)
- [Source: _bmad-output/refactor-architecture-poco.md] (§3a step 3, line 100) — `virtual bool CheckCondition(GameSnapshot s) => CheckCondition();`
- [Source: Assets/Scripts/Characters/WinningConditions/WinningCondition.cs:11–22] — the abstract base to extend
- [Source: Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs] — `FromLiveState` (Story 2.1)
- [Source: Assets/Scripts/Tests/PlayMode/GameSnapshotBuilderLosslessnessTests.cs] — host-harness + POmniscience spawn pattern to reuse

### Previous story intelligence

- Story 2.1 added the builder + the `SnapshotBuilder` category and the host-harness POmniscience spawn pattern — reuse both.
- Story 1.4 Test B is the parameterized-per-field sentinel template; this story generalizes it into a reusable helper.
- `CharacterSnapshot`/`GameSnapshot` are immutable with value equality — "corrupting a field" = constructing a new one-field-changed instance, never mutating.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Debug Log References

- No issues. The one-line delegating overload compiled clean; all 51 PlayMode regression tests stayed green (delegation is behaviorally a no-op).

### Completion Notes List

- Production change is exactly one method: `WinningCondition.CheckCondition(GameSnapshot) => CheckCondition()` (delegates to the pull). Prod never calls the overload yet, so behavior/cost are byte-identical; the differential is test-only.
- **No in-prod canary shipped** → the "flag OFF in release" AC is vacuous; documented in the `SnapshotDifferential` header so no one hunts for a missing flag.
- `SnapshotDifferential.AssertAgrees` rebuilds the snapshot locally each call, never caches it (statelessness AC). `SingleFieldCorruptions` is the reusable per-field sentinel mechanism for 2.3–2.6.
- The sentinel mechanism is proven to bite via `FakeOwnerChainReader` (a fake snapshot-reading condition): corrupting `IsChained` flips its verdict. Real conditions (still delegating) trivially agree — expected until they are migrated.
- **Verification:** `Differential` 2/2; regression 51/51 PlayMode; console clean.

### File List

- **Modified:** `Assets/Scripts/Characters/WinningConditions/WinningCondition.cs` (added delegating `CheckCondition(GameSnapshot)` overload + `using CorruptionDuPortail.Domain;`)
- **Added:** `Assets/Scripts/Tests/PlayMode/SnapshotDifferential.cs` (helper: `AssertAgrees` + `SingleFieldCorruptions`)
- **Added:** `Assets/Scripts/Tests/PlayMode/SnapshotDifferentialTests.cs` (differential + sentinel meta-test)

### Change Log

- 2026-06-10 — Added the dual `CheckCondition(GameSnapshot)` signature (delegating default) + the stateless differential harness and the per-field mutation-sentinel mechanism (proven to bite). Additive, no prod behavior change. Story 2.2 → review.
