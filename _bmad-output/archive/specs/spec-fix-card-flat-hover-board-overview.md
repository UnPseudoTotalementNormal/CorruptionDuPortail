---
title: 'Fix hovered card lying flat when on the board overview during Vote'
type: 'bugfix'
created: '2026-07-07'
status: 'done'
baseline_commit: '3a01567c3552063a358376cb7d59ea62d0e53c6e'
context:
  - '{project-root}/_bmad-output/implementation-artifacts/investigations/card-flat-horizontal-on-vote-hover-investigation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** During the Vote a hovered player card rotates flat/horizontal on the table when the player has arrow-navigated to a board-overview (overhead) camera. The Vote stays `CameraMode.Embodied` the whole phase, so `CardPlayerAnimation.Hover` still runs the first-person "look-at camera" rotation — now aiming the card's face at the overhead camera, which flattens it. (Root cause Confirmed in the linked investigation.)

**Approach:** The look-at hover must run only when the seated first-person node is the LIVE camera, not merely when the mode is `Embodied`. Extend `CameraModeChannel` (the existing decoupled broadcast the card already reads — its own doc states its purpose is to gate "are we seated first-person") with a `SeatedFirstPersonLive` flag written by `AvatarCameraArbiter`, and gate the look-at on it. On the overview the card falls back to the existing plain flat-lift hover.

## Boundaries & Constraints

**Always:** `AvatarCameraArbiter` stays the SOLE writer of `CameraModeChannel`. The new flag is set wherever `_firstPersonActive` presentation is applied (`ApplyFirstPersonPresentation`), so it tracks both mode changes and arrow-nav. Null-tolerant: an unwired channel behaves as today. Non-FPS / non-live-FP hover keeps the current flat-lift behavior.

**Ask First:** Adding any pitch-clamp to `HoverFocusMath` (a defense-in-depth extra beyond the gate) — do NOT do it unless asked.

**Never:** No change to game-state authority, RPCs, or the seated-pitch clamp. Do not force-activate the seated first-person camera. Do not alter the intended look-at hover when the seated FP node IS live. Do not add a singleton or couple the card to the arbiter directly.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Seated FP live | mode Embodied, `SeatedFirstPersonLive == true`, hover a card | Look-at rotation + computed lift (unchanged current behavior) | N/A |
| Board overview | mode Embodied, `SeatedFirstPersonLive == false`, hover a card | Plain flat-lift hover, NO look-at; card never lies flat | N/A |
| Arrow away mid-hover | card hovered on FP node, then arrow to overview | Flag flips false; next hover uses flat path (reticle-exit already resets rotation) | N/A |
| Channel unwired | `cameraModeChannel == null` | Flat-lift hover (as today) | Null-guard |
| Non-Embodied | mode Board/FreeRoam | Flat-lift hover (as today) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/CameraModeChannel.cs` -- add `SeatedFirstPersonLive` state + arbiter-only setter + change event
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- push the flag from `ApplyFirstPersonPresentation` (`_firstPersonActive` truth)
- `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs` -- `Hover` gate at `:58`: require the FP node live in addition to `Current == Embodied`
- `Assets/Scripts/Presentation/HoverFocusMath.cs` -- unchanged (root cause is the gate, not the math)

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Avatars/CameraModeChannel.cs` -- add `public bool SeatedFirstPersonLive { get; private set; }`, `event Action<bool> OnSeatedFirstPersonLiveChanged`, and `SetSeatedFirstPersonLive(bool)` (no-op + no event when unchanged); reset to `false` in `OnEnable` -- give consumers a live "is the seated FP node the active camera" signal
- [x] `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- in `ApplyFirstPersonPresentation`, call `_cameraModeChannel?.SetSeatedFirstPersonLive(_embodied)` (the same truth that drives `_embodiedCamera.SetActive`) -- broadcast arrow-nav + mode transitions
- [x] `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs` -- gate the `_firstPerson` look-at (`Hover`) AND the per-frame `Update` disarm on `_channel.SeatedFirstPersonLive` in addition to `Current == Embodied`; keep `_cam != null` and null-tolerance -- run look-at only when the seated FP camera is actually driving `Camera.main`, and release cleanly if the player arrows away mid-hover
- [x] `Assets/Scripts/Tests/Editor/CameraModeChannelTests.cs` -- EditMode test for `SetSeatedFirstPersonLive` (defaults false, fires once on change, suppressed when unchanged, independent of mode)

**Acceptance Criteria:**
- Given the Vote with the seated first-person node live, when hovering a card, then the look-at rotation + lift play exactly as before.
- Given the Vote after arrowing to a board overview, when hovering a card, then the card does a plain flat lift and never rotates to horizontal.
- Given an unwired `cameraModeChannel`, when hovering a card, then behavior is the current flat-lift (no NRE).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after edits) -- expected: no compile errors
- `mcp__UnityMCP__run_tests` (EditMode, filter CameraModeChannel/hover) -- expected: green

**Manual checks:**
- In-game Vote: on the seated first-person view, hover a card → tilts up to face you (unchanged). Arrow to the top-down board overview, hover a card → it lifts flat, does NOT lie down horizontal (the reported bug is gone).

## Suggested Review Order

**The gate (root-cause fix)**

- Entry point: the look-at now requires the seated FP node to be the live camera, not just Embodied mode.
  [`CardPlayerAnimation.cs:61`](../../../Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs#L61)

- Same requirement added to the per-frame disarm so an in-flight hover releases when arrowing away.
  [`CardPlayerAnimation.cs:96`](../../../Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs#L96)

**The signal (plumbing)**

- New `SeatedFirstPersonLive` flag + change event on the existing decoupled channel — completes its stated purpose.
  [`CameraModeChannel.cs:31`](../../../Assets/Scripts/Avatars/CameraModeChannel.cs#L31)

- Arbiter (sole writer) broadcasts the flag from the same truth that drives the embodied camera; tracks mode + arrow-nav.
  [`AvatarCameraArbiter.cs:227`](../../../Assets/Scripts/Avatars/AvatarCameraArbiter.cs#L227)

**Peripherals**

- EditMode test pinning the channel signal (defaults false, fires once, no-op when unchanged, mode-independent).
  [`CameraModeChannelTests.cs:1`](../../../Assets/Scripts/Tests/Editor/CameraModeChannelTests.cs#L1)
