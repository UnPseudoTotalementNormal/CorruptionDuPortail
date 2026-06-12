# Story 10.5: Mono statics by fan-in (SelectionFlowService & co) — inject or record

Status: ready-for-dev

## Story

As a developer,
I want the non-replicated Mono singletons (SelectionFlowService 16, FocusManager, …) migrated by descending fan-in or recorded as opt-outs,
so that the full 24-singleton census converges toward the one-surviving-static endgame.

## Acceptance Criteria

1. **Full Mono-static census first:** the 24-singleton recon (2026-06-11) minus the replicated 8 minus GameManager/CharacterManager = the Mono-static population (SelectionFlowService 16, FocusManager 3 instance-hits, TooltipSystem?, DevIdentityController 6, others — enumerate by grep `static.*instance` at dev time). Each gets: migrate / opt-out + reason.
2. **`SelectionFlowService` (fan-in 16) migrated** — its consumers are powers (StartCharacterSelection flows, e.g. `PTruthChains:67,83`) and UI; powers consume via the `Power` base-field pattern; scene UI lane A.
3. **Dev/editor-only statics** (DevIdentityController…) are legitimate opt-out candidates (debug tooling) — recorded, not contorted.
4. **Census table closes** in architecture doc §4: every one of the 24 original statics has a final state (migrated / opt-out+reason / dead).
5. **Gated:** suite per batch; registry/guards green; sprint-status (`epic-10 → done`).

## Tasks / Subtasks

- [ ] **Task 1:** Grep census of remaining statics; decisions table.
- [ ] **Task 2:** SelectionFlowService migration (root accessor + Power base field + lane A UI).
- [ ] **Task 3:** Remaining migrate-set by descending fan-in; opt-outs recorded.
- [ ] **Task 4:** §4 census close; gates; sprint-status; commits per target.

## Dev Notes

- SelectionFlowService drives the click-to-target UX — selection flows are choreography-sensitive (player-perceived). Access-path-only changes; if any selection feels different in the boot smoke, stop (golden-blind territory — the ear/eye is the test here, per the Epic 4 A/B precedent).
- The Mono statics are NOT NetworkBehaviours: no OnNetworkSpawn, no lane C. Scene-placed → lane A; service-like → root accessor consumed by already-injected callers (powers).
- Staleness: census-driven; "24" was the 2026-06-11 figure — Epics 6-10 will have killed several already.

### Project Structure Notes

- Modified: per-target service files, `CompositionRoot.cs`, consumers, architecture doc §4, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Editor-only code stays `#if UNITY_EDITOR`-fenced (DevIdentityController decisions). No FindObjectOfType introduced anywhere.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §1 (24 statics), §4 census, §8 Epic 10] / [epics.md#Story 10.5]
- [Source: Assets/Scripts/UI/.../SelectionFlowService] (253 LOC, fan-in 16) — primary target.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
