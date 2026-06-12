# Story 12.2: Reroute the justified UI consumers

Status: review

## Story

As a developer,
I want the UI consumers the triage justified rerouted onto injected dependencies (lane A mostly),
so that the UI's worthwhile decoupling lands without churning the whole layer.

## Acceptance Criteria

1. **Scope = the 12.1 table's REROUTE rows, nothing more.** Batched by shape (trivial lane A first); each batch compiler-clean, suite-gated.
2. **Serialized-field safety on every wiring** (append-only, MCP-wired, read-back verified — D-NFR3); UI prefab-placed consumers respect the prefab-can't-ref-scene constraint (lane B push from their host, or root accessor where the host is itself spawned — per the established precedents).
3. **`Smartphone/` stays client-only** (reads state, emits wrapped ServerRpcs — never mutates) — any reroute preserves that boundary exactly.
4. **Registry/guards:** migrated UI types appended; both guards green; opt-out rows from 12.1 NOT appended (they keep their façade legitimately).
5. **Gated:** suite + boot smoke (full UI pass: phone apps, bars, recaps, vote flow) unchanged per batch; sprint-status.

## Tasks / Subtasks

- [x] **Task 1:** Batch plan from the 12.1 table (by shape + risk). Batch 1 = scene lane-A; Batch 2 = prefab push.
- [x] **Task 2:** Migrate per batch (6.1 template / push / root per row). Misjudged rows updated in §4g first, then implemented.
- [x] **Task 3:** Wiring verification sweep (SceneWiringGuard is the net — both batches end with both guard categories green).
- [x] **Task 4:** Gates; sprint-status; commits per batch (batch 1 = `4b17bea`).

## Dev Notes

- Mechanical by construction: every judgment was made in 12.1. If a row turns out misjudged mid-batch (e.g. a "trivial lane A" is actually prefab-placed), update the TABLE first, then implement — keep the map true.
- UI churn risk is scene-file merge weight: batch wiring commits tightly (scene + code same commit), never leave a scene wired against unmerged code.
- Staleness: fully driven by the 12.1 table.

### Project Structure Notes

- Modified: UI files per table, GameScene/prefabs (wiring), registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Canvas split/perf rules untouched; no `UnityEvent` gameplay wiring; TMP/uGUI conventions as-is.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3, §8 Epic 12] / [epics.md#Story 12.2]
- [Source: _bmad-output/implementation-artifacts/12-1-*.md] — the triage table (the whole scope).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile 0 errors (refresh_unity force after each batch). Both DI-seam guards (DiSeamGuard + SceneWiringGuard, 7 tests) green after each batch.
- EditMode 201/201, PlayMode 148/148 — baselines held, goldens unchanged, after each batch.
- Scene wiring read-back verified per consumer (manager GameObject id → resolved component ref) before saving GameScene.

### Completion Notes List

**Batch 1 — scene lane-A (committed `4b17bea`):** 5 scene-placed consumers rerouted off the locator onto `[SerializeField]` injected fields, MCP-wired in GameScene + read-back verified:
- `AnonymeMessageButton`, `InfoTableSystem` (both scene instances: the SmartphoneApp InfoTable + the InfoWorldCanvas prefab-instance), `PowersBar`: GameManager + CharacterManager.
- `CardPickerManager` (on the `Board` GameObject): BoardManager + CharacterManager + FocusManager; `MoveFocusParticlesToLayer` de-staticed to read the injected field.
- `TooltipLinkParser` (child of `TooltipManager`): CharacterManager.
- Registry: `All +=` the 5; `InjectedManagerTypes += FocusManager` (so SceneWiringGuard wiring-checks `CardPickerManager.focusManager`).
- **`ShutOffGameButton` reclassified REROUTE → OPT-OUT:** triaged lane A in 12.1 but lives prefab-only on `GameEndigStateUI.prefab` (lane A physically impossible). §4g table corrected; left on the façade for 12.3.

**Batch 2 — prefab push (this commit):** 5 prefab-placed consumers rerouted by reading an `ICharacterQuery` slice their parent/host already carries (guard #1 source-scan only — `NoLocatorOnly`):
- New public accessor `Card.CharacterQuery => characterManager` (mirror of the existing `Card.GameInfoRevealer`, 7.3/7.4).
- `MeIconCard` + `NoteRibbon` → `card.CharacterQuery`.
- `VoteStateUI` → the `StateUI` base `CharacterQuery` (9.1).
- `NoteChoosePanel` (no Card of its own) → slice pushed by the creating `NoteRibbon` via `SetTarget(..., ICharacterQuery)`.
- `AwakeningRecapCorruption` → slice pushed by its `AwakeningRecapStateUI` host (a StateUI carrying `CharacterQuery`) via a new pushed `AwakeningRecapEventComponent.CharacterQuery` property — **route corrected** (12.1 assumed a StateUI base; the real base `AwakeningRecapEventComponent` carries no slice).
- `NoteRibbon`/`NoteChoosePanel` keep `NoteManager.instance` (recorded §4f survivor — not forbidden).

**OPT-OUTs recorded (verify-don't-force — no injection context):**
- `AwakeningRecapMessages` + `AnonymousRevealedMessagesComponent` (`AnonymousRevealedMessageRecap.cs`): presentation views of the **MessageManager** singleton's reveal list (non-de-singletonised message singleton, §4 census) + a `GameManager.currentDay` read. Recorded; death → 12.3.
- `SelectPanelPlayer`: a `Resources.Load` prefab panel whose `CreatePannel` has no caller in the codebase to push a slice from. Recorded; death → 12.3.
- §4g table + the `CharacterManager` §9.3 census comment updated to match (rerouted vs still-on-façade-for-12.3).

**AC mapping:** AC1 (scope = 12.1 REROUTE rows; misjudged rows updated in the table first) ✅; AC2 (serialized-field safety: append-only, MCP-wired, read-back verified; prefab consumers via push/base, never a prefab→scene ref) ✅; AC3 (Smartphone/ client-only boundary untouched — only InfoTable read-rerouting, no RPC/authority change) ✅; AC4 (registry appended for the migrated; OPT-OUT rows NOT appended; both guards green) ✅; AC5 (suite + guards gated per batch; sprint-status) ✅.

### File List

**Batch 1 (commit `4b17bea`):**
- `Assets/Scripts/UI/BoardUI/AnonymeMessageButton.cs`
- `Assets/Scripts/UI/InfoTable/InfoTableSystem.cs`
- `Assets/Scripts/Board/UI/PowerBar/PowersBar.cs`
- `Assets/Scripts/UI/BoardUI/CardPickerManager.cs`
- `Assets/Scripts/UI/Misc/ShutOffGameButton.cs` (reverted to façade + OPT-OUT comment)
- `Assets/Scripts/TooltipSystem/TooltipLinkParser.cs`
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` (All += 5, InjectedManagerTypes += FocusManager)
- `Assets/Scenes/GameScene.unity` (lane-A wiring)
- `_bmad-output/refactor-architecture-despaghetti.md` (§4g ShutOffGameButton correction)

**Batch 2 (this commit):**
- `Assets/Scripts/Board/Card.cs` (public `CharacterQuery` accessor)
- `Assets/Scripts/UI/CardUI/MeIconCard.cs`
- `Assets/Scripts/UI/StateUI/VoteStateUI.cs`
- `Assets/Scripts/NoteSystem/NoteRibbon.cs`
- `Assets/Scripts/NoteSystem/NoteChoosePanel.cs` (`SetTarget` takes pushed `ICharacterQuery`)
- `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapEventComponent.cs` (pushed `CharacterQuery` base property)
- `Assets/Scripts/UI/StateUI/AwakeningRecapStateUI.cs` (host push)
- `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapCorruption.cs`
- `Assets/Scripts/UI/StateUI/AwakeningRecap/AwakeningRecapMessages.cs` (OPT-OUT comment)
- `Assets/Scripts/UI/Misc/AnonymousRevealedMessageRecap.cs` (OPT-OUT comment)
- `Assets/Scripts/UI/SpawnPanels/SelectPanelPlayer.cs` (OPT-OUT comment)
- `Assets/Scripts/Characters/CharacterManager.cs` (§9.3 census comment update)
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` (NoLocatorOnly += 5)
- `_bmad-output/refactor-architecture-despaghetti.md` (§4g batch-2 rows + outcome)
- `_bmad-output/implementation-artifacts/12-2-reroute-the-justified-ui-consumers.md` (this file)
- `_bmad-output/implementation-artifacts/sprint-status.yaml`

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | 12.2 (Epic 12 / D6). Batch 1 (`4b17bea`): 5 scene lane-A UI reroutes off GameManager/CharacterManager/BoardManager/FocusManager `.instance` onto MCP-wired `[SerializeField]` fields; ShutOffGameButton reclassified OPT-OUT (prefab-only). Batch 2: 5 prefab-push reroutes via `Card.CharacterQuery` / `StateUI.CharacterQuery` / host pushes (MeIconCard, NoteRibbon, VoteStateUI, NoteChoosePanel, AwakeningRecapCorruption); AwakeningRecapMessages + AnonymousRevealedMessagesComponent + SelectPanelPlayer recorded OPT-OUT (verify-don't-force). Both DI-seam guards green; EM 201/201, PM 148/148; goldens unchanged. |
