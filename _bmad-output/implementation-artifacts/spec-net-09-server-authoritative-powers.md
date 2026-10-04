---
title: 'NET-09 — Server-authoritative power use: every decision and every use validated on the server'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-08-powers-projection.md']
fixes: ['F15', 'F16']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-powers-poco-v2-architecture.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem 1 — client-side decisions.** Three powers compute their gameplay outcome on a client from that client's
replica: Embrace of Shadows and Cursed Vision on the caster's client, Lack of Affection on the contacted target's client
(`RunClientDecisionEffects`, `Power.cs:232-241`; `PEmbraceOfShadows.cs:49-70`, `PCursedVision.cs:30-46`,
`PLackOfAffection.cs:47-80`). Their authoritative mutation is then self-RPC'd (`CorruptPlayerServerRpc`,
`RequireOwnership = false`, `Character.cs:181-186`). A stale or wrong replica (F12: a client holding a default role) turns
directly into a **wrong authoritative outcome** — the desync leaks into the real game state. This client-side placement
was chosen only because owner-local reveals executed on the server landed on the host's board; that is a routing problem,
not a reason to decide on the client.

**Problem 2 — no server validation.** Effect RPCs (`OnCardClickedRpc`, `ObserveServerRpc`, `TryHealServerRpc`, …) and
`OnUsedServerRpc` apply without checking anything (`Power.cs:338-373`, `:393-398`): not the uses left, not that the
owner is awake/not chained, not the current game state, not the target's validity, not that the sender owns the power.
A double click or a click at the end of the awakening timer (within RPC latency) applies the effect twice or after the
owner was put to sleep; `powerUseLeft` can go negative.

**Approach:** (1) Every power decision runs **on the server** (`RunDecisionEffects`); `RunClientDecisionEffects` is deleted.
Effects aimed at one viewer (owner-local reveal, local chat line) are **routed** by the server to that viewer
(`GetSafeRpcTarget`), never executed on the host. Picker sounds key off the server verdict (`onPowerVerdict`, already
routed to the caster) instead of the client's replica. (2) One **atomic server entry** per use:
`ServerTryUse(sender, targets…)` validates (sender owns the power or is the host acting for a simulated owner, `CanUse`
evaluated on server truth incl. uses left / awake / chained / current state, target rules), then applies effects and
consumes the use in the same call. A rejected use silently resets the owner's power UI (no player-facing message) and logs `[POWER] rejected`.

## Boundaries & Constraints

**Always:**
- Behaviour-preserving for legitimate single uses: same effects, same verdicts, same sounds (now verdict-driven), same
  reveals visible to the same viewers.
- "Act-then-RPC" powers (Vision of the Impossible, Lack of Affection — `Power.cs:375-381` note) become single RPCs whose
  server body does validation + effect + consume.
- Passive / auto-trigger powers (server-driven) keep their server path; validation applies to player-initiated uses.
- Simulated bots: the host may act for an owner ≥ 100 (`IsLocalOrSimulated`), `GetSafeRpcTarget` on every targeted RPC.
- Copied/stolen one-shot despawn (`DespawnSpentCopyNextFrameServer`) keeps its deferral semantics.
- Every power keeps or gains a 2-NM test of its legit path (the PR #91 catalogue gives most of them).

**Decided (Poyo, 2026-10-04):**
- A rejected use gives **no message**: the owner's power UI silently resets to its normal state.

**Ask First:**
- Any power whose current client-side decision turns out to read information the server does not have (stop + report).

**Never:**
- Decide a gameplay outcome on a client.
- Execute a viewer-specific effect on the host for a remote viewer.
- Trust a client-supplied owner/sender id.

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| Embrace by a remote client, correct guess | Server decides success, target corrupted, reveal shown on the caster's board only, success sound on caster |
| Embrace with caster's replica holding a wrong role | Outcome still correct (server truth) |
| Double click on a 1-use power | Second use rejected, effect applied once, `powerUseLeft` = 0 |
| Click arrives after owner slept | Rejected, owner UI reset |
| Client targets an invalid target (forged RPC) | Rejected |
| Host acting for bot 102 | Accepted |
| Lack of Affection on a remote target | Server decides; reveal routed to the target viewer; contact sound on target client |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Powers/Power.cs:216-275` -- delete `RunClientDecisionEffects`/`EmitVerdictFromClient`/`ReportVerdictServerRpc`; add `ServerTryUse` validation helper; `:338-373` fold `OnUsedServerRpc` into the atomic path; guard `powerUseLeft` ≥ 0
- `Assets/Scripts/Characters/Powers/PEmbraceOfShadows.cs`, `PCursedVision.cs`, `PLackOfAffection.cs` -- server decisions; picker sounds from verdict
- All player-initiated power RPCs (see RPC inventory in the epic: `PBlessing`, `PBoundByInk`, `PCardsShuffling`, `PChainedByTheShadows`, `PClandestineObservation`, `PCorruptingMark`, `PDroolyHealing`, `PHighPriorityBounty`, `POmniscience`, `PPersonalBeacons`, `PReincarnation`, `PTruthChains`, `PVisionOfTheImpossible`, `PMarqueHurluberluges`) -- route through `ServerTryUse`
- `Assets/Scripts/Characters/Powers/Runtime/Executors/RevealInfoExecutor.cs:16-27`, `ChatLocalExecutor.cs` -- server-side: viewer-specific effects sent to the viewer (until NET-10 replaces reveals with the ledger)
- `Assets/Scripts/Characters/Character.cs:181-195` -- `CorruptPlayerServerRpc` / `HealPlayerServerRpc` become server-internal (no client entry)
- `Assets/Scripts/Domain/Powers/PowerContext.cs:22-36` -- `IsTrueLocalTarget` becomes server-computed routing info
- Tests: `Tests/PlayMode/OwnerLocalEffectBoundaryTests.cs`, power decision suites, PR #91 2-NM power suites

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Powers/PowerAuthorityTests.cs` -- NEW red-first 2-NM: (a) remote Embrace with a tampered caster replica role → wrong outcome today (red), correct after; (b) double-use RPC → applied twice today (red), once after; (c) use after sleep rejected; (d) forged sender rejected
- [ ] Atomic `ServerTryUse` + per-power migration (one commit-sized step per family: reveal powers, corruption powers, copiers, chat powers)
- [ ] Viewer routing for owner-local reveals/chat; verdict-driven picker sounds
- [ ] Existing power suites unchanged-green

**Acceptance Criteria:**
- Given any player-initiated power use, then the outcome is computed on the server from server state only.
- Given a duplicated or late use request, then the effect is applied at most once and never after the owner can no longer use the power.
- Given an owner-local effect for a remote caster, then it appears on that caster's client only, never on the host's.

## Verification

- PlayMode `PowerAuthorityTests` red → green; all power suites + `OwnerLocalEffectBoundaryTests` green; full suites green
- 2-build playtest (mandatory — gate like PR #75): every active power used once by a **remote client**, plus a fast double-click on a 1-use power
