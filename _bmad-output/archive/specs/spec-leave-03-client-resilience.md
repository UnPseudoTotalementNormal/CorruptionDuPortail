---
title: "Phase 3 — Client resilience: host-drop detection, leave button, disconnect UI"
status: draft
epic: epic-player-leave-stability
phase: 3
depends_on: [1]
---

# Phase 3 — Client resilience: host-drop detection, leave button, disconnect UI

## Goal
Make the client survive losing the host and give a non-host client a clean way to leave. Add minimal user-facing feedback. Today: if the host vanishes, clients are stranded in `GameScene` with a dead `NetworkManager`, no notification, no return path.

## Context (current gaps, grep-verified zero hits)
- All disconnect callbacks are server-only (`if (IsServer)`); no client-side `OnClientStopped` / `OnTransportFailure` / `OnServerStopped` anywhere.
- Graceful host end exists: `GameManager.ShutOffGameRpc` → `ShutOffGame` (`GameManager.cs:539-550`) shuts down + loads scene 1 on everyone. Only the host can fire it (`ShutOffGameButton` disabled for non-servers).
- `GameSceneOnlineChecker.Start` (`Assets/Scripts/GameSceneOnlineChecker.cs:13-21`) reloads scene 0 if not connected — but only at scene `Start`, not continuously.
- `LobbyManager.OnLobbyError` produces strings (e.g. "Connexion au lobby perdue", `LobbyManager.cs:370,375,416`) that no one displays.
- `LobbyManager.LeaveLobby` (`:290`) exists; `OnApplicationQuit` (`:53`) fires it on OS quit.
- No non-host in-game leave button. No disconnect popup / waiting / status UI.

## Tasks
1. **Client-side host-drop detection.** Subscribe (client, non-host) to `NetworkManager.OnClientStopped` and/or `OnTransportFailure` in a persistent-enough scope (a component living in `GameScene` or on a DDoL bootstrap). On unexpected disconnect (host lost, not a graceful `ShutOffGame`): shut down NGO cleanly and return the local client to the menu (scene 1), then surface a notification. Distinguish graceful (`ShutOffGame` already handles it) from abrupt so the notification only shows on unexpected loss.
2. **Non-host "leave to menu" button.** Add an in-game control for a non-host client to leave: local `NetworkManager.Shutdown()` + `LobbyManager.LeaveLobby()` + load scene 1. Mirror the existing MainMenu error-teardown sequence. (Host keeps `ShutOffGameButton`.)
3. **Minimal disconnect UI.** A lightweight notification surface that shows on: host loss ("Connexion à l'hôte perdue"), and (optional) another player leaving. Wire the already-produced `LobbyManager.OnLobbyError` strings into it so they are finally displayed. Keep it minimal — placeholder visuals per project UI status (design-owned palette; do not enshrine).
4. **Continuous online guard (optional hardening).** Consider extending `GameSceneOnlineChecker` beyond `Start` (e.g. react to disconnect) so a client that loses the connection is not stranded even if step 1's callback path misses.
5. Tag diagnostics `[LEAVE]`.

## Acceptance Criteria
- **AC1 (host drop)** — Given a non-host client in `GameScene`, When the host disconnects abruptly (crash/Alt-F4, no `ShutOffGameRpc`), Then the client returns to the menu (scene 1) within a bounded time and sees a "host connection lost" notification, with no stranded dead-`NetworkManager` state.
- **AC2 (graceful end unchanged)** — Given the host presses `ShutOffGameButton`, When `ShutOffGame` runs, Then all clients return to menu as before, and the abrupt-loss notification does NOT show (graceful path distinguished).
- **AC3 (non-host leave)** — Given a non-host client in-game, When they press the leave button, Then they cleanly shut down NGO, leave the cloud lobby, and load the menu; server-side `HandlePlayerLeft` (Phase 1) processes their departure per policy.
- **AC4 (UI)** — Given a disconnect notification is raised, When it fires, Then the existing `LobbyManager.OnLobbyError` string is displayed (previously produced-but-never-shown).

## Out of scope
Reconnection/rejoin. Host migration. Mid-game state logic (Phase 2).

## Verification
`read_console` clean; `run_tests` green. Manual/MCP play-mode check for host-drop return-to-menu if not fully unit-testable on loopback.
