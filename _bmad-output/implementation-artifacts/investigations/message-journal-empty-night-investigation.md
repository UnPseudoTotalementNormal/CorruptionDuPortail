# Investigation: Message journal empty at night (no corruption / robot / messages)

## Hand-off Brief

1. **What happened.** After wiring the per-turn journal, the night reveal opened the panel but it was **empty** — no corruption count, no robot line, no revealed messages — even after a message was sent. (Confirmed by Poyo's playtest.)
2. **Root cause (Confirmed by reasoning + Poyo).** The journal renders a turn entry only if a `TurnStat` exists for that day; the server-side recorder never populated `turnStats` because it was tied to a **spawn-time `AwakeningState.onStateEndServer` subscription** in `MessageManager.OnNetworkSpawn` that did not fire reliably. Empty `turnStats` ⇒ zero turn entries ⇒ empty panel.
3. **Fix (applied, EditMode 443/443).** Record the `TurnStat` directly from the awakening **recap** (`AwakeningRecapMessages.ShowEvent`, server) — a moment guaranteed to run — instead of a spawn-time state subscription. Deps resolved in `OnNetworkSpawn` (DI-guard compliant). Needs a 2-build client playtest to confirm.

## Case Info

| Field | Value |
| ----- | ----- |
| Date | 2026-07-16 |
| Status | Fix applied, pending playtest confirmation |
| Branch | `feat/message-journal-per-turn` |
| Evidence | Source code, EditMode tests, Poyo's playtest observations |

## Problem Statement

Poyo: « l'interface s'affiche mais j'ai envoyé un message et rien ne s'est passé la nuit, y'a même pas eu le recap de corrompu ou ciblage de robot. Est-ce que c'est buggé parce qu'il n'y avait pas de robot ? » — plus a design clarification: no Robot in the game → show only corruption; but a **fake** Robot counts as present (omitting the clause would leak that it's fake).

## Confirmed Findings

### Finding 1 — Journal turn entries are gated on `turnStats`
**Evidence:** `Assets/Scripts/UI/MessageJournal/GameMessageJournalDataSource.cs` `GetTurns` iterates the deduped `turnStats`; a day with no `TurnStat` produces no entry. Corruption, robot line, and messages all hang off the `TurnStat` row.
**Deduction:** "no corruption shown at night" ⇒ `turnStats` empty ⇒ the recorder never ran.

### Finding 2 — The recorder was tied to a spawn-time state subscription
**Evidence (pre-fix):** `MessageManager.OnNetworkSpawn` resolved `characterQuery`/`roleTargetSystem` via `CompositionRoot` behind hard `Assert`s, then subscribed to `AwakeningState.onStateEndServer`. The recording ran only if that subscription fired.
**Deduction (with Poyo's domain input — "le moment de l'abonnement n'est pas le bon"):** the subscription did not reliably drive `OnAwakeningStateEnd`, so `turnStats` stayed empty. Sending a message still worked because `SendMessageRpc` is independent of the spawn/subscription path — matching the symptom (message sent, nothing revealed).

### Finding 3 — Bisected: the panel appeared **empty**, not absent
**Evidence:** Poyo confirmed the journal panel APPEARED at night (frontispiece visible) but with no turns. ⇒ `AwakeningRecapMessages.ShowEvent` DOES run (the recap trigger works); only the data was missing. This makes "record from the recap" a valid fix.

## Refuted Hypotheses

### Hypothesis (Poyo) — "bug because there was no Robot"
**Status:** Refuted. `TurnStatCalculator.Compute` handles `hasRobot=false` without crashing (sentinel `robotTargetCount = -1`, clause omitted). And a **fake** Robot is already counted as present: the recorder finds it via `role.roleID == RoleID.Robot` (fakes carry a real role), so `hasRobot=true` and the clause shows "Personne n'a ciblé le Robot" (0 targeters on the fake client id) — no fake leak. The no-Robot path is not the cause of the empty panel.

## Fix (applied)

- **`MessageManager`**: removed the fragile spawn-time `onStateEndServer` subscription. `OnNetworkSpawn` now only resolves `characterQuery`/`roleTargetSystem` (DI-guard: `CompositionRoot` referenced only inside `OnNetworkSpawn`). New public `RecordCurrentTurnStat()` (server) computes + appends the `TurnStat` using those fields.
- **`AwakeningRecapMessages.ShowEvent`** (server): calls `MessageManager.instance.RecordCurrentTurnStat()` **before** `RevealAllMessage()`, then `OpenForReveal()`. The stat is recorded at a moment guaranteed to run, from data still intact (RoleTargetSystem resets only at the next awakening's start).
- **Gotcha hit + resolved:** a first attempt resolved deps lazily inside the recorder → tripped `DiSeamNoLocatorGuardTests` (CompositionRoot must be referenced only in `OnNetworkSpawn`). Moved resolution back into `OnNetworkSpawn`.

**Verification:** compiles clean; EditMode **443/443**. Runtime confirmation = Poyo's 2-build playtest (Claude can't playtest).

## Conclusion

**Confidence: Medium-High.** The empty-panel → empty-`turnStats` → recorder-never-ran chain is Confirmed by code + the bisect (panel appeared empty). The exact reason the old subscription didn't fire wasn't isolated to a single line, but the fix removes the whole fragile path by recording from the guaranteed-to-run recap. Final confirmation pending playtest.

## Follow-up: 2026-07-16 #2 — the fix was racy; refined to scene-wired refs

**New evidence (Poyo):** with the first fix ("record from recap", deps resolved in `OnNetworkSpawn`) the journal **worked once, then went empty again** — intermittent. That is the tell of a **spawn-order race**: `RecordCurrentTurnStat` resolved `characterQuery`/`roleTargetSystem` from the composition root **at spawn**; when `MessageManager` spawned before `CharacterManager`/`RoleTargetSystem`, those came back null and were cached null → the recorder's null-guard `return`ed → `turnStats` empty → blank panel. Favourable spawn order → worked; unfavourable → empty. (The PlayMode test file was NOT the cause — it doesn't touch runtime.)

**Constraint discovered:** `MessageManager` is a migrated DI consumer, so `DiSeamNoLocatorGuardTests` forbids `RoleTargetSystem.instance` and requires any `CompositionRoot` reference to sit **only inside `OnNetworkSpawn`** — which is exactly the racy moment. (The guard is a source substring scan: even the word "CompositionRoot" in a comment outside `OnNetworkSpawn` trips it.)

**Refined fix (applied, green):** resolve the deps NOT at spawn but as **scene-wired `[SerializeField]` references** — `characterManager` + `roleTargetSystem` on `MessageManager`, wired in GameScene (like `RobotBoardInfo`/`CorruptionBoardInfo`). A serialized reference is injection (guard-compliant, not the forbidden `.instance` locator) AND is available at `Awake`, so the recorder never races. `Awake` asserts both are wired; `SceneWiringGuardTests` enforces the wiring. `RecordCurrentTurnStat` (still called from `AwakeningRecapMessages.ShowEvent`) reads the fields directly.

**Verification:** compiles clean; **EditMode 443/443** (DI + SceneWiring guards pass); **PlayMode recorder 2/2** (with-robot + no-robot). Runtime confirmation = Poyo's playtest.

## Follow-ups
- If the board widgets (`CorruptionBoardInfo`/`RobotBoardInfo`) resolve `RoleTargetSystem` via the composition root at spawn, they may share the same latent race — worth a look (out of scope here).
