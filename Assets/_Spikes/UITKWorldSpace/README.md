# Spike A — World-space UI Toolkit + reticle pick (issue #63)

**Throwaway.** Self-contained (own asmdef, no dependency on `Game`). Delete the folder when the
question is answered. Runs on the project's current **Unity 6.2** — no engine upgrade needed.

## The question
Can the first-person, screen-centre **reticle** hover + confirm elements on a **world-space UITK panel**,
the way `Reticle.ReticleInteractor` already does for world-space uGUI?

It can't reuse the reticle as-is: UITK panels are outside the uGUI `EventSystem` / `GraphicRaycaster`
pipeline, so `EventSystem.RaycastAll` never sees them. This probe is the parallel bridge under test —
screen-centre ray → panel plane → `RuntimePanelUtils.CameraTransformWorldToPanel` → `IPanel.Pick` — which
is the path reported buggy on 6.3 (inverted/unscaled coords, pivot-dependent picking, Jan 2026).

## Setup (≈5 min, in a throwaway scene)
1. **Create a world-space PanelSettings:** `Assets > Create > UI Toolkit > Panel Settings`. In the inspector
   set **Render Mode = World Space**.
2. **Add the panel object:** create an empty GameObject in front of a camera. Add a **UI Document** component
   (`Add Component > UI Toolkit > UI Document`). Assign:
   - **Source Asset** = `UITKWorldSpaceSpike.uxml`
   - **Panel Settings** = the world-space PanelSettings from step 1.
   Position/scale the GameObject so the panel is visible to the camera; its transform IS the panel plane.
3. **Add `ReticleUITKProbe`** to the same GameObject. Leave Camera empty to use `Camera.main`, or assign one.
   (Optional: drag `UITKWorldSpaceSpike.uss` into the *Style Sheet* field if the panel renders unstyled.)
4. Press **Play**. Keep the game-view centre (the "reticle") over the panel.

## What to look for — the verdict
- ✅ **PASS:** the highlight tint lands on the **exact** element under the screen centre and tracks correctly
  as you rotate/move the camera; **Confirm** (LMB / Space / gamepad A) logs the right Button. Picking stays
  correct at different panel **pivots**, camera **distance**, and **FOV**.
  → Reticle-driven world-space UITK is viable; the gameplay-UI migration path is open (as a separate, later
  decision, gated on the engine version you choose).
- ❌ **FAIL:** the highlight is **offset / mirrored**, drifts with distance or FOV, or `Pick` returns `<null>`
  over visible elements (turn on **Log Every Frame** to inspect the raw `panelPos`). Workarounds exist
  (manual Y-invert, divide by PixelsPerUnit, top-aligned pivot) but they're undocumented patches.
  → Keep the gameplay UI on uGUI; revisit at 6.7 LTS.

Record the outcome (a sentence + a screenshot/clip) on issue #63, Spike A checklist.

## Scope notes
- Hover/pick is the decisive test and needs no input. The Confirm log proves **targeting**; wiring a Button to
  real game logic afterwards is a one-liner and is *not* what this spike de-risks.
- This says nothing about the 139 DOTween animations, UISoftMask, or the 6.2 → 6.4 engine upgrade — those are
  separate costs tracked in #63.
