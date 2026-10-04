---
title: 'NET-06 — State-transition ordering barrier, client state index, vote & chaining races'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
fixes: ['F9', 'F10', 'F11']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem 1 — RPC overtakes state.** `GameManager.SwitchGameState` (`GameManager.cs:369-382`) runs the server's
`OnEnd/OnStartStateServer` (which write NetworkVariables: `isAwakened`, `isChained`, `powerUseLeft`, …) and immediately
sends `OnEnd/OnStartStateClient` RPCs. NGO sends RPCs right away but NetworkVariable deltas at the end of the tick, so
clients execute `OnStartStateClient` **before** the NVs written in the same frame arrive: state-entry UI reads stale
values. And the client `Update` (`:245-255`) drives `StateUpdateClient` from the replicated `currentGameStateIndex`, which
also lags the RPC → for ≥ 1 tick the client updates the **old** state after having ended it.

**Problem 2 — vote races.** `OnPlayerVotedRpc` (`VoteState.cs:55-84`) has no current-state guard: a vote sent in the RPC
window after the server tallied (`OnEndStateServer`) is still recorded and broadcast (deferred-work.md:23). The voter id is
client-supplied (`OnPlayerVoted` puts `GetLocalClientId()` in the payload), so the server cannot tell who actually voted.

**Problem 3 — chaining enumeration.** The client chaining animation (`ChainingState.cs:55-80`) enumerates the live
`chainingPlayers` NetworkList across `await`s; the host clears it when **its own** animation ends, truncating the loop on
any slower client → chain animations silently skipped.

**Approach:** (1) An **ordering barrier**: client-transition RPCs are queued and sent on the next network tick, after the
transition frame's NV deltas were enqueued (reliable sequenced delivery then guarantees NV-before-RPC). (2) The transition
RPC carries the new state index; the client keeps a local `clientStateIndex` set in the same handler that calls
`OnStartStateClient`, and `StateUpdateClient` uses it. (3) Votes go through a dedicated server RPC carrying the sender
identity; the server rejects votes outside an active vote. (4) The chaining animation iterates a copied snapshot of ids
taken at `OnStartStateClient`.

## Boundaries & Constraints

**Always:**
- Proto gate first: 2-NM test proving a NV written by `OnStartStateServer` is already visible on the client inside
  `OnStartStateClient` once the barrier is in place (and invisible without it — red). If the "next tick" mechanism does
  not hold in NGO 2.12, fall back to carrying the needed values as RPC parameters per state (documented per state).
- Transition order preserved: End(old) → Start(new), never coalesced, even for same-frame double transitions
  (`RoleAttributionState` advances inside its own `OnStartStateServer`).
- Vote identity: `SenderClientId` is the voter, except when the sender is the host: the host may vote **as** a simulated
  bot (id ≥ 100) or as its debug-possessed identity (bot-debug flow, `IsLocalOrSimulated`).
- `GetSafeRpcTarget` untouched; `DoStateMethodRpc` stays for other state methods.

**Ask First:**
- Any visible timing change > 1 network tick on state entry.

**Never:**
- Delay server-side state logic (only the client notification moves).
- Accept a vote whose voter is not the sender (outside the host/bot rule).

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| NV written in OnStartStateServer | Visible in client OnStartStateClient |
| Double transition in one frame | Client gets End A, Start B, End B, Start C in order |
| Late vote after tally | Server rejects (log), no broadcast, recap unchanged |
| Client forges another voter id | Recorded as the sender, or rejected if sender already voted |
| Host votes for bot 101 | Accepted as 101 |
| Host finishes chaining anim first | Slow client still plays every chain animation |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/GameLogic/GameManager.cs:245-255` (`Update`), `:369-382` (`SwitchGameState`), `:479-505` (`DoStateMethodRpc`/`CallStateMethodRpc`) -- transition queue flushed on `NetworkManager.NetworkTickSystem.Tick`; `clientStateIndex`
- `Assets/Scripts/GameLogic/GameStates/VoteState.cs:38-84` -- `OnPlayerVoted` → new `GameManager`/vote-host `SubmitVoteServerRpc(votedId, asSimulatedId, RpcParams)`; state guard
- `Assets/Scripts/GameLogic/GameStates/ChainingState.cs:52-80` -- copy ids at start
- Consumers of `currentGameStateIndex` on clients (UI reading the current state) -- audit with grep, move to `clientStateIndex` where they react to transitions
- Tests: `Tests/PlayMode/GameLogic/GameStates/*`, vote tests (`VoteStateTests*`), `Tests/EditMode/GameLoopMachineTests`

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Ordering/StateTransitionBarrierTests.cs` -- NEW proto/red-first (NV visible in OnStartStateClient; double transition order)
- [ ] `Tests/PlayMode/Vote/VoteAuthorityTests.cs` -- NEW red-first: late vote rejected; forged voter id ignored; host-as-bot accepted
- [ ] `Tests/PlayMode/GameLogic/ChainingSnapshotTests.cs` -- NEW: clear list mid-animation on host → client still iterates all ids
- [ ] Implement barrier + client index, vote RPC + guard, chaining snapshot

**Acceptance Criteria:**
- Given any state transition, when the client runs `OnStartStateClient`, then every NV the server wrote in that transition frame already holds its new value on the client.
- Given a vote received after the vote state ended, then it is rejected and nothing is broadcast.
- Given two players chained and a host that finishes animating first, then every client plays both chain animations.

## Verification

- PlayMode new suites red → green; full suites green (watch `GameLoopMachine` / state-ordering goldens)
- 2-build playtest: full night + vote + double chaining with the client artificially slower (background load)
