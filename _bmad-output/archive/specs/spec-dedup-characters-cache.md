---
title: 'Defensive dedup + tripwire log in CharacterManager characters cache'
type: 'bugfix'
created: '2026-07-07'
status: 'in-review'
baseline_commit: '322d6b1738428d2dd099d8f2b00145f602e481a3'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/technomancer-duplicate-card-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Playtest 2026-07-03: on the Mage Occulte client only, the technomancer player appeared twice (duplicate table card + duplicate role-bar icon). Investigation concluded the client's replicated `networkedCharacters` `NetworkList` held the same entry twice (NGO initial-sync + pending-delta double-delivery class, plausibly re-exposed by the 2.6.0 → 2.12.0 bump). Every UI surface and targeting path projects this list via `GetCharacters()`, so one replica-level duplicate corrupts the whole client.

**Approach:** Make the projection self-healing: in `CharacterManager.RebuildCharactersCache`, skip any entry whose resolved `Character` is already in `_charactersCache`, and emit a loud `[CHARLIST]`-tagged error log when a duplicate is dropped — the log is a permanent tripwire that proves the NGO-layer divergence in `Player.log` on the next occurrence.

## Boundaries & Constraints

**Always:**
- Dedup key = **resolved `Character` reference** (same replica object). Replica duplication duplicates the *entry*, so both resolve to the same object.
- Preserve list order and the existing dirty-cache semantics (unresolved refs still skipped, cache stays dirty).
- Tripwire log must ship in player builds (it fires only on anomaly, never in steady state) and carry one greppable tag `[CHARLIST]` + ownerClientId + NetworkObjectId.
- Red-first regression test (project rule for subtle-root-cause bugfixes).

**Ask First:**
- Any change to `networkedCharacters` write paths (`AddNewCharacter` / `RemoveCharacter`) or to NGO-layer behavior.

**Never:**
- Dedup by `ownerClientId` — a freshly spawned Character can transiently read `FAKE_CLIENT_ID` on clients before its NetworkVariable delivers; id-based dedup could drop a legitimate player.
- Touch `GetSafeRpcTarget` / `IsLocalOrSimulated` / any RPC path (NFR5: relocated, never edited).
- Attempt to fix the NGO layer itself or bump packages in this change.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Healthy list | N distinct resolvable entries | Cache = N characters, order preserved, no log | N/A |
| Replica duplicate | Same entry twice (both resolve to same `Character`) | Cache contains it once; one `[CHARLIST]` error log with ownerClientId + NetworkObjectId | Log, drop, continue |
| Unresolved ref | Entry whose `TryGet` fails | Skipped, `_cacheDirty` stays true (unchanged behavior) | N/A |
| Duplicate + unresolved mix | Duplicate entry AND an unresolved entry | Duplicate dropped + logged; cache still dirty | Log, drop, continue |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/CharacterManager.cs:144-163` -- `RebuildCharactersCache`: the single projection point NetworkList → `_charactersCache`; the ONLY place to dedup (all reads funnel through `_characters`/`GetCharacters`)
- `Assets/Scripts/Characters/CharacterManager.cs:171` -- `networkedCharacters` (private `NetworkList<NetworkBehaviourReference>`): replicated source; test injects the duplicate here via reflection
- `Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs` -- harness to spawn a networked CharacterManager in PlayMode tests
- `Assets/Scripts/Tests/PlayMode/BoardTests.cs:124` -- existing example of `AddNewCharacter`-based PlayMode test setup to imitate

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Tests/PlayMode/Characters/CharacterManagerDedupTests.cs` -- NEW red-first PlayMode test: spawn manager via NetworkTestHelper, add a character (`AddNewCharacter`), reflection-append the same `NetworkBehaviourReference` to `networkedCharacters` a second time, force `_cacheDirty`, assert `GetCharacters()` returns the character exactly once (+ healthy-path test: N distinct characters → N entries) -- proves the bug class and pins the fix
- [x] `Assets/Scripts/Characters/CharacterManager.cs` -- in `RebuildCharactersCache`, before `_charactersCache.Add`, skip when `_charactersCache.Contains(_character)`; on skip, `Debug.LogError` tagged `[CHARLIST]` with ownerClientId + NetworkObjectId + list count -- self-healing projection + permanent tripwire

> Note (2026-07-07): Unity MCP unavailable in the implementing session — compile check + red/green test run pending in-editor (see Verification).

**Acceptance Criteria:**
- Given a `networkedCharacters` replica containing the same entry twice, when any consumer calls `GetCharacters()`, then the character appears exactly once and one `[CHARLIST]` error log was emitted.
- Given a healthy list, when the cache rebuilds, then behavior is byte-identical to today (same order, same dirty semantics, zero logs).
- Given the new test suite ran red before the fix (duplicate visible twice), when the fix is applied, then it runs green.

## Design Notes

Reference-equality dedup is safe by construction: a legitimate game can never contain the same `Character` instance twice (server guard `CharacterManager.cs:402` dedups by clientId at spawn). `List.Contains` is O(n²) worst case over player count (≤ ~15) inside an event-driven rebuild — negligible. `Debug.LogError` (not `LogWarning`): must be impossible to miss in `Player.log` and in the editor console during playtests.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after edit -- expected: zero compile errors
- `mcp__UnityMCP__run_tests` (PlayMode, filter `CharacterManagerDedupTests`) -- expected: red before fix, green after
- `mcp__UnityMCP__run_tests` (PlayMode, assembly Tests.PlayMode) -- expected: full suite green (no regression in Corruption/Board/GameSnapshot suites that rely on `GetCharacters`)

> **PENDING (2026-07-07):** Unity MCP unavailable in the implementing session — none of the above has run. The new test file was written to disk directly (no .meta yet): per project memory (silent compile exclusion), positively confirm the 2 tests APPEAR in the Test Runner after import, don't just check the console. For a true red run: temporarily comment the dedup guard (`CharacterManager.cs:165-172`), run `CharacterManagerDedupTests` (expect the duplicate test red), restore, rerun (expect green), then full PlayMode.

## Suggested Review Order

**The fix — self-healing projection**

- Reference-equality dedup guard; drops the NGO-delivered duplicate before it reaches any consumer
  [`CharacterManager.cs:165`](../../Assets/Scripts/Characters/CharacterManager.cs#L165)

- Once-per-duplicate tripwire memory — the divergence persists in the replica, the log must not flood
  [`CharacterManager.cs:130`](../../Assets/Scripts/Characters/CharacterManager.cs#L130)

**Regression net**

- Injects the exact replica end state (duplicate entry) into the private NetworkList via reflection
  [`CharacterManagerDedupTests.cs:94`](../../Assets/Scripts/Tests/PlayMode/Characters/CharacterManagerDedupTests.cs#L94)

- Second dirty read pins once-only logging (unexpected second LogError fails by default)
  [`CharacterManagerDedupTests.cs:108`](../../Assets/Scripts/Tests/PlayMode/Characters/CharacterManagerDedupTests.cs#L108)

- Healthy path byte-identical: count, insertion order, distinctness, zero logs
  [`CharacterManagerDedupTests.cs:119`](../../Assets/Scripts/Tests/PlayMode/Characters/CharacterManagerDedupTests.cs#L119)

**Peripherals**

- Review-deferred items (replica divergence unrepaired, permanent-dirty rebuild)
  [`deferred-work.md`](deferred-work.md)
