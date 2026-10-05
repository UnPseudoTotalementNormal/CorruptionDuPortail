---
title: 'NET-08 — A character''s power list is a projection of replicated ownership; runtime passive flag replicated'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-07-role-as-replicated-state.md']
fixes: ['F13', 'F14']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** On clients, `role.powers` (33 readers: power bar, role card, awakening, copiers, targeting) is a
hand-maintained `List<Power>` edited by three event RPCs: `Character.CheckForPowersRpc` (adds from
`GetComponentsInChildren`, never removes stale entries, `Character.cs:115-131`), `PowerManager.RemovePowerFromCharacterPowerListRpc`
(`PowerManager.cs:143-170`, races the despawn) and `Power.OnReparentedClientRpc` (`Power.cs:496-526`, silently skips
when the character is not yet in the roster). It converges only if parent-sync, spawn, despawn and those RPCs arrive in a
favourable order; copiers (Incomplet, Ugues, Luma) stress exactly those paths. Separately, `PReincarnation` flips the
plain field `isPassive` via an Everyone RPC (`PReincarnation.cs:113-117`) — event-state that a missed/reordered message
leaves wrong forever (24 readers of `isPassive`).

**Approach:** The truth already replicates: every `Power` has `NetworkVariable<ulong> ownerClientId`. Make `role.powers`
a **projection**: a per-NetworkManager power registry (spawn/despawn hooks + `ownerClientId.OnValueChanged`) rebuilds
each character's list deterministically (order = server-assigned `grantOrder` NV), and raises the existing
`onPowersUpdated` / `onPowerReparented` events from those state changes. Delete the three list-editing RPCs. Replace the
runtime `isPassive` mutation with `NetworkVariable<bool> passiveOverride` + an `IsPassive` accessor.

## Boundaries & Constraints

**Always:**
- Projection identical on server and clients (server uses the same registry — one code path).
- Order: `NetworkVariable<int> grantOrder` assigned by the server at grant (monotonic per session); tie → NetworkObjectId.
- `authoredIsPassive` / `BaseIsPassive` semantics (presentation, copier filters) unchanged.
- Despawned / destroyed powers can never linger (projection only lists spawned, alive powers).
- Events fire once per effective change (no per-frame pump — memory: `GetCharacters(true)` pump pitfall).

**Ask First:**
- Any power-bar ordering change visible to players (should be none: grant order == today's add order on a healthy client).

**Never:**
- Keep any RPC that mutates `role.powers`.
- Mutate `isPassive` at runtime after this spec (field stays authored-only).

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| Role attribution | Each character lists its powers on every peer, same order |
| Incomplet reincarnation grants | Granted copies appear on owner's list everywhere, hidden-from-rolecard flag respected |
| Ugues steals 3 at start | Victim loses, Ugues gains, on every peer |
| One-shot copy spent → despawn | Disappears everywhere, no null husk |
| Reparent before character in roster | Appears once the character resolves (projection retries) |
| Reincarnation post-use | `IsPassive` true on every peer, survives any message order |

</frozen-after-approval>

## Code Map

- NEW `Assets/Scripts/Characters/Powers/Runtime/PowerRegistry.cs` -- per-NM registry, projection by owner, events
- `Assets/Scripts/Characters/Powers/Power.cs:35,42-60,68,120-135,181-200,485-526` -- register/unregister on spawn/despawn, `grantOrder`, `passiveOverride`, `IsPassive`; delete list edits in `OnReparentedClientRpc` (keep server `OnReparentedServer` NV write)
- `Assets/Scripts/Characters/Character.cs:115-136` -- delete `CheckForPowersRpc`; `role.powers` fed by registry
- `Assets/Scripts/Characters/Role.cs:28` -- `powers` stays the read surface (filled by the projection)
- `Assets/Scripts/GameLogic/PowerManager.cs:109-170` -- delete `RemovePowerFromCharacterPowerListRpc`
- `Assets/Scripts/Characters/CharacterManager.cs` (`GivePowerToCharacter`, `RemovePowerFromCharacter`) -- assign `grantOrder`
- `Assets/Scripts/Characters/Powers/PReincarnation.cs:17,113-117` -- `SetPassive` writes `passiveOverride` server-side
- `isPassive` readers (24) → `IsPassive`
- `DesyncDigest` -- add `Powers` component (owner → [powerName, powerUseLeft, isCopied])

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Replication/PowerProjectionTests.cs` -- NEW red-first 2-NM: (a) reparent while the target character is unresolved on the client → today the power is missing from the new owner's list (red), after fix present; (b) Ugues steal at start; (c) spent copy despawn; (d) reincarnation passive flip seen on client
- [ ] Implement registry + projection, delete RPCs, `IsPassive` migration
- [ ] Run copier suites (`StolenPowerSelectorTests`, Incomplet/Ugues/Luma PlayMode suites) unchanged-green

**Acceptance Criteria:**
- Given any sequence of grant / steal / reparent / despawn, then every peer's `role.powers` for every character equals the server's (same powers, same order).
- Given Reincarnation used, then `IsPassive` is true on every peer.
- Given the codebase, then no RPC edits `role.powers`.

## Verification

- PlayMode new suite red → green; copier + power suites green; full suites green
- 2-build playtest: game with Incomplet + Ugues + Luma; role cards / power bars identical on every client; zero `[DESYNC] component=Powers`
