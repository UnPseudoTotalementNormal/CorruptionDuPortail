# Investigation: Start-of-game role reveal shows another player's card

## Hand-off Brief

1. **What happened.** On one build instance, the start-of-game role reveal flipped a *non-local* player's card (role "Dr Gloubi") instead of the local player's own — confirmed by the "Moi" badge sitting on a *different* card than the revealed one.
2. **Where the case stands.** Root-cause **class** is Confirmed: the personal reveal is computed purely client-side in `GameInfoRevealer` by matching `Character.ownerClientId == GetLocalClientId()` in a single fragile snapshot at `RoleAttributionState` end, with **no server authority and no reconciliation**. The exact trigger (which value was transiently wrong) is Hypothesized and needs runtime logs.
3. **What's needed next.** Add targeted logging around the reveal-dict build to capture `LocalClientId`, each `ownerClientId`, and `_cacheDirty` at the instant of `OnRolesAttributed`, then reproduce; in parallel, move the personal reveal to server authority (targeted RPC) so a transient client-side mismatch cannot occur.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-09                                                                 |
| Status           | Active                                                                     |
| System           | Unity 6000.2.6f2, NGO + Facepunch (Steam) transport, multi-instance build playtest (Windows) |
| Evidence sources | Tester screenshot (1 frame, multi-instance), source code, git log          |

## Problem Statement

Tester reports: in a multi-instance build playtest, one instance is bugged — at game start the "role reveal" flipped a card that is **not its own** but another player's. Reported as game-breaking. Screenshot shows, on the bugged instance, the "Moi" badge on one card while the *revealed* card (showing a character portrait/role) is a **different** card; on the healthy instances the "Moi" badge and the revealed card are the same.

## Evidence Inventory

| Source                | Status    | Notes                                                                 |
| --------------------- | --------- | --------------------------------------------------------------------- |
| Tester screenshot     | Partial   | Single frame, low-res. Shows Moi badge ≠ revealed card on bugged instance. No logs. |
| Source code           | Available | Reveal path fully traced (see Source Code Trace).                     |
| Player/console logs   | Missing   | No Player.log / Debug output from the bugged instance.                |
| Repro steps           | Missing   | Intermittent; no deterministic trigger captured.                      |
| Git history           | Available | Recent network refactors touch the exact resolution path (see Findings). |

## Confirmed Findings

### Finding 1: The start-of-game personal reveal is 100% client-side, no server authority

**Evidence:** `Assets/Scripts/GameLogic/GameInfoRevealer.cs:33-60`

**Detail:** `OnRolesAttributed()` (fires on `RoleAttributionState.onStateEndClient`) rebuilds `charactersInfoRevealed` and, per character, `AddCharacterToInfoList` sets `isRoleRevealed = RevealLevel.Personal` **iff** `_character.ownerClientId.Value == GetLocalClientId()` (`GameInfoRevealer.cs:55-57`). There is no `ServerRpc`/authority check — each client decides for itself which card is "mine". The card later reads this via `Card.ShowPseudoWithRevealedInfo` → `gameInfoRevealer.GetCharacterInfo(characterInfo.ownerClientId.Value).isRoleRevealed` (`Assets/Scripts/Board/Card.cs:232-242`).

### Finding 2: The reveal dict is computed once from a synchronous snapshot and never reconciled

**Evidence:** `Assets/Scripts/GameLogic/GameInfoRevealer.cs:33-40`, `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:65-78`

**Detail:** `RoleAttributionState.OnStartStateServer` assigns roles then calls `gameManager.NextGameState()` **immediately** (line 70); the rest (`WaitAndNextState`, a 3 s delay) is dead code after an early `return`, with the comment `//TODO: TEMP FIX MAYBE DIDNT EVEN WORK` (lines 72-78) — evidence the team previously hit a timing problem here and abandoned the fix. The client builds the reveal dict in one pass over `GetCharacters()` at state-end and never re-validates it against server truth.

### Finding 3: Identity resolution runs through recently-refactored, network-critical cache/registry code

**Evidence:** `Assets/Scripts/Characters/CharacterManager.cs:60-100,165-201`; git `d6412c5` "fix character-list bug", `73c48ab` "implement async character registry"

**Detail:** `GetLocalClientId()` = `_debugPossessedId ?? NetworkManager.LocalClientId` (`CharacterManager.cs:165`). `GetLocalCharacter`/`GetCharacters` resolve through `_charactersCache` with a `_cacheDirty` flag; when a `NetworkBehaviourReference` is unresolved the character is **silently skipped** and the cache stays dirty (`CharacterManager.cs:81-100`). `Character.ownerClientId` defaults to `GameValues.FAKE_CLIENT_ID = ulong.MaxValue` until the NetworkVariable replicates (`Character.cs:18`, `GameValues.cs:5`). These paths were refactored recently (network-critical area).

## Deduced Conclusions

### Deduction 1: The Personal flag landed on a non-local clientId in this client's dict

**Based on:** Findings 1 & the screenshot (Moi badge ≠ revealed card).

**Reasoning:** The "Moi" badge is placed by `BoardManager.ShowAllPlayerCards` matching `card.characterInfo.ownerClientId.Value == GetLocalClientId()` **at display time** (`Assets/Scripts/Board/BoardManager.cs:156-161`). The reveal is placed by `GameInfoRevealer` matching `ownerClientId == GetLocalClientId()` **at RoleAttribution-end time**. Both use the *same* equality; for them to disagree, the snapshot used by the reveal must have matched a **different** clientId than the one used for the badge.

**Conclusion:** Either `GetLocalClientId()` returned a different value at dict-build time vs display time, or the character set the dict iterated had transient/duplicated `ownerClientId` values (e.g. unresolved entries reading `ulong.MaxValue`, cache skipping the local character, or a stale cache). Because the dict is never reconciled (Finding 2), the wrong flag is permanent for the round.

## Hypothesized Paths

### Hypothesis 1: ownerClientId / local-id not final at the single snapshot instant (primary)

**Status:** Open

**Theory:** At `OnRolesAttributed`, the client's view of `ownerClientId` values and/or `NetworkManager.LocalClientId` was not yet at its final state (replication still settling, or `_charactersCache` dirty/partial per `CharacterManager.cs:81-100`), so `== GetLocalClientId()` matched the wrong (or no) character. The reveal is then frozen wrong.

**Supporting indicators:** Dead "TEMP FIX" delay in `RoleAttributionState` (Finding 2); `ownerClientId` default = `ulong.MaxValue` until replicated; silent-skip cache; intermittent ("game-breaker but rare") nature fits a race.

**Would confirm:** Logs from a bugged instance showing, at `OnRolesAttributed`, `LocalClientId` and each `ownerClientId` where the local character's id is absent/duplicated or `_cacheDirty == true`.

**Would refute:** Logs showing the dict was built with the correct local id matched to the correct single character, yet the wrong card still revealed.

### Hypothesis 2: Cache/registry regression resolves the wrong Character (secondary)

**Status:** Open

**Theory:** The recent async-registry + `_charactersCache` refactor (`73c48ab`, `d6412c5`) returns a stale or mis-ordered list so the local identity resolves to the wrong Character object at dict-build time.

**Supporting indicators:** Network-critical code refactored recently; symptom is an identity-resolution error; memory flags this area as "hold network-critical".

**Would confirm:** Reproduce on the commit before the registry refactor and observe the bug disappear; or logs showing `GetCharacters()` returned a stale list at the reveal instant.

**Would refute:** Bug reproduces identically on the pre-refactor commit.

### Hypothesis 3: Dev possession active in a development build (low)

**Status:** Open

**Theory:** `DevIdentityController` is gated by `#if !UNITY_EDITOR && !DEBUG return;` (`Assets/Scripts/Misc/DevIdentityController.cs:13-16`). Unity defines `DEBUG` in **development builds**, so F2/F3 (`SetPossessedIdentity`) are live for testers. A possession change could shift `GetLocalClientId()`.

**Supporting indicators:** Playtesters typically run development builds.

**Would refute:** Possession shifts *both* badge and reveal together (they share `GetLocalClientId()`), so it does **not** by itself produce the badge≠reveal mismatch — weakens this as the lone cause. Keep only as an aggravator.

## Missing Evidence

| Gap                                   | Impact                                                       | How to Obtain                                              |
| ------------------------------------- | ----------------------------------------------------------- | --------------------------------------------------------- |
| Player.log from the bugged instance   | Would show errors/NRE and timing around reveal              | Collect `%USERPROFILE%/AppData/LocalLow/<company>/<game>/Player.log` from tester |
| Snapshot of ids at `OnRolesAttributed`| Confirms which id was matched and cache state               | Add temporary Debug.Log (see Diagnostic)                  |
| Deterministic repro                   | Lets us bisect the refactor commits                         | Repro plan below                                          |

## Source Code Trace

| Element       | Detail                                                                                              |
| ------------- | -------------------------------------------------------------------------------------------------- |
| Error origin  | `Assets/Scripts/GameLogic/GameInfoRevealer.cs:55-57` (`AddCharacterToInfoList` Personal assignment) |
| Trigger       | `OnRolesAttributed` on `RoleAttributionState.onStateEndClient` (`GameInfoRevealer.cs:30-40`)        |
| Condition     | `ownerClientId == GetLocalClientId()` snapshot wrong/incomplete at that instant; never reconciled   |
| Related files | `Board/Card.cs:226-245`, `Board/BoardManager.cs:136-163`, `Characters/CharacterManager.cs:165-201`, `GameStates/RoleAttributionState.cs:65-78`, `Characters/Character.cs:18,35-62` |

## Conclusion

**Confidence:** Medium

The root-cause **class** is Confirmed: the personal role reveal at game start is decided unilaterally by each client from a one-shot `ownerClientId == LocalClientId` match (`GameInfoRevealer.cs:55-57`), with no server authority and no later reconciliation (Finding 2). This architecture turns any transient inconsistency in identity/replication at that single instant into a **permanent** wrong-card reveal — matching the observed badge≠reveal mismatch. The **exact** transient value that went wrong is still Hypothesized (H1 primary, H2 secondary) and requires logs to pin down.

## Recommended Next Steps

### Fix direction

- **Make the personal reveal server-authoritative.** Have the server send each client a targeted reveal for their own `clientId` (reuse the existing `SendRevealLevelRpc` / `GetSafeRpcTarget` path) instead of clients self-matching `ownerClientId == LocalClientId`. Server truth cannot mismatch.
- **Or, at minimum, make the client computation self-healing:** resolve the local character via the async registry (`GetCharacterAsync`/`GetLocalCharacter`) and recompute the reveal when `onLocalIdentityChanged` / `onCharactersListUpdated` / `ownerClientId.OnValueChanged` fire, rather than once at a fragile snapshot. Re-validate `charactersInfoRevealed` whenever `_cacheDirty` was true at build time.
- Remove the dead `WaitAndNextState` / "TEMP FIX" code in `RoleAttributionState` once the real fix lands.

### Diagnostic

Add temporary logging in `GameInfoRevealer.OnRolesAttributed` / `AddCharacterToInfoList`:
`Debug.Log($"[reveal] local={CharacterManager.instance.GetLocalClientId()} char={_character.ownerClientId.Value} dirty={...} chars=[{ids}]")`. Ship a dev build to the tester and capture the bugged instance's `Player.log`.

## Reproduction Plan

Local repro attempt: host + 2-3 clients (or simulated players via F1), start games repeatedly; to widen the race window, artificially delay/await character `ownerClientId` replication or force `_cacheDirty` true at `OnRolesAttributed`. Expected (current code): occasionally a client reveals a non-local card. Verification of fix: with server-authoritative reveal, the revealed card always equals the "Moi" card across N starts.

## Side Findings

- `RoleAttributionState.cs:72-78` is unreachable dead code after `return` (line 71) — `WaitAndNextState` never runs. Confirmed.
- `GameInfoRevealer.GetCharacterInfo` can call `AddCharacterToInfoList(GetCharacter(_clientId,false), ...)` where `GetCharacter` may return `null`, risking an NRE at `GameInfoRevealer.cs:55` (`_character.ownerClientId`). Hypothesized; uncaught in `GetCharacterInfo`.

## Follow-up: 2026-06-09

### Fix applied

Root-cause class addressed in `Assets/Scripts/GameLogic/GameInfoRevealer.cs`:

- Removed the snapshot-time "own role" stamping (`ownerClientId == observerId` → `Personal`) from `AddCharacterToInfoList`, `AddCharacterToSimulatedInfoList`, and `GetSimulatedBrain`. Entries now init `False`. This is what could land `Personal` on the wrong card when an id was mid-replication at role attribution.
- Added the invariant **"a player always sees their own role"** at **read time** in `GetCharacterInfo` via `EnsureOwnRoleRevealed(_clientId, _observerId, _info)` (both real and simulated branches). The "Moi" badge (`BoardManager.cs:156`) and the reveal now both evaluate `== local id` at display time, so they can no longer diverge. Self-healing, no network-ordering hazard, server-authoritative reveals (powers) untouched.

All reads of `isRoleRevealed` go through `GetCharacterInfo` (verified by grep), so the read-time invariant is honored everywhere (`Card`, `InfoTableSystem`, `TargetUtils`, `CharactersBarObject`, `TakeDownThePortalState`).

### Validation

- Unity compile: clean (no CS errors).
- PlayMode tests: **31/31 passed**, incl. `VisionPowerTests.POmniscience_RevealsRoleOnUsage` and Corruption suite.

### Residual

- Live multi-instance build repro not yet re-run by tester — confirm on next dev build.
- Side findings still open: dead code `RoleAttributionState.cs:71-78`; potential NRE in `GetCharacterInfo` when `GetCharacter` returns null (unchanged by this fix).

### Updated Conclusion

**Confidence: High** that the wrong-card reveal can no longer originate from a role-attribution timing snapshot. Status: Fix applied, pending live build confirmation.

## Follow-up: 2026-06-09 #2

### Regression found via instrumentation, then fixed

Tester reproduced the symptom (own card + another player's card both revealed) on a client after the first fix. Tagged `[REVEAL]` diagnostics captured the mechanism:

- Client (real local id = 1) console: `SetRevealLevel target=0 observer=0 local=1 field=isCorruptRevealed` (an inbound network RPC, legit pre-existing corruption reveal) immediately followed by `own-invariant applied client=0 observer=0`, then `card display owner=0 local=1 isRevealed=True` — the **host's** card revealed on the client.
- Host (local id = 0) console: correct (`card display owner=1 local=0 isRevealed=False`).

**Cause (Confirmed):** the first-fix `EnsureOwnRoleRevealed` judged "this is my card" via `_clientId == _observerId`. But `_observerId` in the RPC write-path is a hardcoded **sentinel `0`** meaning "the local main knowledge dict" (`SetRevealLevelRpc`/`SetRevealLevel` pass `0`), **not** the real local client id. The host's real `clientId` is also `0`, so on any non-host client every inbound reveal *about the host* (e.g. the start-of-game corruption reveal) made `0 == 0` true and stamped `isRoleRevealed = Personal` on the host's entry. Host was immune because for it `0` is genuinely its own id.

### Final fix

`EnsureOwnRoleRevealed` now takes a `_selfId` that is the **actual owner of the knowledge**: `CharacterManager.instance.GetLocalClientId()` for the real brain, and `_observerId` (the real bot id ≥100) for the simulated brain. The sentinel `0` is no longer used as an identity. This also fully covers the original timing bug, since nothing is stamped at role-attribution time any more.

### Validation

- Live: tester confirms fixed (client now sees only its own role).
- Compile clean; PlayMode **31/31 passed**.
- Diagnostic `[REVEAL]` logs removed.

### Side note (separate, not addressed)

The hardcoded `observerId = 0` sentinel colliding with the host's real `clientId = 0` is a latent design smell across the reveal RPC path; it bit us only because the invariant read it as identity. Worth a dedicated cleanup later (distinguish "local dict" from "client 0"). Also still open: client receiving the host's `isCorruptRevealed` at start may itself be unintended — confirm with design.

### Updated Conclusion

**Confidence: High.** Root cause (timing snapshot) and the introduced regression (sentinel/host-id collision) both Confirmed and fixed. Status: Concluded, pending commit.
