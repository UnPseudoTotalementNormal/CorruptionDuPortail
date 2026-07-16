---
title: 'Anonymous messages rebuilt as a unified per-turn journal (UITK) with server-side per-day persistence'
type: 'feature'
created: '2026-07-16'
status: 'draft'
approved: ''
baseline_commit: 'c8efdd3b'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/anonymous-message-menu-investigation.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-lobby-role-attribution-uitk.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-infotable-uitk-rt.md'
  - '{project-root}/_bmad-output/implementation-artifacts/ui-toolkit-style-guardrails.md'
design_reference: 'In-session visualize mockup (LOOK & interaction reference, NOT code): widget recap_par_tour_valide_mockup. Diegetic direction + per-turn stat phrasing + palette captured in the investigation follow-up #2.'
---

<!-- DRAFT — not frozen. Poyo still has features/feel to add ("on rajoute des trucs, je t'en parle après") and final approval pending. The Intent + Locked-decisions reflect the design validated "pour l'instant"; the Open-decisions section lists what is not yet settled. -->

## Intent

**Problem.** The anonymous-message feature is three uncoordinated UI surfaces over one server store (see investigation):
- `SendMessagePanel` (write) — out of scope here.
- `AwakeningRecapMessages` — the night reveal (animated, everyone, few seconds).
- `AnonymousRevealedMessagesComponent` — the sacoche archive (persistent list).

The night reveal and the sacoche archive **look identical by accident** (same `anonymousMessagePrefab`, same duplicated `SpawnNewMessageText`/day-format). Poyo wants them to be **literally one surface**, rebuilt in UI Toolkit, and **enriched with per-turn game info** beyond messages: the corrupted-player count and the Robot-targeting count, per turn.

**Solution — one unified per-turn journal.**
- **(A) One surface, two triggers.** A single UITK panel is the journal. The **night** presents it (already scrolled to the bottom) and the newest turn **writes itself in** (fade/rise cascade); the **sacoche** reopens the same panel to browse. No second component, no duplicated format. Replaces both `AwakeningRecapMessages` and `AnonymousRevealedMessagesComponent`.
- **(B) Per-turn info.** Each turn entry shows a stat line + that turn's messages. Stat line (accord singulier/pluriel):
  - `{n}/{total} corrompu(s)` — number **gold**, word red.
  - `Personne n'a ciblé le Robot` / `{n} joueur a ciblé le Robot` / `{n} joueurs ont ciblé le Robot` — number **gold**, `Robot` orange. Omitted entirely when there is no Robot in the game.
- **(C) Server-side per-day persistence (the technical core).** Today **nothing** is stored per turn, and Robot-targeting data is **erased every awakening** (`RoleTargetSystem.cs:49-52`). Both numbers are already computed live server-side but discarded. Add a server-authoritative, day-indexed record captured at awakening end **before** the reset. This becomes the single source the journal reads (and, optionally later, the existing board widgets).

## Locked decisions (design validated "pour l'instant")

1. **One component, two triggers** — night reveal (present + write-in the new turn) and sacoche (reopen to browse) are the SAME UITK surface over the SAME data. `AwakeningRecapMessages` + `AnonymousRevealedMessagesComponent` are deleted.
2. **Robot targeting = aggregate count only** (anonymous; never "who"). No new deanonymization.
3. **Per-turn stat phrasing** as in Intent (B); numbers gold, `corrompu(s)` red, `Robot` orange.
4. **Palette lane = LobbyRoles green/faction** (near-black `rgb(10,7,8)`, off-white `rgb(240,231,221)`, accent green `rgb(95,217,135)`, gold numbers `~rgb(238,201,122)`, red `rgb(219,74,86)`, orange `rgb(226,150,58)`). NOT the gold RoleCard lane.
5. **Diegetic, not dashboard** ([[feedback-diegetic-ui-over-web]]) — continuous flow, no rounded bordered cards; turns separated by a plain hairline rule with a centered turn label; centered serif frontispiece title (upright). Current separator/ornament choices are provisional (diamonds were tried and removed).
6. **"Write-on" reveal** — panel opens scrolled to bottom; the newest turn's stat + messages fade/rise in staggered; past turns dimmed by opacity for history depth.
7. **All palette/size values PLACEHOLDER, design-owned** — tokenize into `variables.uss` during build (LobbyRoles is literal today); never enshrine.

## Open decisions (NOT settled — resolve before freeze)

- **O1. RESOLVED (Poyo 2026-07-16): screen-space UITK overlay** (RoleCard-style: `PS_ScreenOverlay`, no `PanelInputConfiguration`). Reuse the LobbyRoles visual language without the RT tablet plumbing. **Night trigger reuses the existing `AwakeningRecap` event system** (drive the UITK panel from an `AwakeningRecapEventComponent`); refactor it minimally ONLY if that system doesn't lend itself to UITK — do not rebuild it wholesale.
- **O2. In-world title.** "Messages anonymes" (functional) vs an in-world name (Le Courrier / La Sacoche / Rumeurs / Murmures) vs no title. Poyo's call.
- **O3. Turn separator treatment** — plain rule + centered label validated in principle; exact ornament/typography still open.
- **O4. Empty turn** — a turn with 0 messages and (if Robot present) 0 targeting: still show the entry with just the corrupted count, or skip it? (Recommend: always show — the corrupted count alone is signal.)
- **O5. Poyo's additional features/feel** — pending ("on rajoute des trucs"). Spec extension points flagged in Data model.
- **O6. Sacoche availability** — can the journal be opened any time, or only after the first reveal / gated like today's `messageLeft`? (Today the bag opens `RevealedMessagePanel` unconditionally.)

## Boundaries & Constraints

**Always:**
- **Server authority.** The per-day record is written on the server only; clients read a replicated mirror. Both source values are already server-side (`Character.isCorrupted` NetworkVariables; server-fed `RoleTargetSystem.currentTargetingDataList`).
- **Capture before the reset.** Robot-targeting is cleared at the *next* awakening's start (`RoleTargetSystem.cs:41-52`). Record at **awakening end** (server) — the same moment `RobotBoardInfo`/`CorruptionBoardInfo` already recompute (`OnAwakeningStateEnd` / `onStateEndServer`). Safe: end precedes the next start's reset.
- **Single source of truth.** The recorded per-day values are computed once by one recorder and appended to one replicated list. The journal reads only that list (+ existing per-day `revealedMessages`). Do NOT re-derive the numbers in the view.
- **NetworkList late-joiner dedup.** `NetworkList` replays a same-tick-added entry twice for late joiners ([[reference_ngo_networklist_sync_duplicate]], project has no upstream fix). The new per-day stat list needs a ref-dedup guard like `CharacterManager`'s `[CHARLIST]`. Dedup by a stable key (day), NOT by value.
- **Message data reused as-is.** `MessageManager.revealedMessages` already carries `day` and the reveal→archive move (`RevealAllMessage`) works — keep it. The journal joins stats + messages by `day`.
- **UITK conventions** (RoleCard template): `[RequireComponent(UIDocument)]`, guarded `TryInitialize()` in OnEnable+Start, `Q<>` by name, BEM classes, dynamic children in C#, `var(--cdp-*)` tokens, transitions via USS OR `experimental.animation` (NOT DOTween — no DOTween in UITK surfaces).
- **Rebuild-safe animation.** The write-in cannot rely on a USS `transition` if the entry element is freshly created each rebuild (no resolved "from") — use `experimental.animation.Start(...)` ([[reference_uitk_transition_and_overlay_scale]]), as `LobbyRolesUitkController` does for card fades.
- **State-screen input hygiene** — if screen-space overlay, root `pickingMode = Ignore`, world mask via a scrim child ([[reference_uitk_state_screen_blocks_world_input]]); never `pickingMode = Position` on the fullscreen root.
- **Faction colors are data, not new tokens** — corrupted/Robot accents should map from the `FactionDatabase` SO vocabulary where they represent factions, not invented USS tokens.
- **Before mutating any scene/prefab** (GameScene wiring: `Bag3D`/`RevealedMessagePanel`/`AnonymousMessageStation`), copy the original into repo-root `BackupToolkit/` (gitignored) ([[feedback-toolkit-scene-backup]]).

**Ask First:**
- O1 rendering path (overlay vs tablet RT) — do not build the RT plumbing without confirmation.
- Retiring vs disabling the old surfaces: `RevealedMessagePanel` (uGUI `PanelComponent`), `AnonymousMessageStation` children, the `AwakeningRecapMessages` recap event, `anonymousMessagePrefab`.
- Whether the existing duplicated board widgets (`CorruptionBoardInfo`, `RobotBoardInfo`, `AwakeningRecapCorruption`) should be refactored to read the new single source (scope-creep risk — default: leave them, only the journal reads the new list).
- Any change to `MessageManager` being a §4 survivor singleton (the two deleted leaves used the bare `instance`; the new controller should resolve via `CompositionRoot` like `SendMessagePanel` does).

**Never:**
- Do not display `MessageInfo.senderClientId` (stored but must stay hidden — full anonymity).
- Do not add gameplay data — both values already exist server-side; only *persistence* is new.
- Do not dedup the stat list by a mutable/duplicated key (e.g. per-character ownerId); key on `day`.
- `AudioSource` (use FMOD — the open sound is `event:/Interface/Messages General + Anomaly/Menu 2`), `System.Threading.Tasks.Task` (UniTask), owner-write NetworkVariables, direct RPC without `GetSafeRpcTarget`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Record turn (server) | Awakening ends | Compute `corruptedCount` (LINQ: non-anomaly ∧ isCorrupted ∧ ¬isFake), `nonAnomalyTotal`, `robotTargetCount` (`GetAllTargetersForTarget(robotId).Count`); append `TurnStat{day,…}` to the replicated list; then existing `RevealAllMessage()` moves that day's messages to `revealedMessages` | No Robot in game → `robotTargetCount = -1` sentinel / hasRobot=false → clause omitted in view |
| Night reveal | Awakening recap event fires | Journal panel presented for everyone, scrolled to bottom; newest turn entry writes in (stat then messages, staggered fade/rise); dwell reuses existing timing (`base 2s + 0.05s/char`, clamp 3–10s); then panel hides | Zero messages this turn → still show the entry (corrupted count) unless O4 says skip |
| Open sacoche | Player clicks `Bag3D` (`Board3DButton` → target CustomButton) | Same journal panel opens, static (no write animation), scrolled to bottom, full history browsable | O6: gating TBD |
| Group by day | Journal builds | One entry per day present in `revealedMessages`/stat list; entry = header `TOUR n` + stat line + that day's messages | Days with a stat but no message still render the stat |
| Stat phrasing | `corruptedCount`, `nonAnomalyTotal`, `robotTargetCount` | Gold numbers; `corrompu`(1)/`corrompus`(>1); `Personne n'a`/`1 joueur a`/`n joueurs ont ciblé le Robot`; Robot orange | `nonAnomalyTotal==0` guard (no `x/0`) |
| Late joiner | Client connects mid-game | Replicated stat list + `revealedMessages` show canonical history; **no duplicated same-tick entry** (dedup guard) | Dedup by `day` |
| Bot / simulated caller | Host acts for a bot | Recording is server-side, list-replicated — bot-safe by construction; any RPC uses `GetSafeRpcTarget` | N/A |
| Reconnection | (out of scope) | — | — |

## Data model & persistence (the core)

**New replicated record.** A day-indexed stat entry, unmanaged (NetworkList constraint):
```
struct TurnStat : INetworkSerializable, IEquatable<TurnStat>
{
    int day;
    int corruptedCount;     // non-anomaly ∧ isCorrupted ∧ ¬isFake
    int nonAnomalyTotal;    // denominator for "n/total"
    int robotTargetCount;   // -1 (or hasRobot=false) when no Robot in game
}
```
Home: extend `MessageManager` with `NetworkList<TurnStat> turnStats` (keeps the message + stat records co-located; the manager is already the §4 survivor over both message lists), OR a dedicated `TurnJournalManager` if we want the message singleton untouched. **Recommend: extend `MessageManager`** — least surface, journal reads one manager.

**Recorder (server).** A server-only path (POCO computation + thin NetworkBehaviour hook, mirroring project POCO patterns for testability) subscribed to the awakening-end hook (same as `RobotBoardInfo.OnAwakeningStateEnd` / `onStateEndServer`):
1. Compute the three values (extract the existing LINQ from `CorruptionBoardInfo.cs:62-66` and the `GetAllTargetersForTarget(...).Count` from `RobotBoardInfo.cs:64-74` into a pure function).
2. Append `TurnStat{ currentDay, … }` to `turnStats`.
3. (existing) `RevealAllMessage()` still runs to move messages to the archive.
Ordering guarantee: this fires at awakening **end**; `RoleTargetSystem` clears at the next awakening **start** — targeting data is intact at record time.

**Pure decision seam (testable):** `TurnStatCalculator.Compute(characters, robotId?, targeters)` → `TurnStat`. No NGO, no MonoBehaviour — unit-testable like `RoleDistributor`.

**Extension points (Poyo's future features, O5):** add fields to `TurnStat` + one calc line + one view clause each. The recorder/view are additive by design.

## UITK surface & triggers

- **Controller** `MessageJournalController` (UIDocument) — builds the turn-entry list from `MessageManager` (stats + `revealedMessages`), grouped by day; `Open(animateNewest: bool)` / `Close()`. Resolves the manager via `CompositionRoot.For(NetworkManager)` (not the bare singleton).
- **Night trigger** — the `AwakeningRecap` event becomes a thin `AwakeningRecapEventComponent` that calls `Open(animateNewest:true)` and reports its duration to the recap state (reuse `EvaluateDuration`), replacing `AwakeningRecapMessages`' bespoke reveal loop.
- **Sacoche trigger** — `Bag3D`/`RevealedMessageButton` `onButtonClicked` → `Open(animateNewest:false)` instead of the old `PanelComponent.TryOpenPanel`.
- **UXML/USS** — `Assets/UI/Screens/MessageJournal/MessageJournal.uxml` + `.uss`; tokens from `variables.uss`/`theme.tss`. Entry rows built in C# (dynamic count).

## Code Map

**Reuse / read:**
- `Assets/Scripts/UI/RoleCard/RoleCardController.cs` — UITK overlay convention template + open/close + USS-transition-with-collapse pattern.
- `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` — dynamic list build + `experimental.animation` fade on rebuilt elements + the green/faction USS vocabulary (literal today).
- `Assets/Scripts/Board/UI/CorruptionBoardInfo.cs:62-66` — corrupted-count LINQ (extract to pure calc).
- `Assets/Scripts/Board/UI/RobotBoardInfo.cs:64-74` — Robot-target count via `RoleTargetSystem.GetAllTargetersForTarget`.
- `Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs` — targeting list + the reset hook (do not touch the reset; record before it).
- `Assets/Scripts/GameLogic/GameStates/AwakeningRecapState.cs` + `AwakeningRecapEventComponent` base — the night host.
- `Assets/Scripts/MessageSystem/MessageManager.cs` — extend with `turnStats`; `revealedMessages`/`RevealAllMessage` reused.
- `Assets/Scripts/Characters/RoleID.cs` (`Robot = 3917`), `Assets/Scripts/Domain/FactionType.cs`.
- `Assets/UI/Styles/{variables.uss,theme.tss}`.

**Create:**
- `Assets/UI/Screens/MessageJournal/MessageJournal.{uxml,uss}`.
- `Assets/Scripts/UI/MessageJournal/MessageJournalController.cs`.
- `Assets/Scripts/.../TurnStatCalculator.cs` (pure) + `TurnJournalRecorder` (server hook) + `TurnStat` struct.
- Tests: `TurnStatCalculatorTests` (EditMode, incl. no-Robot + 0-non-anomaly edge cases), a NetworkList dedup guard test, and an `AwakeningRecap` duration test.

**Delete / retire (Ask First):**
- `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapMessages.cs`.
- `Assets/Scripts/UI/Misc/AnonymousRevealedMessageRecap.cs` (`AnonymousRevealedMessagesComponent`).
- GameScene: `RevealedMessagePanel` (uGUI `PanelComponent`) + `AnonymousMessageStation` message children + `anonymousMessagePrefab`; re-wire `Bag3D`/`RevealedMessageButton` to the new controller.

## Test & verification plan

- **EditMode:** `TurnStatCalculator` (corrupted count, denominator, Robot count, no-Robot sentinel, 0-non-anomaly guard); dedup guard; recap duration.
- **PlayMode:** record-on-awakening-end appends exactly one `TurnStat`; late-joiner sees no duplicate (2-NM loopback — assert from HOST, [[reference_isowner_unreliable_multi_nm_tests]]).
- **Perceptual (Poyo):** 2-build client playtest of the night reveal + sacoche browse (Claude does not playtest — [[feedback-no-playtest-by-claude]]).
- After each code change: `read_console` for compile errors before assuming anything works; `run_tests` after a slice.
- Gate: `gds-code-review` + `party-mode` before merge (Poyo's requested process).

## Build order (proposed, slice into stories)

1. **Persistence core (headless). ✅ DONE (branch `feat/message-journal-per-turn`, not committed).** `TurnStat` struct + `TurnStatCalculator` (pure Compute + DedupByDay) + `CharacterFactionState` projection in `Assets/Scripts/MessageSystem/`; `MessageManager` gains `NetworkList<TurnStat> turnStats`, resolves `CharacterQuery`/`RoleTargetSystem` via `CompositionRoot` in `OnNetworkSpawn`, and records `(currentDay, corruptedCount, nonAnomalyTotal, robotTargetCount)` on `AwakeningState.onStateEndServer` (server-only, before the next-awakening reset). Recorder lives ON `MessageManager` (no new scene NetworkObject — avoids GlobalObjectIdHash risk). 7 EditMode tests (`TurnStatCalculatorTests`). Compile clean, **EditMode 443/443**. Dedup = read-side `DedupByDay` (view applies it in slice 2).
2. **UITK journal surface (Play harness). ✅ DONE (code, compiling; harness scene + perceptual check pending).** `Assets/UI/Screens/MessageJournal/MessageJournal.{uxml,uss}` + `MessageJournalController` (overlay RoleCard-style: `cdp-is-hidden`/`cdp-is-collapsed` + `pickingMode`, `Open(animateNewest)`/`Close`, scrim dismiss, staggered write-in via `experimental.animation`, auto-scroll bottom, dimmed history) + `IMessageJournalDataSource`/`DemoMessageJournalDataSource` seam + `--cdp-color-journal-*` tokens (green lobby lane, provisional). Stat line rendered as UITK rich-text (`<color=#…>` gold/red/orange) in one Label — CONFIRM in playtest, else split into spans. Frontispiece = LiberationSans (no serif asset; true serif = add a `.ttf`, deferred). Compiles clean. NEEDS: a spike harness scene + Poyo's Play (Claude can't playtest / UITK not in screenshots).
3. **Wire into GameScene. 🔶 PARTIAL.** DONE: `AwakeningRecapMessages` repurposed to drive the overlay (kept `EvaluateDuration` → same non-skippable duration; server `RevealAllMessage` then `OpenForReveal`); `MessageJournalOverlay` GO added to GameScene (UIDocument → PS_ScreenOverlay + uxml, `MessageJournalController` + `GameMessageJournalDataSource` wired); controller gained `OpenForReveal`/`OpenForBrowse` + reveal-mode animation-on-rebuild; data source made lazy-resolve (survives late network spawn). BackupToolkit/GameScene.unity.bak taken. PENDING: re-point the sacoche `RevealedMessageButton` onClick → `MessageJournalController.OpenForBrowse` (UnityEvent not MCP-editable — manual Inspector re-point or a runtime bridge); then delete the two old leaves (`AnonymousRevealedMessagesComponent`, old `RevealedMessagePanel`) after playtest confirms. Night reveal needs a 2-build client playtest.
4. **Polish pass (Poyo's feel additions, O5).**
5. `gds-code-review` + `party-mode` → address → merge to `Dev`.

## Commit / process notes
- Commit the generated `.md` artifacts with their slice ([[feedback-commit-generated-md-artifacts]]).
- No commit/push without Poyo's explicit per-change OK ([[feedback-no-commit-without-authorization]]).
- Serialized-field rewiring (Bag3D → new controller) via Unity MCP, append-don't-rename, verify every instance ([[feedback-serialized-field-rewiring]]).
