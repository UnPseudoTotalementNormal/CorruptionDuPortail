---
title: 'NET-07 — A character''s role is replicated state (RoleID NetworkVariable), not an RPC'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-04-characters-avatars-snapshot.md', 'spec-net-06-state-transition-ordering.md']
fixes: ['F12']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Roles reach clients through `CharacterManager.GiveRoleToCharacterRpc(SendTo.Everyone)` (`CharacterManager.cs:348-362`).
Each peer awaits `GetCharacterAsync`, a promise resolved only by `RegisterSpawnedCharacter`, which fires **once** at
Character spawn (`Character.cs:53-62`, `CharacterManager.cs:85-106`). If the Character spawned on that client before it
appeared in the list replica, the promise is created after the only resolve call → **orphaned forever, the role is never
applied** — exactly card 3 of the 2026-10-04 screenshot (default `Role`: empty name, faction 0 = anomaly/red, portrait 0 →
white). For fake characters it is certain: `RoleAttributionState` spawns them and sends their role in the same frame
(`RoleAttributionState.cs:82-90`), the list delta arrives after the RPC, and fakes never call `RegisterSpawnedCharacter`.
Each peer also re-broadcasts `UpdateRoleRpc` + `CheckForPowersRpc` (N² fan-out). The whole `Role` crosses the wire with
assembly-qualified type names (`Role.cs:96-120`), fragile across builds.

**Approach:** The authoritative identity of a role is its `RoleID` (unique per `RoleDataObject`). Add
`NetworkVariable<RoleID> roleId` on `Character` (server-write). Every peer builds its local `Role` from the shared
registry (`GameAssetHolder.roleDataObjects`, cloned like attribution does) on spawn **and** on `OnValueChanged`, sets
`ownerClientId`, and raises `onRoleUpdated`. Delete `GiveRoleToCharacterRpc`, `UpdateRoleRpc`, `AskForRoleUpdateRpc`,
`GetCharacterAsync` and its promises. Late, reordered or duplicated messages can no longer lose a role.

## Boundaries & Constraints

**Always:**
- `RoleID` uniqueness across `GameAssetHolder.roleDataObjects` enforced by an EditMode guard test (and the registry
  covers every role `RoleAttributionState` can draw, fakes included).
- Server keeps building the role exactly as today (`ApplyRole`: Clone → powers → owner); it additionally writes
  `roleId.Value` **after** granting powers.
- Client-side role object is rebuilt only when `roleId` changes (identity stable otherwise — `role.powers` handled by NET-08).
- "No role yet" is an explicit state (`roleId` sentinel / `HasRole`) — consumers that read `role` during the lobby keep
  their null/empty guards; `[ROLE]` tripwire (NET-00) fires if a real character has no role after GameIntroduction.
- `onRoleUpdated` fires on every peer, once per change (Card, CharactersBarObject, PowersBar subscribe today).
- Simulated bots: host builds them like any other character (no special path).

**Ask First:**
- If any runtime mutation of role *fields* (not powers) is discovered during implementation (audit says none: grep
  `role.<field> =` hits only attribution) — stop and report.

**Never:**
- Send the full `Role` over the wire anymore.
- Rely on `RegisterSpawnedCharacter` to deliver state.

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| Normal attribution | Every peer: role name/faction/portrait correct before GameIntroduction shows the role |
| Fake characters | Fakes have their role on every peer (red today) |
| Character in list after RPC would have arrived | Role still applied (NV is state) |
| Duplicate/reordered messages | Irrelevant — idempotent rebuild from roleId |
| Unknown roleId (bad data) | `[ROLE] unknown roleId=X` error, role stays empty, no exception |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Character.cs:17,33,45-66,100-131,169-172` -- `roleId` NV, spawn/OnValueChanged rebuild, delete role RPCs
- `Assets/Scripts/Characters/CharacterManager.cs:85-106,348-362,366-386` -- delete `GetCharacterAsync`, promises, `GiveRoleToCharacterRpc`; `AskForUpdateAllCharactersRpc` keeps only the list refresh event (or is removed if unused after NET-08)
- `Assets/Scripts/Characters/ICharacterCommand.cs:33` -- interface surface update
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:139-158` -- write `roleId` instead of RPC
- `Assets/Scripts/GameAssetHolder.cs:10-40` -- `GetRoleDataObject(RoleID)` lookup (cached dictionary)
- `Assets/Scripts/Characters/Role.cs:89-130` -- `NetworkSerialize` kept only if another wire path still needs it (grep `Role` RPC params: `PBlessing`, `PChainedByTheShadows`, `PDroolyHealing`, `PVisionOfTheImpossible` send `Role` — migrate those params to `RoleID` in this spec)
- Consumers: `Board/Card.cs:162-165,396-399`, `Board/UI/CharacterBar/CharactersBarObject.cs:150-195`, `Board/UI/PowerBar/PowersBar.cs:45-60`, `GameStates/GameIntroductionState.cs:84-90`
- `DesyncDigest` -- add `Roles` component (owner → roleId)

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/Editor/RoleRegistryGuardTests.cs` -- NEW: unique RoleID per RoleDataObject; every attributable role resolvable
- [ ] `Tests/PlayMode/Replication/RoleReplicationTests.cs` -- NEW red-first 2-NM: (a) fake characters' roles on the client after attribution (red today); (b) character registered before list replica → role still applied; (c) every client role equals the server's
- [ ] Implement NV + rebuild + deletions; migrate `Role`-typed RPC params to `RoleID`
- [ ] Remove `CheckForPowersRpc` call sites tied to role delivery (power list itself → NET-08)

**Acceptance Criteria:**
- Given role attribution with fakes, when GameIntroduction starts, then every peer holds the server's role for every character (name, faction, portrait, roleID).
- Given any message ordering, then no client can end a game start with a default (empty) role on a real or fake character.
- Given the codebase, then no RPC carries a `Role` object.

## Verification

- EditMode guard + PlayMode new suite red → green; full suites green (victory goldens, role reveal, copiers suites)
- 2-build playtest: 6+ players with fakes; every card/role bar identical on every client; zero `[ROLE]` / `[DESYNC] component=Roles`
