---
title: 'NET-00 — Desync observability: tripwires + public-state digest'
type: 'feature'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
fixes: ['F19']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-network-sync-hardening.md'
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/technomancer-duplicate-card-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** When a player reports "my screen is different", nothing in `Player.log` says whether a desync happened,
in which system, or since when. Only `CharacterManager` has a tripwire (`[CHARLIST]`). We cannot measure the effect of
the following specs, and we cannot triage the next playtest report.

**Approach:** (1) a pure Domain digest that hashes a canonical projection of the **public** replicated state per named
component (roster, characters, character flags, game state; later roles/powers); (2) a server-driven `DesyncMonitor`
that, at quiet points, sends the server's per-component hashes to every client; each client recomputes, and a
**persistent** mismatch logs `[DESYNC]` with both projections, on the client AND (via report RPC) on the host;
(3) cheap tripwires on known-hazard reads (`[ROSTER]`, `[ROLE]`). Detection only — no auto-heal in this spec.

## Boundaries & Constraints

**Always:**
- Digest components are **public** state only (what every peer is supposed to hold identically). Per-viewer private
  knowledge (GameInfoRevealer, icon slices, chat channels) is out of the digest.
- Canonical projection: sort by clientId / NetworkObjectId, fixed field order, culture-invariant formatting. Pure
  Domain POCO (`CorruptionDuPortail.Domain`), EditMode-tested.
- Tolerate NGO ordering: a mismatch counts only if it **persists** across a re-check (client re-requests, server sends
  a fresh digest; two consecutive mismatches of the same component ⇒ `[DESYNC]`).
- Logs ship in player builds, `Debug.LogError`, one greppable tag per class, rate-limited (once per component per state).
- Simulated bots (≥100) are host-local: the host never compares against itself.

**Ask First:**
- Any on-screen indicator (even dev-only) — UI is design-owned.
- Turning detection into auto-resync (that is per-system, inside NET-01/04/07/10).

**Never:**
- Send private knowledge in a digest payload.
- Block or alter gameplay on a mismatch.
- Use `System.Threading.Tasks.Task` (UniTask only).

## I/O & Edge-Case Matrix

| Scenario | State | Expected | Error handling |
|---|---|---|---|
| Healthy | All components equal | No log | N/A |
| Transient lag | Client NV delta 1 tick behind at first check | Re-check passes → no log | N/A |
| Persistent roster divergence | Client lost an entry | `[DESYNC] component=Roster` on client + host, both projections dumped | Log once per component per state |
| Duplicate roster row | Two rows same clientId on a replica | `[ROSTER] duplicate clientId=X` once | Log, continue |
| Default role after intro | Local replica holds default Role for a real character after GameIntroduction | `[ROLE] character=X has no role` once | Log, continue |
| Host only | No remote clients | Monitor idle | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Domain/` -- NEW `DesyncDigest.cs` (pure): `ComputeComponentHashes(PublicStateProjection) → IReadOnlyDictionary<string, ulong>` + `Describe(component, projection)` for the dump
- `Assets/Scripts/Network/` -- NEW `DesyncMonitor.cs` (NetworkBehaviour, scene-placed in GameScene, `[SerializeField]` wired to CharacterManager / LobbyPlayerInfoHolder / GameManager — memory: scene-wirer, never resolve a peer manager in `OnNetworkSpawn`)
- `Assets/Scripts/GameLogic/GameManager.cs:369-382` -- `SwitchGameState`: raise a server-side "state settled" hook the monitor schedules from (≈1 s after `OnStartStateServer`)
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:117-129` -- `GetPlayerInfo`: `[ROSTER]` duplicate tripwire (removed again by NET-01 once the replica cannot duplicate)
- `Assets/Scripts/Characters/Character.cs:169-178` -- `GetRole`/`GetOwnerPseudo`: `[ROLE]` / `[ROSTER] missing` tripwires (missing pseudo is logged, display fallback is NET-03)
- `Assets/Scripts/Characters/CharacterManager.cs:165-172` -- existing `[CHARLIST]`; keep

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/DesyncDigestTests.cs` -- NEW: same projection in different input order ⇒ same hashes; one field differs ⇒ only that component differs; empty projection stable
- [ ] `Domain/DesyncDigest.cs` + `Domain/PublicStateProjection.cs` -- components v1: `Roster` (clientId, playerName, isReady), `Characters` (ownerClientId set), `CharacterFlags` (isChained, isCorrupted, isEliminated, isAwakened per owner), `GameState` (currentGameStateIndex). Extension point: NET-07 adds `Roles`, NET-08 adds `Powers`
- [ ] `Network/DesyncMonitor.cs` -- server: on settled hook, build projection, send `ReceiveDigestRpc(hashes)` to `ClientsAndHost` minus host; client: wait 3 network ticks, compare, on mismatch `RequestRecheckServerRpc`; server answers with fresh hashes to the sender (`RpcTarget.Single(sender)`); second mismatch ⇒ `[DESYNC]` dump + `ReportDesyncServerRpc(component, clientDump)` so the host log holds it too
- [ ] `Tests/PlayMode/Desync/DesyncMonitorTests.cs` -- NEW 2-NM: (a) healthy game state ⇒ zero `[DESYNC]`; (b) reflection-inject a divergent roster entry on the client replica ⇒ exactly one `[DESYNC] component=Roster` (LogAssert)
- [ ] Scene wiring in GameScene via Unity CLI (`set_serialized_field`), verify re-read (memory: serialized-field rewiring rule)

**Acceptance Criteria:**
- Given a healthy 2-NM game, when the monitor runs at every state transition, then no `[DESYNC]` is logged.
- Given a client replica whose roster differs persistently, when the monitor runs, then exactly one `[DESYNC] component=Roster` error is logged on the client and one on the host, each containing both projections.
- Given a transient one-tick lag, when the re-check passes, then nothing is logged.

## Design Notes

The digest is a tripwire, not an oracle: it proves *that* and *where* replicas diverged, which is exactly what the
current reports lack. Component hashes (not one global hash) make the log actionable. 64-bit FNV-1a over UTF-8 of the
canonical string is enough (non-adversarial). Payload ≈ 8 bytes × components per transition — negligible.

## Verification

- `unity command console_status` → no compile errors
- `unity command run_tests --mode EditMode --filter DesyncDigestTests`
- `unity command run_tests --mode PlayMode --filter DesyncMonitorTests` (red: inject test fails before monitor exists)
- Full EditMode + PlayMode suites green
- 2-build playtest (Poyo): grep both `Player.log` for `[DESYNC]` after a full game
