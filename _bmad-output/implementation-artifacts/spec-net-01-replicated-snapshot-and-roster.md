---
title: 'NET-01 — Full-snapshot replicated collection + migrate the player roster off NetworkList'
type: 'bugfix'
created: '2026-10-04'
status: 'draft'
epic: 'epic-network-sync-hardening.md'
fixes: ['F1']
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-network-sync-hardening.md'
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/technomancer-duplicate-card-investigation.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-ready-system.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `LobbyPlayerInfoHolder.playerInfos` is an NGO `NetworkList<PlayerInfo>`. A client that synchronizes in the
same tick as another player's `Add` receives that entry twice (NGO #3280). The list is then mutated **by index**: ready
toggles (`playerInfos[i] = …`, `LobbyPlayerInfoHolder.cs:255`), profile updates (`:195`) and leaves (`RemoveAt`, `:85`).
On the diverged client every index write lands on the wrong row → other players' entries are overwritten or removed →
**missing pseudos for some players on some clients only** (playtest 2026-10-04, cards 3/4/5/7).

**Approach:** Introduce one reusable primitive — a **full-snapshot replicated collection**: a `NetworkVariable` holding an
immutable, IEquatable, INetworkSerializable snapshot (array of entries). A change replicates the whole value; NGO's
`NetworkVariable` overrides `WriteFieldSynchronization`, so a late joiner can never double-apply. Migrate the roster to
it with **keyed upsert semantics** (unique `playerClientId`). Keep `LobbyPlayerInfoHolder`'s public API so consumers
change minimally; replace `playerInfos.OnListChanged` with a single `onRosterChanged` event.

## Boundaries & Constraints

**Always:**
- Proto gate FIRST: a 2-NM test proving the primitive cannot duplicate when the server mutates in the same tick a
  client synchronizes, and that a value change is observed by the client (`OnValueChanged` fires, value equal). If NGO
  serialization of the generic snapshot misbehaves, fall back to concrete non-generic structs (`PlayerRoster`) — the
  semantics, not the generic, are the deliverable.
- Server-only writes; copy-modify-assign (never mutate the held array in place — NGO change detection compares values).
- Uniqueness by `playerClientId` enforced in the snapshot builder (upsert); order = insertion order of first join
  (stable, matches today's display order).
- `isReady` stays owned exclusively by `SetReadyServer` (carry-over rule from `UpdatePlayerInfo`, `:184-188`).
- Size bound: `MAX_PLAYERS` entries (`GameValues`), assert on overflow.
- Every consumer of `playerInfos` migrated in the same commit (list in Code Map); `StaticSingletonCensusGuardTests` /
  DI guards stay green.

**Ask First:**
- Changing `PlayerInfo` wire fields (Steam-reserved fields stay).
- Any ordering change of the lobby player list.

**Never:**
- Keep a `NetworkList` for the roster "in parallel".
- Dedup by name; key is `playerClientId` only.
- Touch `GetSafeRpcTarget` / `IsLocalOrSimulated` semantics.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected | Error handling |
|---|---|---|---|
| Burst join | 3 clients join within one tick window | Every replica holds exactly N unique entries | N/A |
| Ready toggles after burst | Each player toggles ready twice | All replicas identical to server after each toggle | N/A |
| Leave | Player B disconnects | B removed on every replica, nobody else touched | N/A |
| Re-save same client | Second `SavePlayerInfo` for an existing id | Upsert (name updated, isReady preserved), count unchanged | N/A |
| Unknown id update | `UpdatePlayerInfo` for absent id | No-op | N/A |
| Overflow | > MAX_PLAYERS adds | Rejected + `[ROSTER] overflow` error | Log, ignore |
| Bot | `AddDebugPlayer(≥100)` | Entry present, auto-ready, host-local reads OK | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:23` -- `NetworkList<PlayerInfo> playerInfos` → `NetworkVariable<PlayerRosterSnapshot> _roster` + `IReadOnlyList<PlayerInfo> Players` + `event Action onRosterChanged`
- `LobbyPlayerInfoHolder.cs:74-88,110-115,173-199,201-215,237-295` -- remove/add/update/ready/AllReady/ReadyCount re-expressed on the snapshot (pure builder does the mutation)
- `Assets/Scripts/Network/Player/PlayerInfo.cs` -- unchanged wire struct (entry type)
- NEW `Assets/Scripts/Network/Replication/` -- `SnapshotList<T>` (or `PlayerRosterSnapshot`) + pure `RosterBuilder` (Domain-side if it has no NGO dep: Upsert/Remove/SetReady/Find)
- Consumers to migrate: `Avatars/AvatarNameplate.cs:101-125,202-204`, `Network/ConnectedPlayerPanel.cs:20-40`, `UI/LobbyRoles/GameLobbyRolesDataSource.cs:50-87`, `UI/SpawnPanels/PlayerButtonObject.cs:25`, `GameLogic/GameStates/LobbyState.cs:97-122`, `Extensions/UlongExtensions.cs:17-27`, `Characters/Character.cs:174-178`
- Tests to migrate: `Tests/PlayMode/LobbyPlayerInfoHolderTests.cs`, `Tests/PlayMode/GameLogic/GameStates/LobbyReadyAutoStartTests.cs`, `LobbyStateStartGuardTests.cs`, `Tests/PlayMode/Infra/TestStaticReset.cs`, `CorruptionTests.cs`, `EntrapmentPowerTests.cs`
- Reference: `Library/PackageCache/com.unity.netcode.gameobjects@*/Runtime/NetworkVariable/Collections/NetworkList.cs:291-334` (index-based deltas), NetworkVariable `WriteFieldSynchronization`

## Tasks & Acceptance

**Execution:**
- [ ] `Tests/PlayMode/Replication/SnapshotListProtoTests.cs` -- NEW 2-NM proto gate: (a) server value change replicates, `OnValueChanged` fires on client; (b) server mutates in the same frame a 2nd client connects → that client holds exactly the server value (no dup); (c) 10 consecutive changes in one frame → client converges to the last
- [ ] `Tests/EditMode/RosterBuilderTests.cs` -- NEW: upsert keeps order & isReady, remove by id, set-ready only touches one row, overflow rejected, duplicates impossible by construction
- [ ] `Tests/PlayMode/Replication/RosterDivergenceTests.cs` -- NEW red-first: 2-NM, burst-join + ready toggles + leave → every replica == server roster (fails today via the duplicate+index path: reproduce with reflection-injected duplicate on the old NetworkList, assert wrong-row overwrite)
- [ ] Implement primitive + roster migration + consumers, remove `[ROSTER] duplicate` tripwire from NET-00 (now impossible), keep `[ROSTER] missing`
- [ ] `DesyncDigest` `Roster` component reads the new snapshot

**Acceptance Criteria:**
- Given 3 clients joining within one tick, when every player toggles ready twice and one leaves, then every replica's roster equals the server's (same ids, names, ready flags, order).
- Given the old NetworkList with an injected duplicate, when a ready toggle runs, then the red test demonstrates the wrong-row overwrite (documents the bug); with the snapshot it cannot occur.
- Given any consumer that listened to `playerInfos.OnListChanged`, when the roster changes, then it is refreshed via `onRosterChanged` exactly once per change.

## Design Notes

Payload: `PlayerInfo` ≈ 2×64 + 2×8 + 1 bytes → ≈ 150 B; 15 players ≈ 2.3 KB per change, only on join/leave/ready/rename
(a handful per lobby). Bandwidth irrelevant; correctness absolute. The same primitive is reused by NET-04
(characters/avatars) and NET-11 (chat channels), so the proto gate pays off three times.

## Verification

- `unity command run_tests --mode PlayMode --filter SnapshotListProtoTests` (gate — stop and report if red)
- `unity command run_tests --mode EditMode --filter RosterBuilderTests`
- `unity command run_tests --mode PlayMode --filter RosterDivergenceTests` red → green
- Full EditMode + PlayMode green
- 2-build playtest: 4 players, 3 joins in 2 s, everyone toggles ready, one leaves and rejoins → identical lobby list everywhere, no `[DESYNC]`/`[ROSTER]`
