# Investigation: late-joining client keeps a ghost Character (ownerClientId = FAKE) and misses a player

## Hand-off Brief

1. **What happened.** On clients whose GameScene synchronization took far longer than NGO's `SpawnTimeout` (10 s), the
   in-game tripwire reported a persistent `[DESYNC] component=Characters` from the lobby to the end of the game: one
   Character shows `ownerClientId = 18446744073709551615` (`GameValues.FAKE_CLIENT_ID`, the NetworkVariable default) and
   a real player is missing (Confirmed, two runs).
2. **Where the case stands.** Concluded, root cause Confirmed with a deterministic repro (2/2): NGO defers a
   synchronizing client's object creations without limit but purges the deltas / parent syncs for those objects after
   `SpawnTimeout`; `CharacterManager.AddNewCharacter` writes owner, parent and roster after the spawn, so a player
   seated during a long load is lost on the loading client.
3. **What's needed next.** Fix applied in spec `spec-late-joiner-character-desync.md`: every client start path raises
   `SpawnTimeout` to the join window (`JoinHandshake.DeferredMessageWindowSeconds`); regression = EditMode
   `ClientConnectionPayloadTests` + autoplay `join-spawn-during-load`.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | N/A (found by the autoplay campaign) |
| Date opened      | 2026-10-05 |
| Status           | Concluded — root cause Confirmed (deterministic repro), fix in spec-late-joiner-character-desync |
| System           | Windows 11, Unity 6000.5.0f1, NGO 2.12 (`com.unity.netcode.gameobjects@aaabf07f880c`), UTP loopback, dev build, 8 player processes on one machine (heavily loaded: scene loads of 31-47 s) |
| Evidence sources | autoplay run folders (journals + player logs), source code |

## Problem Statement

From the campaign: "on one client the Characters projection has 18446744073709551615 in place of a real player, and
another real player is missing; CharacterFlags/Powers also mismatch; same scenarios pass on rerun → intermittent."
Treated as hypothesis; verified below.

## Evidence Inventory

| Source | Status | Notes |
| ------ | ------ | ----- |
| `AutoplayRuns/net-20261005-204241-c7-seed81` (chat-private, FAIL) | Available | host.log + 7 client logs + journals |
| `AutoplayRuns/net-20261005-204818-c7-seed51` (client-owner-local-powers, FAIL) | Available | same |
| `AutoplayRuns/net-20261005-210700-c7-seed81` (chat-private rerun, PASS) | Available | control run |
| NGO source (`Library/PackageCache/com.unity.netcode.gameobjects@aaabf07f880c`) | Available | deferred-message purge |
| Server-side ordering of CreateObject vs deltas for a synchronizing client | Missing | see Missing Evidence |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | Correlate purged deferred messages with desynced clients | High | Done | Finding 3 |
| 2 | Map purged object ids (1, 7, 10, 13, 14, 18, 21-24) to objects | Medium | Open | needs a spawn log of NetworkObjectIds |
| 3 | Deterministic repro with `stall-load` + a client joining during the hold | High | Open | Reproduction Plan |
| 4 | Check every post-Spawn NetworkVariable write on join (powers, lobby info, avatars) | Medium | Open | same failure class |

## Timeline of Events (run seed81)

| Time (client real s) | Event | Source | Confidence |
| -------------------- | ----- | ------ | ---------- |
| 35.7 | client id 4 connected (load 31.4 s) | `…client6-seed81/events.ndjson` | Confirmed |
| 45.4-48.6 | clients 2, 3, 1, 7 connected (loads 42-43 s) | journals | Confirmed |
| 51.1 | client id 6 connected after a 45.5 s load | `…client2-seed81/events.ndjson` | Confirmed |
| 51.6 | client id 5 connected (load 46.8 s) | `…client3-seed81/events.ndjson` | Confirmed |
| during load | client 6 purges `NetworkVariableDeltaMessage` (ids 1, 10, 13) and `ParentSyncMessage` (21, 22) after 10 s | `…/client2.log` (lines ~60-230) | Confirmed |
| lobby → end | `[DESYNC] … client=6 persistent mismatch` in every phase | `…/client2.log:231` onwards, `host.log` | Confirmed |

## Confirmed Findings

### Finding 1: the desync is persistent, not a sampling artefact

**Evidence:** `AutoplayRuns/net-20261005-204241-c7-seed81/client2.log:231-1553` — `persistent mismatch` for
CharacterFlags / Characters / Powers / Roles in states 0:0 … 1:8.

**Detail:** the tripwire re-checks after the state settles; the mismatch survives the whole game.

### Finding 2: the ghost value is the NetworkVariable default

**Evidence:** host.log `[DESYNC] component=Characters` — client projection `0, 18446744073709551615, 2, 1, 3, 7, 6`
vs host `0, 4, 2, 1, 3, 7, 6, 5`; `Assets/Scripts/Characters/Character.cs:19`
`ownerClientId = new(GameValues.FAKE_CLIENT_ID)`.

**Detail:** the client never received the server's write of `ownerClientId` for that Character.

### Finding 3: desync ⇔ purged NetworkVariable deltas, on every client, in every run

**Evidence:** NGO warning `[Deferred OnSpawn] Messages were received for a trigger of type NetworkVariableDeltaMessage
associated with id (N), but the NetworkObject was not received within the timeout period 10 second(s)` counted per
client log:

| Run | Clients with NV / ParentSync purges | Clients with desync |
| --- | --- | --- |
| seed81 FAIL | client2 (id 6), client3 (id 5) | the same two |
| seed51 FAIL | client1, 3, 5, 6, 7 | the same five |
| seed81 rerun PASS | none (only `RpcMessage` purges) | none |

**Detail:** clients that only lost `RpcMessage`s stayed in sync.

### Finding 4: the server writes the Character's identity after spawning it

**Evidence:** `Assets/Scripts/Characters/CharacterManager.cs:411` `InstantiateAndSpawn(_characterPrefab, …)`, then
`:415-419` `TrySetParent` (→ ParentSyncMessage), `:423` `ownerClientId.Value = _clientId`, `:426`
`networkedCharacters.Value = …WithAdded(…)`. The method's own comment: "todo: create all characters on start, and
simply change ownerID when starting the game, to not have spawn issues".

### Finding 5: the purge window is the configured SpawnTimeout

**Evidence:** `Assets/Scenes/BootScene.unity:620` `SpawnTimeout: 10`; NGO `DeferredMessageManager.CleanupStaleTriggers`
(`…/Runtime/Messaging/DeferredMessageManager.cs:84`) → `PurgeTrigger`.

## Deduced Conclusions

### Deduction 1: a Character spawned while a client is still synchronizing loses its post-spawn state on that client

**Based on:** Findings 2-5.

**Reasoning:** the spawn is serialized with the default `ownerClientId`; the identity, parent and roster entry follow
as separate messages. A client whose scene load lasts longer than 10 s after those messages arrive defers them (the
object does not exist for it yet), then NGO purges them; when the object is finally spawned on that client it carries
the default value and nothing re-sends the delta (the server's value no longer changes).

**Conclusion:** ghost Character (`FAKE_CLIENT_ID`) and, if the `networkedCharacters` delta (object id 1 in seed81 is
the first scene object, likely CharacterManager) is purged too, a missing roster entry. Intermittent because it needs
a load longer than SpawnTimeout overlapping another player's join — rare on a real machine, frequent with 8 processes
on one laptop, but a slow PC or disk can hit it in production.

## Hypothesized Paths

### Hypothesis 1: the purged deltas are the cause of the ghost / missing player

**Status:** Confirmed

**Theory:** Deduction 1.

**Supporting indicators:** Finding 3 (perfect correlation over 3 runs, 14 clients); Finding 4 (ordering in code).

**Would confirm:** deterministic repro: hold a client's load > 10 s while another client joins → desync N/N; with
the hold < 10 s → no desync.

**Would refute:** a desync with no NV purge, or NV purges without desync.

**Resolution:** autoplay `join-spawn-during-load` (client 1 held 25 s by stall-load, host seats a bot 5 s into the
load) failed 2/2 (`AutoplayRuns/net-20261005-220124-c3-seed94`, `…-220242-c3-seed94`): the bot (id 100) shows
`18446744073709551615` on the loading clients and the last player is missing; every loading client logged
`nvPurged=2 parentPurged=1`. A first attempt that relied on another real client joining during the hold did not
reproduce (joins completed only when the held load was released), which is why the host-side bot trigger was added.
Refutation pass: no desync without NV purges and no NV purge without desync in any of the 5 runs examined.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| NetworkObjectId → object map for ids 1, 7, 10, 13, 14, 18, 21-24 | confirms which objects (CharacterManager, Characters, powers) lost deltas | log spawned ids on the host in the repro run |
| Whether NGO sends CreateObject for objects spawned during a client's synchronization before or after its scene load completes | explains why the object arrives after the deltas | read NGO SceneEventMessage / CreateObject handling, or trace in the repro |

## Source Code Trace

| Element | Detail |
| ------- | ------ |
| Error origin | `Assets/Scripts/Characters/CharacterManager.cs:411-426` (`AddNewCharacter`): spawn, then parent + identity + roster writes |
| Trigger | a client joins (server adds its Character) while another client is still synchronizing GameScene |
| Condition | the synchronizing client's load outlasts `SpawnTimeout` (10 s) after receiving the deltas |
| Related files | `Assets/Scripts/Characters/Character.cs:19`, `Assets/Scenes/BootScene.unity:620`, NGO `DeferredMessageManager.cs:84` |

## Conclusion

**Confidence:** High (Confirmed root cause, deterministic repro)

NGO (`CreateObjectMessage.Handle` → `NetworkSceneManager.ShouldDeferCreateObject` / `DeferCreateObject`) holds object
creations for a client that is still synchronizing until the sync ends, while `NetworkVariableDeltaMessage` and
`ParentSyncMessage` for those objects go to `DeferredMessageManager`, purged after `NetworkConfig.SpawnTimeout`
(`BootScene.unity:620`, 10 s). The Character spawn payload carries the default owner; the owner / parent / roster
writes that follow (`CharacterManager.cs:415-426`) are lost on any client whose load outlasts the window.

### Fix direction

Keep the deferred-message window open for the longest load the host tolerates: `ClientConnectionPayload.Apply` (the
documented hook called before every `StartClient`) raises `SpawnTimeout` to `JoinHandshake.DeferredMessageWindowSeconds`
(join cap 90 s + 30 s). This covers every NetworkVariable, not only the Character. Optional hardening (not needed for
the fix): write the Character's owner before `Spawn` so the spawn payload is already correct (the method's own todo).

## Reproduction Plan

Autoplay `play-net` with 3 clients: client 1 `-autoplay-stall-load 25` (its load held 25 s after synchronization
starts, > SpawnTimeout); client 2 connects only after client 1's synchronization started (a connect delay lever), so the
host spawns client 2's Character during client 1's hold. Expect on client 1: NV / ParentSync purges + persistent
`[DESYNC] component=Characters` with a FAKE owner; control: `stall-load 5` → no desync.

## Side Findings

- `RpcMessage` purges for an early object id happen on every client in every run (also in PASS runs): benign so far.
- Scene loads of 31-47 s show the machine was saturated during the campaign (8 players on one laptop).
