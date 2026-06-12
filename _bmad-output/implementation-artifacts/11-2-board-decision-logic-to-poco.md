# Story 11.2: Board — decision logic to POCO

Status: ready-for-dev

## Story

As a developer,
I want board/despawn decision logic extracted into tested POCOs,
so that board rules are EditMode-testable.

## Acceptance Criteria

1. **Inventory first:** `BoardManager` (257 LOC) + board components pass: decision (card placement/ordering rules, despawn eligibility, board-state arithmetic) vs glue (NGO spawn/despawn calls, DOTween, scene refs). Inventory pasted into Dev Agent Record.
2. **Characterize-then-extract** per candidate: golden on current behaviour (EditMode where possible, PlayMode if NGO-coupled), then POCO into `Domain` (or Game-POCO, recorded), adapter applies decisions (NFR4).
3. **Despawn authority untouched:** POCO decides WHAT despawns; the adapter's server-side `Despawn(destroy:true)` call is the only executor (NFR5-adjacent — no authority move).
4. **EditMode tests shipped per core**; suite + fixture unchanged; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Inventory; candidate list.
- [ ] **Task 2:** Characterize + extract per candidate (one commit each).
- [ ] **Task 3:** Tests; Domain purity; gates; sprint-status.

## Dev Notes

- Board is tween-heavy: remember "gameplay state is never the result of a tween" (project-context) — if the inventory finds state derived from tween completion, that is a FINDING to surface (pre-existing fragility), not something to silently fix (behaviour-preserving) — record for Poyo.
- `CancellableTaskHandler` patterns in `Board/CardComponents/` are async glue, not decisions — stay.
- Staleness: inventory-driven; BoardManager reshaped by 10.3.

### Project Structure Notes

- New: Domain POCOs + tests. Modified: board files (thinning). Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- UniTask only; DOTween SetLink/Kill hygiene untouched; pooling rules; EditMode-first.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.4, §8 Epic 11] / [epics.md#Story 11.2]
- [Source: Assets/Scripts/Board/BoardManager.cs + Board/CardComponents/].

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
