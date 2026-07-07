# Investigation: Tooltip 3D bounding box placement is inaccurate

## Hand-off Brief

1. **What happened.** For 3D linked objects, the tooltip's screen-space size is derived from only **2 of the 8 corners** of the world AABB (`_worldBounds.min`/`.max`), which under perspective projection does not bound the object's on-screen silhouette — so the tooltip is placed at a wrong distance from the object edge. **Confirmed** (`Assets/Scripts/TooltipSystem/TooltipManager.cs:185-192`).
2. **Where the case stands.** Root cause identified and confirmed by code inspection. The 3D branch is geometrically wrong; the UI/WorldSpace branch does it correctly (projects all rect corners then takes screen min/max).
3. **What's needed next.** Trivial fix: project all 8 AABB corners, accumulate screen-space min/max. → `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-07                                                                 |
| Status           | Active                                                                     |
| System           | Unity 6000.2.6f2, Corruption Du Portail, branch Dev                        |
| Evidence sources | Source code (TooltipSystem, TransformExtensions)                           |

## Problem Statement

User (Poyo): "j'ai l'impression que le bound que crée le HoverTooltipComponent pour placer le tooltip au bord de l'objet fonctionne un peu moyennement avec des objets 3D."

Premise partly refined: the bound is not built in `HoverTooltipComponent` (a thin trigger, 62 lines). It is computed in `TooltipManager.PlaceTooltip` — the 3D branch specifically.

## Evidence Inventory

| Source                        | Status    | Notes                                                        |
| ----------------------------- | --------- | ----------------------------------------------------------- |
| `TooltipManager.cs`           | Available | `PlaceTooltip` + `GetScreenBoundingBoxAndCenter`            |
| `HoverTooltipComponent.cs`    | Available | Trigger only; no bounds math                                |
| `TransformExtensions.cs`      | Available | `GetWorldBounds` — correct world AABB via Renderer.bounds   |
| `TransformExtensionsTests.cs` | Available | Tests GetWorldBounds, not the screen projection             |
| Runtime repro / screenshot    | Missing   | No captured example of the misplacement magnitude           |

## Confirmed Findings

### Finding 1: 3D branch projects only 2 of 8 AABB corners

**Evidence:** `Assets/Scripts/TooltipSystem/TooltipManager.cs:185-192`

**Detail:**
```csharp
Vector3 _boundsMin = _worldBounds.min;
Vector3 _boundsMax = _worldBounds.max;
Vector2 _screenMin = RectTransformUtility.WorldToScreenPoint(Camera.main, _boundsMin);
Vector2 _screenMax = RectTransformUtility.WorldToScreenPoint(Camera.main, _boundsMax);
_componentBoundingBoxSize = new Vector2(
    Mathf.Abs(_screenMax.x - _screenMin.x),
    Mathf.Abs(_screenMax.y - _screenMin.y));
```
A world AABB has 8 corners. `min` and `max` are two opposite corners differing in **all three axes including depth (Z)**. Perspective projection is non-linear in depth: the two projected corners do not span the screen-space silhouette of the box. The near-Z corner projects "wider", the far-Z corner "narrower"; `|screenMax - screenMin|` is neither the width nor the height of what the player sees.

### Finding 2: The UI/WorldSpace branch does the correct thing

**Evidence:** `Assets/Scripts/TooltipSystem/TooltipManager.cs:216-232`

**Detail:** For WorldSpace UI it calls `GetWorldCorners` (4 corners of the flat rect), projects **each** corner, then accumulates screen min/max. That is the geometrically correct pattern — and it is exactly what the 3D branch fails to do (it skips 6 corners). The correct approach exists in the same method; the 3D branch just doesn't follow it.

### Finding 3: GetWorldBounds itself is correct

**Evidence:** `Assets/Scripts/Extensions/TransformExtensions.cs:18-33`

**Detail:** Returns a proper world-space AABB by encapsulating all child `Renderer.bounds`. The world bounds are correct; the fault is purely in projecting them to screen space.

## Deduced Conclusions

### Deduction 1: Placement error scales with object depth and off-center camera angle

**Based on:** Finding 1.

**Reasoning:** When the object is centered on screen and thin in Z, min/max happen to approximately bound the silhouette → looks fine. As the object gains depth, is viewed at an angle, or sits off-center (large screen-X/Y), the perspective foreshortening between the near and far corner grows → the 2-corner box diverges from the real silhouette. Hence "fonctionne un peu **moyennement**" — sometimes acceptable, sometimes visibly off.

**Conclusion:** Symptom is intermittent and geometry-dependent, consistent with the 2-corner projection error rather than a constant offset bug.

## Source Code Trace

| Element       | Detail                                                                        |
| ------------- | ----------------------------------------------------------------------------- |
| Error origin  | `TooltipManager.cs:185-192` (3D branch of `PlaceTooltip`)                     |
| Trigger       | Hover over a linked object with **no** RectTransform and no BoundsOverride    |
| Condition     | 3D object with non-trivial depth / off-center / angled camera view            |
| Related files | `HoverTooltipComponent.cs` (trigger), `TransformExtensions.cs` (world AABB)   |

## Conclusion

**Confidence:** High.

**Confirmed root cause:** The 3D placement branch estimates screen size from only the two diagonal corners of the world AABB. Under perspective projection this does not bound the object's on-screen silhouette, so `_componentBoundingBoxSize` (used at `TooltipManager.cs:195` to offset the tooltip off the object edge) is wrong — producing the "moyennement" placement. The correct multi-corner pattern already exists in the same file's WorldSpace branch.

## Recommended Next Steps

### Fix direction

In the 3D branch (`TooltipManager.cs:184-192`), enumerate **all 8 corners** of `_worldBounds`, `WorldToScreenPoint` each, and accumulate screen min/max — mirroring the WorldSpace branch (`:218-232`). Derive both `_componentScreenPos` (center of the screen-space box) and `_componentBoundingBoxSize` from that accumulated box.

Sketch:
```csharp
Bounds _wb = _linkedGameObject.transform.GetWorldBounds();
Vector3 c = _wb.center, e = _wb.extents;
Vector2 sMin = Vector2.positiveInfinity, sMax = Vector2.negativeInfinity;
for (int i = 0; i < 8; i++)
{
    Vector3 corner = c + new Vector3(
        (i & 1) == 0 ? -e.x : e.x,
        (i & 2) == 0 ? -e.y : e.y,
        (i & 4) == 0 ? -e.z : e.z);
    Vector2 sp = RectTransformUtility.WorldToScreenPoint(Camera.main, corner);
    sMin = Vector2.Min(sMin, sp);
    sMax = Vector2.Max(sMax, sp);
}
_componentBoundingBoxSize = sMax - sMin;
_componentScreenPos = (sMin + sMax) * 0.5f;
```
Note: use the projected-corners centre (not `WorldToScreenPoint(center)`) so center and size come from the same silhouette box.

### Diagnostic

- Add a unit/PlayMode test projecting a known cube AABB at an angled camera; assert the 8-corner box ⊇ the 2-corner box and matches the rendered silhouette within tolerance.
- Optional temporary `[TOOLTIP3D]` debug draw of the computed screen box to eyeball against the object.

### Edge cases to guard

- Corner behind the camera → `WorldToScreenPoint` returns garbage/negative. Consider clamping or skipping when any corner's camera-space z < near plane.
- `Camera.main` null / not-tagged — current code already assumes `Camera.main`; unchanged but worth a null guard.

## Side Findings

- The 3D branch computes `_componentScreenPos` from `WorldToScreenPoint(center)` while size comes from corners — inconsistent basis; the fix unifies them. (`TooltipManager.cs:182` vs `:185-192`.)
- Hard dependency on `Camera.main` throughout `PlaceTooltip`; no null guard. Low priority. (`TooltipManager.cs:171,176,182,187-188`.)
- `HoverTooltipComponent` premise correction: it holds only offset direction + optional `tooltipBoundsOverride`; all bounds math lives in `TooltipManager`. (`HoverTooltipComponent.cs:17-22`.)
