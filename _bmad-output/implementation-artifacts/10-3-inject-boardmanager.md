# Story 10.3: Inject BoardManager (fan-in 13)

Status: ready-for-dev

## Story

As a developer,
I want `BoardManager` consumers to receive it injected,
so that board/despawn operations stop going through a static.

## Acceptance Criteria

1. **Recipe §7 applied** to `BoardManager` (replicated, 257 LOC, fan-in 13, 5 For-hits of its own): census → optional `IBoardService` slice (record the call) → root accessor → consumers per lane → static narrowed/annotated; BoardManager's own hub-hops rerouted too.
2. **Despawn paths unchanged:** server-side `NetworkObject.Despawn(destroy: true)` flows byte-identical (these are the dangerous members — clients must never gain a destroy path); fixture green.
3. **Board goldens/flows unchanged** (card lifecycle, board snapshots if pinned); `Card.cs` interactions respect the lane decision recorded in 7.2.
4. **Gated:** suite + fixture per batch; registry/guards green; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Census + slice decision.
- [ ] **Task 2:** Root accessor; inert commit.
- [ ] **Task 3:** Batches per lane (board scene components lane A; spawned board objects lane C; BoardManager's own hops).
- [ ] **Task 4:** Static narrowing; gates; sprint-status; commits.

## Dev Notes

- Despawn authority is the NFR-critical surface here (project-context: `Despawn(destroy:true)` server-only, never raw `Destroy()` on a NetworkObject). The reroute must not move any despawn call across an authority boundary — access path only.
- Staleness: census-driven.

### Project Structure Notes

- Modified: `BoardManager.cs`, `CompositionRoot.cs`, board consumers, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- NGO lifecycle rules (Spawn/Despawn server-only; OnNetworkDespawn teardown) — untouched, verbatim.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7, §8 Epic 10] / [epics.md#Story 10.3]
- [Source: Assets/Scripts/Board/BoardManager.cs] / [_bmad-output/implementation-artifacts/7-2-*.md] — Card lane precedent.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
