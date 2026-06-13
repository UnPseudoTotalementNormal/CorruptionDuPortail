# Investigation: Corruption ciblée + Concentration ne révèle pas le rôle (client non-hôte)

## Hand-off Brief

1. **What happened.** A non-host client used targeted corruption (`PCorruptingMark`) with the Concentration effect; the role reveal of the corrupted target was written to data but never appeared on screen (Confirmed by code reading).
2. **Where the case stands.** Root cause Confirmed: `GameInfoRevealer.SetRevealLevel` gates its UI refresh on `_observerId == GetLocalClientId()`, but the RPC reveal path hardcodes `_observerId = 0` (a storage sentinel). On any non-host client `GetLocalClientId() != 0`, so the card-flip and the `onCharacterInfoRevealedChanged` event are both skipped — the role is revealed in data but the card never re-renders. Host (clientId 0) accidentally passes the guard.
3. **What's needed next.** Make the targeted-reveal RPC path fire the local UI refresh on non-host clients (pass the real local id, or fix the guard). Trivial-to-small fix in `GameInfoRevealer`. Add a PlayMode test for Concentration (currently 0 coverage).

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-13                                                                 |
| Status           | Active                                                                     |
| System           | Unity 6000.2.6f2, NGO, branch `network-avatar`                             |
| Evidence sources | Source code (Confirmed); no runtime logs; no tests for this path           |

## Problem Statement

User (Poyo) report: *"un client a utilisé le pouvoir de corruption ciblé avec l'effet concentration donc il aurait dû avoir le rôle de la personne reveal mais rien ne s'est passé après l'avoir corrompu."*

Targeted corruption + Concentration should reveal the corrupted target's role to the corrupter. Corruption succeeded; the bonus role reveal produced no visible effect.

## Evidence Inventory

| Source                         | Status    | Notes                                                                 |
| ------------------------------ | --------- | --------------------------------------------------------------------- |
| `PCorruptingMark.cs`           | Available | Targeted corruption power + Concentration effect impl                 |
| `PCConcentrated.cs`            | Available | Concentration PowerComponent; auto-fires on power use                 |
| `GameInfoRevealer.cs`          | Available | Reveal storage + RPC dispatch + UI-refresh guards (defect here)       |
| `Card.cs`, `CharactersBarObject.cs` | Available | Card flip visual + bar refresh consumers                         |
| Runtime logs / repro           | Missing   | No logs from the reported session                                     |
| Automated tests for Concentration | Missing | Grep over `Tests/**` for Concentrat/lastCorrupted/OnConcentrated = 0 hits |

## Timeline of Events (reconstructed control flow, server-authoritative)

| Step | Event                                                                                                   | Source                              | Confidence |
| ---- | ------------------------------------------------------------------------------------------------------- | ----------------------------------- | ---------- |
| 1    | Corrupter picks target → `OnCharacterPicked` (owner client)                                              | `PCorruptingMark.cs:44`             | Confirmed  |
| 2    | `OnCardClickedRpc` (SendTo.Server) sent, then local `SetRevealLevel(isCorruptRevealed, Personal, observer=ownerClientId)`, then `OnUsed()` | `PCorruptingMark.cs:52-55` | Confirmed  |
| 3    | Server: `OnCardClickedRpc` → `StoreLastCorrupted` → `lastCorruptedCharacterId.Value = target`            | `PCorruptingMark.cs:65-77`          | Confirmed  |
| 4    | Server: `OnUsedServerRpc` → `OnUsedServer` → `onPowerUsedServer.Invoke()`                                | `Power.cs:182-224`                  | Confirmed  |
| 5    | `PCConcentrated.OnPowerUsed` runs; if precedent power unused → `OnConcentratedEffectServer()`            | `PCConcentrated.cs:30-52`           | Confirmed  |
| 6    | `SendRevealLevelRpc(lastCorrupted, isRoleRevealed, Personal, toObserver=ownerClientId, showInfo=true)`   | `PCorruptingMark.cs:134-138`        | Confirmed  |
| 7    | On corrupter client: `SetRevealLevelRpc` non-Public branch → `SetRevealLevel(target, isRoleRevealed, Personal, observerId=**0**, showInfo)` | `GameInfoRevealer.cs:211-214` | Confirmed |
| 8    | Data write OK; **both UI guards `observerId(0) == GetLocalClientId()` fail on non-host** → no card flip, no event | `GameInfoRevealer.cs:169-181`  | Confirmed  |

## Confirmed Findings

### Finding 1: The targeted reveal RPC passes the storage sentinel `0` as observerId

**Evidence:** `GameInfoRevealer.cs:213` — non-Public branch of `SetRevealLevelRpc` calls `SetRevealLevel(_clientId, _revealVariableName, _revealLevel, 0, _showInfo)`.

**Detail:** For real (non-simulated, id < 100) clients, `0` is a "local main dict" sentinel. `GetCharacterInfo` for observer < 100 ignores the observerId and always returns `charactersInfoRevealed[_clientId]` (`GameInfoRevealer.cs:128-132`), so the **data write is correct** — `isRoleRevealed` becomes `Personal` for the target. The sentinel is acknowledged in the code comment at `GameInfoRevealer.cs:133-137`.

### Finding 2: The UI-refresh guards compare the sentinel against the real local client id

**Evidence:** `GameInfoRevealer.cs:169` (`if (_showInfo && _observerId == CharacterQuery.GetLocalClientId())` → card flip via `boardManager.visibleCards.Find(...).ShowPseudoWithRevealedInfo(true)`) and `GameInfoRevealer.cs:178` (`if (_observerId == CharacterQuery.GetLocalClientId()) onCharacterInfoRevealedChanged?.Invoke();`).

**Detail:** With `_observerId == 0` and a non-host `GetLocalClientId()` (1, 2, 3…), both conditions are false. The card never flips and the bar event never fires. On the host, `GetLocalClientId() == 0`, so `0 == 0` passes and the reveal shows — the bug is **non-host-only**.

### Finding 3: The visible role reveal depends solely on these guarded UI hooks

**Evidence:** `Card.cs:250-269` `ShowPseudoWithRevealedInfo` → `ShowRevealedCard` is the card-flip visual; it is invoked from inside the guarded block at `GameInfoRevealer.cs:173`. The global refresh (`characterManager.AskForUpdateAllCharactersRpc` at `Power.cs:226`; `CharactersBarObject.UpdateCharacter` at `CharactersBarObject.cs:180-198`) updates the bar overlay/tooltips, **not** the card flip.

**Detail:** Because no other code path flips the card to the revealed state for this reveal, skipping the guarded block = no visible effect, matching "rien ne s'est passé." The data sits revealed in `charactersInfoRevealed`, so a later unrelated refresh that re-runs `ShowPseudoWithRevealedInfo` for that card would suddenly show the role (a diagnostic tell).

### Finding 4: Asymmetry confirms the diagnosis — the corruption marker reveal works because it uses the real local id

**Evidence:** `PCorruptingMark.cs:53-54` calls `SetRevealLevel(..., RevealLevel.Personal, ownerClientId.Value)` **directly and locally** with `_observerId = ownerClientId.Value` (the real local id). Guard `ownerClientId == GetLocalClientId()` passes → marker shows. The role reveal instead goes through the RPC path with sentinel `0` → guard fails.

## Hypothesized Paths

### Hypothesis 1: UI-refresh guard sentinel collision (observer 0 vs real local id) — PRIMARY

**Status:** Confirmed.

**Theory:** Findings 1–4 above. Deterministic on any non-host client.

**Resolution:** Confirmed by code reading. The RPC reveal path writes data but skips the only UI hooks that flip the card, on every non-host client.

### Hypothesis 2: Concentration never fired (precedent-power gate)

**Status:** Open (cannot rule out without runtime data, but not required to explain the symptom).

**Theory:** `PCConcentrated.OnPowerUsed` returns early if there is no precedent sibling power (`PCConcentrated.cs:33`) or if the precedent power's `powerUseLeft != maxPowerUse` (`PCConcentrated.cs:38`, i.e. the precedent power was already used this turn). If the player had already used the precedent power, Concentration is silently skipped.

**Would confirm:** Runtime log showing `OnConcentratedEffectServer` not reached; or the corrupter had used the sibling power earlier that turn.

**Would refute:** H1 fully explains the symptom on a non-host client even when Concentration fires correctly. If the affected player was non-host, H1 is sufficient and H2 is moot.

### Hypothesis 3: `lastCorruptedCharacterId` race (reveal targets default 9999999)

**Status:** Refuted.

**Theory:** If `OnUsedServerRpc` were processed before `OnCardClickedRpc`, `lastCorruptedCharacterId` would still be its default `9999999` and the reveal would hit a ghost id.

**Resolution:** Both RPCs are sent from the same client on the same NetworkObject (`PCorruptingMark`/the Power) over the reliable channel; `OnCardClickedRpc` is invoked before `OnUsed()` (`PCorruptingMark.cs:52` then `:55`), so NGO preserves order and the server sets `lastCorruptedCharacterId` before `onPowerUsedServer` fires. The write is a server-side NetworkVariable assignment read on the same server frame.

## Missing Evidence

| Gap                                   | Impact                                                            | How to Obtain                                                              |
| ------------------------------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------- |
| Was the affected player host or peer? | Confirms H1 applies (peer) vs. forces look at H2 (host)          | Ask Poyo / repro; H1 predicts the bug only for non-host clients           |
| Runtime logs of the session          | Would show whether `OnConcentratedEffectServer` was reached      | Re-run with a tagged `[CONCENTR]` log at `PCorruptingMark.cs:134`         |
| Concentration test coverage          | No regression guard exists for this whole effect                 | Add PlayMode test (see Reproduction Plan)                                  |

## Source Code Trace

| Element       | Detail                                                                                                  |
| ------------- | ------------------------------------------------------------------------------------------------------ |
| Error origin  | `Assets/Scripts/GameLogic/GameInfoRevealer.cs:169` and `:178` — UI guards keyed on `_observerId == GetLocalClientId()` |
| Trigger       | `SetRevealLevelRpc` non-Public branch passing sentinel `0` (`GameInfoRevealer.cs:213`), reached from `PCorruptingMark.OnConcentratedEffectServer` → `SendRevealLevelRpc` (`PCorruptingMark.cs:134-138`) |
| Condition     | Corrupter is a non-host client (`GetLocalClientId() != 0`); reveal is non-Public (Personal)             |
| Related files | `PCorruptingMark.cs`, `PCConcentrated.cs`, `Power.cs`, `Card.cs`, `CharactersBarObject.cs`              |

## Conclusion

**Confidence:** High (Confirmed root cause by code reading; deterministic for non-host clients).

The role-reveal data is written correctly, but the visible card flip and the `onCharacterInfoRevealedChanged` UI event are both gated on `_observerId == GetLocalClientId()` inside `GameInfoRevealer.SetRevealLevel`. The targeted-reveal RPC path passes the storage sentinel `_observerId = 0`, which equals the local id only on the host. On any non-host client the UI hooks are skipped, so the corrupter sees nothing despite the role being revealed in state. This is the same sentinel-vs-real-clientId collision the read-path comment at `GameInfoRevealer.cs:133-137` already warns about, surfacing on the UI-refresh side.

## Recommended Next Steps

### Fix direction

In the targeted (non-Public) reveal RPC path, the UI refresh must fire for the local real player. The RPC `SetRevealLevelRpc` runs only on the intended observer client (`RpcTarget.Single(toObserverId)`), so on that client `GetLocalClientId()` *is* the observer. Cleanest option: in the non-Public branch of `SetRevealLevelRpc` (`GameInfoRevealer.cs:213`), pass `CharacterQuery.GetLocalClientId()` instead of the literal `0`. Storage is unchanged (for ids < 100 `GetCharacterInfo` ignores observerId and uses `charactersInfoRevealed`), and the guards at `:169`/`:178` then pass on non-host clients too. Verify the Public branch and the simulated branch (`SetRevealLevelSimulatedRpc`, already uses `_intendedReceiverId`) are unaffected, and re-check the host path (still `0 == 0`).

### Diagnostic (if confirming before fixing)

- Add a tagged log `[CONCENTR]` at `PCorruptingMark.cs:134` (effect reached) and at `GameInfoRevealer.cs:167` (data written) and inside the `:169` guard (UI fired). Reproduce as a non-host client; expect "reached + written" but **not** "UI fired".
- Confirm with Poyo whether the affected player was the host or a remote client.

## Reproduction Plan

1. Host + at least one remote client. Remote client owns a role with `PCorruptingMark` carrying the Concentration component, precedent sibling power unused.
2. Remote client corrupts a target. Expect: corruption marker appears (works), target's role card does **not** flip (bug).
3. Host performs the same: role card flips (works) — confirms non-host-only.
4. Regression test (PlayMode, no coverage today): drive `OnConcentratedEffectServer` with a simulated non-host observer; assert `GetCharacterInfo(target).isRoleRevealed == Personal` AND that the UI-refresh event/`ShowPseudoWithRevealedInfo` is invoked. This is the first test to touch the Concentration path.

## Side Findings

- The entire Concentration effect (`PCConcentrated` + `IConcentratedPowerEffect` + `OnConcentratedEffectServer`) has **zero automated coverage** (grep over `Tests/**` returned nothing). CLAUDE.md mandates tests for new powers/network flows.
- Related prior art: `_bmad-output/implementation-artifacts/investigations/role-reveal-wrong-card-investigation.md` and the read-time own-role invariant (`GameInfoRevealer.cs:141-152`) — same sentinel/real-id collision class, previously fixed on the read side only.

## Follow-up: 2026-06-13

### New Evidence — the defect is systemic, not specific to Concentration

User confirmed the affected player was a **non-host client** → H1 verified.

Auditing every caller of the RPC reveal path shows the bug is at the **sink**, affecting all of them — Concentration was just where it surfaced:

| Caller | Reveal | Level | showInfo | Broken on non-host |
| ------ | ------ | ----- | -------- | ------------------ |
| `PCorruptingMark.cs:136` (Concentration) | `isRoleRevealed` | Personal | true | yes |
| `PCardsShuffling.cs:97` | `isRoleRevealed` (correct guess) | Personal | true | yes |
| `PBlessing.cs:55` | `isRoleRevealed` | Personal | true | yes |
| `PCorruptionParanoia.cs:12` | `isCorruptRevealed` | Personal | true | yes |
| `PCorruptionKnowledge.cs:48` | `forceCorruptOnRoleRevealed` | Personal | (true) | yes (also kills bar via the gated event) |
| `PCorruptionInsight.cs:19`, `PChainedByTheShadows.cs:42`, `PPersonalBeacons.cs:39` | various | Personal | true | yes |
| `PHighPriorityBounty.cs:59`, `TakeDownThePortalState.cs:96` | `isRoleRevealed` | Public | true | immediate flip also missed on non-host (Public branch `:205` uses the same `0`) |

### Why it was written this way (root design flaw)

The `_observerId` parameter of `SetRevealLevel` carries **two responsibilities**:
1. **Storage selector** — which knowledge dict to write: the real-local dict (sentinel `0`, since `GetCharacterInfo` ignores observerId for ids < 100) or a simulated bot brain (id ≥ 100).
2. **Viewer identity** — the UI guards `_observerId == GetLocalClientId()` (`:169`, `:178`) use it to decide "is this reveal for the player at this screen?"

For **direct local calls** the caller passes the real local id, so both meanings coincide and the UI fires. When the **RPC layer** was added, the author needed the "real-local dict" selector and reused the `0` sentinel (the same convention the read-path comment at `:133-137` documents). That `0` then leaks into the UI guards, where it equals `GetLocalClientId()` **only by accident on the host** (clientId 0). The parameter name `_observerId` reads like "the viewer" while it is actually used as "the storage key" here — that conflation is what masked the bug. The host's literal clientId 0 is the exact sentinel/clientId-0 collision the read path was already hardened against (`EnsureOwnRoleRevealed` deliberately uses `GetLocalClientId()`, not the passed observerId), but the **UI guards never received the same hardening**.

### Revised fix direction (sink-level — fixes all callers at once)

In `SetRevealLevelRpc` (the **real-client** RPC; runs only on the intended viewer — `RpcTarget.Single` for Personal, broadcast for Public where every receiver is an intended viewer), replace the two `0` literals at `:205` (Public branch) and `:213` (non-Public branch) with `CharacterQuery.GetLocalClientId()`. Storage is unchanged (observer < 100 ignores the id in `GetCharacterInfo`); the UI guards now pass for the actual local viewer on every client, host or not. **Leave `SetRevealLevelSimulatedRpc` untouched** — bot brains (id ≥ 100) must not flip the human host's card, and its non-Public branch correctly passes `_intendedReceiverId`.
