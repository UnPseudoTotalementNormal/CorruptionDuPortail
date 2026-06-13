# Story 10.5: Mono statics by fan-in (SelectionFlowService & co) — inject or record

Status: review

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

- [x] **Task 1:** Grep census of remaining statics (22 live project statics); decisions table (Dev Agent Record + archi §4f).
- [x] **Task 2:** SelectionFlowService migration — root accessor + `Power.selectionFlowService` base field (15 powers) + `TakeDownThePortalState` GameState lane-B (it's a POCO singleton, not scene UI — so lane-B/C resolve via the root, not lane-A; AC2 premise corrected).
- [x] **Task 3:** Remaining migrate-set by descending fan-in — FocusManager migrated (next after SelectionFlowService); everything else (fan-in ≤ 3, UI/global/static) recorded as opt-out/deferred with reasons.
- [x] **Task 4:** §4f census close (every static terminal); gates (compile 0, DiSeamGuard 4/4, EM 162/162, PM 148/148); sprint-status `epic-10 → done`; per-target commits.

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

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors. DiSeamGuard 4/4. EM 162/162 + PM 148/148 — all baselines held.
- One guard iteration: `MigratedConsumers_DoNotReferenceTheLocator` flagged `PHighPriorityBounty` still holding `SelectionFlowService.instance`. Root cause: the file had only been **partially read** (a 22-line region) before the `replace_all` Edit, so the edit's `replace_all` operated on the cached region (which did not contain the occurrences at lines 89/105) and reported "all replaced" vacuously. Re-read the relevant span and re-applied → clean. **Lesson: `replace_all` needs the WHOLE file in context; a partial Read can make it a silent no-op that still reports success.** (The other 3 partially-read powers — PTruthChains/PBlessing/PCardsShuffling — happened to apply correctly; only PHighPriorityBounty slipped, caught by the guard.)

### Completion Notes List

**Task 1 — census (22 live project statics).** Grep `static … instance`/`Instance` under `Assets/Scripts` (Plugins excluded). The 2026-06-11 "24" is now 22 (two killed during Epics 6–9). Full decision table in architecture doc **§4f**. By descending fan-in: SelectionFlowService (16) + FocusManager → **migrate**; everything else (fan-in ≤ 3) → **record/opt-out** with reasons.

**Task 2/3 — migrations (bundled, one commit — the two services co-migrate through the same seams).**
- **SelectionFlowService — MIGRATED + locked.** A **POCO** singleton (`sealed class : ISelectionFlowService`, eager `static _instance = new()` → never null), not scene UI — so the AC2 "scene UI lane A" premise was **corrected**: consumers resolve it via the composition root (`CompositionRoot.For(nm).SelectionFlowService`) like the other Epic-10 singletons. Injected **concrete** (Epic 10 D-NFR6 default; the existing `ISelectionFlowService` is available for an Epic-11 narrowing). 15 targeting powers rerouted onto a new `Power.selectionFlowService` base field (lane C); `TakeDownThePortalState` onto a new `GameState.selectionFlowService` lane-B field (pushed by `SetupGameStates`). **No UI leaf reads it** → the lock is total. `instance` annotated façade.
- **FocusManager — MIGRATED + locked.** Scene singleton; `Power.focusManager` base field covers `Power.StopUse` + `PVisionOfTheImpossible`; `TakeDownThePortalState` via `GameState.focusManager` lane-B. Null-tolerant (StopUse already null-guards). Unregistered survivors keep the global — `SelectionFlowService` (service-to-service focus calls) + `CardPickerManager` (UI) → Epic 11/12. `instance` annotated façade.
- No registry edits: the powers + Power are already in `All` (Epic 7); SelectionFlowService stays **off** the registry (it still reads the FocusManager global, so registering it would trip guard #1).

**Task 4 — census close.** §4f records every one of the 22 statics with a terminal state (AC4): 1 surviving root (`CompositionRoot`) + 2 root-backed God Objects (instance dies 12.3) + 9 locked Epic-10 façades + GameAudioManager/LobbyManager/InputManager permanent opt-outs (global façades) + UI leaves → Epic 12.2 + ArrowManager/CardEffectManager/PowerManager → Epic 11/12 + GameAssetHolder/BoardCameraManager non-issues (0 `.instance` consumers). **Epic 10 (D4) complete.**

**⚠️ Acceptance note (golden-blind):** SelectionFlowService drives the click-to-target **choreography** (player-perceived). The change is **access-path-only** — the same POCO singleton, same method calls, resolved via the root instead of the static — so it is behaviour-identical by construction, and the full suite (incl. the multi-client fixture booting the power graph) is green. But per the story Dev Notes, the *feel* of selection is the real test: this wants **Poyo's eye/ear in a live playtest** before final acceptance. That is exactly what the `review` status gates.

### File List

**Modified (production):**
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `SelectionFlowService` + `FocusManager` accessors (instance + `Services`).
- `Assets/Scripts/Characters/Powers/Power.cs` — `using UI.BoardUI.Selection;`; `selectionFlowService` + `focusManager` base fields + lane-C resolve; `FocusManager.instance` → `focusManager` (StopUse).
- `Assets/Scripts/Characters/Powers/{PVisionOfTheImpossible,PTruthChains,PReincarnation,PPersonalBeacons,POmniscience,PLackOfAffection,PHighPriorityBounty,PEmbraceOfShadows,PDroolyHealing,PCursedVision,PCorruptingMark,PChainedByTheShadows,PCardsShuffling,PBoundByInk,PBlessing}.cs` — `SelectionFlowService.instance` → `selectionFlowService` (PVisionOfTheImpossible also `FocusManager.instance` → `focusManager`).
- `Assets/Scripts/GameLogic/GameState.cs` — `selectionFlowService` + `focusManager` lane-B props.
- `Assets/Scripts/GameLogic/GameManager.cs` — `SetupGameStates` pushes both.
- `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs` — `SelectionFlowService.instance` → `selectionFlowService`, `FocusManager.instance` → `focusManager`.
- `Assets/Scripts/UI/BoardUI/Selection/SelectionFlowService.cs` — `instance` recorded-façade annotation.
- `Assets/Scripts/FocusSystem/FocusManager.cs` — `instance` recorded-façade annotation.

**Modified (tests):**
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"SelectionFlowService.instance"` + `"FocusManager.instance"` → `ForbiddenLocators`.

**Docs:** `_bmad-output/refactor-architecture-despaghetti.md` (§4f census close); this story; `sprint-status.yaml` (10-5 → review, epic-10 → done).

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 10.5 (Epic 10 census close). Migrated the two highest-fan-in Mono statics off their globals: SelectionFlowService (POCO singleton, 15 powers via `Power.selectionFlowService` + TakeDownThePortalState lane-B; fully locked) + FocusManager (Power base + TakeDownThePortalState lane-B; UI/service-leaf survivors recorded). Both concrete, root-served, locked in guard #1. §4f closes the full 22-static census — every static terminal (opt-outs/non-issues/UI-deferrals recorded). Compile 0; DiSeamGuard 4/4; EM 162/162 + PM 148/148. **Epic 10 (D4) complete → epic-10 done.** |
| 2026-06-12 | gds-code-review (3 adversarial layers) — **PASS / clean**. 0 actionable (0 decision / 0 patch / 0 defer), 3 dismissed: Edge Hunter stale-destroyed-FocusManager-ref (unreachable, degrades to same guarded skip), Auditor DevIdentityController AC-wording (no own static; handled via §4a), Auditor no-Assert-on-2-new-fields (consistent with Epic-10 null-tolerant pattern). Blind Hunter confirmed no find-replace cross-wiring; Edge Hunter confirmed behaviour-preserving on every path (POCO never-null, FocusManager scene-Awake-before-spawn, harness null-safe, multi-NM = same global); Auditor verified census real (live grep) + lock reasoning load-bearing. Status held at `review` (not auto-`done`) — the golden-blind selection choreography still wants Poyo's live playtest as the final acceptance gate. |

## Senior Developer Review (AI)

**Date:** 2026-06-12 · **Reviewer:** gds-code-review (Blind Hunter + Edge Case Hunter + Acceptance Auditor, claude-opus-4-8) · **Outcome:** **Approve (clean)**

**Action Items:** 0 (0 decision-needed, 0 patch, 0 defer). 3 findings dismissed as noise/justified:
- [Edge] Stale destroyed-`FocusManager` reference if the scene singleton were `Destroy`ed mid-session — **dismissed**: no teardown-during-play path exists, and the only guarded consumer (`Power.StopUse`) uses Unity `!= null` which treats a destroyed object as null, degrading to the same guarded skip. No fix warranted.
- [Auditor] AC1 listed `DevIdentityController` as a static to enumerate — **dismissed**: it has no `static instance` of its own (it is a debug *consumer* of `CharacterManager.instance`), correctly recorded in §4a, not §4f. AC wording imprecision, handled correctly.
- [Auditor] No `Assert.IsNotNull` on the two new lane-C fields — **dismissed**: consistent with every other Epic-10 injected field (chatManager/roleTargetSystem/…), justified by the recorded null-tolerance (POCO eager-`new` / StopUse null-guard).

**Verified:** no find-replace cross-wiring (Blind); behaviour-preserving on every branch/boundary/lifecycle incl. spawn-ordering, harness null-safety, and the multi-NM fixture (Edge); AC1–AC5 satisfied with the census cross-checked against live grep, the AC2 POCO-not-lane-A correction sound, and the "SelectionFlowService stays off the registry because it still reads FocusManager/BoardManager globals" reasoning confirmed load-bearing (Auditor).
