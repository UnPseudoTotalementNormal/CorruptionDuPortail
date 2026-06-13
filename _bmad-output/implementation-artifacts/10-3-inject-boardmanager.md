# Story 10.3: Inject BoardManager (fan-in 13)

Status: review

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

- [x] **Task 1:** Census + slice decision (despawn-NFR + "5 own hops" premises corrected).
- [x] **Task 2:** Root accessor (concrete) + `GameState.boardManager` lane-B push field.
- [x] **Task 3:** 7 GameStates lane B (SetupGameStates push) + GameManager:551 root; managers/services/UI recorded (verify-don't-force).
- [x] **Task 4:** Static annotated (recorded façade, NO lock — registered FocusManager survivor blocks it); §4d census; gates EM 162 / PM 148; sprint-status.

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

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors. EditMode **162/162** + PlayMode **148/148** unchanged.

### Completion Notes List

**Task 1 — census + slice decision (two story premises corrected).**

`BoardManager` (global namespace, 257 LOC, fan-in 13) is a non-de-singletonised replicated singleton (naive Awake duplicate-guard, no `BoardManager.For(nm)`) — same shape as Chat/RoleTarget (10.1/10.2).

- **AC1 "5 For-hits of its own" is STALE.** BoardManager has ZERO hub-hops left — its `characterManager` (7.2) + `gameInfoRevealer` (7.3) are already lane-A injected `[SerializeField]` fields. Nothing of its own to reroute.
- **AC2 despawn-authority is MOOT for BoardManager.** Its cards are LOCAL objects (`Instantiate(cardPrefab)` / `Destroy(_card.gameObject)`), NOT `NetworkObject`s — there is no `NetworkObject.Despawn(destroy:true)` in BoardManager, so no server-authority boundary to preserve. (The NFR rule still holds project-wide; it simply has no surface here.)

Census of `BoardManager.instance`, partitioned:

- **GameStates (lane B push) — MIGRATE NOW (7):** VoteState, VoteRecapState, TakeDownThePortalState, GameIntroductionState, GameEndingState, ChainingState, AwakeningState. They already receive pushed deps (`gameManager`/`characterManager`/… via `SetupGameStates`); add a `GameState.boardManager` field pushed the same way. The bulk of the call sites.
- **GameManager `OnPlayerDisconnectedRpc` (:551) — MIGRATE NOW (root):** `DestroyCard(visibleCards.Find(fake))`. GameManager is not a registered DI consumer (it is the hub) and has a `NetworkManager`, so it resolves `CompositionRoot.For(NetworkManager).BoardManager` directly.
- **MIGRATE NOW (completed after Poyo directed completing the wiring — see addendum):**
  - `GameInfoRevealer` (registered `All`, NetworkBehaviour) — **lane C**: added an `OnNetworkSpawn` that resolves `boardManager` through the root (it previously inited only in `Start()`); reroute its `visibleCards` read. Code-only, no wiring.
  - `FocusManager` (registered `All`, **`MonoBehaviour`**, 3 hits) — **lane A**: `[SerializeField] private BoardManager boardManager` + `Start` null-guard + reroute; **MCP scene-wired** to the GameScene `Board` object + read-back verified.
  - `CardEffectManager` (`MonoBehaviour`, 2 hits) — **lane A** same as FocusManager; **registered** (newly added to `All`) + scene-wired + read-back verified. Harness `PowerGoldenTraceTests` updated to reflection-wire its `boardManager` (the new Assert caught the un-wired harness — the intended loud failure).
- **RECORD (genuinely un-wireable):**
  - `SelectionFlowService` (POCO `sealed class`, 2 hits) — no Unity lifecycle, cannot take a `[SerializeField]`; takes the board as a parameter from the calling state when POCO-ised → **Epic 11.2**.
  - `CardPickerManager` (UI leaf, 4 hits) — keeps the global (no new field added → no unwired-ref risk) → **Epic 12.2** (already §4a-recorded).

**Guard-#1 lock ADDED.** `"BoardManager.instance"` added to `ForbiddenLocators`. All registered consumers are now clean (the GameStates are lane-B/not registered; FocusManager + GameInfoRevealer + CardEffectManager registered + migrated). The two recorded survivors (SelectionFlowService POCO + CardPickerManager UI) are NOT registered, so guard #1 does not scan them. `typeof(BoardManager)` added to `InjectedManagerTypes` so guard #2 verifies the FocusManager + CardEffectManager scene wires (it passed → the wires are present).

**Addendum (process).** First pass took the minimal-risk route — migrated only the GameStates + GameManager and *recorded* the managers to avoid MCP scene-wiring. Poyo corrected this ([[feedback-serialized-field-rewiring]] reinforced): NEVER defer a wireable consumer to dodge scene-wiring — it produces an untraceable runtime NRE for him at playtest, and the skip-decision is forgotten by then. Completed the wiring (FocusManager + CardEffectManager lane-A MCP-wired + read-back; GameInfoRevealer lane-C) + the lock.

**Slice decision: inject CONCRETE `BoardManager`, NO `IBoardService` (D-NFR6, recorded).** The migrated-now consumers (GameStates) drive card-lifecycle/animation methods (`ShowAllPlayerCards`/`HideAllCards`/`visibleCards`/events) — presentation orchestration, no decision logic a mock would test. Mirrors 10.1/10.2. Extract a slice later if a board decision-logic test needs one (Epic 11.2 board-logic-to-POCO).

**Task 2 — root accessor + lane-B field.** `CompositionRoot` gains a `BoardManager` accessor on the instance + `Services` struct, forwarding to `global::BoardManager.instance` (BoardManager is global namespace; concrete, no scene-wiring/`InjectedManagerTypes`). `GameState` base gains `public BoardManager boardManager { get; set; }` alongside the existing pushed deps.

**Task 3 — lane-B migration + GameManager root.** `GameManager.SetupGameStates` pushes `clonedGameState.boardManager = CompositionRoot.For(NetworkManager).BoardManager;`. The 7 GameStates rerouted `BoardManager.instance.` → `boardManager.` (all uses are client-side card-lifecycle methods, after SetupGameStates push). `GameManager.OnPlayerDisconnectedRpc` resolves a local `CompositionRoot.For(NetworkManager).BoardManager` (the hub is not a registered DI consumer, has a NetworkManager). The managers/services/UI are recorded (Task 1), NOT migrated.

**Task 4 — static lock + gate (AC1, AC4).** `BoardManager.instance` annotated as the recorded-callers façade (`// dies in 12.3`); guard #1 gained `"BoardManager.instance"` and `typeof(BoardManager)` added to `InjectedManagerTypes` (guard #2 verifies the FocusManager + CardEffectManager scene wires — passed). §4d census table added. Two gate iterations: (1) a comment-substring false positive — `BoardManager` is registered in `All` since 7.2, so guard #1 source-scans BoardManager.cs *including comments*; my annotation literally contained "BoardManager.instance"/"CompositionRoot" → reworded (same trap fixed in FocusManager/CardEffectManager comments); (2) `PowerGoldenTraceTests` spawned CardEffectManager without wiring its new `boardManager` field → the new `Start` Assert fired loudly (the intended fail) → harness reflection-wires it like production. EM 162 / PM 148 green; despawn paths untouched (cards are local — no `NetworkObject.Despawn` in BoardManager).

### File List

**Modified (production):**
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `BoardManager` accessor on the instance + `Services` struct (→ `global::BoardManager.instance`).
- `Assets/Scripts/GameLogic/GameState.cs` — `public BoardManager boardManager { get; set; }` lane-B pushed field.
- `Assets/Scripts/GameLogic/GameManager.cs` — `SetupGameStates` pushes `boardManager` (from the root); `OnPlayerDisconnectedRpc` resolves the board through the root.
- `Assets/Scripts/GameLogic/GameStates/{VoteState,VoteRecapState,TakeDownThePortalState,GameIntroductionState,GameEndingState,ChainingState,AwakeningState}.cs` — `BoardManager.instance` → `boardManager` (lane-B inherited field).
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` — lane-C `boardManager` field + `OnNetworkSpawn` resolve + reroute.
- `Assets/Scripts/FocusSystem/FocusManager.cs` — lane-A `[SerializeField] boardManager` + `Start` null-guard + reroute.
- `Assets/Scripts/Board/CardEffects/CardEffectManager.cs` — lane-A `[SerializeField] boardManager` + `Start` null-guard + reroute.
- `Assets/Scripts/Board/BoardManager.cs` — `instance` recorded-callers-façade annotation (`// dies in 12.3`). No code/visibility change.

**Scene (GameScene.unity):** FocusManager.boardManager + CardEffectManager.boardManager wired to the `Board` GameObject (MCP `manage_components` set_property + read-back verified).

**Modified (tests):**
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` — `CardEffectManager` → `All`; `typeof(BoardManager)` → `InjectedManagerTypes`.
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` — `"BoardManager.instance"` → `ForbiddenLocators`.
- `Assets/Scripts/Tests/PlayMode/PowerGoldenTraceTests.cs` — reflection-wire `CardEffectManager.boardManager`.

**Docs:**
- `_bmad-output/refactor-architecture-despaghetti.md` — §4d BoardManager static census (+ stale-premise corrections).
- `_bmad-output/implementation-artifacts/deferred-work.md` — story-10.3 record.
- `_bmad-output/implementation-artifacts/10-3-inject-boardmanager.md` (this story); `sprint-status.yaml`.

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Injected BoardManager (fan-in 13) off the global into every reachable consumer: 7 game-loop GameStates (lane-B `GameState.boardManager` push from the root), GameManager (root), FocusManager + CardEffectManager (lane-A `[SerializeField]`, MCP scene-wired + read-back), GameInfoRevealer (lane-C `OnNetworkSpawn`). Concrete (no `IBoardService` — D-NFR6). Guard #1 locks `BoardManager.instance`; `instance` → recorded façade (`// dies 12.3`). Only genuinely un-wireable consumers recorded: SelectionFlowService (POCO → Epic 11.2) + CardPickerManager (UI → Epic 12.2). Stale AC premises corrected (zero own hops; despawn-NFR moot — cards local). §4d census. EM 162/162 + PM 148/148 (goldens unchanged; PowerGoldenTraceTests harness wire added). Initially partial (managers recorded) → completed the wiring per Poyo's never-defer-wiring rule. Status → review. |
