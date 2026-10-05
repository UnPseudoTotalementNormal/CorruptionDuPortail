---
title: 'Rejoin 01 — a mid-game leaver''s seat is reserved for a grace delay'
type: 'feature'
created: '2026-10-05'
status: 'in-review'
baseline_commit: 'ae76f3b8a30beaaf5af9c4b1dcbed7918feb2b6e'
context:
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A player who drops mid-game (crash, network) is chained at once (July leave policy) and can never come back.
Rejoin needs his seat to survive the drop.

**Approach:** first step of the rejoin chantier (owner decisions 2026-10-05: seat reserved with a delay, local session
token, "Rejoindre la partie en cours" button). A mid-game disconnect RESERVES the seat for `REJOIN_GRACE_SECONDS`
(120 s): the player shows as left ("(parti)", existing roster `hasLeft`), sleeps through his turns, does not vote; the
game never waits on him. When the delay expires the July rule applies unchanged (instant chain, victory re-check).
Lobby leaves are unchanged (the character is removed).

## Boundaries & Constraints

**Always:** reuse the existing departed-set (`HasClientLeft`) for "skip / no vote"; one idempotent pipeline (liveness +
transport ignition); pure bookkeeping in Domain (`SeatReservations`), clock injected.

**Ask First:** anything that un-chains a player; changing the victory rules.

**Never:** chain a reserved seat before the delay; let a state wait on an absent player.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Drop mid-game | real client disconnects | seat reserved, not chained, shown "(parti)", current state unblocked | N/A |
| Night while away | his role's layer comes | skipped (sleeps) | N/A |
| Vote while away | vote | excluded from the denominator | N/A |
| Mage chained while away | portal step | step skipped | N/A |
| Delay expires | 120 s later | instant chain + victory re-check (last anomaly → élus win) | N/A |
| Drop in lobby | lobby | character removed (unchanged) | N/A |
| Already chained player drops | mid-game | nothing to reserve, state unblocked | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Domain/SeatReservations.cs` -- reserve / release / take expired (pure).
- `Assets/Scripts/GameLogic/GameManager.cs` -- `HandlePlayerLeft` mid-game branch reserves; `ExpireReservedSeats` (Update) chains on expiry; `RejoinGraceSeconds`, `IsSeatReserved`, `ReservedSeatIds`.
- `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs` -- departed seats skipped when a layer wakes.
- `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs` -- portal step skipped when the Mage is away.
- `Assets/Scripts/GameValues.cs` -- `REJOIN_GRACE_SECONDS = 120`.
- `Assets/Scripts/Autoplay/*` -- lever `rejoin-grace`, events `seat.grace`, `seat.reserved`, `seat.released`.

## Tasks & Acceptance

**Execution:**
- [x] `SeatReservations` + EditMode `SeatReservationsTests` (5).
- [x] `GameManager` reserve / expire; `AwakeningState` skip; portal skip.
- [x] PlayMode `PlayerLeaveMidGameTests`, `PlayerLeaveIdempotencyTests` moved to the new rule (reserved, then chained once on expiry).
- [x] Autoplay lever + events; scenarios `client-disconnect-reserved` (new), `client-leaves-at-vote` (grace 10 s).
- [x] Docs: REFERENCE.md, project-context.md.

**Acceptance Criteria:**
- Given a real client dropping mid-game, when the server handles it, then the seat is reserved and not chained, and the game goes on.
- Given the grace delay expired, when the server ticks, then the seat is chained exactly once and the victory re-check runs.
- Given `client-disconnect-reserved` and `client-leaves-at-vote`, when they run, then they pass (seat reserved, later chained, no hang, no desync).

## Spec Change Log
