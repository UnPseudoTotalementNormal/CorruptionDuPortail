---
title: "Phase 2 — Unblock waiting states + victory re-check on leave"
status: draft
epic: epic-player-leave-stability
phase: 2
depends_on: [1]
---

# Phase 2 — Unblock waiting states + victory re-check on leave

## Goal
When `HandlePlayerLeft` chains a mid-game leaver, the active game state must stop waiting on that player, and victory must be re-evaluated immediately (so the last-anomaly-leaves → chosen-win edge resolves instantly). This is the network-critical heart of the epic — implement and test in isolation.

## Context (stall points, verified)
- `AwakeningState` — `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs`. A layer advances only when `currentlyAwakenedCharacters` drains (`OnCharacterAwakeningChanged:203-208` removes a character when it *sleeps*, then `GoToNextAwakeLayer` if empty). Chaining sets `isChained` but does **not** sleep → a chained-but-awakened leaver would keep the layer stuck. Layer timer (`:259`) and 0.00045/frame fake-skip (`:251`) are the only current rescues.
- `TakeDownThePortalState` — `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs`. Single-target flow to `mageCharacterOwnerId`. If the Mage leaves, click subscriptions never resolve → hang.
- `VoteState` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs`. Auto-close denominator counts `GetCharacters().Count(CanVote)`; post-vote tally `.Find`s the voted character.
- `VictoryConditionCheckState.OnStartStateServer` — `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:22-55`. Builds `GameSnapshotBuilder.FromLiveState`, runs `VictoryEvaluator.Evaluate`, then either `Loop.NextGameState()` (no winners) or `SetWinnersServer` + `Loop.SetGameState(typeof(GameEndingState))`.
- Win-condition that resolves the edge: `WChosenChainedAllAnomaly` (all anomalies chained → chosen win). Chaining the last anomaly should trigger it.

## Tasks
1. **Extract a reusable victory evaluation.** Refactor `VictoryConditionCheckState.OnStartStateServer` so the snapshot+evaluate+decide logic is a callable method, e.g. `bool TryResolveVictoryNow()` returning true and jumping to `GameEndingState` if there are winners, false otherwise. `OnStartStateServer` calls it (then `NextGameState` on false, preserving current flow). This lets `HandlePlayerLeft` request an out-of-band check without disrupting the loop when there is no winner.
2. **Wire the Phase-1 hook:** after chaining a mid-game leaver, `HandlePlayerLeft` calls `TryResolveVictoryNow()`. If it ends the game → done. If not → continue to state-unblock (step 3). Do **not** blindly `SetGameState(VictoryConditionCheckState)` from arbitrary states (would disrupt mid-state flow); the extracted method avoids that.
3. **Unblock `AwakeningState`.** Add a server hook the pipeline calls when a character is chained mid-awakening: if the leaver is in `currentlyAwakenedCharacters` (or is the actor the current layer waits on), remove it and re-run the layer-complete check (`if count == 0 → GoToNextAwakeLayer`). A chained character must not keep a layer open. (Chained characters are already skipped when a layer *starts*, `:91` — this handles the mid-layer case.)
4. **Unblock `TakeDownThePortalState`.** If the leaver is `mageCharacterOwnerId` (or the currently-awaited actor), abort/complete the state cleanly (advance the loop) instead of hanging. Guard the single-target RPC path against a missing character.
5. **Harden `VoteState`.** Recompute the eligible-voter denominator after a leave (chained characters are ineligible via `CanVote`), so an in-progress vote can still auto-close; null-guard the post-vote `.Find` so a chained/absent voted character does not NRE the tally.
6. **Null-guard `ChainingState`** resolution (`GetCharacter` may return null for an already-removed id) so the animation loop never NREs.
7. Tag diagnostics `[LEAVE]`.

## Acceptance Criteria
- **AC1 (last anomaly)** — Given a mid-game state where the leaver is the last remaining un-chained anomaly, When they leave, Then they are chained and `TryResolveVictoryNow()` ends the game with the chosen (élus) as winners **immediately** (no waiting for the next scheduled victory check).
- **AC2 (awakening)** — Given `AwakeningState` is waiting on an awakened actor, When that actor leaves, Then their chain removes them from `currentlyAwakenedCharacters` and the layer advances (no hang; night progresses within a bounded frame budget without relying on the layer timeout).
- **AC3 (mage/portal)** — Given `TakeDownThePortalState` is waiting on the Mage, When the Mage leaves, Then the state completes/advances cleanly with no hang and no NRE.
- **AC4 (vote)** — Given a vote is open, When a not-yet-voted eligible player leaves, Then the denominator updates so the vote can still auto-close, and the tally does not NRE on the absent player.
- **AC5 (no-winner)** — Given a leaver whose chaining does NOT complete any win-condition, When `HandlePlayerLeft` runs, Then the game does NOT jump to `GameEndingState` and the current state continues normally.

## Out of scope
Client-side host-drop (Phase 3). Guardrails/timeouts (Phase 4).

## Verification
`read_console` clean; `run_tests` PlayMode green. New tests for AC1–AC5 land in Phase 5 but at least AC1 (last-anomaly) and AC2 (awakening) should have a smoke test here to prove the unblock.
