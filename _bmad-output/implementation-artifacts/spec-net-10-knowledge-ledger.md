---
title: 'NET-10 — Private knowledge in a server ledger, pushed as per-viewer slices'
type: 'refactor'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-09-server-authoritative-powers.md']
fixes: ['F17']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-characterbar-corruption-client-icon.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-fix-reveal-rpc-ui-refresh-nonhost.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** What each player *knows* (role revealed, corruption revealed, hacked marker — `CharacterInfoReveal`) exists
**only on the client**, built incrementally by reveal RPCs (`GameInfoRevealer.cs:188-291`). For real players the server
keeps no record, so a missed, duplicated or misordered reveal can never be detected or repaired. The client wipes its
dictionary when it processes `RoleAttributionState.onStateEndClient` (`:52-64`) — correct only if no reveal for the new
game is processed before that RPC (implicit ordering). Simulated bots have a separate host-side brain path with an
overloaded `_observerId` (storage sentinel vs viewer id — deferred-work.md:118), a known source of bugs (non-host reveal
regression). In a deduction game, knowledge **is** the game: a divergence here is invisible and fatal.

**Approach:** Apply the proven `PlayerIconManager` pattern (server ledger + full per-viewer slice push): a pure
`KnowledgeLedger` POCO on the server holds every viewer's `CharacterInfoReveal` per target (monotonic levels, explicit
`ClearHacked`, Public = every viewer). Every reveal goes through the server ledger, which pushes the **full slice** of
each affected viewer to that viewer only (`GetSafeRpcTarget`; host applies locally; bots ≥ 100 keyed on the host by
viewer). Clients **replace** their knowledge with the slice and diff old/new to drive the existing UI side effects
(card flip, `onCharacterInfoRevealedChanged`). Ledger reset happens server-side at role attribution and is pushed.

## Boundaries & Constraints

**Always:**
- Same visible behaviour: the same viewers see the same reveals with the same card animation rules (`showInfo` travels
  with the change set of a push; a chaining reveal still does not double-flip).
- "A player always sees their own role" stays a read-time invariant (`EnsureOwnRoleRevealed`, `:147-153`).
- Slice payload = entries (targetId + 4 levels) + monotonic `version`; a client ignores an older version.
- A client can request its slice (`RequestKnowledgeSliceServerRpc`) — used on spawn and by NET-00 on a mismatch.
- NET-00 gains a **per-viewer** `Knowledge` component: the server sends each viewer the hash of *its own* slice only.
- Public API used by executors/UI (`GetCharacterInfo`, `onCharacterInfoRevealedChanged`) keeps its shape.

**Ask First:**
- Any reveal whose visibility rule is unclear while porting (stop + report; reveal rules are design-owned).

**Never:**
- Send a viewer anything outside its own slice.
- Write knowledge on a client (clients only apply slices).

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| Personal reveal for viewer A | Only A's slice changes; A's card flips (if showInfo) |
| Public reveal (chaining) | Every viewer's slice, bots included; no double flip |
| POmniscience expiry | `isHacked` cleared in the Robot's slice only |
| New game attribution | Ledger reset server-side; no stale reveal survives regardless of message order |
| Duplicate/old push | Ignored by version |
| Bot viewer 101 | Host holds slice keyed 101; host's own slice untouched |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` (whole file) -- becomes: server ledger owner + client slice holder; delete `SetRevealLevelRpc`, `SetRevealLevelSimulatedRpc`, `ClearHackedRpc`, `ClearHackedSimulatedRpc`, `simulationsKnowledge` dual path
- NEW `Assets/Scripts/Domain/KnowledgeLedger.cs` (pure) + EditMode tests
- `Assets/Scripts/GameLogic/PlayerIconManager.cs:128-500` -- reference implementation (slice-by-viewer, host short-circuit, bot keying)
- Callers: `Characters/Powers/Runtime/Executors/RevealInfoExecutor.cs`, `RevealPublicExecutor.cs`, `GameLogic/ChainingManager.cs:96`, `GameStates/TakeDownThePortalState.cs:145`, `GameStates/GameEndingState.cs:77`, `Characters/Powers/POmniscience.cs` (clear hacked)
- UI consumers: `Board/Card.cs:250-269`, `CardHackGlitch`, InfoTable, character bar badges
- `Domain/RevealVisibilityRules.cs` -- kept for the UI-refresh rule

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/KnowledgeLedgerTests.cs` -- NEW: monotonic, public fan-out, clear-hacked, reset, version
- [ ] `Tests/PlayMode/Replication/KnowledgeLedgerReplicationTests.cs` -- NEW red-first 2-NM: (a) a reveal processed on the client before the attribution-end RPC is wiped today (red), survives after; (b) client slice == server ledger slice after a full night of reveals; (c) bot slice never overwrites host slice
- [ ] Port every reveal path; delete client-write paths; NET-00 `Knowledge` component
- [ ] Existing reveal suites (`OwnerLocalEffectBoundaryTests`, reveal-wrong-card, non-host reveal refresh) green

**Acceptance Criteria:**
- Given any game, then each viewer's client knowledge equals the server ledger's slice for that viewer at every state transition.
- Given any message ordering around role attribution, then no reveal is lost or carried over.
- Given the codebase, then no client writes knowledge locally.

## Verification

- EditMode + PlayMode new suites; full suites green
- 2-build playtest: Robot hack + expiry, Embrace success/fail, Cursed Vision, a chaining — boards identical to expectations per viewer; zero `[DESYNC] component=Knowledge`
