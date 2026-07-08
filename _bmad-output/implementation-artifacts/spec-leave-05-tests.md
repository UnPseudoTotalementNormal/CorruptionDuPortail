---
title: "Phase 5 — Test coverage for player-leave stability"
status: draft
epic: epic-player-leave-stability
phase: 5
depends_on: [1, 2, 3, 4]
---

# Phase 5 — Test coverage for player-leave stability

## Goal
Lock the epic behind PlayMode/EditMode tests on the 2-NetworkManager loopback substrate. Every policy branch and every unblock path from Phases 1–4 has a test.

## Context
- Substrate: `Tests/PlayMode/Desingleton/MultiClientGameFixture.cs`, `CoexistenceGateTests.cs`; helper `Tests/PlayMode/NetworkTestHelper.cs` (bounded waits, `RegisterCompositionRoot`, poll-frames). Assembly `Tests.PlayMode`.
- Gotcha (memory): `NetworkBehaviour.IsOwner` is unreliable on client replicas in the 2-NM-in-one-process fixture — drive owner-write assertions from the HOST, verify server→client replication.
- Phase 0 already added `PlayerLeaveMidGameTests.cs` — extend it.

## Tasks (one test per policy branch / unblock)
1. **Lobby leave** — a client leaving in `LobbyState` → its Character removed, `playerInfos` entry gone, avatar despawned; exactly once; no error.
2. **Mid-game chain** — a client leaving mid-game → Character `isChained == true`, role revealed, `ownerClientId != FAKE_CLIENT_ID`, no card animation invoked, no NRE.
3. **Last-anomaly instant win** — construct a state where the leaver is the last un-chained anomaly → leaving ends the game with chosen (élus) winners immediately (assert `GameEndingState` reached + winners set), driven from host.
4. **Awakening unblock** — leaver is an awakened actor blocking a layer → leaving advances the layer within a bounded frame budget without relying on the layer timeout.
5. **Mage/portal unblock** — leaver is `mageCharacterOwnerId` during `TakeDownThePortalState` → state advances cleanly, no hang/NRE.
6. **Vote hardening** — a not-yet-voted eligible player leaves during an open vote → denominator updates, vote can auto-close, tally does not NRE.
7. **No-winner leave** — chaining a leaver that completes no win-condition → game stays in flow, does NOT jump to `GameEndingState`.
8. **Min-players gate** (EditMode where possible) — start blocked below minimum, allowed at/above.
9. **Clean reset** — second host/join in the same play session starts with no stale static/registry from the first.
10. **Host-drop** (loopback where feasible, else documented manual/MCP check) — non-host client returns to menu on host loss.
11. **Bot exclusion** — `HandlePlayerLeft(clientId >= 100)` early-returns; bots never trigger the disconnect path.

## Acceptance Criteria
- **AC1** — Tasks 1–7 and 11 have passing PlayMode tests; task 8 has a passing test (EditMode preferred); task 9 has a passing loopback test.
- **AC2** — All tests are deterministic, order-independent, use poll-frames (no `WaitForSeconds`), and `[TearDown]` leaves `NetworkManager.Singleton == null`.
- **AC3** — Owner-write assertions are driven from the host and verify server→client replication (per the `IsOwner` gotcha).
- **AC4** — Full suite green: `mcp__UnityMCP__run_tests` EditMode + PlayMode; `read_console` clean.

## Out of scope
Reconnection tests (feature out of scope). Load/soak testing.

## Verification
`mcp__UnityMCP__run_tests` full EditMode + PlayMode green; `read_console` clean.

## Coverage matrix (Phase 5 audit, 2026-07-08)

Automated substrate = 2-NetworkManager loopback (`MultiClientGameFixture`) + host-only harnesses
(`LeaveUnblockSeamTests`, `LobbyStateStartGuardTests`) + pure EditMode decision tests. The loopback
fixture seeds `DummyGameState`s and spawns no `ChainingManager`/`BoardManager`/`GameInfoRevealer`, so the
REAL RoleAttribution/Awakening/Vote/TakeDownThePortal/VictoryConditionCheck states cannot run in-process.
"Partial" below = the decision + per-state seam are unit-tested, but the live full-scene state transition /
RPC fan-out is manual (see checklist).

| # | Task | Status | Test(s) |
|---|---|---|---|
| 1 | Lobby leave → character removed | **Covered** | `LobbyStateStartGuardTests.HandlePlayerLeft_LobbyLeave_RemovesCharacter_RecordsDeparted_NoError` (new) |
| 2 | Mid-game chain (isChained, no anim, no fakify, no NRE) | **Covered** | `PlayerLeaveMidGameTests.RealClient_DropsMidGame_IsChainedByUnifiedPipeline` (real loopback client drop) |
| 3 | Last-anomaly instant win → GameEndingState + winners | **Partial** (decision only) | `LeaveVictoryResolverDecisionTests.LastAnomalyChained_...ResolverWouldEndGame` (EditMode). Live GameEndingState transition = **manual** |
| 4 | Awakening unblock (layer advances w/o timeout) | **Partial** (seam only) | `LeaveUnblockSeamTests.AwakeningState_ChainedAwakenedLeaver_IsDrainedFromLayer`. Full night flow = **manual** |
| 5 | Mage/portal unblock (TakeDownThePortalState advances) | **Covered** (seam) | `LeaveUnblockSeamTests.TakeDownThePortal_MageLeaves_AdvancesLoop` + `..._NonMageLeaves_IsNoOp` |
| 6 | Vote hardening (denominator collapse, no tally NRE) | **Covered** (seam) | `LeaveUnblockSeamTests.VoteState_DepartedNonVoterLeaves_ReducesDenominator_CollapsesTimer` |
| 7 | No-winner leave → stays in flow, no GameEndingState jump | **Covered** | `LeaveUnblockSeamTests.TryResolveVictoryNow_NoWinningCondition_ReturnsFalse_AndDoesNotTransition` + `LeaveVictoryResolverDecisionTests.AnomalyStillUnchained_...ResolverWouldContinue` (EditMode) |
| 8 | Min-players gate (start blocked below min, allowed at/above) | **Covered** | `LobbyStateStartGuardTests.OnStartGameButtonPressed_FewerPlayersThanMandatoryRoles_...` + `..._PlayersBetweenMinAndMax_AdvancesTheState` + `..._MorePlayersThanRoles_...` (max) |
| 9 | Clean reset (no stale static/registry next session) | **Covered** | `LobbyStateStartGuardTests.ResetSessionStatics_ClearsInstanceAndRegistry_NoStaleSurvivor`; loopback teardown also asserts `Singleton == null` + empty registry every test |
| 10 | Host-drop → non-host returns to menu | **Partial** (decision only) | `HostDropPolicyTests` (4 branches, EditMode). Real-socket host loss = **manual** |
| 11 | Bot exclusion (`HandlePlayerLeft(id >= 100)` early-returns) | **Covered** | `LobbyStateStartGuardTests.HandlePlayerLeft_SimulatedBotId_EarlyReturns_NoStateMutation` (new) |

Totals after Phase 5: EditMode **292/292**, PlayMode **190/190** (baseline 292/188; +2 PlayMode from tasks 1 & 11). `read_console` clean.

## Manual verification checklist

Substrate limit (documented, not a gap): the loopback fixture cannot run the real state graph, so the ACs
below are proven at the decision + per-state-seam level in code and must be smoke-checked by a human in a
real build before shipping the epic. Run each in **two instances** (Host + one real Client; use a second
machine or a second Steam account for the Facepunch transport, or two local editor/build instances if a
loopback dev transport is available). Start a real match (roles distributed, past the lobby) unless noted.

- [ ] **Task 3 — last-anomaly instant win (AC3).** Set up a match with exactly one Anomaly and the rest
  Chosen (élus). Have the Anomaly player Alt-F4 / quit mid-game. EXPECT: the game transitions to
  `GameEndingState` immediately with the Chosen as winners (no hang, no extra night). Host console shows
  `[LEAVE] Player N was the last anomaly — victory resolved instantly`.
- [ ] **Task 4 — awakening unblock, full night (AC4).** Put an awakened actor (e.g. a role that acts at
  night) mid-`AwakeningState` and have that player leave WHILE the layer is waiting on them. EXPECT: the
  layer advances within a second or two WITHOUT waiting out the layer timeout; the night proceeds to the
  next layer/state. Confirm no other awakened player is dropped (leaver-specific drain).
- [ ] **Task 2 — mid-game chain, full scene.** Any non-last-anomaly player leaves mid-game. EXPECT: their
  character is instantly chained (role revealed on the board, chained visual) with NO card-flip animation,
  the seat is NOT fakified, and no server error is logged.
- [ ] **Task 5 — Mage/portal unblock, full scene.** During `TakeDownThePortalState`, the Mage
  (`mageCharacterOwnerId`) leaves. EXPECT: the portal step advances cleanly, no hang, no NRE.
- [ ] **Task 6 — vote hardening, full scene.** Open a vote; a not-yet-voted eligible player leaves. EXPECT:
  the eligible denominator drops so the vote can auto-close on the remaining present voters; the tally does
  not NRE; a chained-but-still-present player remains eligible to vote.
- [ ] **Task 10 — host-drop return-to-menu.** With a Host + Client mid-game, kill the Host (Alt-F4 / close
  the process). EXPECT: the Client is returned to the main menu with a host-loss notification (not stranded
  in a dead `GameScene`). Verify a graceful host "leave to menu" does NOT show the abrupt-loss notification.
- [ ] **Phase 4 — connection / wait timeouts.** Attempt to join a host that never accepts (bad
  code / firewalled) and confirm the connection attempt times out and returns to menu rather than hanging.
