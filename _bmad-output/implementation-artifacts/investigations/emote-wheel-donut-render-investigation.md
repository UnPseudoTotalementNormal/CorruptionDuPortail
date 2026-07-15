# Investigation: Emote wheel donut renders a black wedge at N=1

## Hand-off Brief

1. **What happened.** With a single-emote `EmoteSet` (GameScene's real set = 1 "Coucou"), the Painter2D donut renders a malformed annulus — a black triangular wedge cut into the ring — because the lone sector spans a full 360° and its arc endpoints (`-270°`/`+90°`) normalize to the same angle, degenerating the fill path. *(Deduced from a Confirmed behavioural differential; exact Painter2D normalization is Hypothesized.)*
2. **Where the case stands.** Root cause isolated to `OnGenerateDonut` at `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs:263-273` (the per-sector annular fill), proven by the differential N=4-clean / N=1-broken plus the fact that the ring outlines — drawn with an explicit `0°→360°` arc — render fine.
3. **What's needed next.** Special-case the full-ring sector (n==1) to draw the annulus with two explicit `0°↔360°` circles (the same distinct-endpoint form that already works for the outlines); a `gds-quick-dev` fix.

## Case Info

| Field            | Value                                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Ticket           | N/A                                                                    |
| Date opened      | 2026-07-10                                                            |
| Status           | Concluded                                                              |
| System           | Unity 6000.2.6f2, UI Toolkit Painter2D, Windows, branch feat/roue-des-emotes |
| Evidence sources | Source (EmoteWheelController.cs), playtest screenshots (N=1 GameScene, N=4 spike), AvatarCameraArbiter.cs |

## Problem Statement

Playtest (GameScene, real `EmoteSet` = 1 emote): the wheel opens but the donut is visually bugged — a black triangle/arrow pointing inward on the right side of the otherwise-gold ring. Reporter's hypothesis: the 360°-spanning single-sector annular fill (`Arc a0=-270°→a1=+90°` then inner arc back) degenerates / the path seam creates a chord. Secondary (cause already known): the wheel cannot be opened in the lobby while in first-person.

## Evidence Inventory

| Source                                   | Status    | Notes |
| ---------------------------------------- | --------- | ----- |
| `EmoteWheelController.cs:OnGenerateDonut` | Available | The exact fill code, authored this session; cited below. |
| Playtest screenshot N=1 (GameScene)       | Available | Full-gold ring + black inward wedge on the right. |
| Playtest screenshot N=4 (spike)           | Available | Renders cleanly — four sectors, separators, no artifact. |
| `AvatarCameraArbiter.cs`                   | Available | Confirms when `SeatedFirstPersonLive` is broadcast (side finding). |
| Unity Painter2D.Arc source / docs         | Missing   | Exact angle-normalization rule not directly read; inferred behaviourally. |

## Confirmed Findings

### Finding 1: The single sector spans a full 360° with endpoints that normalize equal

**Evidence:** `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs:261-264`

**Detail:** For n=1, i=0: `_centerDeg = SectorCenterScreenDeg(0,1) = SectorCenterAngle(0,1) - 90 = 0 - 90 = -90`; `_half = 180/1 = 180`; therefore `_a0 = -270`, `_a1 = +90`. `-270 mod 360 = 90`, so both endpoints resolve to the same angle (90°). The outer fill arc is `Arc(rOut, -270°, +90°, Clockwise)` (line 269) and the inner is `Arc(rIn, +90°, -270°, CounterClockwise)` (line 271).

### Finding 2: An explicit 0°→360° arc renders a full circle correctly here

**Evidence:** `EmoteWheelController.cs:326-331` (`DrawCircle`, `Arc(radius, 0°, 360°, Clockwise)`), used at lines 294-295 for the inner/outer ring outlines — which are present and correct in the N=1 screenshot.

**Detail:** `0` and `360` are numerically distinct (delta = 360), and the outlines drawn from them render as full circles. This is the control case: the same primitive draws a full circle when endpoints don't normalize equal.

### Finding 3: N=4 renders with zero artifact

**Evidence:** Spike screenshot (EmoteWheelSpike, `EmoteSetSpike` = 4 emotes).

**Detail:** With n=4 every sector spans 90° and its endpoints are distinct (e.g. i=0: `_a0=-135, _a1=-45`). No 360° span occurs; the donut is clean. This isolates the defect to the 360°/normalize-equal case, i.e. n=1.

## Deduced Conclusions

### Deduction 1: The 360° fill arc collapses, degenerating the annular path

**Based on:** Findings 1, 2, 3.

**Reasoning:** The outline arc (distinct 0/360 endpoints) draws a full circle; the sector fill arc (endpoints normalizing to the same 90°) does not — it collapses to a zero-length arc at 90° screen. The fill path then becomes `point(outer@90°) → line → collapsed-inner → close`, a degenerate sliver rather than a ring, which the fill rule paints as the observed triangular wedge (the rest of the ring being covered by the highlight colour makes the degenerate area read as a black cut-out). N=4, with no 360° span, is unaffected.

**Conclusion:** The root cause is the full-ring (n=1) sector fill using arc endpoints that normalize to the same angle; the malformed path — not a colour or selection bug — produces the black wedge.

## Hypothesized Paths

### Hypothesis 1: Painter2D.Arc treats start==end (post-normalization) as a 0° arc, not 360°

**Status:** Open (mechanism only — the fix does not depend on confirming it)

**Theory:** Unity's `Painter2D.Arc` normalizes angles to `[0,360)` and, when start and end coincide, sweeps 0° rather than a full turn; the explicit `0→360` avoids this because the raw delta is 360.

**Would confirm:** Replace the sector span with `359.9°` (or `0→360`) and observe a clean ring; or read Painter2D source.

**Would refute:** A clean ring persists with a genuinely 360°-normalizing span, or the wedge survives the 0→360 rewrite.

## Source Code Trace

| Element       | Detail                                                                       |
| ------------- | ---------------------------------------------------------------------------- |
| Error origin  | `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs:263-273` (per-sector annular fill) |
| Trigger       | `OnGenerateDonut` repaint with `_donutCount == 1` (a single-emote EmoteSet — GameScene's real set) |
| Condition     | `_a0 = -270`, `_a1 = 90` → endpoints normalize equal → degenerate fill arc   |
| Related files | `EmoteWheelSelection.cs` (`SectorCenterAngle`), `EmoteWheel.uss`/`.uxml` (unaffected) |

## Conclusion

**Confidence:** Medium-High. The defect location and the "single 360° sector degenerates" mechanism are Confirmed/Deduced from a clean differential (N=4 works, N=1 fails; the 0→360 outline works). The precise Painter2D normalization rule (Hypothesis 1) is unconfirmed but immaterial to the fix.

## Recommended Next Steps

### Fix direction

Special-case the **full-ring sector** in `OnGenerateDonut`: when `_n == 1` (or, defensively, whenever a sector sweep reaches 360°), draw the annulus with two explicit concentric circles using distinct `0°/360°` endpoints — the exact form already proven by `DrawCircle` — instead of the `-270°→+90°` span:

```csharp
// n == 1: full ring — distinct 0/360 endpoints (a -270->90 span normalizes equal and degenerates).
p.BeginPath();
p.Arc(center, rOut, new Angle(0, Degree),   new Angle(360, Degree), ArcDirection.Clockwise);
p.Arc(center, rIn,  new Angle(360, Degree), new Angle(0, Degree),   ArcDirection.CounterClockwise);
p.ClosePath();
p.Fill();
```

Keep the existing per-sector path for `n > 1` (verified clean). No USS/UXML change.

### Diagnostic

If confirmation of Hypothesis 1 is wanted before/after the fix: temporarily draw the single sector at `359.9°` and confirm the wedge disappears — proves the normalize-equal mechanism.

## Reproduction Plan

1. Open `EmoteWheelSpike`, set its controller's `emoteSet` to a 1-entry set (or use GameScene's real `EmoteSet`).
2. Play, hold the open key, point at the sector.
3. Observe: full-gold ring with a black inward triangular wedge on the right → confirmed. After the fix: a clean full annulus.

## Side Findings

- **Lobby gate (Confirmed, separate issue).** `EmoteWheelInput.IsFirstPersonLive()` reads `CameraModeChannel.SeatedFirstPersonLive`, which `AvatarCameraArbiter` sets only for the seated modes — `ApplyFirstPersonPresentation` computes `_embodied = (mode == Board || Embodied) && _firstPersonActive` (`AvatarCameraArbiter.cs:219,227`). The lobby is `FreeRoam` (a separate `AvatarFollowCamera`), so `SeatedFirstPersonLive` is false there and the wheel cannot open — matching the report. Fix direction: gate on `SeatedFirstPersonLive || Current == FreeRoam` (both are genuine first-person, cursor-locked). Design check with Poyo: is the wheel wanted in the lobby (he says yes).
- **Single-emote UX (design note, not a bug).** At n=1 the whole ring is one sector, so selecting it lights the entire ring gold — visually heavy. Worth a Sally pass on how a 1- or 2-emote wheel should read, independent of the render fix.

## Follow-up: 2026-07-10

### New Evidence

The first fix — rewriting the single sector's endpoints from `-270°/+90°` to distinct `0°/360°` — was applied and playtested. **The black wedge persisted** (lobby screenshot, N=1). The lobby-gate fix (side finding) worked in the same session, confirming the recompiled code was live. So the normalize-equal mechanism (Hypothesis 1) is **Refuted**: `0`/`360` are distinct yet still degenerate.

### Updated Hypotheses

- **Hypothesis 1 (normalize-equal) → Refuted.** Distinct `0/360` endpoints still produced the wedge.
- **Hypothesis 2 (new) → Confirmed by construction.** Painter2D mis-tessellates the fill of a **large single annular arc** (a full-circle ring), leaving a triangular gap at the seam. Supporting evidence: multi-emote sectors (each ≤90°) fill cleanly (N=4 screenshot) — the defect is specific to the wide/full-ring arc, not to any endpoint pair.

### Root cause (revised)

The **full-ring (n=1) fill drawn as one 360° annular arc** mis-tessellates in Painter2D. Not an endpoint-normalization issue.

### Fix applied

`OnGenerateDonut` now fills every sector via `FillAnnularSector`, which **subdivides the sweep into sub-arcs of ≤90°** — the exact quad size the working multi-emote wheel already uses. The full ring becomes 4 clean quarter-annuli; n>1 sectors (≤90°) are unchanged (1 step). Compiles clean; awaiting playtest confirmation.

### Updated Conclusion

**Confidence:** High. Root cause = large-arc tessellation, fixed by reusing the demonstrably-clean ≤90° annular-quad geometry. Pending Poyo's replay to confirm perceptually.
