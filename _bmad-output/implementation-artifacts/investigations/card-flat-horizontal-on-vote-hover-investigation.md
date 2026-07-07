# Investigation: hovered card lies flat / horizontal during seated Vote

## Hand-off Brief

1. **What happened.** During the Vote, a hovered card rotates to lie **flat/horizontal on the table** (screenshot) instead of tilting up — because the player has arrow-navigated to the **top-down board overview** camera, but the first-person look-at hover still runs and orients the card toward that overhead camera.
2. **Where the case stands.** Root cause **Confirmed**: the Vote stays `CameraMode.Embodied` the whole phase, but arrow-nav lets the player switch the live camera to an overhead board-overview node that becomes `Camera.main`. `CardPlayerAnimation.Hover` gates only on `Current == Embodied` (not on the seated FP node being live) and reads `Camera.main`, so it look-at-rotates the hovered card toward the overhead camera → card lies flat. The screenshot IS that top-down overview.
3. **What's needed next.** Fix the gate: only run the FPS look-at hover when the **seated first-person node is the live camera** (e.g. also require `_firstPersonActive`), not merely when the mode is Embodied. → `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-07                                                                 |
| Status           | Active                                                                     |
| System           | Unity 6000.2.6f2, Windows build (CorruptionDuPortail.exe)                  |
| Evidence sources | 2 screenshots (Discord, Wouh 2026-07-03 14:53), source code, git log       |

## Problem Statement

Poyo (reporting Wouh's screenshot): "Petit bug : les cartes se décalent sur le côté." The hovered card — the one with the "Voter / N'a pas voté" vote UI open on it — is displayed rotated to horizontal (lying flat on the table). User hypothesis: it happens **on hover**, because the flat card is the one with the vote UI open.

## Evidence Inventory

| Source | Status | Notes |
|---|---|---|
| Hover rotation mutation | Confirmed | `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs:69` `SlerpLayerRotation(hoverLayer, _pose.Rotation)` |
| Look-at pose geometry | Confirmed | `Assets/Scripts/Presentation/HoverFocusMath.cs:47-59` orients local face-normal → camera direction |
| Face axes = up/forward | Confirmed | `Assets/Scripts/Board/CardComponents/CardVisualComponents.cs:38-39` `hoverFaceLocalNormal=Vector3.up`, `hoverFaceLocalUp=Vector3.forward` |
| FPS hover gated to Embodied vote | Confirmed | `CardPlayerAnimation.cs:58` `_channel.Current == Avatars.CameraMode.Embodied && _cam != null` (`_cam = Camera.main`) |
| Only the hovered card gets look-at | Confirmed | rotation applied inside `Hover(...)`, per-card, only on pointer-enter |
| Vote canvas does NOT rotate the card | Confirmed | `Board/UI/VoteCanvas/VoteCanvas.cs:122-140` only `DOAnchorPos` (slides panel) |
| Vote = Embodied for the WHOLE phase | Confirmed | `Assets/Scripts/Avatars/AvatarCameraModePolicy.cs:41-42` `VoteState/VoteRecapState => Embodied` |
| Arrow-nav ON in Embodied; player can switch to a board-overview camera | Confirmed | `Assets/Scripts/Avatars/AvatarCameraArbiter.cs:185-186, 231-244` (`_firstPersonActive` flips off) |
| One MainCamera ("CameraBrain"); embodied cam is a virtual cam that only drives it while it holds priority | Confirmed | `Assets/Scenes/GameScene.unity:2103-2110`; `Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs:41,54,137` |
| Seated FP pitch HARD-CLAMPED ±40° — cannot look near-straight-down | Confirmed | `AvatarEmbodiedCamera.cs:52-53`; `Assets/Scripts/Avatars/EmbodiedLookClamp.cs:35` |
| Hover gate reads Camera.main, checks only `Current == Embodied` (not FP-node-live) | Confirmed | `CardPlayerAnimation.cs:57-58` |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | Confirm seated Vote camera == Camera.main / can go overhead | High | Done | Resolved: seated pitch clamped ±40° (can't), but arrow-nav to board-overview makes an overhead cam Camera.main while mode stays Embodied |
| 2 | Whether `hoverFaceLocalNormal=Vector3.up` is the intended face axis | Medium | Done | Refuted as cause; overhead camera fully explains the flat pose |

## Confirmed Findings

### Finding 1: The hover look-at rotates the card's local +up axis to point at the camera

**Evidence:** `HoverFocusMath.cs:47-59`; `CardVisualComponents.cs:38-39`; applied `CardPlayerAnimation.cs:69`.

**Detail:** `Compute` builds `_N = (cameraPos - pivotPos).normalized` and rotates so the local face basis (`hoverFaceLocalNormal = Vector3.up`, `hoverFaceLocalUp = Vector3.forward`) maps onto `(_N, world-up-flattened)`. The card's local +up axis is forced to point at the camera. There is **no clamp** on how vertical `_N` may be.

### Finding 2: When the camera is above the card, "up faces camera" == card lies flat

**Evidence:** geometry of Finding 1.

**Detail:** As `_N` approaches world +up (camera directly overhead), the card's up-axis points straight up, i.e. the card plane becomes horizontal — flat on the table. This is exactly the "carte retournée à l'horizontal" in the screenshot. The effect is continuous: the steeper the camera looks down, the flatter the hovered card.

### Finding 3: The rotation only affects the hovered card, in Embodied vote

**Evidence:** `CardPlayerAnimation.cs:46-77` (per-card `Hover`), gate `:58`.

**Detail:** Only the card under the reticle gets the look-at; siblings keep their rest pose. Matches the screenshot: one card flat, the rest normal. The gate ties it to the seated Vote (`Embodied`), which is exactly when the vote UI is open — consistent with the user's "on hover, vote UI open" observation.

## Deduced Conclusions

### Deduction 1 (ROOT CAUSE): the FPS look-at hover runs while Camera.main is the overhead board-overview camera

**Based on:** Findings 1–3 + camera-mode verification.

**Reasoning:** The Vote is `CameraMode.Embodied` for the entire phase, but arrow-nav is ON (`AvatarCameraArbiter.cs:185-186`). When the player arrows away from the seated first-person node to a **board-overview / top-down camera**, that node wins Cinemachine priority and becomes `Camera.main` — while the mode is still `Embodied` (`_firstPersonActive` flips off, but the mode does not). `CardPlayerAnimation.Hover` gates the look-at only on `_channel.Current == Embodied` (`CardPlayerAnimation.cs:58`) and reads `Camera.main` — so it still runs, now aiming the hovered card's face at the OVERHEAD camera. "Face the camera" with an overhead camera == card lies flat on the table.

**Conclusion:** The horizontal card is the look-at hover firing against the wrong (overhead) camera because the gate is too coarse. The screenshot's top-down framing is precisely the board-overview camera — direct confirmation. This is the root cause.

### Deduction 2 (REFUTED alternative): steep seated pitch flattens the card

**Based on:** original hypothesis.

**Reasoning:** Would require the seated FP camera to look near-straight-down. **Refuted** — seated pitch is hard-clamped to ±40° (`AvatarEmbodiedCamera.cs:52-53`, `EmbodiedLookClamp.cs:35`); the FP camera physically cannot get near-vertical over the cards. The overhead angle comes from a DIFFERENT camera (the board overview), not from steep seated pitch.

## Hypothesized Paths

### Hypothesis 1 (user premise: "it happens on hover / vote UI open")

**Status:** Confirmed. The look-at rotation is applied only on hover, only in Embodied vote (vote UI open). Refutation attempt: could the vote canvas itself rotate the card? No — `VoteCanvas` only slides its panel (`VoteCanvas.cs:122-140`); refuted that alternative.

### Hypothesis 2: face-normal axis is mis-configured (off by 90°)

**Status:** Refuted (as the cause). If the axis were wrong the card would look wrong at normal seated angles too; the intended seated hover works (per the sibling investigation `card-lowers-on-vote-button-hover`). The flat pose is explained entirely by the overhead camera (Deduction 1), so no axis misconfiguration is needed. Left recorded in case a residual small tilt error surfaces.

## Missing Evidence

| Gap | Impact | How to Obtain |
| --- | ------ | ------------- |
| Camera mode + pitch at the moment of the screenshot | Distinguishes Deduction 1 (steep angle) from Hyp 2 (wrong axis) | Verification agent / repro with on-screen camera-angle log |
| Seated camera pitch clamp values | Bounds how flat the card can get | camera controller source |

## Source Code Trace

| Element | Detail |
| --- | --- |
| Error origin | `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs:57-58` (gate: `Current == Embodied` + `Camera.main`, too coarse) |
| Rotation applied | `CardPlayerAnimation.cs:69` `SlerpLayerRotation(hoverLayer, _pose.Rotation)`; pose from `HoverFocusMath.cs:47-59` |
| Trigger | Pointer-enter a card while mode is Embodied AND player has arrowed to the board-overview (overhead) camera |
| Condition | `Camera.main` = overhead board-overview node (arrow-nav during Embodied) → look-at aims card face up → card horizontal |
| Related files | `AvatarCameraArbiter.cs:185-244`, `AvatarCameraModePolicy.cs:41-42`, `AvatarEmbodiedCamera.cs`, `CardVisualComponents.cs:35-43`, `Card.cs:357-368` |

## Conclusion

**Confidence:** High.

The hovered card lies flat because the first-person look-at hover runs against the wrong camera. The Vote is `CameraMode.Embodied` for the entire phase, but arrow-nav lets the player switch the live board camera to an overhead **board-overview** node, which becomes `Camera.main`. `CardPlayerAnimation.Hover` gates only on `_channel.Current == Embodied` and reads `Camera.main`, so it still applies the look-at — now orienting the hovered card's face toward the overhead camera, i.e. flat on the table. Only the hovered card is affected. The screenshot's top-down framing is that board-overview camera — direct confirmation. The competing "steep seated pitch" theory is refuted by the hard ±40° pitch clamp.

## Recommended Next Steps

### Fix direction (out of scope here — `gds-quick-dev`)
Tighten the gate so the look-at hover only runs when the seated first-person pose actually drives `Camera.main`, not merely when the mode is Embodied. By mechanism:
- **Preferred — gate on the live FP node:** expose/observe `_firstPersonActive` (or "SeatedFirstPerson is the current board camera", `AvatarCameraArbiter.cs:214-244`) and require it in addition to `Current == Embodied` at `CardPlayerAnimation.cs:58`. When on a board overview, fall back to the plain flat hover.
- **Defense-in-depth — clamp the look-at pitch:** in `HoverFocusMath.Compute`, clamp `_N`'s angle-from-horizontal to a max so the card can never fully flatten even if fed an overhead camera. Guards other future camera sources too.

### Diagnostic
Temp `[HOVERPOSE]` log at the `Hover` call: `Camera.main.name`, `_channel.Current`, and `Vector3.Angle(_N, Vector3.up)`. Hover while on the seated FP node vs after arrowing to the overview → confirm the flat pose correlates with `Camera.main` = the overview node.

## Reproduction Plan
Enter the Vote (Embodied). Arrow-navigate from the seated first-person view to the board-overview / top-down camera. Hover one of your cards (vote UI opens). → the hovered card rotates flat/horizontal on the table (as in the screenshot). Deterministic. Contrast: hovering while still on the seated first-person node tilts the card up correctly.

## Status: Concluded (root cause Confirmed, High confidence).
