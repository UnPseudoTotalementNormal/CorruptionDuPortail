---
title: 'Lobby tablet app renders on top of the 3D + auto-closes on game launch'
type: 'bugfix'
created: '2026-06-20'
status: 'done'
baseline_commit: '7c76b1e5c36f8fd056c38031bd9a640c4195fae3'
context:
  - '{project-root}/_bmad-output/project-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Two issues with the lobby tablet (from the lobby-menu-on-tablet change): (1) the `GameSettings` "Lobby" app on the tablet is **occluded by 3D geometry** — it sits on the `UI` layer (5), so the main `CameraBrain` draws it inline with the world instead of on top. (2) The tablet, auto-raised in the Lobby, **stays up when the game launches** instead of lowering.

**Approach:** The "render on top via a dedicated layer camera" the user describes **already exists**: `PhoneCamera` renders ONLY the `Phone` layer (8) with `clearFlags = Depth` (depth-cleared → always on top), and `CameraBrain`'s culling mask already excludes `Phone`. The existing apps (`InfoTable`, `ChatUI`) are on `Phone` and render correctly on top. The Lobby app was simply left on `UI`. **Fix (1): move the whole Lobby app subtree to the `Phone` layer** — no new camera. **Fix (2): `LobbyAppPresenter` closes the tablet (`TryClosePanel`) on Lobby exit.**

## Boundaries & Constraints

**Always:**
- Reuse the existing `PhoneCamera` / `Phone` layer overlay (the established tablet-rendering mechanism); the Lobby app must match `InfoTable`/`ChatUI` (layer `Phone`, 8).
- Set the **entire** Lobby app subtree (every rendering descendant — `Background`, tabs, scroll, sliders, text) to `Phone`, not just the root — a child left on `UI` stays occluded.
- The layer is a HOST concern (the tablet host owns it), consistent with the modularity contract — the scene instance is set, not necessarily the host-agnostic prefab.

**Ask First:**
- Setting the `GameSettingsPanel.prefab` asset itself to `Phone` (default = set only the scene instance subtree; leave the prefab host-neutral unless Poyo wants tablet to be its canonical home).

**Never:**
- Do not add a new camera, change `CameraBrain`'s culling mask, the other overlay cameras (`AboveBlurCamera`/`FrostCamera`), or the URP renderer — the overlay stack already works.
- Do not regress `InfoTable`/`ChatUI` rendering, the carousel, or the auto-open/re-home behaviour.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Lobby app shown | tablet raised in Lobby | renders ON TOP of the 3D room (not clipped by table/walls), like InfoTable/Chat | N/A |
| Game launches | Lobby → next state | tablet auto-closes (lowers); re-homes to the default app for the next open | N/A |
| Board phase re-open | `openOnCamera` opens the tablet later | shows the default app (InfoTable), rendered on top, unaffected | N/A |
| Swipe to other apps in Lobby | swipe to Chat/InfoTable | all tablet apps render on top consistently | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scenes/GameScene.unity` — the `Lobby` app at `---GameVisuals---/TabletParent/Tablet/ScreenCanvas/ScreenMask/SmartphoneApps/Lobby` (currently layer `UI`/5); siblings `InfoTable`/`ChatUI` are layer `Phone`/8. Cameras: `CameraBrain` (main, excludes Phone), `PhoneCamera` (renders Phone, depth-clear → on top).
- `Assets/Scripts/Smartphone/Apps/Lobby/LobbyAppPresenter.cs` — `OnGameStateChanged`: the Lobby-exit branch closes the tablet.
- `Assets/Scripts/Smartphone/SmartphoneController.cs` — `TryClosePanel()` (reused).

## Tasks & Acceptance

**Execution:**
- [x] Render on top — `Lobby` app root set to `Phone` (8) in GameScene; the subtree + the **runtime-built** role-slider widgets are layered in code (the widgets aren't in the scene, so MCP can't reach them): `LobbyAppPresenter.Awake` → `gameObject.SetLayerRecursively("Phone")` (tablet host's concern); `RoleAttributionSettingTab` sets each instantiated widget to `layoutTransform.layer` (host-agnostic — follows its container). Reuses the existing `GameObjectExtension.SetLayerRecursively`. Also fixes raycasting (PhoneCamera is the canvas event camera → it now sees the sliders).
- [x] Auto-close — `LobbyAppPresenter.OnGameStateChanged`: on the **Lobby → game** transition (gated by `_previousValue` being LobbyState — review patch, NOT every later phase change), re-home a tablet still on the lobby app, then `smartphone.TryClosePanel()`.
- [x] Verify — `read_console` clean; EditMode 228/228, PlayMode 165/166 (1 = pre-existing `AvatarSpawnTests` flake, passes isolated), 3 guards green. Manual (tablet draws over the 3D + closes on Start) = Poyo.

**Acceptance Criteria:**
- Given the Lobby is active, when the tablet is raised, then the GameSettings panel renders on top of the 3D room (no geometry clips it), matching InfoTable/Chat.
- Given the host presses Start (or the state otherwise leaves the Lobby), when the transition happens, then the tablet closes automatically.
- Given the full suite runs, then EditMode + PlayMode + the three DI guards stay green (no regression).

## Design Notes

- **Why a layer move, not a new camera:** `PhoneCamera` (cullingMask = `Phone` only, `clearFlags = Depth`) is exactly the "separate camera in layer mode" overlay the request asks for, and it already draws the tablet on top; `CameraBrain` already culls `Phone`. The Lobby app was occluded only because it inherited the overlay's `UI` layer when extracted. Matching it to the sibling apps' `Phone` layer is the whole fix.
- **Whole subtree:** Unity renders per-GameObject layer, so every Image/TMP descendant must be on `Phone`; setting only the root leaves children occluded.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` — expected: zero errors after the change.
- `mcp__UnityMCP__run_tests` EditMode + PlayMode + the three guards — expected: green, no regression.

**Manual checks:**
- Enter the Lobby: the tablet GameSettings panel is drawn over the 3D table/room, not clipped. Press Start: the tablet lowers. During the board phase the tablet (InfoTable) still opens on its camera, on top.
