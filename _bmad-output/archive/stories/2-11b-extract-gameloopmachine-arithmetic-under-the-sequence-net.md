# Story 2.11b: Extract `GameLoopMachine` arithmetic under the sequence net (2-phase; remainder HELD to Epic 5)

Status: done

## Story

As a developer (Poyo),
I want only the `GameLoopMachine` advance/rewind arithmetic extracted as a POCO, leaving the NetworkVariable ownership in the adapter,
so that the easy math becomes EditMode-testable without opening the network-critical index ownership (HELD to Epic 5).

## Acceptance Criteria

**Given** the sequence golden of 2.11a is green
**When** the arithmetic (`NextGameState` / `PreviousGameState` / `SwitchGameState` index math, day-pass / first-loop detection) is extracted into `Domain`
**Then** `ignoreGameLoop` becomes a call parameter / internal state, not a global mutable field **in the POCO**
**And** the adapter integration preserves the `OnEnd → write currentGameStateIndex.Value → OnStart` ordering, re-verified green against the 2.11a journal
**And** the POCO returns a decision; the adapter performs the write and fires events (NFR4)
**And** this is explicitly the 2-phase split: full index ownership + reflection RPC dispatch remain HELD to Epic 5

## Acceptance reading notes (binding)

1. **What moves (pure arithmetic only):**
   - `NextGameState` index math (`GameManager.cs:156-173`): `+1` wrap-at-count; the **day-pass** branch (`wasInGameLoop && next-not-in-loop && !ignoreGameLoop && !ignoreGameLoopThisCall` → jump to the **first** in-loop index, raise `fireNewDayPassed`); the **first-loop** detection (entering an in-loop state while `!gameHasStartedFirstLoop` → raise `fireGameStarted`, set `GameHasStartedFirstLoop`).
   - `PreviousGameState` index math (`GameManager.cs:182-192`): `-1` wrap-at-0; the **reverse day-pass** branch (`wasInGameLoop && prev-not-in-loop && !ignoreGameLoop` → jump to the **last** in-loop index). **No** event raise, **no** first-loop on rewind (mirrors current).
2. **What does NOT move (HELD to Epic 5):** `SwitchGameState` itself — the `OnEndStateServer` → `OnEndStateClient` RPC → `currentGameStateIndex.Value` write → `OnStartStateServer` → `OnStartStateClient` RPC sequence, the NetworkVariable index ownership, and the reflection RPC dispatch. `SwitchGameState` is called UNCHANGED with the POCO's computed index. The 2.11a golden re-verifies the order is byte-identical.
3. **POCO** `GameLoopMachine` (pure Domain, decision-only NFR4):
   - `GameLoopTransition Advance(int currentIndex, IReadOnlyList<bool> isInGameLoop, bool gameHasStartedFirstLoop, bool ignoreGameLoop, bool ignoreGameLoopThisCall)` — returns `{ NewIndex, FireNewDayPassed, FireGameStarted, GameHasStartedFirstLoop }`.
   - `int Rewind(int currentIndex, IReadOnlyList<bool> isInGameLoop, bool ignoreGameLoop)` — returns the new index only.
   - No NGO, no `NetworkVariable`, no event invoke inside the POCO — it returns a decision the adapter applies.
   - **`ignoreGameLoop` is a parameter, not a Domain field** (the AC's "no global mutable field" — satisfied *in the Domain*). The `GameManager.ignoreGameLoop` serialized scene field + the test knob STAY (out of scope: scene asset + 20+ test setups depend on it); the adapter forwards it per call.
   - Edge: when no state has `isInGameLoop == true`, `First/LastInGameLoopIndex` return `-1` exactly like the current `FindIndex`/`FindLastIndex` (→ the existing `SwitchGameState` assert fires identically). Preserve as-is.
4. **Adapter** (`GameManager.NextGameState` / `PreviousGameState`): build the `isInGameLoop` list from `gameStates` in dictionary order (same order as `GetGameState(index)` = `gameStates.Keys.ElementAt(index)`), call the POCO, then apply in the SAME order as today: `onNewDayPassed?.Invoke()` (if `FireNewDayPassed`) → set `gameHasStartedFirstLoop` → `onGameStarted?.Invoke()` (if `FireGameStarted`) → `SwitchGameState(NewIndex)`. (`onNewDayPassed`'s `gameLoopCount++` subscriber does not touch `gameHasStartedFirstLoop`, so the pre-invoke read the POCO uses is faithful.)
5. **No behavior change (NFR1).** The 2.11a `GameLoopOrdering` golden + all victory/vote PlayMode goldens stay green unchanged.

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (`run_tests` GameLoopOrdering + regression; console clean)
- [ ] **T1 — `GameLoopMachine` + `GameLoopTransition` in Domain** (notes 1, 3); `DomainPurity` green
- [ ] **T2 — Re-point `GameManager.NextGameState` / `PreviousGameState`** to the POCO (note 4); `SwitchGameState` untouched; `read_console` clean
- [ ] **T3 — EditMode `GameLoopMachineTests`** `[Category("GameLoopMachine")]`: advance +1 / wrap / day-pass / day-pass suppressed (call param + global) / first-loop fire / first-loop already-started; rewind -1 / wrap / reverse day-pass / suppressed
- [ ] **T4 — Prove**: `run_tests category: GameLoopMachine` (EditMode) + `GameLoopOrdering` (PlayMode, re-verify 2.11a journal) + full regression green; console clean
- [ ] **T5 — `/gds-code-review`** (`# REVIEW-REQUIRED`) before merge; triage + fix actionable

## Dev Notes

**Created:** `Assets/Scripts/Domain/GameLoopMachine.cs`; `Assets/Scripts/Tests/Editor/GameLoopMachineTests.cs`.
**Modified:** `Assets/Scripts/GameLogic/GameManager.cs` (`NextGameState` / `PreviousGameState` delegate the arithmetic).
**Must NOT change:** `SwitchGameState`, the RPC dispatch, the NetworkVariable index ownership, the `GameManager.ignoreGameLoop` field, the 2.11a golden.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.11b] (lines 366–379)
- [Source: Assets/Scripts/GameLogic/GameManager.cs:152-209] — the arithmetic to extract + the HELD `SwitchGameState`
- [Source: Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/GameLoopTransitionOrderingTests.cs] — the 2.11a sequence net
- [Source: Assets/Scripts/Domain/ChainingResolver.cs] — the decision-only Domain POCO pattern (2.10)

### Previous story intelligence

- 2.11a pinned the host-local order `EndServer → EndClient → IndexChanged → StartServer → StartClient` (host dispatches its own ClientsAndHost RPC synchronously). This story keeps `SwitchGameState` byte-identical so that order holds.
- Tests.Editor already references `CorruptionDuPortail.Domain` — no asmdef edit needed.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

_(to fill)_

### File List

_(to fill)_

### Change Log

- 2026-06-10 — Story 2.11b drafted; extract `GameLoopMachine` advance/rewind arithmetic into Domain, adapter keeps NV ownership + transition order. `# REVIEW-REQUIRED`.
