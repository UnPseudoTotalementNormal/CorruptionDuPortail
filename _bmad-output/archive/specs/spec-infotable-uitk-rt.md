---
title: 'InfoTable (tableau d''enquête) en UI Toolkit via RenderTexture→RawImage'
type: 'feature'
created: '2026-07-09'
status: 'in-progress'
baseline_commit: '84e848e66b068d73907c201c42dead124dd8217c'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/tablet-uitk-feasibility-investigation.md'
  - '{project-root}/_bmad-output/implementation-artifacts/ui-toolkit-style-guardrails.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The tablet's investigation board (`InfoTable`) is a placeholder uGUI grid built from nested `HorizontalLayoutGroup` prefabs + a `Header`/`ChildHeader` size-sync hack. It must become the *real* deduction board — a Players×Roles matrix of clickable 3-state cells with conflict detection — rebuilt in UI Toolkit, which suits the grid far better than layout-group juggling.

**Approach:** Author the grid in UXML/USS driven by a UITK controller, rendered by a **screen-space panel → RenderTexture → uGUI RawImage** placed inside the existing tablet `ScreenCanvas` (the proven Path B from the feasibility spike). This reuses the tablet's stencil `Mask`, PhoneCamera overlay and swipe carousel unchanged — only the app *content* becomes UITK. Build & validate first in the standalone `TabletOnlySpike` harness (Play-testable without a MP game via a demo data source), then port to `GameScene`, replacing the old uGUI board.

## Boundaries & Constraints

**Always:**
- Presentation-only, client-side. No RPCs, no `NetworkVariable` writes, no server mutation — read replicated state exactly like the current `InfoTableSystem`.
- Reuse the spike's RT pattern (`SpikeRawImageRt`): `panelSettings.targetTexture = RT`, `RawImage.texture = RT`, `SetScreenToPanelSpaceFunction` mapping screen→RawImage rect via the **PhoneCamera**, **no Y-flip**.
- UITK root `width:100%; height:100%`; RT aspect ≈ tablet screen (1443×913 → e.g. 1440×912) to avoid stretch.
- Conflict/reveal semantics ported **verbatim** from the uGUI logic (see Design Notes). Behaviour-preserving.
- Follow the `RoleCardController` conventions: `[RequireComponent(UIDocument)]`, guarded `TryInitialize()` in OnEnable+Start, `Q<>` by name, BEM classes, `var(--cdp-*)` tokens via `theme.tss`, dynamic children in C#.
- All colours/sizes come from `variables.uss` tokens (PLACEHOLDER, design-owned). Add new `--cdp-*` tokens sampled from the current board, never invented.
- Before mutating any scene/prefab, copy the original into repo-root `BackupToolkit/` (gitignored, outside Assets).

**Ask First:**
- Retiring/deleting the old `InfoTableSystem` + `InfoTableUI` prefabs (vs just disabling them).
- Any change to `GetSafeRpcTarget`/`IsLocalOrSimulated`-adjacent code, or to the carousel `SmartphoneController`.
- Enshrining any colour/size as final (palette is design-owned — keep provisional + tokenized).

**Never:**
- A dedicated `PS_ScreenOverlay` `targetTexture` mutation at runtime (it is shared with RoleCard — would hijack it). Use a **new dedicated PanelSettings** asset.
- Native world-space UITK (Path A) — Path B was chosen.
- `AudioSource`, `System.Threading.Tasks.Task`, owner-write NetworkVariables.
- Root `pickingMode = Position` on a fullscreen element (project trap — blocks world input); pick per-cell.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Build grid | Game started, N non-fake players, M distinct roles (by `roleName`), capacities from ALL chars incl. fakes | Header row = corner "Joueurs / Rôles" + M role columns (suffix `*k` when capacity k>1); one row per non-fake player | Empty/no roles → empty grid, no throw |
| Cell click | User clicks a state swatch (Sure/Maybe/SurelyNot) on an unlocked cell | That state toggles; a cell holds at most one state; background = state colour | Locked cell → click ignored |
| Local conflict | A player row has >1 cell marked **Sure** | Row flagged `PlayerMultipleRoles`: row bg = conflict colour, the Sure cells tinted conflict | Drops to None when ≤1 Sure |
| Over-capacity | More players marked **Sure** for role R than R's capacity | All those (unlocked, non-local-conflict) rows flagged `RoleOverCapacity` | Cleared when count ≤ capacity |
| Role revealed | `GetCharacterInfo(clientId).isRoleRevealed` becomes >0 (Personal/Public) | That row locks: correct role=Sure, others=SurelyNot, all cells non-interactive, row = locked colour | Already locked → skip |
| Click while app off-center | Pointer over where the (slid-off) RawImage would map | `ScreenToPanel` returns NaN (gated on `app.IsOpen` + rect bounds) → no phantom clicks | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/UI/InfoTable/InfoTableSystem.cs` -- OLD uGUI board; source of truth for logic to port, then neutralized.
- `Assets/Scripts/UI/InfoTable/InfoRoleChecker.cs` / `InfoTablePlayerRoleHandler.cs` -- OLD cell + row conflict logic (port verbatim into the POCO model).
- `Assets/Scripts/UI/InfoTable/ConflictType.cs` -- reused enum (None/PlayerMultipleRoles/RoleOverCapacity).
- `Assets/Scripts/UI/Spike/SpikeRawImageRt.cs` -- RT→RawImage + ScreenToPanel reference to productionize.
- `Assets/Scripts/UI/RoleCard/RoleCardController.cs` -- UITK controller convention template.
- `Assets/Scripts/Characters/{Character,Role,CharacterManager,PortraitTable}.cs`, `GameLogic/{GameInfoRevealer,GameManager}.cs` -- data source (see Design Notes for exact members).
- `Assets/Scripts/Smartphone/{SmartphoneApp,SmartphoneController}.cs` -- carousel contract (app needs RectTransform+CanvasGroup; `IsOpen` gate).
- `Assets/UI/{Screens/RoleCard,Styles/variables.uss,Styles/theme.tss,PanelSettings/PS_ScreenOverlay.asset}` -- UITK infra to mirror.
- `Assets/Scenes/Spikes/TabletOnlySpike.unity` -- build/validate harness. `Assets/Scenes/GameScene.unity` -- final port target (InfoTable app under `TabletParent→Tablet→ScreenCanvas→ScreenMask→SmartphoneApps`, `neighborApps.Right=Chat`).

## Tasks & Acceptance

**Execution:**
- [x] `BackupToolkit/` -- copy `InfoTable.prefab`, `GameScene.unity`, `TabletOnlySpike.unity` before any mutation.
- [x] `Assets/Scripts/UI/InfoTable/IInfoTableDataSource.cs` -- interface + POCO DTOs (`InfoTablePlayerRow{pseudo, clientId}`, role identity by `roleName`); events `OnRebuildRequested`, `OnRevealChanged`; `GetPlayers()`, `GetAllRoleCapacities()` (incl. fakes), `GetReveal(clientId)`.
- [x] `Assets/Scripts/UI/InfoTable/InfoTableModel.cs` -- pure C# matrix of `CellState{None,Sure,Maybe,SurelyNot}`; `SetCell`, local-conflict (`>1 Sure` in row), global over-capacity, reveal-lock. Ports `InfoTablePlayerRoleHandler`/`CheckGlobalConflicts` logic. Raises `OnChanged`. No Unity deps.
- [x] `Assets/Scripts/UI/InfoTable/GameInfoTableDataSource.cs` -- `MonoBehaviour, IInfoTableDataSource`; SerializeField `CharacterManager`/`GameInfoRevealer`/`GameManager`; adapts `GetCharacters(false)`, `onGameStarted`, `onCharacterInfoRevealedChanged`, `RevealLevel`.
- [x] `Assets/Scripts/UI/InfoTable/DemoInfoTableDataSource.cs` -- harness stub feeding the mockup set (afzaf / Simulated 1 / Simulated 2 × Dr Gloubi / L'Orpheline / Va'ahl le Mage Occulte); a key rebuilds/reveals for manual test.
- [x] `Assets/UI/Screens/InfoTable/InfoTable.uxml` + `InfoTable.uss` -- root `info-table` (100%/100%, `overflow:hidden`); controller builds header row + player rows + cells dynamically; BEM classes, tokenized.
- [x] `Assets/Scripts/UI/InfoTable/InfoTableUitkController.cs` -- `[RequireComponent(UIDocument)]`; binds a `IInfoTableDataSource`; builds the grid from the model; cell swatch click → `model.SetCell` → re-render conflict/lock visuals; guarded init.
- [x] `Assets/Scripts/UI/InfoTable/InfoTableRtPresenter.cs` -- productionized `SpikeRawImageRt`: create RT at runtime, `panelSettings.targetTexture`, `RawImage.texture`, `SetScreenToPanelSpaceFunction` via PhoneCamera; return NaN unless the owning `SmartphoneApp.IsOpen`; release RT in OnDisable.
- [x] `Assets/UI/PanelSettings/PS_InfoTable_RT.asset` -- dedicated PanelSettings (RenderMode ScreenSpaceOverlay=0, `theme.tss`, no baked RT — set at runtime), ScaleMode matching RT resolution.
- [x] `Assets/Scenes/Spikes/TabletOnlySpike.unity` -- repurposed the `SpikeTabletUITK` GO: UIDocument→PS_InfoTable_RT+InfoTable.uxml, replaced spike scripts with `InfoTableRtPresenter` (→SpikeRawImage + MainCamera) + `InfoTableUitkController` (→DemoInfoTableDataSource) + `DemoInfoTableDataSource`. Saved. **AWAITING Poyo Play-verify.**
- [x] `Assets/Scripts/Tests/Editor/InfoTableModelTests.cs` -- unit-test the I/O matrix rows (build, cell toggle, local conflict, over-capacity, reveal-lock).
- [x] `Assets/Scenes/GameScene.unity` -- harness validated by Poyo. Ported: RawImage `InfoTableRawImage` (full-stretch) under the `InfoTable` app; new root `InfoTableUITK` GO with UIDocument(PS_InfoTable_RT + InfoTable.uxml) + `GameInfoTableDataSource` (→ CharacterManager/GameInfoRevealer/GameManager) + `InfoTableRtPresenter` (→ RawImage, PhoneCamera, ownerApp=InfoTable) + `InfoTableUitkController` (→ data source). Old `InfoTableSystem` disabled + `Content` child left occluded. **Added `PanelInputConfiguration` to the GameScene EventSystem** — REQUIRED for RT-panel pointer routing (an RT panel isn't on-screen so the input module needs it to redirect screen→panel; RoleCard worked without it only because it's a screen-space overlay). **NEEDS Poyo in-game verify: InfoTable cells click AND RoleCard still opens.**

**Acceptance Criteria:**
- Given a started game on the tablet, when Poyo opens InfoTable, then a Players×Roles UITK grid renders inside the tablet frame, clipped by the existing mask, sliding with the carousel.
- Given the grid is open, when a cell swatch is clicked, then its state cycles and the background reflects Sure/Maybe/SurelyNot; a second Sure in the same row raises the row conflict; exceeding a role's capacity across rows raises the over-capacity conflict.
- Given a player's role is revealed, when the reveal event fires, then that row locks to the correct role and becomes non-interactive.
- Given the old board, when the migration lands, then exactly one (UITK) board shows and `run_tests` (EditMode) is green including `InfoTableModelTests`.

## Design Notes

**Ported logic (from `InfoTablePlayerRoleHandler` + `InfoTableSystem.CheckGlobalConflicts`):**
- Cell = single `CellState` (uGUI used 3 mutually-exclusive Toggles that can all be off → the `None` state).
- Local conflict: `sureCount = row.Count(c => c.State==Sure); conflict = sureCount>1 ? PlayerMultipleRoles : None`. Sure cells in a conflicting row are tinted conflict; locked rows never conflict.
- Global: count Sure per role across rows; if `count > capacity(role)` mark those rows `RoleOverCapacity` unless locked or already `PlayerMultipleRoles`. Reset over-capacity rows to local state before recomputing.
- Reveal-lock: correct role→Sure, others→SurelyNot, all cells `SetLocked(true)`, row = locked colour; driven by `GetCharacterInfo(clientId).isRoleRevealed > 0`.
- Role identity + capacity key on `roleName` (`Role.IsTheSameRole`). Column capacity counts ALL chars incl. fakes; player rows exclude fakes.

**Colours:** mockup shows Sure=green / Maybe=yellow / SurelyNot=red (old code used grey for SurelyNot); add `--cdp-color-info-sure/-maybe/-not/-conflict/-locked` tokens (provisional). The mockup ANOMALY/CHOSEN legend is out of scope unless trivially free.

**Data-source seam:** the `IInfoTableDataSource` interface is what lets the harness run without a live game (DemoInfoTableDataSource) and the POCO model be unit-tested. `GameInfoTableDataSource` is the only piece touching NGO types; keep it thin.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected: zero compile errors after each script batch.
- `mcp__UnityMCP__run_tests` (EditMode, filter InfoTable) -- expected: `InfoTableModelTests` green.

**Manual checks (Poyo, Play mode — UITK renders only in Play):**
- `TabletOnlySpike.unity`: grid renders in the tablet frame; clicking swatches changes state; a 2nd Sure in a row → row conflict; over-capacity across rows → conflict; a demo reveal locks a row; overflow clipped at the frame edge; no phantom clicks when swiped off-center.
