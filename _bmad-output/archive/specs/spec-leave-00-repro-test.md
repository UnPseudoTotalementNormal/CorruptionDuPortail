---
title: "Phase 0 — Repro test: real client drops mid-game"
status: draft
epic: epic-player-leave-stability
phase: 0
depends_on: []
---

# Phase 0 — Repro test: real client drops mid-game

## Goal
Before any fix, add a failing/characterization PlayMode test on the 2-NetworkManager loopback substrate that drops a real client **mid-game** and observes what the game does today. This is the safety net Phases 1–2 are graded against. No production code changes.

## Context
- Substrate: `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs` (host + real in-process client + simulated bot), `CoexistenceGateTests.cs` (dual `NetworkManager` + `UnityTransport` loopback, `StartHost`+`StartClient`).
- Helper: `Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs` — bounded spawn waits (`WaitUntilSpawnedOrTimeout`), `RegisterCompositionRoot`, poll-frames (never `WaitForSeconds`).
- Today's mid-game disconnect handler: `GameManager.OnPlayerDisconnectedServer` (fakifies). No test currently drops a real client mid-game.

## Tasks
1. Add `Assets/Scripts/Tests/PlayMode/Desingleton/PlayerLeaveMidGameTests.cs` in assembly `Tests.PlayMode`.
2. Build a fixture flow that: starts host + one real client, spawns their Characters, advances the game past `LobbyState` into an active in-game state (at minimum `RoleAttributionState` completed so roles exist; ideally into `AwakeningState`).
3. Disconnect the real client mid-game via the substrate (client `Shutdown()` on the second NetworkManager — the same disconnect path `CoexistenceGateTests` sequences around), then poll-wait for the server to process `OnClientDisconnectCallback`.
4. Assert (characterization — record actual current behavior, mark expected-future in comments):
   - The leaver's Character `ownerClientId` after disconnect (today: `FAKE_CLIENT_ID`).
   - Whether the current game state advances or stalls within a bounded frame budget.
   - No unhandled NRE on the server (guard: fail the test if the console logs an error).
5. Add a `[TAG]` `[LEAVE]` prefix on any diagnostic `Debug.Log` used, so `read_console` can be filtered.
6. Follow the fixture teardown discipline (despawn host GameManager/CharacterManager first; assert `NetworkManager.Singleton == null` after both `Shutdown()`).

## Acceptance Criteria
- **AC1** — Given host + one real client past the lobby with roles assigned, When the real client disconnects, Then the test observes the server processing the disconnect within a bounded frame budget (no infinite wait) and the assertions above run.
- **AC2** — Given the disconnect is processed, When the console is inspected, Then the test fails if any server-side error/exception was logged (documents the current NRE hazard if present).
- **AC3** — The test is deterministic and order-independent, uses poll-frames (no `WaitForSeconds`), and its `[TearDown]` leaves `NetworkManager.Singleton == null`.

## Out of scope
No production code changes. No fix. Purely observational + net.

## Verification
`mcp__UnityMCP__run_tests` (filter PlayMode / this class) → test compiles and runs; `read_console` clean of compile errors. The test may FAIL/record undefined behavior — that is the expected Phase-0 outcome and the Phase-1/2 target.
