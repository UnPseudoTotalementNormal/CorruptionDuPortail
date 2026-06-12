# Story 12.1: UI inventory + triage (verify-don't-force, recorded)

Status: ready-for-dev

## Story

As a developer,
I want the 48 UI files triaged — reroute vs recorded façade opt-out,
so that Epic 12 spends churn only where it buys maintainability.

**No code change in this story** — the deliverable is the map for 12.2.

## Acceptance Criteria

1. **Triage table** (file → remaining static/manager dependencies → decision → reason) covering every `UI/` file (48 at recon) PLUS any `Smartphone/`/system-UI file still on a façade after Epics 7-11, recorded in the architecture doc (new §: UI triage).
2. **Explicit criteria applied:** REROUTE when a narrow injected dependency simplifies testing or removes a remaining hub/static; OPT OUT when the file is a pure local-player leaf with no decision logic (verify-don't-force, D-NFR6). Borderline cases get one sentence of justification each.
3. **Effort estimate per reroute** (trivial lane A / needs push / needs interface) so 12.2 can batch by shape.
4. **Census cross-check:** the triage reconciles with the §4 static census (every static a UI file still touches is either dying in 12.2/12.3 or a recorded survivor).

## Tasks / Subtasks

- [ ] **Task 1:** Grep inventory of UI-layer dependencies (statics + concrete manager refs) post-Epic-11.
- [ ] **Task 2:** Classify per the criteria; write the table; flag borderlines.
- [ ] **Task 3:** Doc section + census reconciliation; sprint-status; commit (docs only).

## Dev Notes

- UI is LAST by design (lowest architectural payoff, highest churn). The triage is the brake on perfectionism: an opt-out with a recorded reason is a legitimate END STATE, not a failure.
- Smartphone/ files: content-vs-host pattern means many are views over already-decoupled systems — likely heavy opt-out territory.
- Staleness: "48 files" is 2026-06-11; Epics 7-11 already rerouted UI hub-hops (7.4) and presentation subscribers (8.2) — the triage covers the REMAINDER.

### Project Structure Notes

- Modified: architecture doc only. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Smartphone/ client-only; UI placement three-question test (if the triage finds misplaced logic, RECORD it for Poyo — do not move it; behaviour-preserving).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §8 Epic 12, §6 verify-don't-force] / [epics.md#Story 12.1]

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
