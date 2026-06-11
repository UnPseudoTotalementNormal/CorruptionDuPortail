# Story 2.11a: Characterize the `GameLoopMachine` state-transition effect ordering (PlayMode golden, before refactor)

Status: review

## Story

As a developer (Poyo),
I want the load-bearing transition ordering pinned as a PlayMode golden on the *current* code before any extraction,
so that the 2.11b extraction has a faithful sequence net rather than one written alongside the change it guards.

## Acceptance Criteria

**Given** client listeners react to `currentGameStateIndex.OnValueChanged` directly (`BoardCameraManager.cs:53`, `RoomFog.cs:37`, `LightManager.cs:14`)
**When** the characterization test is authored against the current `GameManager`
**Then** it captures an **ordered event journal** asserting the relative order of `OnEndState*`, the `currentGameStateIndex.Value` write (`OnValueChanged`), and `OnStartState*` across a real `SwitchGameState` on the `StartHost` harness — asserting total order, not just index values
**And** this golden is green on the current (pre-refactor) code

### Acceptance reading notes (binding)

1. **The transition-as-it-exists-today** is `GameManager.SwitchGameState:196-209` (server-only):
   ```
   _oldGameState.OnEndStateServer();                 // (a) direct, sync
   DoStateMethodRpc(_old, OnEndStateClient, clients);// (b) RPC → ClientsAndHost
   currentGameStateIndex.Value = newGameStateIndex;  // (c) write → fires OnValueChanged
   _newGameState.OnStartStateServer();               // (d) direct, sync
   DoStateMethodRpc(_new, OnStartStateClient, clients);//(e) RPC → ClientsAndHost
   ```
   The `*Client` halves go through the reflection RPC dispatch (`DoStateMethodRpc` → `CallStateMethodRpc`, `SendTo.SpecifiedInParams` → `RpcTarget.ClientsAndHost`). On the `StartHost` harness host==server, so the host receives its own client RPC. **The host-local ordering of (b)/(e) relative to (a)/(c)/(d) is whatever NGO actually does — the journal pins it as discovered, not as assumed.** (Characterization: capture reality, even if it diverges from the conceptual "OnEnd drained before the write" mental model. The divergence, if any, is a technical NGO-dispatch fact, not a game-design choice.)
2. **`OnValueChanged` is a first-class journal event.** The test subscribes to `currentGameStateIndex.OnValueChanged` and records `(previous→current)` in the same journal, so the write's position relative to the four state callbacks is pinned (this is the listener seam `BoardCameraManager`/`RoomFog`/`LightManager` depend on).
3. **Two DISTINCT recording state types** (`_RecA`, `_RecB`), not two instances of one type: `CallStateMethodRpc` resolves the target state by `GetType().FullName` (`GameManager.cs:308`), so two instances of the same type would both dispatch to the first instance and corrupt the journal. Each type overrides the four `OnStart/EndState{Server,Client}` and appends to a shared static journal, calling `base` (preserves the event-fire + `stateUI` semantics).
4. **The spawn-time index-0 start is not under test.** `OnNetworkSpawn` calls `OnStartStateServer()`/`OnStartStateClient()` at index 0 directly (not via RPC). The journal is cleared *after* setup settles, immediately before the `NextGameState`/`SwitchGameState` under test, so it captures only the transition.
5. **Decision-only / no behavior change (NFR1).** This story adds **only a test**. No production file changes. The golden must be green on current code; it is the oracle 2.11b re-verifies green after extraction.
6. **Scope: a single N→N+1 `SwitchGameState`** with `ignoreGameLoop = true` (no day-pass / first-loop branch — those arithmetic branches are 2.11b's concern). The journal pins the *effect ordering* of one transition; 2.11b pins the *arithmetic*.

## Tasks / Subtasks

- [ ] **T0 — Baseline green** (`run_tests` regression categories + console clean)
- [ ] **T1 — `GameLoopTransitionOrderingTests`** PlayMode, `[Category("GameLoopOrdering")]`: StartHost + spawned `GameManager` NetworkObject; two distinct `_RecA`/`_RecB` states added pre-spawn; subscribe `currentGameStateIndex.OnValueChanged`; clear journal post-setup
- [ ] **T2 — Discovery run**: log the ordered journal (`[LOOPORDER]` tag), `run_tests category: GameLoopOrdering`, read the observed total order from console
- [ ] **T3 — Pin the observed order** as explicit sequential asserts (total order), with the `OnValueChanged(0→1)` position fixed relative to the four callbacks; green on current code
- [ ] **T4 — Prove**: `run_tests category: GameLoopOrdering` + full regression green; console clean

## Dev Notes

**Created:** `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/GameLoopTransitionOrderingTests.cs`.
**Modified:** none (test-only — NFR1).
**Must NOT change:** `GameManager.SwitchGameState` / `NextGameState` / the RPC dispatch.

### Harness shape (mirrors `WinningConditionGoldenMasterTests` SetUp)

- StartHost; `GameManager` GO + `NetworkObject`, `ignoreGameLoop = true`; add `_RecA`, `_RecB` (+ settings) to `gameStates` **before** `Spawn()` so `SetupGameStates` clones them and assigns `gameManager`.
- After spawn settles, fetch the cloned instances back from `gameManager.gameStates.Keys` (the dict holds the clones, not the originals), subscribe nothing extra (the overrides self-record), and subscribe `currentGameStateIndex.OnValueChanged`.
- Clear the static journal, then call `_gameManager.NextGameState()` on the server, yield a few frames to drain the deferred client RPCs, then assert.
- TearDown: reset the `GameManager`/`CharacterManager` static `instance` (statics survive PlayMode sessions — see golden TearDown).

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.11a] (lines 353–365)
- [Source: Assets/Scripts/GameLogic/GameManager.cs:196-209] — `SwitchGameState` (the ordering under characterization)
- [Source: Assets/Scripts/GameLogic/GameManager.cs:304-312] — `CallStateMethodRpc` reflection dispatch (resolves by `GetType().FullName`)
- [Source: Assets/Scripts/Tests/PlayMode/Characters/WinningConditionGoldenMasterTests.cs] — the faithful StartHost harness this mirrors
- [Source: Assets/Scripts/Board/BoardCameraSystem/BoardCameraManager.cs:53, Assets/Scripts/FX/RoomFog.cs:37, Assets/Scripts/Board/LightManager.cs:14] — the `OnValueChanged` listener seam

### Previous story intelligence

- 2.8–2.10 established extract-to-Domain decision-only POCOs. 2.11a is **pure characterization** (test-only) before the 2.11b 2-phase extraction. Full index ownership + RPC dispatch stay HELD to Epic 5.
- The golden TearDown must null the static `instance` fields (domain reload may be off across PlayMode sessions).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8

### Completion Notes List

- **Discovered host-local total order** (pinned as the golden) for a single N→N+1 `SwitchGameState` on the StartHost harness:
  `EndServer:A | EndClient:A | IndexChanged:0→1 | StartServer:B | StartClient:B`.
- **Key finding:** the host dispatches its own `ClientsAndHost` RPC **synchronously** — `OnEndStateClient` runs immediately after `OnEndStateServer` and **before** the `currentGameStateIndex.Value` write; likewise `OnStartStateClient` runs synchronously right after `OnStartStateServer`. This matches the conceptual "OnEnd drained → write → OnStart" model (no deferral on the host path). The journal pins it as discovered, so 2.11b's adapter must reproduce this exact sequence.
- The `currentGameStateIndex.Value` write (its `OnValueChanged`) sits strictly between the two OnEnd halves and the two OnStart halves — the listener seam (`BoardCameraManager`/`RoomFog`/`LightManager`) reads the new index only after the old state has fully ended.
- **FixedString64Bytes constraint surfaced:** `DoStateMethodRpc` serializes the state's `GetType().FullName` into a `FixedString64Bytes`; a nested test type name overflowed 64 bytes (`...OrderingTests+_RecA`). Recording states are therefore **top-level** (`RecStateA`/`RecStateB`, FullName 45 bytes) and **distinct types** (the RPC resolves the target by FullName, so two instances of one type would corrupt the journal).
- Test-only (NFR1): no production file touched. Green on current pre-refactor code; full PlayMode suite 133/133.

### File List

- **Added:** `Assets/Scripts/Tests/PlayMode/GameLogic/GameStates/GameLoopTransitionOrderingTests.cs`

### Change Log

- 2026-06-10 — Story 2.11a drafted; characterize `GameLoopMachine` transition effect ordering as a PlayMode golden before the 2.11b extraction.
- 2026-06-10 — Pinned the host-local transition total order as `[Category("GameLoopOrdering")]` golden; green on current code (133/133 PlayMode). Story 2.11a → review.
