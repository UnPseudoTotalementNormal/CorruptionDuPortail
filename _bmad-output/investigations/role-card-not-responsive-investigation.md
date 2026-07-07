# Investigation — Role card (UI Toolkit) not responsive across resolutions

## Hand-off Brief
The UITK role card is bigger (as a fraction of screen) at 1920×1080 than at 2560×1440 because `PS_ScreenOverlay` uses **ConstantPhysicalSize** (`m_ScaleMode: 1` in the `PanelSettings` enum), which keys off DPI and ignores both the screen resolution and the reference resolution — so the card renders at fixed px while the game's uGUI (which uses ScaleWithScreenSize) scales. The trap: `m_UiScaleMode`/`m_ScaleMode` value `1` means **different things** for CanvasScaler vs PanelSettings. Fix: set `PS_ScreenOverlay.m_ScaleMode: 2` (ScaleWithScreenSize) and mirror the game's CanvasScaler (`m_ReferenceResolution: 1920×1080`, MatchWidthOrHeight, match 0).

**Status:** Concluded. **Confidence:** High (Confirmed).

## Problem Statement
Owner reports the role card is "not responsive": at 1920×1080 it occupies a larger fraction of the screen than at 2560×1440 (same role, Technomancien). For a 16:9 UI, responsive = identical screen fraction at every resolution.

## Confirmed Findings
- **F1 — PanelSettings scale mode is ConstantPhysicalSize.** `Assets/UI/PanelSettings/PS_ScreenOverlay.asset:21` → `m_ScaleMode: 1`. In `UnityEngine.UIElements.PanelScaleMode` the enum is `0=ConstantPixelSize, 1=ConstantPhysicalSize, 2=ScaleWithScreenSize`. So the panel is ConstantPhysicalSize — it sizes by physical DPI (`m_ReferenceDpi: 96`, `m_FallbackDpi: 96`), NOT by screen resolution. At the editor/runtime fallback DPI it behaves like ConstantPixelSize → fixed px → larger screen fraction at lower resolution. This is the root cause.
- **F2 — The reference resolution was inert.** Because the mode is ConstantPhysicalSize, `m_ReferenceResolution` is ignored (it only feeds ScaleWithScreenSize). Confirms the prior "fix" (changing it 1920→2560, commit 693273a) had zero effect on scaling.
- **F3 — The game's uGUI uses ScaleWithScreenSize @ 1920×1080.** `Assets/Scenes/GameScene.unity` CanvasScalers (e.g. `:433-438`, `:9905-9910`, `:10143-10148`, and 5+ more): `m_UiScaleMode: 1`, `m_ReferenceResolution: {1920,1080}`, `m_ScreenMatchMode: 0`, `m_MatchWidthOrHeight: 0` (one outlier at `0.457`). In `UnityEngine.UI.CanvasScaler` the enum is `0=ConstantPixelSize, 1=ScaleWithScreenSize, 2=ConstantPhysicalSize` — so value `1` = ScaleWithScreenSize (responsive). **The same integer `1` is ScaleWithScreenSize for CanvasScaler but ConstantPhysicalSize for PanelSettings** — the core trap.

## Refuted Premises
- **"2K (2560×1440) is the reference."** Refuted by F3: every game CanvasScaler references **1920×1080**. The owner authors on a 2K monitor, but the project's scaler reference is 1920×1080. To scale in lockstep with the game, the panel must use the same 1920×1080 reference — not 2560×1440.

## Root Cause
`PS_ScreenOverlay` is ConstantPhysicalSize (F1), so the card does not scale with resolution while the rest of the UI does (F3). The two `ScaleMode` enums assigning different meanings to `1` (F3) hid this — the value looked like it matched.

## Fix (High confidence — mirror the game CanvasScaler)
In `Assets/UI/PanelSettings/PS_ScreenOverlay.asset`:
- `m_ScaleMode: 2`  (ScaleWithScreenSize)
- `m_ReferenceResolution: {x: 1920, y: 1440}` → **1920×1080** (revert 693273a; match the game)
- keep `m_ScreenMatchMode: 0` (MatchWidthOrHeight) and `m_Match: 0` (width)

Result: scaleFactor = screenWidth / 1920 (same formula as the game) → the card holds a constant width fraction (~29%) at 1920, 2560, any 16:9 → identical to the rest of the UI.

Note on size: the card px (panel 560, name 44, portrait 260×400…) were tuned in 2560 render captures under the old ConstantPhysicalSize (where the card sat at ~22% of 2560). After the fix, at 2560 the card scales ×(2560/1920)=1.33 → ~29% — the size seen in the owner's original in-game 2560 screenshots. If ~29% reads too large, scale the px down (do NOT touch the reference).

## Reproduction / Verification
Run the game at 1920×1080 and 2560×1440, open the same role card. Before the fix: card fraction differs (bigger at 1920). After the fix: identical fraction at both.
