---
title: 'Fix reveal RPC UI refresh on non-host clients + extract testable visibility rule'
type: 'bugfix'
created: '2026-06-13'
status: 'done'
baseline_commit: '3afd9b56f34c152efe3f0bc6d1a6d9aab23bec78'
context: ['{project-root}/_bmad-output/implementation-artifacts/investigations/corruption-concentration-no-role-reveal-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every reveal delivered through `GameInfoRevealer.SetRevealLevelRpc` writes the reveal data but never re-renders the card on a **non-host** client. The two UI-refresh side-effects in `SetRevealLevel` (card flip + `onCharacterInfoRevealedChanged`) are gated on `_observerId == GetLocalClientId()`, but the RPC path hardcodes the storage sentinel `_observerId = 0`, which equals the local id only on the host (clientId 0). Reported via corruption + Concentration role reveal (`PCorruptingMark.OnConcentratedEffectServer`) on a non-host client; the defect is at the sink and silently breaks `PCardsShuffling`, `PBlessing`, `PCorruptionParanoia/Knowledge/Insight`, `PChainedByTheShadows`, `PPersonalBeacons` and all Public reveals.

**Approach:** (1) Behavioural fix at the sink — `SetRevealLevelRpc` passes the real local viewer id instead of `0`. (2) Extract the viewer-visibility decision into a pure Domain rule `RevealVisibilityRules.ShouldRefreshLocalUi(viewerId, localClientId)` so the contract is named and unit-testable in EditMode without NGO — this is what removes the manual "every power × every RPC" playtest matrix. (3) Lock it: 4 EditMode assertions on the rule + one PlayMode integration test that simulates a non-host viewer. The deeper signature split (storage-key vs viewer-id) is deferred to a backlog story (see deferred-work.md).

## Boundaries & Constraints

**Always:** Fix at the sink so every caller benefits — never patch individual powers. The viewer-visibility predicate lives in one place (the Domain rule) and both UI guards call it. Storage/data-write behaviour stays byte-identical (host path unchanged: `GetLocalClientId() == 0`). Domain rule stays pure — no UnityEngine, no NGO (respect the Domain purity CI guard). C# 9 only (plain static class, block-scoped namespace — no record/file-scoped namespace). New `.cs` files created via Unity `create_script` (avoids the silent compile-exclusion trap), not raw Write.

**Ask First:** Any change to the data-storage selection in `GetCharacterInfo`, to `EnsureOwnRoleRevealed`, or any widening of scope into the full `SetRevealLevel` signature split (that is the deferred story, not this one).

**Never:** Do NOT modify `SetRevealLevelSimulatedRpc` — bot brains (id ≥ 100) must not flip the human host's card; its non-Public branch correctly uses `_intendedReceiverId`. No new RPCs, no public reveal-method signature changes, no observer-id model refactor in this story.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rule — host views own/target | `ShouldRefreshLocalUi(0, 0)` | `true` | N/A |
| Rule — sentinel reaches non-host (the bug) | `ShouldRefreshLocalUi(0, 1)` | `false` | N/A |
| Rule — normal client views self-targeted reveal | `ShouldRefreshLocalUi(1, 1)` | `true` | N/A |
| Rule — bot brain never the local human view | `ShouldRefreshLocalUi(101, 1)` | `false` | N/A |
| Integration — Personal reveal, non-host viewer | host + `SetPossessedIdentity(1)`, `SetRevealLevelRpc(target, isRoleRevealed, Personal, true)` | data `isRoleRevealed=Personal` AND `onCharacterInfoRevealedChanged` fires | N/A |
| Integration — host viewer / simulated reveal | unchanged paths | identical to today | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Domain/RevealVisibilityRules.cs` -- NEW pure Domain rule; namespace `CorruptionDuPortail.Domain`; `Game` + `Tests.Editor` asmdefs already reference Domain (no asmdef change).
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` -- `SetRevealLevelRpc` (~:200-215): two `0` literals at ~:205 (Public) and ~:213 (non-Public) → `CharacterQuery.GetLocalClientId()`. `SetRevealLevel` (~:154-182): both UI guards (~:169 card flip, ~:178 event) call the Domain rule. `SetRevealLevelSimulatedRpc` (~:218-232) untouched.
- `Assets/Scripts/Characters/CharacterManager.cs` -- `GetLocalClientId()` (:289) `= _debugPossessedId ?? NetworkManager.LocalClientId`; `SetPossessedIdentity(ulong?)` (:394) = the test seam for a non-host viewer.
- `Assets/Scripts/Tests/Editor/RevealVisibilityRulesTests.cs` -- NEW EditMode tests (Tests.Editor asmdef, references Domain).
- `Assets/Scripts/Tests/PlayMode/CorruptionTests.cs` -- existing single-host harness wiring revealer + characterManager; add the integration regression test here.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Domain/RevealVisibilityRules.cs` -- create (Unity `create_script`) a pure static class with `public static bool ShouldRefreshLocalUi(ulong viewerId, ulong localClientId) => viewerId == localClientId;`. XML-doc the contract: returns true when the reveal's viewer is the local player, i.e. when the local UI must refresh. No Unity/NGO usings.
- [x] `Assets/Scripts/GameLogic/GameInfoRevealer.cs` -- in `SetRevealLevelRpc` replace both `0` literals (Public ~:205, non-Public ~:213) with `CharacterQuery.GetLocalClientId()`; in `SetRevealLevel` compute `bool _isLocalViewer = RevealVisibilityRules.ShouldRefreshLocalUi(_observerId, CharacterQuery.GetLocalClientId());` and use it for both UI guards. Add a one-line comment on the RPC change (runs only on the intended viewer; storage unaffected since `GetCharacterInfo` ignores observerId < 100). Do not touch `SetRevealLevelSimulatedRpc`.
- [x] `Assets/Scripts/Tests/Editor/RevealVisibilityRulesTests.cs` -- create (Unity `create_script`) EditMode tests asserting the four I/O-matrix rule rows: (0,0)=true, (0,1)=false, (1,1)=true, (101,1)=false.
- [x] `Assets/Scripts/Tests/PlayMode/CorruptionTests.cs` -- add `[UnityTest] SetRevealLevelRpc_PersonalReveal_FiresUiRefresh_ForNonHostViewer`: `SetPossessedIdentity(1)`, add a target character, subscribe to `onCharacterInfoRevealedChanged`, call `SetRevealLevelRpc(target, nameof(CharacterInfoReveal.isRoleRevealed), Personal, true)`, `yield return null` (await a frame), assert the event fired AND `GetCharacterInfo(target).isRoleRevealed == Personal`; restore identity with `SetPossessedIdentity(null)` so teardown is clean.

**Acceptance Criteria:**
- Given a non-host local viewer (`GetLocalClientId() != 0`), when a Personal reveal arrives via `SetRevealLevelRpc` with `showInfo=true`, then `onCharacterInfoRevealedChanged` fires and the data is set to `Personal`.
- Given the host or a simulated/bot reveal, when delivered, then behaviour is identical to before the change (no regression; host card not spuriously flipped for bot brains).
- Given the visibility decision, when evaluated, then it is sourced from the single Domain rule used by both UI guards (no duplicated inline `==` comparison).
- Given EditMode + PlayMode suites, when run after the change, then they compile clean (`read_console`) and pass.

## Design Notes

Why this altitude (party-mode consensus): the minimal `0 → GetLocalClientId()` is the behavioural fix; the Domain-rule extraction is what makes the whole class unit-testable — one pure function, four assertions, retires the manual NGO permutation matrix because every power funnels through the same path. `SetRevealLevelRpc` runs only on the intended viewer (`RpcTarget.Single` for Personal, broadcast for Public where every receiver is a viewer), so passing the real local id is always correct; for real clients `GetLocalClientId() < 100` and `GetCharacterInfo` selects storage by `_clientId` (ignoring observerId), so the write target is unchanged. The full `storageKey`/`viewerId` signature split (the real cure for the parameter overload) is intentionally deferred — ~12 call sites, its own story.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after each new/edited script) -- expected: no compile errors; new `.cs` land in their asmdef (not the silent-exclusion bucket).
- `mcp__UnityMCP__run_tests` EditMode filter `RevealVisibilityRules` -- expected: 4 assertions pass.
- `mcp__UnityMCP__run_tests` PlayMode filter `CorruptionTests` -- expected: new test passes; `PCorruptingMark_CorruptsTarget`, `PBlessing_*`, `PAutoCorruption_*` still pass.
- `mcp__UnityMCP__run_tests` full EditMode + PlayMode -- expected: green (no reveal/winning-condition regressions).

## Suggested Review Order

**The fix**

- The named, pure decision — design intent, EditMode-testable, kills the manual NGO matrix.
  [`RevealVisibilityRules.cs:19`](../../Assets/Scripts/Domain/RevealVisibilityRules.cs#L19)

- The behavioural fix: the reveal RPC passes the real local viewer id, not the `0` storage sentinel.
  [`GameInfoRevealer.cs:211`](../../Assets/Scripts/GameLogic/GameInfoRevealer.cs#L211)

- Both UI side-effects now read one rule instead of duplicated inline `==` comparisons.
  [`GameInfoRevealer.cs:171`](../../Assets/Scripts/GameLogic/GameInfoRevealer.cs#L171)

**Regression coverage**

- EditMode pins the contract — incl. the sentinel-on-non-host row that was the shipped bug.
  [`RevealVisibilityRulesTests.cs:15`](../../Assets/Scripts/Tests/Editor/RevealVisibilityRulesTests.cs#L15)

- PlayMode integration: non-host viewer via the possession seam, asserts the UI event fires.
  [`CorruptionTests.cs:220`](../../Assets/Scripts/Tests/PlayMode/CorruptionTests.cs#L220)
