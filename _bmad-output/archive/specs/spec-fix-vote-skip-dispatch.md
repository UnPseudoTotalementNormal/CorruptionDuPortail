---
title: 'Fix vote-skip: route through the working player-vote dispatch'
type: 'bugfix'
created: 2026-07-08
status: 'done'
context: []
baseline_commit: 65e642c66caaa1a82fdeb8719854d7a428ca02ba
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** During `VoteState`, pressing the skip vote button does nothing (no effect, no error, host + client). Root cause (investigation `investigations/vote-skip-regression-investigation.md`, HIGH confidence): the click reaches `VoteStateUI.OnVoteSkipButtonPressed()`, but its server dispatch attaches the handler only in `VoteStateUI.OnNetworkSpawn()`, which never fires — `VoteStateUI` is a `NetworkBehaviour` on a `NetworkObject`-less prefab, plain-`Instantiate`d and never spawned — so the skip message dead-ends on the server.

**Approach:** A skip IS a vote for `SKIP_VOTE_ID`. Reroute `VoteStateUI.OnVoteSkipButtonPressed()` through the already-working player-vote path `VoteState.OnPlayerVoted(SKIP_VOTE_ID)` (which dispatches via `gameManager.DoStateMethodRpc(... OnPlayerVotedRpc ..., server)` on a real spawned NetworkObject), and delete the dead `NetworkAction`/`OnNetworkSpawn` machinery.

## Boundaries & Constraints

**Always:** The skip must register a `SKIP_VOTE_ID` vote for the local client through the SAME server-authoritative dispatch as a player-vote (`DoStateMethodRpc` → `OnPlayerVotedRpc`), pass the existing `CanVote` gate, and work for host and client identically. Behaviour-preserving otherwise.

**Ask First:** none.

**Never:** Do NOT touch the board `SkipButton` (the awakening/dodo `SleepCharacterServerRpc`) — out of scope (owner decision). Do NOT change the skip tally semantics (`VoteTally` / `SKIP_VOTE_ID`). Do NOT add a `NetworkObject` to the StateUI prefab or otherwise try to make StateUIs network-spawned. Do NOT change the player-vote path.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Client skips | Non-host client, in VoteState, has not voted | `OnPlayerVotedRpc(localClientId, SKIP_VOTE_ID)` runs on server; local client added to `votesForPlayer[SKIP_VOTE_ID]`; refresh RPC + auto-close check fire | N/A |
| Host skips | Host, in VoteState, has not voted | Same as above (host is a client too) | N/A |
| Skip after already voting | Client already in some vote list | Rejected by `CanVote` (already voted) — no double vote | Existing warn log |
| Departed/ineligible skips | `CanVote` false (departed/eliminated/fake) | No vote registered | Existing behavior |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/UI/StateUI/VoteStateUI.cs` -- the broken skip UI: rewire `OnVoteSkipButtonPressed()`, delete the dead `NetworkAction` (`onVoteSkipButtonPressedByClient`), `OnVoteSkipButtonPressedServer`, and the `OnNetworkSpawn`/`OnNetworkDespawn` overrides that only existed to attach it (also fixes the named-message-handler leak).
- `Assets/Scripts/GameLogic/GameStates/VoteState.cs` -- `OnPlayerVoted(ulong)` (:44) is the reused dispatch; `OnVoteSkipButtonPressed(ulong)` (:175) becomes dead → remove it (only the removed VoteStateUI handler called it).
- `Assets/Prefabs/StateUI/VoteStateUI.prefab` -- `SkipVoteButton` UnityEvent → `VoteStateUI.OnVoteSkipButtonPressed` (unchanged; already correct).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/UI/StateUI/VoteStateUI.cs` -- `OnVoteSkipButtonPressed()` now calls `((VoteState)owningGameState).OnPlayerVoted(VoteState.SKIP_VOTE_ID)`. Deleted the `onVoteSkipButtonPressedByClient` field, `OnVoteSkipButtonPressedServer`, and both `OnNetworkSpawn`/`OnNetworkDespawn` overrides (they only held the dead wiring + a base call; base runs regardless). Dropped now-unused `Network.Action`/`Unity.Netcode` usings.
- [x] `Assets/Scripts/GameLogic/GameStates/VoteState.cs` -- Removed the now-unreferenced `OnVoteSkipButtonPressed(ulong)` method.

**Acceptance Criteria:**
- Given a client (or host) in `VoteState` who has not voted, when they press the skip button, then a `SKIP_VOTE_ID` vote is registered for their client id and the vote refresh/auto-close behaves exactly as for a player-vote.
- Given the change, when the full test suite runs, then EditMode and PlayMode stay green (no regression) and `read_console` is clean.
- Given a grep of the codebase, when searching for `onVoteSkipButtonPressedByClient` / `VoteState.OnVoteSkipButtonPressed`, then there are no remaining references (fully removed, no dangling NetworkAction registration/leak).

## Design Notes

`OnPlayerVoted` already stamps the sender as `CharacterQuery.GetLocalClientId()` and targets the server, so `OnPlayerVoted(SKIP_VOTE_ID)` is semantically identical to the intended `OnPlayerVotedRpc(localClientId, SKIP_VOTE_ID)` — the `SKIP_VOTE_ID` bucket already exists (seeded in `OnStartStateServer`). No new RPC is introduced. Removing the `OnNetworkSpawn` override is safe because it never executed for a `NetworkObject`-less StateUI (that was the bug); the base StateUI sets up via its own non-network hook.

## Verification

**Commands:**
- `mcp__UnityMCP__run_tests` (EditMode) -- expected: 292/292 pass (no regression).
- `mcp__UnityMCP__run_tests` (PlayMode) -- expected: 190/190 pass (no regression).
- `mcp__UnityMCP__read_console` -- expected: no compile errors.

**Manual checks (real MP, owner):**
- Host + ≥1 client, reach VoteState, press skip → the skip registers (skip count increments; vote can resolve to SKIP) for both host and client. (Not automatable — StateUI + real vote UI need a live match.)

## Suggested Review Order

- Entry point: the rewired click handler — a skip is now a vote for SKIP_VOTE_ID through the working dispatch.
  [`VoteStateUI.cs:32`](../../Assets/Scripts/UI/StateUI/VoteStateUI.cs#L32)

- The reused, server-targeted dispatch (unchanged) that the skip now rides — proves authority is preserved.
  [`VoteState.cs:44`](../../Assets/Scripts/GameLogic/GameStates/VoteState.cs#L44)

- Supporting removal: the dead server method + NetworkAction/OnNetworkSpawn machinery are gone (also closes a named-message-handler leak).
  [`VoteState.cs:175`](../../Assets/Scripts/GameLogic/GameStates/VoteState.cs#L175)

## Review outcome (2026-07-08)

Three adversarial reviewers (blind / edge-case / acceptance), Opus, on the scoped 2-file diff:
- **Acceptance auditor:** fully compliant, no violations. **Blind hunter:** 4 findings, all rejected on verification (server routing preserved via `OnPlayerVoted`→server `OnPlayerVotedRpc` `Assert.IsTrue(IsServer)`; sender via `GetLocalClientId`; cast safe; members public).
- **Edge-case hunter:** parity confirmed, no base behaviour dropped, `owningGameState` guaranteed, and the change **closes** a pre-existing named-message-handler leak. One LOW finding — a late/stale-vote RPC-latency race — is **pre-existing on the player-vote path** (skip now shares it, does not introduce it) → deferred to `deferred-work.md` (proper fix guards the shared `OnPlayerVotedRpc`, out of this spec's scope).
- No `intent_gap` / `bad_spec` / `patch` → no loopback. Tests: EditMode 292/292, PlayMode 190/190, `read_console` clean.
