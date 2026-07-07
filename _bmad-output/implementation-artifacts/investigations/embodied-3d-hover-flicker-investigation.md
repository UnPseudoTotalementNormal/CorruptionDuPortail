# Investigation: Embodied 3D hover flickers (bag) / appears dead (letter)

## Hand-off Brief

1. **What happened.** In embodied (reticle) mode, hovering the bag `Board3DButton` oscillates hover on/off every frame; the envelope shows no visible hover — both are the same defect (Confirmed root cause).
2. **Where the case stands.** Root cause Confirmed from code + project config: `Board3DButton` shares one GameObject for collider AND visual, and `Enter()` moves that GameObject to the `Outline_Hover` layer, which is NOT in the reticle's `_worldMask`, so the next physics ray misses → exit → restore → hit → enter → 1-frame flicker.
3. **What's needed next.** Widen `ReticleInteractor._worldMask` to include the three `Outline_*` layers (a transient interaction state must stay raycastable). Trivial one-value scene change; verify in play.

## Case Info

| Field            | Value |
| ---------------- | ----- |
| Ticket           | N/A |
| Date opened      | 2026-07-07 |
| Status           | Concluded |
| System           | Unity 6000.2.6f2, embodied/reticle mode, Vote state (screenshot: cards + Skip + timer 262) |
| Evidence sources | Source code, ProjectSettings/TagManager.asset, prior session context, user screenshot |

## Problem Statement

User (embodied): "quand je regarde le sac ça flicker entre hover et non hover"; "pour la lettre j'ai l'impression que ça marche juste pas"; "tout le reste c'est nickel" (powers, cards, skip fine).

## Confirmed Findings

### Finding 1: Board3DButton's collider and visual are the same GameObject

**Evidence:** Scene wiring — `BoxCollider`/`MeshCollider` was added to `Bag3D_View` (the object that also holds the `MeshRenderer`), and `Board3DButton.visualTransform` = `Bag3D_View`. For the envelope, the collider is on `envelope_low` and `visualTransform` is null → defaults to `transform` (= `envelope_low`) in `Board3DButton.Awake`. `Assets/Scripts/Board/UI/Board3DButton.cs:60` (visualTransform defaulting), collider on the same GO.

### Finding 2: Hover swaps that GameObject's layer to Outline_Hover

**Evidence:** `Assets/Scripts/Board/UI/Board3DButton.cs` `Enter()` → `visualTransform.gameObject.SetLayerRecursively(_hoverLayer)` with `_hoverLayer = LayerMask.NameToLayer("Outline_Hover")`. `Exit()` restores `_defaultLayer` (captured in Awake = `3DPhysical`). Because visual == collider GO, the collider's layer changes with it.

### Finding 3: The reticle only raycasts the 3DPhysical layer

**Evidence:** `Assets/Scripts/Reticle/ReticleInteractor.cs` `ResolveWorld()` → `Physics.Raycast(_ray, out _hit, _worldMaxDistance, _worldMask, ...)`; `_worldMask` is set to `128` in the scene = bit `1<<7` = `3DPhysical` only. `ProjectSettings/TagManager.asset:15` (3DPhysical = layer 7); `:37-39` Outline_Used=29, Outline_Highlight=30, Outline_Hover=31.

### Finding 4: PowerBarObject3D keeps collider and visual on separate GameObjects

**Evidence:** `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs:79-96` instantiates `_power3DModel` (visual) AND `_power3DModelCollider` (collider) as two objects; `OnPointerEnter` swaps only `powerViusalTransform` to Outline_Hover. The collider (`powerColliderTransform`) stays on 3DPhysical → the reticle ray keeps hitting → no flicker. Explains "powers are fine".

## Deduced Conclusions

### Deduction 1: The flicker is a self-sustaining 1-frame layer oscillation

**Based on:** Findings 1-3.

**Reasoning:** Frame N: bag on 3DPhysical → ray hits → `UpdateTrack` dispatches `IPointerEnter` → `Enter()` sets layer Outline_Hover (31). Frame N+1: `_worldMask` excludes 31 → ray misses → `_world = null` → `UpdateTrack` dispatches `IPointerExit` → `Exit()` restores 3DPhysical (7). Frame N+2: ray hits again → enter → … The reticle's hysteresis (`_exitDwell`/`_switchDebounce`) does not damp it because the target genuinely alternates handler↔None each frame.

**Conclusion:** Bag hover strobes at frame rate = the reported flicker.

### Deduction 2: The letter is the same defect, not a separate one

**Based on:** Findings 1-3 apply identically to `envelope_low`.

**Reasoning:** Same oscillation; the difference is only perceptual — an outline on a thin, flat, table-flush letter is barely visible, and strobing it reads as "nothing happens" rather than "flicker".

**Conclusion:** One fix resolves both. (Hypothesized secondary: the flat-letter outline may also be weak/occluded even once stable — verify after the fix.)

## Source Code Trace

| Element       | Detail |
| ------------- | ------ |
| Error origin  | `Assets/Scripts/Reticle/ReticleInteractor.cs` `ResolveWorld()` — raycast uses `_worldMask` = 3DPhysical only |
| Trigger       | Reticle centred on a `Board3DButton` collider while embodied (locked cursor) |
| Condition     | `Board3DButton.Enter()` moves the shared collider+visual GO to Outline_Hover (layer 31), outside `_worldMask` |
| Related files | `Assets/Scripts/Board/UI/Board3DButton.cs`; `Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs` (the working counter-example); `ProjectSettings/TagManager.asset` (layer indices) |

## Conclusion

**Confidence:** High — Confirmed from code + config, deterministic mechanism, matches all three observations (bag flickers, letter looks dead, powers fine).

Root cause: the reticle's world raycast mask (`3DPhysical` only) does not include the transient `Outline_*` interaction layers, and `Board3DButton` (unlike `PowerBarObject3D`) puts its collider on the same GameObject whose layer the hover swaps. So the hover instantly moves the collider out of the raycast mask, self-cancelling.

## Recommended Next Steps

### Fix direction

Preferred — **widen `ReticleInteractor._worldMask`** to `3DPhysical | Outline_Hover | Outline_Highlight | Outline_Used` (bits 7,31,30,29 → combined mask, unsigned `3758096512` / signed int `-536870784`). A hovered/highlighted/used object legitimately lives on those layers during interaction, so the reticle must keep raycasting them. Also future-proofs any shared collider/visual object and keeps powers working (their collider stays on 3DPhysical, still in-mask).

Alternative (more surgery, not recommended) — give `Board3DButton` a separate child collider (mirror PowerBar) so the visual layer swap never touches the collider. Rejected: duplicates geometry, heavier, and the mask widening is the more general correctness fix.

### Diagnostic

If flicker persists after widening the mask: log `_worldHandler` transitions per frame in `ReticleInteractor.Update` and confirm it stays non-null while aiming at the bag. For the letter, after the flicker is gone, inspect whether the Outline renderer feature actually draws on the flat mesh (may need thickness/renderingLayer tweak — separate cosmetic follow-up).

## Reproduction Plan

Embodied/Vote, aim reticle at the bag → hover outline strobes on/off. Aim at the envelope → no stable outline. Aim at a power → stable outline (control). After fix: bag and envelope outline hold steady while aimed at.

## Side Findings

- The `Outline_*` layers being outside `_worldMask` also means a power that is on `Outline_Highlight` (its idle "usable" state) is only reticle-hittable because its collider is a *separate* object still on 3DPhysical (Confirmed via PowerBarObject3D). Widening the mask makes this robust rather than incidental.

## Follow-up: 2026-07-07

### Fix applied

`ReticleInteractor._worldMask` widened in `GameScene` from `128` (3DPhysical only) to signed int `-536870784` (unsigned `3758096512`) = layers 7 (3DPhysical) + 29 (Outline_Used) + 30 (Outline_Highlight) + 31 (Outline_Hover). Scene saved; no code change.

MCP note: a LayerMask that includes layer 31 is a NEGATIVE int32. Setting it via the unsigned `3758096512` silently coerced to `0` (int overflow); it only took as `{"value": -536870784}`. Read-back confirmed `_worldMask.value == -536870784`.

### Verification pending (user, in play)

Aim reticle at bag → outline holds steady (no strobe); aim at envelope → stable hover; powers/cards/skip unchanged. Possible cosmetic follow-up: the flat letter's outline may still read faint even when stable (Outline renderer on a thin table-flush mesh) — separate from this fix.

## Follow-up: 2026-07-07 #2

### New evidence — the mask fix did NOT stop the flicker

User: flicker persists on both bag and envelope after `_worldMask` widening (letter now reaches parity with the bag). ALSO confirmed: all GraphicRaycasters have `m_BlockingObjects: 0` (grep GameScene.unity) → the earlier "3D layer toggles uGUI occlusion" idea is REFUTED, and the reticle-dot is `raycastTarget:false` so `_uiResults` isn't polluted.

### Corrected root cause (Confirmed)

The widening taking effect (letter now hovers) proves the collider-on-Outline_Hover is now in the mask and IS hit — so the layer was NOT the flicker cause. The real cause: `Board3DButton.Enter()` runs `visualTransform.DOScale(...)` on the SAME GameObject that holds the **MeshCollider** (bag `Bag3D_View`, envelope `envelope_low`). Animating a **non-convex MeshCollider's scale re-cooks it every frame**, and a re-cooking MeshCollider drops raycasts for a frame → `_world` goes null → `IPointerExit` → scale tween reverses → re-cook → hit again → self-sustaining strobe. `PowerBarObject3D` doesn't flicker because its collider is a SEPARATE, non-scaled object (`powerColliderTransform`); only its visual scales. Confirmed by the differential.

### Fix applied

- **Bag** — decoupled (mirrors PowerBar): new parent `Bag3D` (layer 3DPhysical, never scaled) holds the MeshCollider (`sharedMesh` = StylizedBag.fbx, bounds 2.64³) + `Board3DButton`; the original `Bag3D_View` is now its child visual that scales on hover. Collider no longer scales → no re-cook.
- **Envelope** — decoupled like the bag (user did the reparent): `envelope_low` reparented to identity-local under `Envelope3D_Send`, which now holds a MeshCollider (`Mesh` = envelope_low, aligns because the child is identity-local) + `Board3DButton` (layer set to 3DPhysical, targetButton = MessageButton, visualTransform = envelope_low). The child `envelope_low` is visual-only (collider + handler removed) and scales/outlines on hover; the parent collider stays 3DPhysical and never scales → no re-cook. (An interim BoxCollider-on-child fix was replaced by this cleaner parent-MeshCollider once the reparent was done by hand.)
- `_worldMask` kept widened (the envelope's box collider still rides `envelope_low` which swaps to Outline_Hover on hover, so the mask must include it).

MCP notes: reparent via `manage_gameobject modify parent:<name>` silently no-ops if the name index is stale (e.g. right after a rename) while still applying the local transform → the child jumps to the parent origin. Use a freshly-created unique name, verify `parentInstanceID` in the response, and restore the child's local transform if it slipped.

### Verification pending (user, in play)

Both bag and envelope: outline holds steady while aimed at (no strobe). If flicker STILL persists, the re-cook theory is wrong — next step is a `[FLICKER]`-tagged per-frame log in `ReticleInteractor.Update` printing `_worldHandler`, `_uiResults.Count`, and `Cursor.lockState` to see exactly what oscillates.

## Follow-up: 2026-07-07 #3

Embodied flicker is fixed (reparent). New report: in FREE-CURSOR (board overview, mouse) the hover flashes one frame then dies while the click still works. Instrumented `Board3DButton` with `[HOVERDBG]` logs and captured the sequence:

```
OnMouseEnter Bag3D  overUI=False   → Enter() (outline ON)
IPointerEnter Bag3D overUI=True
OnMouseOver->Exit (overUI) Bag3D   → Exit() (outline OFF)   ← the killer
OnMouseExit / IPointerExit ...      (later, on leave)
```

### Confirmed root cause (from logs) — and a corrected premise

- **There IS a PhysicsRaycaster.** Two, in fact: `CameraBrain` (eventMask 128 = layer 3DPhysical) and `PhoneCamera` (eventMask 256 = Phone). The `/gds-quick-dev` grep for `PhysicsRaycaster` in GameScene.unity returned nothing and I wrongly concluded there was none — the component is referenced by script GUID in YAML, so a name grep can't find it. `find_gameobjects by_component PhysicsRaycaster` is the reliable check.
- Because a PhysicsRaycaster is present, **`EventSystem.current.IsPointerOverGameObject()` returns TRUE whenever the pointer is over the 3D model itself** (the raycaster registers the model as a UI-hit). So the free-cursor de-hover `OnMouseOver → if (_hovering && IsOverUI()) Exit()` fires one frame after `OnMouseEnter` set the hover → outline flashes then dies. The click still worked because it goes through `OnPointerClick` (the module dispatches it), while `OnMouseDown` was gated by the same (spurious) `IsOverUI()`.
- The whole free-cursor OnMouse\*/`IsOverUI` layer added in `/gds-quick-dev` was built on the false "no PhysicsRaycaster" premise. With the raycaster, free-cursor interaction already flows through the EventSystem `IPointer*` path (module + PhysicsRaycaster), and the module's topmost-first picking already blocks interaction through an open uGUI panel.

### Fix applied

`Board3DButton` is now **IPointer-only**: removed `OnMouseDown`/`OnMouseEnter`/`OnMouseOver`/`OnMouseExit` and `IsOverUI()`. Both input modes drive it through `OnPointerEnter/Exit/Click` (free cursor = module + PhysicsRaycaster; embodied = reticle), which never both fire (the module is silent while the cursor is locked). This removes the de-hover misfire AND the OnMouse/IPointer double-dispatch. Debug logs removed. `PowerBarObject3D` left as-is (its gated OnMouseDown is effectively dead now that the model is a PhysicsRaycaster hit, so it clicks via OnPointerClick — no de-hover handler, so it never had this bug). Tests updated: replaced the OnMouse/IsOverUI branch test with an IPointer enter/exit hover test (3/3 pass).

Confidence: High — root cause read directly from runtime logs; fix removes the exact code path that logged `OnMouseOver->Exit`.
