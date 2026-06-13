# Story 9.3: Narrow the CharacterManager statics to the root-backed resolution

Status: done

## Story

As a developer,
I want `CharacterManager.instance` reduced to the `CompositionRoot`-backed surface, with only recorded callers left on any façade,
so that the second God Object stops being globally reachable.

## Acceptance Criteria

1. **Non-UI consumers all injected** (9.1/9.2 done) — grep proves no gameplay code reads `CharacterManager.instance` / bare `For(nm)` outside: the root, CharacterManager internals, the fixture, and recorded exceptions.
2. **Static surface narrowed:** `For(nm)` survives only as the root's per-NM backbone (or is absorbed into the root — pick per the 6.3 design, record); `instance` either dies now (if zero callers remain) or shrinks to the recorded-callers façade (UI awaiting Epic 12) with an explicit `// recorded: dies in 12.3` annotation.
3. **Recorded leftovers table** (caller → reason → planned death) appended to the architecture doc §4 census.
4. **Gated:** suite + fixture + boot smoke unchanged; guards green.

## Tasks / Subtasks

- [x] **Task 1:** Census of remaining static reads done (grep `.instance` + `For(`); partition frozen in Completion Notes (dead: none / reroute-now: GameSnapshotBuilder / record-for-12: all UI + static-utils + W* POCO + Epic-10 singletons + debug).
- [x] **Task 2:** Rerouted the one reroute-now caller (`GameSnapshotBuilder` bare `For` → `CompositionRoot.For(nm).CharacterQuery`); annotated `instance` (recorded-callers façade, `// recorded: dies in 12.3`) + `For` (per-NM backbone). Visibility unchanged — both stay `public` (recorded callers + test fixtures still need them; no InternalsVisibleTo for PlayMode tests).
- [x] **Task 3:** Recorded leftovers census table (caller → reason → death) appended to architecture doc §4a. Gates EditMode **162/162** + PlayMode **147/147** (snapshot goldens gate the reroute); guards green. sprint-status updated.

## Dev Notes

- Mirror of 7.5 for the second God Object, but with an explicitly allowed remainder (UI) — D1 had none because its hub-hops were all mechanical. Honesty rule: every survivor gets a reason and a death date (12.3), or it gets rerouted now.
- The Awake duplicate-guard semantics (same-NM destroyed, foreign-NM registry-only — 5.0c Design B) are LIFECYCLE, not locator — untouched here.
- Staleness: census-driven.

### Project Structure Notes

- Modified: `CharacterManager.cs` (static narrowing), architecture doc, straggler files. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Domain-reload statics rule (SubsystemRegistration reset stays). Bot flow untouched.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §4 census, §8 Epic 9] / [epics.md#Story 9.3]
- [Source: _bmad-output/implementation-artifacts/5-0c-*.md] — Design B semantics to preserve.
- [Source: _bmad-output/implementation-artifacts/9-2-*.md] — previous story.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors. EditMode **162/162** + PlayMode **147/147** unchanged (the only behavioural-adjacent change — the GameSnapshotBuilder `For` reroute — is resolution-identical and is gated by `GameSnapshotBuilderLosslessnessTests` + `SnapshotDifferentialTests`; everything else is comments/doc).

### Completion Notes List

**Task 1 — frozen static census (grep `CharacterManager.instance` + `CharacterManager.For(`).**

- **`CharacterManager.For(` (bare, non-root):** only `GameSnapshotBuilder.cs:34` in production → **reroute-now**. All other `For(` callers are `CompositionRoot` (the root backbone, allowed) + test fixtures (allowed).
- **`CharacterManager.instance`:** zero dead callers; zero cheaply-injectable gameplay callers left (9.1/9.2 injected them). All survivors are **recorded verify-don't-force exceptions** — see the §4a table. Buckets: Epic 10 singletons (`ChatManager`, `LobbyPlayerInfoHolder`); static-utility / POCO façade (`TargetUtils`, `PowerEffectDispatcher`, the four `W*` winning conditions); debug (`DevIdentityController`); Epic 12 UI leaves + entangled (`RoomFog`/`PowersBar`/`AnonymeMessageButton`/`InfoTableSystem` = both-managers; `Note*`/`AwakeningRecapCorruption`/`SelectPanelPlayer`/`Tooltip*`/`VoteStateUI`/`ChatWindow`/`CardPickerManager`/`MeIconCard` = leaves; `CharacterAwakenTimer`/`TakeDownThePortalTextTitle` = prefab-only).

**Task 2 — reroute + annotate (AC1, AC2).** `GameSnapshotBuilder` now resolves through `CompositionRoot.For(gameManager.NetworkManager).CharacterQuery` (read slice) — so the only DIRECT bare-`For` callers are the root + fixtures (AC1). `CharacterManager.instance` annotated as the recorded-callers-only façade with `// recorded: dies in 12.3`; `For` annotated as the per-NM backbone (absorbing-into-root deferred per the 6.3 design, recorded). Neither can be narrowed below `public` yet — the recorded set + the PlayMode fixtures (no InternalsVisibleTo) still reference them; the visibility kill is 12.3's job once the UI/static leftovers are gone.

**Task 3 — doc + gate (AC3, AC4).** §4a census table (caller → member → reason → planned death) added to `refactor-architecture-despaghetti.md`. Suite + fixture green (EM 162 / PM 147), guards green (no new `.instance`/`.For(` in any registered consumer — the reroute uses `CompositionRoot.For`, which is the whitelisted root surface; `GameSnapshotBuilder` is a static util, off the registry). NFR5 untouched (`GetSafeRpcTarget`/`IsLocalOrSimulated` not read here). Awake duplicate-guard semantics (5.0c Design B) untouched — lifecycle, not locator.

### File List

**Modified (production):**
- `Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs` — bare `CharacterManager.For` → `CompositionRoot.For(nm).CharacterQuery` (read slice).
- `Assets/Scripts/Characters/CharacterManager.cs` — `instance` recorded-callers-façade annotation (`// recorded: dies in 12.3`) + `For` per-NM-backbone annotation. No code/visibility change.

**Docs:**
- `_bmad-output/refactor-architecture-despaghetti.md` — §4a CharacterManager static census table.
- `_bmad-output/implementation-artifacts/9-3-*.md` (this story); `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Census of remaining `CharacterManager` statics; rerouted `GameSnapshotBuilder` onto the `CompositionRoot` surface; annotated `instance` (recorded façade, dies 12.3) + `For` (per-NM backbone); §4a census table. EM 162/162 + PM 147/147. Status → review. |
