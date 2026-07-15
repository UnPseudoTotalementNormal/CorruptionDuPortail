# Datamosh shader — prompt d'implémentation (généré par agent Fable 5)

> Cible : réimplémenter un vrai datamoshing (coulures/drag pixel type "waterfall") dans
> `Assets/Shaders/UI_GlitchVideo.shader`. Technique = "hold-the-winner" march directionnel
> edge-triggered, sans frame history, sans quantification spatiale (pas de cubes), sans moyenne
> (pas de flou mou). À passer tel quel à un agent de code.

---

## Implement a fake-datamosh "pixel-drag streak" pass in `Custom/UI_GlitchVideo`

### Mission
Add a **datamoshing** term to the existing uGUI URP shader `Assets/Shaders/UI_GlitchVideo.shader`. The goal is the classic compression-artifact "pixel bleed" look: **long, ragged, mostly-vertical pixel-drag streaks that bleed downward from high-contrast edges**, making the sprite melt/flow while staying semi-recognizable. Dominant new feature — must read instantly at defaults.

Single-pass fragment shader, **NO frame history** (no previous-frame RT, no motion vectors). Fake it entirely from `_MainTex`, UVs, `_Time`. Do not remove/reorder existing effects (band displacement, jitter, RGB split, drift, scanlines, snow).

### Visual target
1. **Directional drag streaks** — content below a bright edge replaced by that edge's color, stretched into a long thin trail (~20–30% sprite height at defaults), near-constant color along its length (a *held* value, not a gradient/blur).
2. **Waterfall raggedness** — adjacent columns differ in length/start → jagged dripping lower boundary.
3. **Melt** — streaks carry **alpha too**, extending past the original silhouette into transparent areas.
4. **Flow direction** — dominantly vertical-down, with a slowly varying tilt (few degrees, continuous — no zones).
5. **Temporal persistence** — streaks hold & creep over ~0.5–2 s; occasional burst-tied re-key; no per-frame re-hash.
6. **Subtle chroma** riding along the streaks.
7. **Partial coverage** — ~half the image moshes, via a smooth low-frequency drifting mask.

### Anti-goals (two previous fails)
- **Fail 1 — invisible averaging smear.** Never *average* march taps → faint blur. Streak color from a **winner/hold selection**; default streak length aggressive (≥0.2 UV).
- **Fail 2 — hard cube/block displacement.** **No `floor()`/quantization in the mosh's spatial fields.** Direction, mask, length all **continuous** (smooth value noise). (Existing band `floor()` stays; quantized *time* is fine, quantized *space* is not.)

### Technique — edge-triggered "hold-the-winner" directional march
- **0. Smooth value noise helper** (build on existing `Hash11/Hash21`): `ValueNoise = lerp(Hash(i),Hash(i+1),smoothstep(f))`. All spatial fields use it → no block edges.
- **1. Temporal state** — `moshTime = _Time.y * _MoshEvolution` (slow, ~0.3). Crossfade discrete states / feed continuous coord; creep, don't snap. Burst-gated low-rate re-key.
- **2. Direction field** — `tilt = (ValueNoise(uv.x*_MoshFlowScale + moshTime*0.5)-0.5)*2*_MoshDirectionSpread; dragDir = normalize(float2(tilt,-1))`. Sample **upstream** (toward +v for on-screen downward drag) — verify sign, flip if streaks fall wrong way (#1 likely mistake).
- **3. Coverage mask** — `mask = smoothstep(1-_MoshCoverage, 1-_MoshCoverage+0.25, ValueNoise2D(uv*_MoshMaskScale + moshTime*0.3)); moshAmount = mask*_MoshIntensity*saturate(gi+0.3)`.
- **4. March (core)** — from the post-band-displacement `uv`, march upstream `TAPS=16` [unroll], keep the single **best** sample by `score = Luma(rgb)*a - i*_MoshFalloff/TAPS`. Winner-take-all IS the edge detector (alpha-weighted luma → transparent never wins, bright opaque edge drags down). **Hold, don't blend:** `moshCol = lerp(texC, best, saturate(moshAmount*_MoshHold))`, `_MoshHold≈1`. `_MoshEdgeThreshold` (~0.08): winner must beat local score → confines streaks below edges, keeps subject readable. **Alpha dragged:** `moshCol.a = max(texC.a, best.a*saturate(moshAmount*_MoshHold))`. Raggedness via `ValueNoise(uv.x*60 + seed)` on streakLen (x-only, continuous). `saturate()` tap UVs (tight-atlas caveat comment).
- **5. Chroma** — 2 extra taps at `bestUV ± stepUV*_MoshChroma*2` → r/b channels; gate by moshAmount.

### Integration order in `frag()` (preserve existing load-bearing order)
1. band displacement + jitter (unchanged; march starts from this `uv`)
2. existing 3-tap RGB-split → `col` (unchanged)
3. **NEW mosh march + hold + chroma → blend into col.rgb AND col.a by moshAmount** — generative, so **before** the tint
4. color drift (existing)
5. `col.rgb *= IN.color.rgb;` (mosh sits before this, like other generative terms)
6. scanlines, snow (existing)
7. `col.a *= IN.color.a;` last + `UNITY_UI_CLIP_RECT` (unchanged, still last)

### New material properties (`[Header(Datamosh)]` + CBUFFER)
| Property | Default | Range | Meaning |
|---|---|---|---|
| `_MoshIntensity` | 0.8 | 0–1 | Master; early-out `if(<=0.001)` around march |
| `_MoshStreakLength` | 0.25 | 0–0.6 UV | Max drag distance |
| `_MoshCoverage` | 0.6 | 0–1 | Fraction inside mask |
| `_MoshDirectionSpread` | 0.15 | 0–0.5 | Max sideways tilt |
| `_MoshFlowScale` | 4 | 1–10 | Direction undulation freq |
| `_MoshMaskScale` | 2.5 | 1–6 | Coverage blob freq |
| `_MoshEvolution` | 0.3 | 0–2 | Creep speed (≪ _GlitchSpeed) |
| `_MoshEdgeThreshold` | 0.08 | 0–0.5 | Winner margin |
| `_MoshFalloff` | 0.15 | 0–1 | Distance penalty |
| `_MoshHold` | 1.0 | 0–1 | Winner replace strength (~1) |
| `_MoshChroma` | 0.3 | 0–1 | R/B fringe |

`TAPS=16` compile-time const (12–24 ok).

### Constraints & perf
Single pass, fragment-only, no new textures/GrabPass/frame-history/C# uploads. ~18 taps on card-sized Images ok; `[unroll]` + `_MoshIntensity` early-out. Keep `multi_compile_local _ UNITY_UI_CLIP_RECT`, stencil, blend, CBUFFER style. Flat sprite → edge-threshold makes it gracefully do ~nothing (correct).

### Acceptance checklist
1. Portrait sprite @ defaults: visible **long vertical trails** below bright edges, near-constant color, ragged lengths, extending past silhouette into transparent.
2. Trails **creep/hold ~1–2 s**, no per-frame re-random; bursts re-key.
3. Zoom moshed region: **no rectangular block edges/seams** (band tears ok).
4. `_MoshHold=0.2` → see fail-1 blur; default 1.0 doesn't; `_MoshIntensity=0` → pixel-identical to pre-change.
5. Near-black Image tint: streaks still visible (mosh before tint multiply).
6. CanvasGroup fade + RectMask2D still work.
7. Compile clean (poll `read_console`).
