# Story 10.2: Inject RoleTargetSystem (fan-in 16)

Status: ready-for-dev

## Story

As a developer,
I want `RoleTargetSystem` consumers to receive it injected,
so that targeting resolution stops going through a static.

## Acceptance Criteria

1. **Recipe §7 applied** to `RoleTargetSystem` (fan-in 16, has `OnNetworkSpawn`): census → optional slice interface (likely `ITargetingService`-shaped if read/record splits cleanly — record the call) → root accessor → consumers per lane → static narrowed/annotated.
2. **Targeting behaviour unchanged:** `NewTargeting` flows (e.g. `PTruthChains.OnCardClickedRpc:34`), target validation paths, and any reveal interactions pass the fixture + goldens unchanged.
3. **Consumers** are mostly powers (the `Power` base-field extension pattern from 7.1/10.1 applies) + `TargetUtils` (static utility — verify-don't-force candidate: if it cannot take injection, route its resolution through a parameter from the calling power, or record).
4. **Gated:** suite + fixture + goldens per batch; registry/guards green; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Census (powers + TargetUtils 5 hits + UI); slice decision.
- [ ] **Task 2:** Root accessor; inert commit.
- [ ] **Task 3:** Batches per lane; `TargetUtils` decision recorded (parameter-injection preferred over root-access in a static util).
- [ ] **Task 4:** Static narrowing; gates; sprint-status; commits.

## Dev Notes

- `TargetUtils` is the interesting case: a static utility consuming a singleton. Cleanest fix = pass the dependency as a parameter from the (already-injected) calling power — no root access inside statics (the root is for lane C glue only, guard-enforced). This becomes the precedent for static-util consumers track-wide.
- Staleness: census-driven.

### Project Structure Notes

- Modified: `RoleTargetSystem.cs`, `CompositionRoot.cs`, powers/UI consumers, `TargetUtils.cs`, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Pure decision logic in statics → candidates for Domain extraction later (Epic 11 notes any found). GetSafeRpcTarget verbatim.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7, §8 Epic 10] / [epics.md#Story 10.2]
- [Source: Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs] / [Assets/Scripts/Characters/Powers/Target/TargetUtils.cs].

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
