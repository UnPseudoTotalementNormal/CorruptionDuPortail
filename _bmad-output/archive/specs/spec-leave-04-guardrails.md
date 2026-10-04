---
title: "Phase 4 — Guardrails: min-players, timeouts, clean session reset"
status: draft
epic: epic-player-leave-stability
phase: 4
depends_on: [1]
---

# Phase 4 — Guardrails: min-players, timeouts, clean session reset

## Goal
Add the missing preventive guards so the game does not enter unstartable/undefined states and does not carry stale state across sessions.

## Context (current gaps)
- `LobbyState.OnStartGameButtonPressed` (`Assets/Scripts/GameLogic/GameStates/LobbyState.cs:30-49`) has only a **maximum** guard (`playerCount > totalRolesToAttribute → warn + return`). **No minimum-players gate.**
- No timeout on connection establishment: `StartClient`/`StartHost` return bools checked synchronously (`MainMenu.cs`), no wait-with-timeout for the connection to actually complete or for expected players to arrive.
- `GameManager.ShutOffGame` (`:545`) uses `UniTask.WaitForSeconds(1)` with no `CancellationToken`.
- Session teardown is implicit (scene unload + NGO despawn). No explicit CompositionRoot/static reset on leave; domain reload disabled → an abrupt teardown skipping `OnDestroy` can strand stale statics until the next scene reload / `SubsystemRegistration` backstop.

## Tasks
1. **Minimum-players gate.** In `LobbyState.OnStartGameButtonPressed`, add a minimum-player check before `Loop.NextGameState()`. Source the minimum from the same replicated `gameSettingsManager` used for the max (add an accessor if none exists), or a sensible authored default. Block start + warn (and surface via UI if Phase 3 landed) when below minimum. **Confirm the minimum value with Poyo** (design-owned) before hardcoding.
2. **Connection timeout.** Wrap client connect (`MainMenu.JoinWithFacepunch`/`JoinWithUnityRelay`) with a bounded wait for the connection to actually establish; on timeout, run the existing error-teardown (`Shutdown` + `LeaveLobby`) and surface the failure. Use UniTask with a `CancellationToken` (never `System.Threading.Tasks.Task`).
3. **Cancellable shutdown wait.** Give `GameManager.ShutOffGame` a `CancellationToken` so a session tearing down does not leave a dangling 1s delay racing scene load.
4. **Explicit session reset on leave.** On leaving/returning to menu, add an explicit reset of the per-NM CompositionRoot / static registries (call the existing reset path used by the `SubsystemRegistration` backstop, or add one) rather than relying solely on `OnDestroy` value-scans. Ensures no stale `instance`/registry entry survives an abrupt teardown into the next session.
5. Tag diagnostics `[LEAVE]`.

## Acceptance Criteria
- **AC1 (min-players)** — Given fewer than the minimum players in lobby, When the host presses start, Then the game does NOT start, and a warning/notification explains why.
- **AC2 (min-players ok)** — Given players between minimum and maximum, When the host presses start, Then the game starts normally (no regression to the existing max guard).
- **AC3 (connect timeout)** — Given a client connect that never completes, When the timeout elapses, Then NGO is shut down, the cloud lobby is left, and the failure is surfaced — no indefinite hang, no stale connecting state.
- **AC4 (clean reset)** — Given a client returns to menu (graceful or abrupt), When teardown completes, Then no stale static `instance` or per-NM registry entry from the previous session survives into a new host/join (verified by a start-fresh check).

## Open decision (Poyo)
Minimum-players value (design-owned). Default proposal until confirmed: block start below the number of mandatory roles, or a fixed floor — TBD with Poyo.

## Out of scope
Reconnection. Mid-game state logic (Phase 2).

## Verification
`read_console` clean; `run_tests` green. EditMode test for the min-players gate; loopback check for clean reset across a second session.
