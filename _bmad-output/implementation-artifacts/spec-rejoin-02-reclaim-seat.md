---
title: 'Rejoin 02 — a dropped player reconnects with his session token and takes his seat back'
type: 'feature'
created: '2026-10-05'
status: 'done'
baseline_commit: 'bbaefc4a'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-rejoin-01-seat-reservation.md'
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A reserved seat (rejoin 01) is useless if nobody can come back: a mid-game connection is refused, and NGO
gives a reconnecting client a NEW clientId while every game system keys the seat by the old one.

**Approach:** (owner decisions 2026-10-05) the host hands each seated player a secret session token, saved on his PC.
A reconnect carries it in the connection payload; mid-game, a token matching a RESERVED seat is approved. The seat keeps
its original id everywhere (character, votes, chat, knowledge…); the server binds the new transport id to that seat
("seat alias") at the boundary: RPC targets (`GetSafeRpcTarget`, direct `RpcTarget.Single`), RPC senders, "is
connected" checks, and on the client `GetLocalClientId()` returns the seat. When his sync completes the server releases
the reservation, clears "left", and re-sends what a fresh client misses. The game must look and play for him exactly as
before the drop (verified by state comparison + screenshots, rejoin 03).

## Boundaries & Constraints

**Always:** `GetSafeRpcTarget` / `IsLocalOrSimulated` patterns kept (ids >= 100 untouched); server authority; pure
bookkeeping in Domain with tests; token never in replicated state; a wrong / stale / unknown token is refused with the
current "game in progress" reason.

**Ask First:** re-keying existing dictionaries instead of aliasing; un-chaining a seat whose grace expired.

**Never:** approve a mid-game join without a matching reserved seat; let two transports own one seat.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rejoin in time | token of a reserved seat | approved, seat bound, reservation released, "left" cleared, plays as before | N/A |
| Wrong / unknown token mid-game | any | refused (game in progress) | reason shown |
| Grace expired (seat chained) | token of an expired seat | refused | reason shown |
| Second drop after a rejoin | same seat | reserved again (new grace), rejoin again possible | N/A |
| Lobby rejoin | any | normal lobby join (lobby leaves remove the character) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Domain/ConnectionPayload.cs` -- format 2: `RejoinToken` (format 1 still parsed).
- `Assets/Scripts/Domain/SeatDirectory.cs` -- tokens (seat ↔ token) + aliases (transport ↔ seat), pure.
- `Assets/Scripts/Characters/CharacterManager.cs` -- owns the directory; `GetSafeRpcTarget` maps seat → transport; client local seat (`AssignLocalSeatRpc`), `GetLocalClientId()`; token issue + `ReceiveRejoinTokenRpc`.
- `Assets/Scripts/Network/RejoinSessionStore.cs` -- client: token + how to reach the host, persisted (PlayerPrefs).
- `Assets/Scripts/Network/ConnectionApprovalGate.cs` -- mid-game approval on a reserved seat's token; no post-sync kick for it.
- `Assets/Scripts/GameLogic/GameManager.cs` -- `CompleteRejoin`; sender mapping (`CurrentStateRpcSenderId`).
- Inbound / outbound mapping: `Power.cs:351,494`, `ChatManager.cs:199,248`, `GameInfoRevealer.cs:265-279`, `PCReparentOnChain.cs:55`, `PlayerIconManager.cs:496`.
- Re-send on rejoin: knowledge slice (client pull), icons slice, chat channels, day counter, current-state client fields.

## Tasks & Acceptance

**Execution:**
- [x] `SeatDirectory` + `ConnectionPayload` v2 with EditMode tests.
- [x] Token issue at seating + client store + payload.
- [x] Approval + bind + `CompleteRejoin`.
- [x] Inbound / outbound mapping, client local seat.
- [x] Re-send on rejoin.
- [x] PlayMode: drop → reconnect with token (new clientId) → seat bound, local character = seat, sender mapped.
- [x] Autoplay: `rejoin-after <s>` (client reconnects in-process), scenario + capture comparison (rejoin 03 visual check).

**Acceptance Criteria:**
- Given a dropped player with a valid token within the grace delay, when he reconnects, then he gets his seat back (role, powers, knowledge, chat channels, votes) and plays on; no desync.
- Given a wrong token or an expired seat, when he connects mid-game, then he is refused with the game-in-progress reason.

## Spec Change Log

- 2026-10-05 — implemented and proven. Found while proving it end to end (autoplay `client-rejoin`, screenshots):
  - Reconnecting inside the old game scene left stale managers (GetGameState out of range, lost chat channels):
    the drop now goes through the real client path (main menu, statics reset) and the rejoin starts from the menu.
  - A peer spawning mid-game started the current state before knowing its seat (no local character, empty board):
    `GameManager` defers that start to `RejoinCatchUpRpc`, sent right after `AssignLocalSeatRpc`, and replays the
    introduction's table (`GameIntroductionState.DealBoard`: player cards + role shelf).
  - Board counters (corrupted total, robot) were RPC-pushed: now NetworkVariables, read on spawn.
  - `TakeDownThePortalState.HighlightRolesRpc` threw on a peer with no board card yet: tolerant lookup.
  - The menu rejoin keeps the session when the connection merely fails; only a host refusal clears it.
  - Seen in 2 of 8 runs (both before the fixes above), not explained yet: the host's transport closes the rejoin connection ~13 s into the
    GameScene load (`ClosedByRemote` on the client, no game-side disconnect on the host; the autoplay retry then hits
    NGO's `ClientLoadedSynchronization` NRE). `client-rejoin` keeps `-autoplay-net-log` to catch it with NGO's log.
  - Not covered by autoplay: the menu button itself (the autoplay connects directly; Relay/Steam only) and a relaunch
    after a real crash (the token survives in PlayerPrefs: EditMode `RejoinSessionStoreTests`).
