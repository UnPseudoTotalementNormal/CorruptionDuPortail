---
title: "Epic — Player-Leave Stability Hardening"
status: draft
type: epic
owner: Poyo
created: 2026-07-08
branch: feat/player-leave-stability
communication_language: Français
document_output_language: English
scope_decision: "A — all phases at once (single branch, phased delivery, test gate between phases)"
---

# Epic — Player-Leave Stability Hardening

## 1. Problem

The game has almost no safety around a player leaving a game or lobby. Disconnect handling exists but is scattered, uncoordinated, and does not unblock the game states that wait on a specific player. A single leave can silently break or stall a match. This epic makes leaving safe: **the game never breaks or hangs when a player disconnects.**

Reconnection (rejoining after leaving) is **explicitly OUT OF SCOPE** (future chantier).

## 2. Current state (investigation — 5 parallel Explore agents, 2026-07-08)

### 2.1 Disconnect handling is scattered and uncoordinated
Four managers each react independently to the same `NetworkManager.OnClientDisconnectCallback`, all server-gated (`if (IsServer)`), with no defined order:

| Manager | File:line | Reaction on disconnect |
|---|---|---|
| `GameManager.OnPlayerDisconnectedServer` | `Assets/Scripts/GameLogic/GameManager.cs:554` | **Fakifies** the seat: `ownerClientId.Value = FAKE_CLIENT_ID` + `AskForUpdateAllCharactersRpc` + destroys one fake card (`OnPlayerDisconnectedRpc:565`). |
| `LobbyState.OnClientDisconnected` | `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:25` | **Removes** the character (`Command.RemoveCharacter`). Subscribed in `OnStateCreated:64`, **NEVER unsubscribed** → also fires mid-game, overlapping GameManager. |
| `LobbyPlayerInfoHolder.OnClientDisconnected` | `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:74` | Removes the `playerInfos` entry. |
| `AvatarManager.OnClientDisconnected` | `Assets/Scripts/Avatars/AvatarManager.cs:191` | Despawns the avatar. |

Two contradictory policies (remove vs fakify) fire on the same event. `GameManager` unsubscribes cleanly in `OnNetworkDespawn:176`; `LobbyState` never does (subscription leak).

### 2.2 Three confirmed gaps (grep-verified, zero hits)
1. **Host crash / Alt-F4 → client stranded.** Every disconnect callback is server-only. No client-side `OnClientStopped` / `OnTransportFailure` / `OnServerStopped`. If the host vanishes, clients are stuck in `GameScene` with a dead `NetworkManager`, no return-to-menu, no notification. `GameSceneOnlineChecker` only checks at `Start`.
2. **No in-game "leave to menu" button for a non-host client.** A client can only Alt-F4 (OS quit → `LobbyManager.OnApplicationQuit` leaves the cloud lobby).
3. **No network UI at all.** No "player disconnected" popup, no "waiting", no reconnection prompt. `LobbyManager.OnLobbyError` strings are produced but never displayed.

Plus: no timeouts on connection establishment or on waiting for a player; session teardown is implicit (scene unload + NGO despawn), no explicit CompositionRoot/static reset on leave; domain reload is disabled, so an abrupt teardown that skips `OnDestroy` can strand stale statics.

### 2.3 Where a vanished player breaks the game
Fakifying the seat does NOT unblock states waiting on a specific `ownerClientId`:

| State | File | Stall / break |
|---|---|---|
| `AwakeningState` | `GameLogic/GameStates/AwakeningState.cs` | An awakened actor that never sleeps blocks its layer. `currentlyAwakenedCharacters` never drains → night hangs (only the layer timer `:259` and the 0.00045/frame fake-skip `:251` rescue it). |
| `TakeDownThePortalState` | `GameLogic/GameStates/TakeDownThePortalState.cs` | Single-target RPC flow to `mageCharacterOwnerId`. If the Mage leaves, the state hangs. |
| `VoteState` | `GameLogic/GameStates/VoteState.cs` | Auto-close denominator counts `GetCharacters().Count(CanVote)`; post-vote tally `.Find`s the voted character (assumes it still exists). |
| `ChainingState` | `GameLogic/GameStates/ChainingState.cs` | Resolves each id via `GetCharacter`; a missing character yields a null and can NRE the animation. |
| `VictoryConditionCheckState` | `GameLogic/GameStates/VictoryConditionCheckState.cs` | Snapshot over `GetCharacters(false)`; a removed character silently changes vacuous-truth outcomes. |

### 2.4 No test simulates a real mid-game disconnect
Disconnect appears in tests only as a **teardown hazard** the fixtures deliberately sequence around (`CoexistenceGateTests` despawns the host GameManager first so `OnPlayerDisconnectedServer` won't NRE on the absent BoardManager). The 2-NetworkManager loopback substrate exists (`Tests/PlayMode/Desingleton/MultiClientGameFixture.cs`, `CoexistenceGateTests.cs`) and `AvatarSpawnTests` exercises `DespawnAvatar`, but nothing drops a real client mid-game to assert game-state behavior.

## 3. Owner-ratified policy (Poyo, 2026-07-08)

- A player who leaves **mid-game** → their character is **instantly CHAINED** (`ChainingManager.ChainCharacterRpc` path — sets `isChained`, reveals role, handles the portal/Mage special case), **WITHOUT the `ChainingState` card animation** (instant). NOT `isEliminated` (`isEliminated` is near-deprecated — do not build on it).
- Chaining is the unifying mechanism because win-conditions already key off it (`WChosenChainedAllAnomaly` = all anomalies chained → chosen/élus win). Chaining a leaver flows into the existing victory evaluation.
- **Edge case:** if the leaver was the **last anomaly**, the chosen (élus) must win **instantly** → re-run victory evaluation right after chaining.
- A player who leaves **in the lobby** (before game start) → character is **removed** (current behavior, conceptually kept).
- Reconnection → out of scope.

## 4. Target architecture

**One authoritative server-side entry point: `HandlePlayerLeft(ulong clientId)`.**
- Owned by the server (host). Replaces the four independent callback reactions with one ordered pipeline.
- Branches on game phase: **in lobby** (before `hasGameStarted` / while `LobbyState` is current) → remove character; **mid-game** → chain instantly + unblock waiting state + re-evaluate victory.
- Fans out the ancillary cleanup in a defined order: player-info removal, avatar despawn, board card cleanup.
- Bots (`clientId >= 100`) never open a transport connection, so they never trigger the callback — the pipeline covers real clients only (documented, not a gap).

Client-side: a separate `OnClientStopped` / transport-failure handler detects host loss and returns the local client to the menu with a notification.

## 5. Stories (phases) — dependency order

| # | Spec file | Goal | Depends on |
|---|---|---|---|
| 0 | `spec-leave-00-repro-test.md` | Failing PlayMode test: real client drops mid-game, assert current broken/undefined behavior. Repro net BEFORE any fix. | — |
| 1 | `spec-leave-01-unified-pipeline.md` | Single `HandlePlayerLeft` server pipeline. Fix `LobbyState` subscription leak. Lobby→remove, mid-game→instant chain (no anim). Ordered ancillary cleanup. | 0 |
| 2 | `spec-leave-02-unblock-states.md` | Unblock waiting states on chain (Awakening / TakeDownThePortal / Vote / Chaining) + reusable victory re-check + last-anomaly instant win. | 1 |
| 3 | `spec-leave-03-client-resilience.md` | Client-side host-drop detection → return to menu + notification. Non-host "leave to menu" button. Minimal disconnect UI (wire existing `OnLobbyError`). | 1 |
| 4 | `spec-leave-04-guardrails.md` | Min-players gate, connection/wait timeouts, explicit static/CompositionRoot reset on leave. | 1 |
| 5 | `spec-leave-05-tests.md` | Test coverage for phases 1–4 (mid-game chain, last-anomaly win, host-drop, lobby-leave, min-players). | 1,2,3,4 |

## 6. Definition of Done (epic)

- A real client dropping at any game phase never hangs or NREs the match; behavior matches §3 policy.
- Host loss returns every client to the menu with a notification (no stranding).
- All new/changed logic covered by PlayMode tests on the 2-NM loopback substrate.
- `mcp__UnityMCP__read_console` clean (no compile errors/warnings) after each phase.
- `mcp__UnityMCP__run_tests` green (EditMode + PlayMode) after each phase.
- No new logic keyed on `isEliminated`.

## 7. Working agreement

- Single branch `feat/player-leave-stability`, phased delivery. Test gate (`run_tests` + `read_console`) between phases.
- Network-critical states (Phase 2) implemented and tested in isolation before stacking Phase 3/4.
- No commit/push without Poyo's explicit per-change OK.
- Commits: English, conventional, body with `UX:` line where player-facing.

## 8. Investigation source references
GameManager `:154-222,539-572`; LobbyState `:25-77`; ChainingManager `:62-104`; ChainingState `:27-69`; AwakeningState `:182-283`; VictoryConditionCheckState `:22-55`; LobbyPlayerInfoHolder `:50-88`; AvatarManager `:125-191`; MainMenu `:70-408`; LobbyManager `:53-425`; GameSceneOnlineChecker `:13-21`. Tests: `Tests/PlayMode/Desingleton/*`, `Tests/PlayMode/Avatars/AvatarSpawnTests.cs`, `Tests/PlayMode/NetworkTestHelper.cs`.
