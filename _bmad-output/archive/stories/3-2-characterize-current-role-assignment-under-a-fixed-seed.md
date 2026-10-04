# Story 3.2: Characterize current role assignment under a fixed seed (golden master)

Status: review

## Story

As a developer (Poyo),
I want the current role-assignment behavior pinned as a golden master under an injected seed,
so that the extracted `RoleDistributor` (3.3) can be proven behavior-preserving despite the logic being randomized.

## Acceptance Criteria

**Given** the frozen role-pool ordering (Story 1.1) and the `IRandomProvider` seam (3.1)
**When** the characterization runs with a fixed seed over representative player-count / settings combinations
**Then** the resulting role→player assignments are captured as golden masters, tagged `[Category("GoldenMaster")]`
**And** the corpus covers boundary cases: minimum players, single-role pool, multi-role pool with exhaustion
**And** these goldens are stable across runs (proving the frozen order + seeded RNG determinize the assignment)

## Acceptance reading notes (binding)

1. **Seed injection without pre-extracting (the seam is RNG-global today).** 3.1 added `IRandomProvider` but did NOT wire it into `RoleAttributionState` (deferred to 3.3). So the live selection still calls `UnityEngine.Random.Range` directly. The faithful way to determinize the **current** code is `UnityEngine.Random.InitState(seed)` immediately before the live selection runs. The 3.3 `RoleDistributor` then reproduces these goldens by driving `UnityRandomProvider` under the same `InitState(seed)` (its `Next(max)` == `Random.Range(0, max)`); the pure `SeededRandomProvider` is for 3.3's separate property tests, not for matching this Unity-seeded golden.
2. **Faithful live invocation, minimal coupling.** Run the REAL `RoleAttributionState.OnStartStateServer()` (not a re-implementation) with `gameManager` wired manually and `gameManager.characterManager` populated with N real characters. The state is constructed standalone (`CreateInstance`) and is NOT registered in `gameManager.gameStates` — this avoids the `SetupGameStates` clone + the spawn-time auto-run, so the selection runs exactly once under the seed we set.
3. **Read-back = the observable assignment.** `GiveRandomRole` sets `_character.role = _newRole` synchronously (RoleAttributionState.cs:110) before the replication RPC, so the golden reads each spawned character's `role.roleName` in add-order (= the `GetCharacters().Where(!isFake)` processing order). RoleDataObjects carry a distinct `roleName` + empty `powers` (so `GivePowerToCharacter` is never called) + `canBeFake = false`, and counts sum to N so `_fakeRoleAmountToRemove == 0` (no fake-character creation path).
4. **Corpus (boundary matrix):**
   - **M-single:** N=1, single role count 1 → trivially that role.
   - **single-role-pool:** N=3, one role count 3 → all three get it (selection always index 0).
   - **multi-exhaustion:** N=5, roles [A:2, B:2, C:1] under ≥2 distinct seeds → the exact assigned sequence (exercises decrement + remove-at-0 + frozen-order re-filtering).
   - Each captured verdict is the CURRENT output, asserted, `[Category("GoldenMaster")]` + `[Category("RoleAssignment")]`.
5. **Stability:** each case runs the selection twice (re-seeding) within the test and asserts identical output, proving InitState + frozen order fully determinize it.
6. **Test-only (NFR1).** No production change.

## Tasks / Subtasks

- [ ] **T0 — Baseline green**
- [ ] **T1 — `RoleAssignmentGoldenMasterTests`** PlayMode harness (golden-harness shape + a standalone `RoleAttributionState` wired to `gameManager`); discovery run logging the assigned `roleName` sequence (`[ROLEGOLD]` tag)
- [ ] **T2 — Pin the observed sequences** as `[Category("GoldenMaster")]` asserts over the boundary corpus + the re-seed stability assertion
- [ ] **T3 — Prove**: `run_tests category: RoleAssignment` + full regression green; console clean

## Dev Notes

**Created:** `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs`.
**Modified:** none (NFR1).
**Must NOT change:** `RoleAttributionState` (re-pointed in 3.3), the frozen pool order, the `IRandomProvider` impls.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 3.2] (lines 417–429)
- [Source: Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:29-130] — the live selection being characterized
- [Source: Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs] — the StartHost + CharacterManager harness reused

### Previous story intelligence

- 3.1 added `IRandomProvider` (+ Unity/seeded impls) but left `RoleAttributionState` untouched — so this story seeds via `UnityEngine.Random.InitState`.
- Role-pool order frozen in 1.1 (`GetFrozenRolePoolOrder`). `_character.role` is a plain settable field (golden harness sets it directly).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- 4 PlayMode goldens `[Category("GoldenMaster")][Category("RoleAssignment")]` pin the live selection under fixed `UnityEngine.Random.InitState` seeds, running the REAL `RoleAttributionState.OnStartStateServer()` (standalone instance, gameManager wired, not in gameStates → no clone/auto-run):
  - M-single (N=1, [Solo:1]) → `[Solo]`
  - single-role-pool (N=3, [Only:3]) → `[Only,Only,Only]`
  - multi-exhaustion (N=5, [A:2,B:2,C:1]) seed 42 → `[B,A,C,B,A]`; seed 99 → `[B,B,C,A,A]`
- **Determinism proven:** each case runs twice on a FRESH state under the same seed and asserts identical output (InitState + frozen pool order fully determinize the assignment).
- **Characterization finding (captured, not fixed):** `OnStartStateServer` mutates the shared `RoleAttributionSetting.roleToAttribute` as roles exhaust — a single state instance cannot be re-run (counts deplete to 0 → `IndexOutOfRange`). Prod is safe because `SetupGameStates` clones the state per game; the determinism re-run uses a fresh state to mirror that. Flagged for 3.3 (the extracted `RoleDistributor` should not mutate caller-owned settings).
- 3.3's `RoleDistributor` reproduces these via `UnityRandomProvider` under the same `InitState(seed)` (its `Next(max)` == `Random.Range(0, max)`).
- Test-only (NFR1). 133/133 PlayMode (129 + 4), EditMode unaffected.

### File List

- **Added:** `Assets/Scripts/Tests/PlayMode/RoleAssignmentGoldenMasterTests.cs`

### Change Log

- 2026-06-11 — Story 3.2 drafted; characterize live role assignment under a fixed seed as golden masters before the 3.3 RoleDistributor extraction.
