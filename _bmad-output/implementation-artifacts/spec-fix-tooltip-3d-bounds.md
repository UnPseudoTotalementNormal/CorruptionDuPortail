---
title: 'Fix 3D tooltip edge placement (project all 8 AABB corners)'
type: 'bugfix'
created: '2026-07-07'
status: 'done'
baseline_commit: 'e8463cc7f4502f29680d2ce1ca9a22aaa24bb685'
context: ['{project-root}/_bmad-output/implementation-artifacts/investigations/tooltip-3d-bounds-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** For a hovered 3D object (no RectTransform), `TooltipManager.PlaceTooltip` estimates the on-screen size from only 2 of the 8 world-AABB corners (`min`/`max`). Under perspective projection those two diagonal corners don't bound the object's screen silhouette, so the tooltip sits at a wrong distance from the object edge — "moyennement" placement that worsens with depth/angle/off-center.

**Approach:** Project **all 8** AABB corners to screen space, accumulate the screen min/max, and derive both the tooltip's reference center and size from that single silhouette box. Extract the projection into a reusable, unit-testable `Bounds` extension.

## Boundaries & Constraints

**Always:** Only modify the 3D `else` branch of `PlaceTooltip`. The new screen center AND size must come from the same accumulated 8-corner box. Guard corners behind the camera near-plane (mirrored/garbage projection). Keep FMOD/NGO/async conventions irrelevant here — pure geometry, no state mutation.

**Ask First:** Any change to the offset formula at `TooltipManager.cs:195`, or to the 2D/UI/BoundsOverride branches.

**Never:** Do NOT touch `_boundsOverride`, `_linkedRectTransform` (UI) branch, or `GetScreenBoundingBoxAndCenter`. Do NOT alter `GetWorldBounds`. No new `.cs` file (append to existing `TransformExtensions.cs`). No behavior change for 2D objects.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Front-facing 3D object | AABB fully in front of camera | Screen box centered on silhouette; symmetric size > 0 | N/A |
| Angled / deep object | AABB with large Z spread, off-center | 8-corner screen box ⊇ old 2-corner box; tooltip hugs true edge | N/A |
| Fully behind camera | All 8 corners with camera-space z <= 0 | Helper returns false; caller falls back to center projection, zero size | Return false, no NaN |
| Partially behind camera | Some corners z <= 0 | Box built from the valid (in-front) corners only | Skip behind-corners |
| Null camera | `Camera.main` is null | Helper returns false | Return false, no crash |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Extensions/TransformExtensions.cs` -- add `TryGetScreenBounds(this Bounds, Camera, out Vector2 size, out Vector2 center)` next to existing `GetWorldBounds`
- `Assets/Scripts/TooltipSystem/TooltipManager.cs:178-193` -- 3D `else` branch; replace 2-corner math with a call to the new helper
- `Assets/Scripts/Tests/Editor/TransformExtensionsTests.cs` -- add EditMode tests for the helper

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Extensions/TransformExtensions.cs` -- add `TryGetScreenBounds`: loop the 8 corners via center±extents, `camera.WorldToScreenPoint` each, skip corners with `z <= 0`, accumulate screen min/max; out `size = max-min`, `center = (min+max)*0.5`; return false if camera null or zero valid corners -- gives the true screen silhouette box
- [x] `Assets/Scripts/TooltipSystem/TooltipManager.cs` -- in the 3D `else` branch, call `_worldBounds.TryGetScreenBounds(Camera.main, out _componentBoundingBoxSize, out _componentScreenPos)`; on false, fall back to `WorldToScreenPoint(Camera.main, _worldBounds.center)` + zero size -- fixes placement, 2D branches untouched
- [x] `Assets/Scripts/Tests/Editor/TransformExtensionsTests.cs` -- unit-test the I/O matrix rows (front-facing symmetry, behind-camera→false, containment vs naive 2-corner) -- lock the geometry

**Acceptance Criteria:**
- Given a 3D object viewed at an angle, when hovered, then the tooltip anchors flush to the object's on-screen edge (no overlap, no large gap).
- Given the same hover on a 2D/UI object or a BoundsOverride, when hovered, then placement is byte-for-byte unchanged (only the `else` branch changed).
- Given the project compiles, when EditMode tests run, then the new `TryGetScreenBounds` tests pass with no new console errors.

## Design Notes

8-corner enumeration via bitmask on extents:
```csharp
Vector3 c = b.center, e = b.extents;
for (int i = 0; i < 8; i++) {
    Vector3 corner = c + new Vector3((i&1)==0?-e.x:e.x, (i&2)==0?-e.y:e.y, (i&4)==0?-e.z:e.z);
    Vector3 sp = camera.WorldToScreenPoint(corner); // z = camera-space depth
    if (sp.z <= 0f) continue;                        // behind camera → mirrored garbage
    min = Vector2.Min(min, sp); max = Vector2.Max(max, sp);
}
```
Use `camera.WorldToScreenPoint` (returns z) rather than `RectTransformUtility.WorldToScreenPoint` (Vector2 only) so the behind-camera guard is possible; x/y are numerically identical for a non-null camera. Tests assert robust properties (symmetry, size>0, containment, false-on-behind) not absolute pixels, to stay independent of EditMode screen size.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after edits) -- expected: no compile errors/warnings from the two changed scripts
- `mcp__UnityMCP__run_tests` (EditMode, filter TransformExtensionsTests) -- expected: all green incl. new cases

**Manual checks:**
- In GameScene, hover a 3D power/object at an oblique camera angle -- tooltip sits flush to the visible edge; re-check a 2D card tooltip is visually unchanged.

## Suggested Review Order

**The fix (entry point)**

- Start here: the 3D `else` branch now delegates to the helper; on failure it falls back to the bounds-center projection with zero size. Confirm no other branch was touched.
  [`TooltipManager.cs:184`](../../Assets/Scripts/TooltipSystem/TooltipManager.cs#L184)

**Geometry helper**

- The core: 8-corner projection, near-plane guard, screen min/max; center and size come from the same box.
  [`TransformExtensions.cs:44`](../../Assets/Scripts/Extensions/TransformExtensions.cs#L44)

- Near-plane guard (patched from `z <= 0` to `z <= nearClipPlane` in review) — skips corners that would explode the box.
  [`TransformExtensions.cs:70`](../../Assets/Scripts/Extensions/TransformExtensions.cs#L70)

**Tests (peripherals)**

- Four cases lock the geometry: symmetry, behind→false, straddle→finite, containment vs old 2-corner box.
  [`TransformExtensionsTests.cs:92`](../../Assets/Scripts/Tests/Editor/TransformExtensionsTests.cs#L92)
