# Investigation — Tablet InfoTable (tableau d'enquête) Canvas→UI Toolkit feasibility

## Hand-off Brief (15s read)

Converting the tablet's **InfoTable** deduction grid to UI Toolkit is **feasible** on Unity 6000.2.6f2 — the
hypothesized blockers (world-space display + masking + 3D interaction) are the *easy* part: masking is native
in UITK (`overflow:hidden`), and the tablet is always driven by a **free OS cursor** (never the reticle). The
real engineering is: (1) one genuine UNKNOWN — does native world-space UITK cooperate with the tablet's
existing **URP Overlay-camera stack** (PhoneCamera renders layer 8 with a copied projection), or do we switch
to a RenderTexture panel; (2) rebuilding a non-trivial interactive **players×roles grid** (sticky cross-headers
+ click-cycling cells + conflict logic) in UXML/USS; (3) the app is a **swipe-carousel member**, so a lone
UITK app doesn't slot into the uGUI `anchoredPosition`+DOTween slide shell.

## Case Info

- Slug: `tablet-uitk-feasibility`
- Type: Exploration / feasibility (no defect)
- Unity: 6000.2.6f2 (= Unity 6.2). URP.
- Target surface: `InfoTable` (GameScene › TabletParent › Tablet › ScreenCanvas › ScreenMask › SmartphoneApps › InfoTable)
- Status: Concluded — confidence **Medium** (one unverified integration unknown)

## Problem Statement

Poyo: can we `/canvas-to-uitk` the tablet's investigation board (`InfoTable`), given it's displayed in
world-space on the tablet and masked so it doesn't spill outside the frame? Check world-space UITK render in
6.2, masking/clipping, 3D interaction (reticle/cursor). Identify the primary blocker + alternatives.

## Confirmed Findings (MCP-inspected live scene)

- **F1 — Tablet rendering = world-space Canvas on a dedicated URP Overlay camera (NOT a RenderTexture).**
  `ScreenCanvas`: `Canvas.renderMode = 2` (World Space), `localScale 0.005`, `worldCamera = PhoneCamera`,
  `GraphicRaycaster.eventCamera = PhoneCamera`. `PhoneCamera`: `renderType = 1` (URP **Overlay**),
  `cullingMask = 256` (layer 8 only), `clearFlags = Depth`, no `targetTexture`, plus `Misc.CameraCopy` copying
  **CameraBrain**'s projection (copyPositionAndRotation = false). → The tablet UI is a world-space canvas on
  layer 8, composited by an overlay pass stacked over the board camera, with matched projection.
- **F2 — Masking = uGUI stencil `Mask`.** `ScreenMask` = `UnityEngine.UI.Mask` (showMaskGraphic false) +
  full-rect `Image` (isMaskingGraphic, stencil material ColorMask 0). Plain rectangular clip.
- **F3 — Interaction = free OS cursor, via PhoneCamera's GraphicRaycaster.** Tablet open forces the cursor
  unlocked in every mode: `AvatarCameraArbiter.ApplyCursorAndLook()` →
  `_lockCursor = _firstPerson && !_tabletOpen` (`AvatarCameraArbiter.cs:266-282`). Smartphone scripts never
  touch `Cursor.*`. Because PhoneCamera's projection = CameraBrain's (CameraCopy), the free cursor's screen
  position projects onto the world-space canvas correctly.
- **F4 — The reticle is IRRELEVANT to the tablet.** `ReticleInteractor` (uGUI-only: `EventSystem.RaycastAll`
  + `ExecuteEvents`/`IPointer*`) gates its world track on `Cursor.lockState == Locked`
  (`ReticleInteractor.cs:224`); the tablet is always Unlocked (F3). So the codebase's uGUI-only synthesized-
  pointer system, which UITK can't receive, never touches this surface.
- **F5 — InfoTable is a real, substantial interactive app.** `UI.InfoTable.InfoTableSystem`
  (`Assets/Scripts/UI/InfoTable/InfoTableSystem.cs`) builds a **players × roles deduction matrix**: rows =
  players, columns = roles, header row + per-player rows, cells = `InfoRoleChecker` (click-cycling
  Sure/Maybe/… states) with cross-referencing sticky headers (`Header`/`ChildHeader`,
  `SetHorizontalHeader`/`SetVerticalHeader`) and global conflict detection (`CheckGlobalConflicts`, role
  over-capacity). Built from prefabs (`Assets/Prefabs/InfoTableUI/*`) via nested `HorizontalLayoutGroup` +
  `LayoutElement`. Data-fed by `GameInfoRevealer` + `CharacterManager`; reveals lock cells when a role is
  revealed. (Corrects an earlier note that only the Lobby app existed — grep missed it: the script lives under
  `UI/InfoTable/`, not `Smartphone/Apps/`.)
- **F6 — InfoTable is a swipe-carousel member.** `SmartphoneApp` on InfoTable, `neighborApps.Right = ChatUI`.
  Shell (`SmartphoneController`) slides apps via `RectTransform.anchoredPosition` + `DOAnchorPos` (DOTween) +
  `CanvasGroup`. (`SmartphoneController.cs:108-116`).
- **F7 — World-space UITK is native (non-experimental) in Unity 6.2**; PanelSettings render mode = World Space.
  Non-XR pointer input integrates with the uGUI EventSystem via auto-created `PanelRaycaster`+`PanelEventHandler`
  (coexists with GraphicRaycaster, sort-order compared). (Unity 6000.2 manual "Create a World Space UI" +
  "Panel Input Configuration".)

## Deduced Conclusions

- **D1 — Masking: solved & simpler in UITK.** F2's rectangular stencil clip → UITK `overflow: hidden` on the
  root (a fixed-size panel is inherently clipped). Not a blocker; a simplification.
- **D2 — Interaction: NOT the blocker (F3+F4+F7).** Always free-cursor + reticle-inert. Mouse into a UITK panel
  is the standard supported path. The whole "3D interaction / reticle" worry is void for this surface.
- **D3 — The genuine UNKNOWN is world-space UITK × the URP Overlay-camera stack (F1+F7).** Today the tablet
  leans on a bespoke overlay camera (layer 8, copied projection) to composite the world-space canvas over the
  scene. Two resolutions:
  - **Path A — native world-space UITK panel** on layer 8, relying on the existing PhoneCamera overlay stack to
    render + the world-space `PanelRaycaster` (event camera = PhoneCamera) for input. Cleanest IF world-space
    UITK honors URP camera-stacking/layer culling the same way mesh geometry does. **Unverified — needs a spike.**
  - **Path B — PanelSettings → RenderTexture** mapped onto the tablet screen mesh. Most battle-tested UITK-on-
    3D-object route; drops the overlay-camera trick entirely. Cost: a custom screen→panel input function
    (pointer → mesh UV → panel coords) since there's no free-cursor projection match anymore.
- **D4 — Shell coupling (F6) forces a scoping choice.** A single UITK InfoTable can't be slid by the uGUI
  `anchoredPosition`+DOTween carousel. Options: (a) standalone UITK UIDocument shown/hidden by the shell
  (drops swipe-to-ChatUI continuity, cheapest), (b) convert the whole smartphone shell + all 3 apps to UITK
  (expensive, coherent), (c) keep a thin uGUI `CanvasGroup` wrapper hosting the UITK panel so the slide still
  works (hacky).
- **D5 — Grid rebuild is real but UITK-favorable (F5).** Nested `HorizontalLayoutGroup`s are exactly what UITK
  flex/grid layout does more cleanly. But the sticky cross-header linking, click-cycling `InfoRoleChecker`
  cells, reveal-locking, and conflict detection are all re-authored as VisualElements + USS + pointer handlers.
  The conflict/data logic (`CheckGlobalConflicts`, roleCounts) is presentation-agnostic and largely portable.

## Hypothesized / Watch-items

- **H1 — UITK root pickingMode blocking.** Project trap (`reference_uitk_state_screen_blocks_world_input`): a
  root set to `Position` picking swallowed input and previously regressed the lobby tablet + 3D cards. Set the
  world-space panel root to `Ignore`, pick per-control.
- **H2 — DOTween easing → USS** only approximated in the prior migration; any tablet/cell tweens re-authored.
- **H3 — Layer-8 overlay compositing of a UITK panel** (the core of D3-Path-A) is the single thing to prove
  first; if it fails, fall back to D3-Path-B (RenderTexture).

## Missing Evidence

- Whether native world-space UITK renders through a URP Overlay camera on a specific layer (D3-Path-A). Resolve
  with a throwaway spike scene, not more reading.
- Design intent: is InfoTable staying inside the swipe carousel, or acceptable as a standalone panel (D4)?
  Design-owned (Poyo).

## Final Conclusion — confidence Medium

**Feasible, and the scary parts are the easy parts.** World-space + masking + interaction all land in UITK's
supported/native path on 6.2; the free-cursor design (F3) removes the reticle problem entirely. The primary
blocker is not on Poyo's list — it's whether native world-space UITK cooperates with the tablet's existing URP
Overlay-camera composite (D3); a RenderTexture panel (Path B) is the proven fallback if it doesn't. Secondary
costs: rebuilding the interactive players×roles grid (F5) and resolving swipe-carousel shell coupling (D4).
`/canvas-to-uitk` gives a UXML/USS scaffold of the layout, but the interactive grid logic, input wiring, and
the render/composite path are hand-work it won't produce.

## Spike results (2026-07-09) — synthetic scene `Assets/Scenes/Spikes/UITKSpike.unity`

Built a throwaway scene to settle rendering + interaction empirically (Poyo ran play mode; Claude can't).
Assets: `SpikePanelController` / `SpikeRenderTextureInput` (`Assets/Scripts/UI/Spike/`), `Spike.uxml` + `Spike.uss`
(`Assets/UI/Screens/Spike/`), `PS_Spike_WorldSpace` (RenderMode=WorldSpace) + `PS_Spike_RenderTexture`.

**CONFIRMED (play-mode observed):**
- **World-space UITK renders in 3D via a normal camera** — panel visible in perspective. Confirmed.
- **Mouse click works NATIVELY, zero custom code** — clicking a cell logs `POINTER DOWN … target 'cell-N'`
  then `cell 'cell-N' CLICKED`. The path is: EventSystem + `PanelInputConfiguration` (auto-create,
  redirection on) → physics ray from the event camera → **a BoxCollider on the UIDocument GameObject** →
  UITK panel coords. The collider is the load-bearing requirement (`PanelSettings.renderMode = WorldSpace`
  ALONE does not pick; `ColliderUpdateMode` enum: 0 = none/manual → add a BoxCollider yourself, or use the
  "_Auto" flavor to auto-size one).
- **Masking works & is simpler** — `overflow:hidden` clipped the oversized red child at the panel edge.
- **PanelRenderMode enum: ScreenSpaceOverlay=0, WorldSpace=1** (NOT 2 — that silently falls back to overlay;
  early spike bug).

**DEDUCED from the spike:**
- The tablet does NOT need the RenderTexture path (Path B) NOR a custom `SetScreenToPanelSpaceFunction`. Native
  world-space UITK + a BoxCollider covers render + mask + click. Path B and my overlay-camera rig were
  over-engineering (Poyo's callout — correct). Deactivated in the scene.
- Cosmetic only: a 180° Y rotation on the panel mirrors it — face the panel to the camera by camera side, not
  by rotating the panel.

**STILL OPEN (does not block feasibility):**
- The tablet currently composites its uGUI canvas via a URP **Overlay camera** (`PhoneCamera`, layer 8). Whether
  native world-space UITK renders through that overlay stack is UNPROVEN — the spike proved it renders via a
  **base** camera (I removed the overlay). For the real tablet this likely doesn't matter: render the world-space
  UITK InfoTable via the main camera on the tablet-screen transform + a BoxCollider; only revisit the overlay
  compositing if the "always-on-top over 3D" behavior is actually required.

**Revised verdict — confidence High.** Feasible and clean: native world-space UITK gives render + mask + click
with no custom input code, just a BoxCollider. `/canvas-to-uitk` scaffolds the UXML/USS; the interactive grid
logic (cells, cross-headers, conflicts) is the real hand-work, and UITK suits it better than nested
HorizontalLayoutGroups.

## Recommended next steps

1. **30-min spike (resolves D3):** throwaway scene — a world-space UITK panel on layer 8, rendered by an
   overlay camera copying the board projection. Does it composite + does the world-space PanelRaycaster click?
   Pass → Path A. Fail → Path B (RenderTexture).
2. **Decision (Poyo, D4):** InfoTable stays in the swipe carousel (→ lean to full-shell UITK later) or OK as a
   standalone shown/hidden panel (→ cheap isolated conversion now)?
3. Then `gds-create-story` for the chosen path; keep Lobby + ChatUI on uGUI meanwhile (coexists).
