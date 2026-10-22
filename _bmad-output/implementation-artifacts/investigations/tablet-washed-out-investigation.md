# Investigation: the lobby tablet shows its UI Toolkit apps washed out

## Hand-off Brief

1. **What happened.** The tablet looked washed out on autoplay recordings only. The game renders it correctly: the
   wash is a double sRGB encode in the autoplay package's `AutoplayRecorder.SaveFrame` (Confirmed).
2. **Where the case stands.** Concluded, High confidence. In-game screenshots taken with `ScreenCapture` show exact
   control colours; recorder frames equal those colours sRGB-encoded a second time, to the unit.
3. **What's needed next.** Fix `SaveFrame` in `com.unpseudo.autoplay` (blit into a linear RenderTexture), release a
   tag, bump `Packages/manifest.json`; re-take the PR #143 in-game captures that came from the recorder.

## Case Info

| Field            | Value                                                                                   |
| ---------------- | --------------------------------------------------------------------------------------- |
| Ticket           | N/A (found while redrawing the lobby tablet, PR #143)                                   |
| Date opened      | 2026-10-19                                                                              |
| Status           | Concluded                                                                               |
| System           | Unity 6000.5.0f1, URP 17.5, linear colour space, Windows dev player (autoplay)          |
| Evidence sources | autoplay captures (AutoplayRuns/), GameScene inspection via `unity command eval`, code  |

## Problem Statement

Poyo, reading the redrawn lobby tablet: the screen looks pale. First claim (Claude): a double gamma encode. Poyo: "I'm
almost sure the double gamma encode comes from you" (measurement error). Poyo's lead: the scene lights should not
interact much with the tablet.

## Evidence Inventory

| Source | Status | Notes |
| ------ | ------ | ----- |
| In-game captures, new screen | Available | `AutoplayRuns/20261019-122708-build-seed4320/rec-00*/` |
| In-game captures, old screen (build 16/10) | Available | main checkout `AutoplayRuns/20261019-085829-build-seed4311/` |
| Off-screen UITK render of the same panel | Available | headless editor, `LobbyRolesUitkController` + demo data |
| GameScene cameras / tablet materials / volumes | Available | dumped by eval (see Confirmed Findings) |
| Runtime content of the UITK RenderTexture | Available | `[TABLETGAMMA]` probe, `Logs/autoplay-player-4323.log`, dump `%LOCALAPPDATA%Low/DefaultCompany/CorruptionDuPortail/tabletgamma_rt.png` |
| Control patches on the tablet canvas | Partial | created while the tablet was off screen (y = -3440 px) and never seen on the captures |
| Render-config sweep screenshots | Missing | code written in the probe, not run (editor stopped) |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | Runtime RT bytes vs off-screen render | High | Done | RT correct: the wash is added at draw time |
| 4 | Render-config sweep (phone post, base HDR, Volume off) with screenshots | High | Open | probe ready, needs an editor to build |
| 5 | Why the control patches never show (child RawImages of LobbyRolesRawImage) | Low | Open | may itself say what draws the visible screen |
| 2 | UI/Default sampling of the RT in the overlay camera (HDR stack, post at the end) | High | Open | |
| 3 | InfoTable: same wash? | Medium | Open | same presenter pattern |

## Confirmed Findings

### Finding 1: the darks are lifted on the tablet only, and before PR #143

**Evidence:** luminance percentiles (5/25/50 %) of the Abyss portrait: source `Assets/Art/Sprites/Portraits/Abyss.jpg`
10/15/30; off-screen UITK render 11/16/36; tablet, new screen 58/73/110; tablet, old screen (build 16/10) 53/65/100;
cork board in game 19/21/32.

**Detail:** a uniform brightening (exposure) would keep 10 near 11-13; the observed lift matches sRGB-encoding an
already-encoded value (10 → 56, 15 → 69, 30 → 97).

### Finding 2: the tablet canvas is drawn by an Overlay camera stacked on the post-processed base camera

**Evidence:** GameScene `---System---/PhoneCamera` = Overlay, cullingMask Phone (256), no target texture, post off;
`---System---/CameraBrain` = Base, HDR on, post on. Global Volume (SampleSceneProfile): Tonemapping Neutral, Bloom
threshold 1 / 0.25, Vignette 0.2. Tablet canvas `TabletParent/Tablet/ScreenCanvas` world space, RawImage
`LobbyRolesRawImage` with UI/Default; the 3D screen mesh behind is URP Lit black (smoothness 0.07).

### Finding 3: the RT format is not the cause

**Evidence:** `LobbyRolesRtPresenter` RT created ARGB32 Default; rebuilt with `RenderTextureReadWrite.Linear`, the
in-game values did not change (run 20261019-123026-build-seed4321). `PS_LobbyRoles_RT.forceGammaRendering = 0`.

### Finding 4: the UITK RenderTexture holds the right colours in the player

**Evidence:** `Logs/autoplay-player-4323.log`: `[TABLETGAMMA] rt sRGB=True fmt=R8G8B8A8_SRGB colorSpace=Linear lum
p05=6 p50=184 patchTop=RGBA(212, 187, 142, 255)`; the same parchment patch is (212, 187, 142) in the off-screen render;
alpha is 255 on every pixel of the dumped RT.

**Detail:** the wash is not written by UI Toolkit nor caused by the RT format or alpha: it is added downstream, when
`LobbyRolesRawImage` (UI/Default) is drawn by the Overlay PhoneCamera.

## Hypothesized Paths

### Hypothesis 1: double gamma encode in the RT → RawImage path

**Status:** Open

**Theory:** UITK writes sRGB-encoded values that are then read as linear and encoded again.

**Would confirm:** RT bytes in the player already equal the source (≈ 10 for darks) while the screen shows ≈ 56.

**Would refute:** RT bytes already lifted, or an equal lift on a non-RT tablet graphic.

**Resolution:** refuted at the RT level (Finding 4: bytes correct, format sRGB). A double encode may still happen at
draw time in the camera stack: that is Hypothesis 3.

### Hypothesis 2 (Poyo): scene lighting affects the tablet

**Status:** Open

**Theory:** a lit material or the scene's lighting adds light to the tablet screen.

**Supporting indicators:** the 3D screen mesh and tablet body use URP Lit.

**Would refute:** the RawImage uses UI/Default (unlit), and the lift shows on the RawImage itself.

**Resolution so far:** the RawImage is UI/Default (unlit, Confirmed). Scene lights cannot light it directly; they can
only reach it through the shared post-processing of the stack (Hypothesis 3). The lit black screen mesh sits behind an
opaque RawImage (alpha 255) and cannot show through.

### Hypothesis 3: the camera stack's HDR / post-processing treats the overlay UI as linear twice

**Status:** Open

**Theory:** PhoneCamera (Overlay, post off) draws into CameraBrain's HDR intermediate; the final pass converts it once
more, or tonemapping/colour handling of the stack lifts it.

**Would confirm:** the lift disappears with PhoneCamera post on, base HDR off, or the Volume at 0 (sweep, backlog #4).

**Would refute:** the lift survives all three, pointing at the RawImage material / canvas settings instead.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| RT bytes at runtime | splits hypothesis 1 from a display-side cause | `[TABLETGAMMA]` probe |

## Follow-up: 2026-10-19

### New Evidence

- Clean sweep (lobby held 20 s by a temporary `[TABLETGAMMA]` delay in `LobbyReady`, role card closed), screenshots
  written by `ScreenCapture.CaptureScreenshot` in the player, `Logs/autoplay-player-4327.log`:
  vertex black 0,0,0; vertex grey 128 → 128; sRGB texture grey 128 → 128; parchment (212, 187, 141) = RT bytes;
  Abyss darks 11/17/37 = source. Same with PhoneCamera post on, base HDR off, Volume weight 0.
- sRGB-encoding the clean values once more gives parchment (235, 222, 196), Abyss 59/73/106, background 90; the
  recorder frames read (236, 222, 196), 58/73/110, ~80-90.

### Additional Findings

**Finding 5: the game renders the tablet correctly; the wash is added by the autoplay recorder.**
`Library/PackageCache/com.unpseudo.autoplay@e02473e1bdf3/Runtime/AutoplayRecorder.cs:180-193`: the frame from
`ScreenCapture.CaptureScreenshotAsTexture()` (sRGB bytes in a linear-flagged texture) is `Graphics.Blit` into
`RenderTexture.GetTemporary(width, height)`, an sRGB target in a linear project, so the blit encodes the bytes a
second time; `ReadPixels` returns the encoded values. `AutoplayCapture.cs:117-119` (verdict screenshots) encodes the
texture directly, which is why the cork board looked right and the tablet (recorded with `-autoplay-record`) did not.

### Updated Hypotheses

- Hypothesis 1 (double gamma): **Confirmed**, but in the capture tool, not in the game's display chain.
- Hypothesis 2 (scene lights on the tablet): **Refuted**. RawImage is unlit UI/Default and the screen colours are exact.
- Hypothesis 3 (camera stack HDR / post): **Refuted**. Turning each off changes nothing on the tablet (parchment
  moves only with PhoneCamera post on, which the game does not use).

### Backlog Changes

- #4 Done. #5 Done (patches were created off screen; visible and exact once the tablet is up). #3 InfoTable: moot.

### Updated Conclusion

**Confidence: High.** No defect in the game. Fix direction: in `AutoplayRecorder.SaveFrame`, use
`RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)` so the blit
copies bytes unchanged (or mark the source texture sRGB). Everything measured on `rec-*` frames before the fix is
lifted the same way, including `tools/maquettes/attribution-roles/captures/jeu/*.jpg` in PR #143.
