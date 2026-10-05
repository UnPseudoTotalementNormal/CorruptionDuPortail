---
title: 'NET-04 — Characters and avatars lists on full snapshots'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
depends_on: ['spec-net-01-replicated-snapshot-and-roster.md']
fixes: ['F5', 'F6']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-dedup-characters-cache.md'
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/technomancer-duplicate-card-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `CharacterManager.networkedCharacters` and `AvatarManager._avatars` are `NetworkList<NetworkBehaviourReference>`
written with `Add` on client connect and `RemoveAt(index)` on a lobby leave (`CharacterManager.cs:445,467-469`,
`AvatarManager.cs:248,270-272`). Burst joins duplicate an entry on a synchronizing client (NGO #3280 — the technomancer
incident); the `[CHARLIST]` dedup hides the duplicate in the projection but the replica stays diverged, so the next
index-based `RemoveAt` removes the **wrong** character/avatar on that client. Both lists also define **order**: card
order on the board and the avatar seat index (`AvatarManager` "stable index = position in the replicated list") — a
diverged replica seats players differently and orders cards differently per client.

**Approach:** Move both lists onto the NET-01 full-snapshot primitive holding `NetworkObjectId`s (resolved through the
owning `NetworkManager.SpawnManager`, memory: never `TryGet` without NM in 2-NM). Keep the existing dirty-until-resolved
projection cache. Server mutates by id (add-if-absent, remove-by-id). The `[CHARLIST]` dedup stays as a tripwire only.

## Boundaries & Constraints

**Always:**
- Resolution via `NetworkManager.SpawnManager.SpawnedObjects[id]` of the manager's own NM (reference
  `reference_char_replica_resolution_multi_nm`).
- Preserve order semantics: insertion order, removal compacts (same as today on a healthy replica).
- Unresolved ids keep the cache dirty (unchanged tolerance for spawn-in-flight).
- `GetCharacters()` defensive copy + `triggerUpdate` semantics unchanged (memory: `GetCharacters(true)` per-frame pump).

**Ask First:**
- Seat stability on disconnect (today a leave re-indexes later seats — unchanged by this spec).

**Never:**
- Dedup by `ownerClientId` (transient `FAKE_CLIENT_ID` mid-replication).
- Change spawn/despawn order (list write before `Despawn`).

## I/O & Edge-Case Matrix

| Scenario | Expected |
|---|---|
| Burst join (3 in one tick window) | Every replica: N characters, N avatars, same order as server |
| Lobby leave after burst join | The leaver — and only the leaver — disappears on every replica |
| Fake characters at role attribution | Present in snapshot; resolved once spawned |
| Spawn in flight | Id unresolved → skipped, cache dirty, appears when spawned |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/CharacterManager.cs:120-190` -- cache rebuild from snapshot ids; `OnListChanged` → `OnValueChanged`
- `CharacterManager.cs:421-452` (`AddNewCharacter`), `:454-480` (`RemoveCharacter`) -- id-based snapshot writes
- `Assets/Scripts/Avatars/AvatarManager.cs:86-150,195-280` -- same migration; seat index from snapshot order
- `Assets/Scripts/Tests/PlayMode/Characters/CharacterManagerDedupTests.cs` -- adapt (duplicate injection now targets the projection input)
- `Assets/Scripts/Tests/PlayMode/Avatars/AvatarSpawnTests.cs` -- keep green (known flake recorded in deferred-work.md:131)

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Replication/CharacterListDivergenceTests.cs` -- NEW red-first 2-NM: inject a duplicate on the client's old NetworkList replica, server `RemoveCharacter(B)` → client loses C instead of B (documents bug); after migration, same scenario → only B removed
- [ ] Same for avatars (seat index identical on host and client after burst join + leave)
- [ ] Migrate both managers; `DesyncDigest.Characters` reads the snapshot

**Acceptance Criteria:**
- Given a burst join and a lobby leave, then every replica holds exactly the server's characters and avatars in the server's order.
- Given any replica, then card order and seat indices are identical to the host's.

## Verification

- PlayMode new suites red → green; `CharacterManagerDedupTests`, `AvatarSpawnTests`, full suites green
- 2-build playtest: 3 quick joins + 1 lobby leave → same card order and seats on every screen
