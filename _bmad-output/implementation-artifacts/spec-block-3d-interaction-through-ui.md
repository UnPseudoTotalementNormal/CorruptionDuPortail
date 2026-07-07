---
title: 'Block 3D board interaction through open UI'
type: 'bugfix'
created: '2026-07-07'
status: 'in-review'
context: []
baseline_commit: '39941eda7f1ad22ced91eeb88aa0937ea1bd2452'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 3D board interactables (`Board3DButton` on the bag/envelope, `PowerBarObject3D` powers) can be hovered and clicked *through* uGUI that sits in front of them — when a panel is open the reticle/cursor still reaches the collider behind it. Interaction must respect the UI on top.

**Approach:** Suppress the 3D interaction path whenever a blocking uGUI graphic is under the pointer, using the mechanism appropriate to each already-mode-separated input path: free cursor gates on `EventSystem.current.IsPointerOverGameObject()`; the embodied reticle suppresses its world-3D track when its own screen-centre `EventSystem.RaycastAll` (already run each frame) returns any uGUI hit.

## Boundaries & Constraints

**Always:** Keep the two input paths mutually exclusive on `Cursor.lockState` (unchanged). The reticle's existing uGUI tracks (cards / vote / skip buttons) and its click-priority order must keep working. A `null`/absent `EventSystem` must never throw — treat "no EventSystem" as "not over UI".

**Ask First:** If suppressing on *any* uGUI hit proves too aggressive (e.g. a persistent transparent world-canvas graphic overlaps the models when no panel is open), before adding depth/z comparison or a blocker layer/whitelist.

**Never:** Do not add a `PhysicsRaycaster`. Do not change which objects are interactable, the gating logic, or the panel wiring. Do not touch uGUI button behaviour. Do not make the reticle HUD dot a raycast target (it is correctly `raycastTarget:false`).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Free cursor, no UI under cursor | unlocked cursor over a 3D model, nothing uGUI in front | OnMouseEnter/Down fire → hover + click | N/A |
| Free cursor, UI panel over model | unlocked cursor over a raycastTarget uGUI covering the model | OnMouseEnter/Down suppressed → no hover, no click | `EventSystem.current == null` → treat as not-over-UI |
| Embodied, no UI at centre | locked cursor, reticle centred on a 3D model, no uGUI at centre | reticle world track hovers/clicks the model | N/A |
| Embodied, panel open at centre | locked cursor, a panel (screen overlay) covers screen centre | reticle world track returns null → no 3D hover/click; uGUI/panel still interactable | RaycastAll empty list → not blocked |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Reticle/ReticleInteractor.cs` -- `ResolveWorld()` runs the screen-centre physics ray for the embodied world-3D track; `ResolveTargets()` already fills `_uiResults` via `EventSystem.RaycastAll`.
- `Assets/Scripts/Board/UI/Board3DButton.cs` -- `OnMouseDown` / `OnMouseEnter` free-cursor entry points for the bag/envelope models.
- `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs` -- `OnMouseDown` / `OnPointerEnter` (the free-cursor `OnMouseEnter` uses a Unity message; power hover uses `OnPointerEnter` + `OnMouseDown`) for powers.
- `Assets/Scripts/Tests/PlayMode/Board3DButtonTests.cs` -- extend with the over-UI gate case.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Reticle/ReticleInteractor.cs` -- extract `internal static bool ShouldSuppressWorld(int uiHitCount) => uiHitCount > 0;`. In `ResolveWorld()`, `return null` when `ShouldSuppressWorld(_uiResults.Count)`. Suppression is scoped to the WORLD track only — the two uGUI tracks (`_ui`/`_body`, resolved by `ResolveTargets`) keep dispatching, so a vote card under the reticle still receives its IPointer events (Vote-safe). The `_worldHandler` still flows through `UpdateTrack(_worldHover, ..., null)`, which dispatches `IPointerExit` to the last world target → no stuck hover in embodied. Keep the `Cursor.lockState == Locked` gate.
- [x] `Assets/Scripts/Board/UI/Board3DButton.cs` -- add `protected virtual bool IsOverUI()` = `EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()`. Early-return from `OnMouseDown` and `OnMouseEnter` when true. Add `OnMouseOver` (fires each frame while the cursor is over the collider): if `_hovering && IsOverUI()`, call `Exit()` — **active de-hover** so a panel opening over an already-hovered model clears the highlight (Unity never sends `OnMouseExit` in that case). Leave `IPointer*` untouched.
- [x] `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs` -- guard `OnMouseDown` with the same `EventSystem.current?.IsPointerOverGameObject()` check (free-cursor click-through). Powers have NO free-cursor hover (their hover is `OnPointerEnter`, reticle-only, already cleared by the reticle's `UpdateTrack` exit), so no `OnMouseOver` de-hover is needed here.
- [x] `Assets/Scripts/Tests/PlayMode/Board3DButtonTests.cs` -- add: (a) EditMode-style truth table for `ReticleInteractor.ShouldSuppressWorld` (0→false, 1→true, N→true); (b) `Board3DButton` de-hover/gate branch test via a test subclass overriding `IsOverUI()` → true: assert a hovered model clears its hover and `OnMouseDown` does not forward; override → false: assert forward works. Document that the real `EventSystem.RaycastAll`/`IsPointerOverGameObject` wiring and the Vote-regression are covered by the manual matrix below (not unit-mockable faithfully).

**Acceptance Criteria:**
- Given a panel (e.g. SendMessagePanel/RevealedMessagePanel) is open over the board, when the player aims the reticle or moves the free cursor over a bag/envelope/power behind it, then no 3D hover feedback plays and no click is delivered.
- Given a model is already hovered (free cursor) and a panel then opens over it without the cursor moving, when the next frame runs, then the model's hover highlight is cleared (no stuck hover).
- Given the reticle is over a vote card / skip button (uGUI), when the player confirms, then the uGUI element still receives its IPointer click (world suppression must not break Vote).
- Given no UI is in front, when the player targets a 3D object, then hover + click work exactly as before in both modes.
- Given there is no active `EventSystem`, when `OnMouseDown`/`OnMouseOver` run, then they do not throw and behave as "not over UI".

## Spec Change Log

- **Party-mode review (Cloud Dragonborn / Link Freeman / Murat), 2026-07-07** — three findings amended the plan:
  - **Stuck hover on transition** (all three): a panel opening over an already-hovered model never triggers `OnMouseExit`/`IPointerExit`. Added active de-hover via `OnMouseOver` (free cursor) and documented that the embodied path already clears via `UpdateTrack(...,null)`. KEEP: the `Cursor.lockState` seam and the two-track reticle structure — they were judged sound.
  - **Vote regression risk** (Murat): scoped the suppression explicitly to the world track and added a Vote non-regression acceptance criterion; the uGUI tracks are untouched by design.
  - **Naive `Count > 0` / fullscreen scrim** (Cloud, Link): kept occlusion (user decision) but added a manual audit that no permanent fullscreen `raycastTarget:true` graphic sits at screen centre in the default board state, and a check that the blocking panels are ScreenSpaceOverlay (so `Count > 0` == "UI in front", no depth compare needed). Extracted `ShouldSuppressWorld` so the decision is unit-tested. The depth/occlusion-compare upgrade stays the documented fallback (frozen "Ask First").

## Design Notes

Decision (user): **occlusion**, not modal — block when a blocking uGUI graphic is physically under the pointer/reticle, matching "cliquer à travers l'UI". The reticle HUD dot is `raycastTarget:false`, so `_uiResults` never contains the reticle's own graphic; combined with the audit that no permanent fullscreen raycast-target sits at centre, `Count > 0` == a genuine blocking UI is in front. Valid because the blocking panels render ScreenSpaceOverlay (always in front of world geo) — to be confirmed during impl; if any blocking panel is World-Space, upgrade to a distance compare (`hit.distance` vs the world-space `RaycastResult`'s), the frozen "Ask First" path.

## Verification

**Commands:**
- `mcp__UnityMCP__run_tests` (PlayMode, `Board3DButtonTests`) -- expected: all pass, incl. `ShouldSuppressWorld` truth table + the de-hover/gate branch test.
- `mcp__UnityMCP__read_console` after recompile -- expected: no compile errors.

**Manual matrix (play mode — the reticle RaycastAll path + Vote regression are only faithfully checkable here):**
- Panel open, seated (locked) + board-overview (free cursor): bag/envelope/powers behind it neither highlight nor click; close panel → they respond again.
- Hover a model (free cursor), then open a panel over it without moving the mouse → highlight clears within a frame.
- Reticle over a vote card / skip button → the card/button still highlights and clicks (Vote intact); reticle over a bare 3D object → it responds; reticle over a 3D object behind an open panel → suppressed.
- Lock flip (Board↔Embodied) mid-hover and tablet-open (cursor unlocks while reticle active) → no stuck highlight, no click leaking through the tablet.
- Confirm `SendMessagePanel` / `RevealedMessagePanel` canvases are ScreenSpace-Overlay; audit for any fullscreen `raycastTarget:true` graphic at centre.
