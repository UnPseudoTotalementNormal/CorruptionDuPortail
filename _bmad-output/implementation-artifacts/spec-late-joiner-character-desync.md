---
title: 'Late-joiner Characters desync: keep the deferred-message window open for the whole join'
type: 'bugfix'
created: '2026-10-05'
status: 'done'
baseline_commit: '8265e82dc4a5cd69d8475a8791ae5a6d17f9ea77'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/late-joiner-character-desync-investigation.md'
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A client whose GameScene synchronization lasts longer than NGO's `SpawnTimeout` (10 s) loses every
NetworkVariable delta / parent sync sent for objects it has not spawned yet: NGO defers object creation until the sync
ends (no time limit) but purges the deltas after `SpawnTimeout`. A player seated during that load (`AddNewCharacter`:
spawn, then owner / parent / roster writes) ends up as a ghost (`ownerClientId = FAKE_CLIENT_ID`) and a real player
goes missing, for the whole game (persistent `[DESYNC] component=Characters`). Reproduced 2/2 with
`join-spawn-during-load` (stall-load 25 s + a bot seated during the load).

**Approach:** every client start path widens NGO's deferred-message window to cover the longest load the host
tolerates (`JoinHandshake.SyncTotalTimeoutSeconds`, 90 s, + margin), in the single documented client-start hook
`ClientConnectionPayload.Apply`. Deferred deltas are then replayed in order once the late objects spawn, for every
NetworkVariable, not only the Character's owner.

## Boundaries & Constraints

**Always:** red-first EditMode test; the window derives from `JoinHandshake.SyncTotalTimeoutSeconds` (one source of
truth); the autoplay scenario `join-spawn-during-load` passes after the fix (and failed before).

**Ask First:** changing NGO package code; restructuring `AddNewCharacter` (spawn order) beyond this fix.

**Never:** lower the host's 90 s join cap; touch the BootScene NetworkManager asset by hand.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Load > 10 s, player seated meanwhile | stall-load 25, spawn-during-load 5 | every client sees every owner and the full roster, 0 desync | N/A |
| Normal join | fast load | unchanged | N/A |
| Load past the 90 s cap | kicked by the host | unchanged (kick) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Network/ClientConnectionPayload.cs` -- single client-start hook (called before every `StartClient`).
- `Assets/Scripts/Network/JoinHandshake.cs:36` -- `SyncTotalTimeoutSeconds = 90f`, the host's join cap.
- `Assets/Scripts/Characters/CharacterManager.cs:404-433` -- `AddNewCharacter` (spawn, then writes): the victim path.
- `Assets/Scenes/BootScene.unity:620` -- serialized `SpawnTimeout: 10` (left as is; overridden at client start).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Tests/Editor/ClientConnectionPayloadTests.cs` -- red-first: after `Apply`, `NetworkConfig.SpawnTimeout >= JoinHandshake.SyncTotalTimeoutSeconds`.
- [x] `JoinHandshake.cs` -- `DeferredMessageWindowSeconds` (= cap + 30 s margin), documented.
- [x] `ClientConnectionPayload.Apply` -- raise `SpawnTimeout` to that window (never lower it).
- [x] `tools/autoplay` -- levers `spawn-during-load`, `connect-delay` documented in REFERENCE.md; scenario `join-spawn-during-load` row.
- [x] `_bmad-output/project-context.md` -- NGO gotcha: deferred deltas purged after SpawnTimeout during a long sync.

**Acceptance Criteria:**
- Given the fix, when the EditMode suite runs, then the new test passes (it failed before the fix).
- Given `join-spawn-during-load` (3 clients, stall-load 25, a bot seated during the load), when it runs, then desync = 0
  and no `[DESYNC]` error (it failed 2/2 before the fix).
- Given `net-sync-3clients` and `join-slow-load-honest`, when they run, then they still pass.

## Spec Change Log

## Verification (2026-10-05)

- EditMode `ClientConnectionPayloadTests.Apply_KeepsDeferredMessagesForTheWholeJoinWindow`: red (10 < 90) before the
  fix, green after; full EditMode 566/566.
- `join-spawn-during-load`: FAIL 2/2 before (`net-20261005-220124`, `-220242`: bot 100 seen as FAKE, last player
  missing, `nvPurged=2` on every loading client), PASS 2/2 after (`-220658`, `-220816`: 0 desync, 0 purged deltas).
- `join-slow-load-honest`, `net-sync-3clients`: PASS.
- Lever `connect-delay` added on the way (first repro attempt) and kept: joins only completed when the held load was
  released, so the host-side `spawn-during-load` trigger is what makes the repro deterministic.
