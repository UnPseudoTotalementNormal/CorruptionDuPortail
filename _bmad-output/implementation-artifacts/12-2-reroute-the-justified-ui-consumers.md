# Story 12.2: Reroute the justified UI consumers

Status: ready-for-dev

## Story

As a developer,
I want the UI consumers the triage justified rerouted onto injected dependencies (lane A mostly),
so that the UI's worthwhile decoupling lands without churning the whole layer.

## Acceptance Criteria

1. **Scope = the 12.1 table's REROUTE rows, nothing more.** Batched by shape (trivial lane A first); each batch compiler-clean, suite-gated.
2. **Serialized-field safety on every wiring** (append-only, MCP-wired, read-back verified — D-NFR3); UI prefab-placed consumers respect the prefab-can't-ref-scene constraint (lane B push from their host, or root accessor where the host is itself spawned — per the established precedents).
3. **`Smartphone/` stays client-only** (reads state, emits wrapped ServerRpcs — never mutates) — any reroute preserves that boundary exactly.
4. **Registry/guards:** migrated UI types appended; both guards green; opt-out rows from 12.1 NOT appended (they keep their façade legitimately).
5. **Gated:** suite + boot smoke (full UI pass: phone apps, bars, recaps, vote flow) unchanged per batch; sprint-status.

## Tasks / Subtasks

- [ ] **Task 1:** Batch plan from the 12.1 table (by shape + risk).
- [ ] **Task 2:** Migrate per batch (6.1 template / push / root per row).
- [ ] **Task 3:** Wiring verification sweep (SceneWiringGuard is the net — every batch ends with both guard categories green).
- [ ] **Task 4:** Gates; sprint-status; commits per batch.

## Dev Notes

- Mechanical by construction: every judgment was made in 12.1. If a row turns out misjudged mid-batch (e.g. a "trivial lane A" is actually prefab-placed), update the TABLE first, then implement — keep the map true.
- UI churn risk is scene-file merge weight: batch wiring commits tightly (scene + code same commit), never leave a scene wired against unmerged code.
- Staleness: fully driven by the 12.1 table.

### Project Structure Notes

- Modified: UI files per table, GameScene/prefabs (wiring), registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Canvas split/perf rules untouched; no `UnityEvent` gameplay wiring; TMP/uGUI conventions as-is.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3, §8 Epic 12] / [epics.md#Story 12.2]
- [Source: _bmad-output/implementation-artifacts/12-1-*.md] — the triage table (the whole scope).

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
