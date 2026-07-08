---
title: "Phase 1 — Unified server-side HandlePlayerLeft pipeline"
status: draft
epic: epic-player-leave-stability
phase: 1
depends_on: [0]
---

# Phase 1 — Unified server-side HandlePlayerLeft pipeline

## Goal
Replace the four independent, order-undefined disconnect reactions with one authoritative server-side pipeline `HandlePlayerLeft(ulong clientId)`. Fix the `LobbyState` subscription leak. Branch on phase: lobby → remove character; mid-game → instant chain (no animation). Keep ancillary cleanup (player-info, avatar, board card) but in a defined order.

## Context (current wiring)
- `GameManager.OnPlayerDisconnectedServer` — `Assets/Scripts/GameLogic/GameManager.cs:554`. Subscribed `:166`, unsubscribed `:176`. Fakifies seat + `OnPlayerDisconnectedRpc:565` destroys a fake card.
- `LobbyState.OnClientDisconnected` — `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:25`. Subscribed in `OnStateCreated:64`, **never unsubscribed** (leak — fires mid-game too).
- `LobbyPlayerInfoHolder.OnClientDisconnected` — `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:74` (removes `playerInfos` entry). Keep.
- `AvatarManager.OnClientDisconnected` — `Assets/Scripts/Avatars/AvatarManager.cs:191` (despawns avatar). Keep.
- Instant-chain primitive: `ChainingManager.ChainCharacterRpc(id)` — `Assets/Scripts/GameLogic/ChainingManager.cs:84` — applies `ChainCharacterServer()` (isChained) + role reveal + portal/Mage special-case + `AskForUpdateAllCharactersRpc`. The card animation lives separately in `ChainingState` (client), so calling this directly = instant, no anim.
- Lobby detection: `GameManager` exposes `hasGameStarted` (IGameLoop) and current state is queryable via `IGameStateQuery` (`GetGameState(currentGameStateIndex.Value)` is a `LobbyState` while in lobby).

## Tasks
1. **Add `HandlePlayerLeft(ulong clientId)` on the server** (in `GameManager`, the manager that already owns the disconnect subscription and has `IGameLoop`/`IGameStateQuery`). Server-guard it. Ignore `clientId >= 100` (bots never disconnect; defensive early-return).
2. **Branch on phase:**
   - **In lobby** (current state is `LobbyState`, or `!hasGameStarted`): call `Command.RemoveCharacter(clientId)` (current lobby behavior).
   - **Mid-game** (owner has a live Character and game started): resolve the leaver's Character; call the instant-chain primitive (`ChainingManager.ChainCharacterRpc(character.ownerClientId.Value)` or a direct server method equivalent) — **no `ChainingState` animation**. Do NOT fakify. Leave the Phase-2 hooks (unblock waiting state + victory re-check) as clearly marked call sites for Phase 2 to fill.
3. **Ancillary cleanup, ordered** (server): player-info removal, avatar despawn, board fake-card cleanup — invoked from `HandlePlayerLeft` in a defined sequence so ordering is explicit rather than callback-race. Preserve current effects (nothing silently dropped).
4. **Remove the fakify path** from the old `OnPlayerDisconnectedServer` and route the single callback subscription to `HandlePlayerLeft`. Keep exactly one `OnClientDisconnectCallback += HandlePlayerLeft` (server), unsubscribe in `OnNetworkDespawn`.
5. **Fix the `LobbyState` leak:** unsubscribe `OnClientDisconnected` symmetrically (in `OnEndStateServer`, mirroring the connect sub/unsub in `OnStartStateServer`/`OnEndStateServer`), OR remove `LobbyState`'s own disconnect subscription entirely now that `HandlePlayerLeft` covers the lobby branch. Prefer removal to eliminate the double-handling; ensure the lobby-remove behavior still happens via `HandlePlayerLeft`.
6. Keep `LobbyPlayerInfoHolder` and `AvatarManager` subscriptions OR fold them into the ordered pipeline — pick one and document it; the invariant is: each ancillary effect happens exactly once, in a known order.
7. Tag diagnostics `[LEAVE]`.

## Acceptance Criteria
- **AC1** — Given a player leaves **in the lobby**, When `HandlePlayerLeft` runs, Then their Character is removed (as today) and their `playerInfos` entry and avatar are gone, with no double-removal and no error.
- **AC2** — Given a player leaves **mid-game**, When `HandlePlayerLeft` runs, Then their Character is **chained** (`isChained == true`, role revealed) with **no card animation played**, and their `ownerClientId` is **not** set to `FAKE_CLIENT_ID`.
- **AC3** — Given any disconnect, When handlers run, Then exactly one code path reacts (no `LobbyState`↔`GameManager` double-handling); the `LobbyState` subscription leak is gone (verified: no residual mid-game `RemoveCharacter` from `LobbyState`).
- **AC4** — Given a simulated bot id (`>= 100`), When `HandlePlayerLeft` is (defensively) called, Then it early-returns without touching state.
- **AC5** — The Phase-0 repro test now observes the chained outcome (or is updated to assert it) with no server NRE.

## Out of scope
State-unblocking and victory re-check (Phase 2). Client-side host-drop (Phase 3).

## Verification
`read_console` clean; `run_tests` PlayMode green including the Phase-0 test updated to the chained expectation.
