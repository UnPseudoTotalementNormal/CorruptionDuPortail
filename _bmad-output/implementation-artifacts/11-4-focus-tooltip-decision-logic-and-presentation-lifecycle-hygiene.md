# Story 11.4: Focus + Tooltip + presentation lifecycle hygiene

Status: ready-for-dev

## Story

As a developer,
I want Focus/Tooltip decision logic extracted and the recorded presentation lifecycle leaks fixed deliberately,
so that the presentation layer is clean and the known debt is closed, not forgotten.

## Acceptance Criteria

1. **Focus/Tooltip inventory + extraction:** `FocusManager` / `TooltipSystem` (+`TooltipLinkParser` — already partially pure?) decision logic (focus priority/stack rules, tooltip link resolution) → POCOs + EditMode tests, same regime as 11.1-11.3.
2. **Lifecycle hygiene pass (the deliberate behaviour-adjacent change of the track):** every presentation component recorded as leaking (`OnValueChanged` subscribed, never unsubscribed — `LightManager` noted in 6.1, others collected by 8.2's Task 2 notes) gets its `OnDestroy`/`OnDisable` unsubscribe — as a SEPARATE commit, explicitly labelled lifecycle hygiene, with the suite green before AND after (the unsubscribes must not change any in-game behaviour — they only fix teardown).
3. **Subscription symmetry rule recorded** in the architecture doc (subscribe in X ⇒ unsubscribe in its teardown mirror) — the convention for all future presentation code.
4. **Gated:** suite + fixture unchanged; sprint-status (`epic-11 → done`).

## Tasks / Subtasks

- [ ] **Task 1:** Focus/Tooltip inventory; extraction candidates; POCOs + tests (one commit each).
- [ ] **Task 2:** Leak census (6.1 note + 8.2 notes + grep `OnValueChanged +=` without matching `-=`); fix table.
- [ ] **Task 3:** Hygiene commit(s) — unsubscribes only, no other change; suite green before/after; boot smoke (state changes still drive light/fog/camera correctly after several transitions).
- [ ] **Task 4:** Doc rule; gates; sprint-status; commits.

## Dev Notes

- WHY the leaks waited until now: fixing them earlier would have mixed a lifetime change into behaviour-preserving reroutes (golden noise risk). Isolated here, labelled, gated = auditable.
- The leaks are mostly benign in-scene (objects live as long as the scene) but bite on scene reload / play-restart with domain reload disabled — that is the test to add if cheap (subscribe-count probe across a simulated re-init).
- `TooltipLinkParser` (2 instance-hits) may already be near-pure — check before extracting (don't reinvent).
- Staleness: leak census depends on 8.2's notes; inventory-driven.

### Project Structure Notes

- New: Focus/Tooltip POCOs + tests. Modified: presentation components (unsubscribes), architecture doc. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- `OnNetworkDespawn` for NetworkVariable unsubscribes (NetworkBehaviours); `OnDestroy` for plain Monos; static-event zombie rule (domain reload disabled).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §8 Epic 11] / [epics.md#Story 11.4]
- [Source: _bmad-output/implementation-artifacts/6-1-*.md Task 1 note] — the recorded LightManager leak (the origin of this story's hygiene scope).
- [Source: Assets/Scripts/FocusSystem/FocusManager.cs + Assets/Scripts/TooltipSystem/].

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
