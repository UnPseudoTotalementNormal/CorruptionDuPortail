---
title: 'FPS card hover v2: gated, look-at-camera, computed no-clip lift (reusable channel)'
type: 'feature'
created: '2026-06-21'
status: 'in-progress'
context: ['{project-root}/_bmad-output/project-context.md']
baseline_commit: '4900be1'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The PR2 hover (fixed -40° pitch + 0.4 local lift) is wrong: (1) the lift is in the card-root local space (×0.5 world = 0.2) while the card is 4.4 world tall, so tilting sinks the card ~1.4 through the table (CLIP); (2) card hover also fires from the mouse GraphicRaycaster in non-FPS phases (board/picker), so the first-person tilt LEAKS where we're not seated.

**Approach (Poyo):** In the embodied first-person Vote ONLY, on hover the card rotates to **LookAt the camera** (yaw+pitch, real facing) and **lifts by a COMPUTED amount = the card's vertical extent once rotated + an offset**, so it floats just above the table and never clips at any angle. Gating uses a clean, REUSABLE **`CameraModeChannel` ScriptableObject** (the arbiter writes the current `CameraMode`; any element reads/subscribes) so the same gate can drive other elements later. The look+lift geometry is a PURE, testable helper with the card's face axes as PARAMETERS (the axis ambiguity that broke v1).

## Boundaries & Constraints

**Always:**
- FPS hover pose applies ONLY when `CameraModeChannel.Current == Embodied`; otherwise the existing FLAT hover plays. No leak into Board/FreeRoam/picker.
- Lift is COMPUTED from the card's rotated vertical extent (world) + a tunable offset, converted back into the compositor "Hover" layer's local space (root world scale 0.5) — never a magic constant; never clips.
- Rotation is a real look-at the active camera (yaw+pitch); the card's local face-normal + face-up are serialized PARAMETERS so the math is correct + tunable per element (not hard-guessed).
- The card animates through the existing compositor "Hover" layer (composes with Flip/Punch); detection stays on the static card root (reticle raycasts that) → no jitter.
- `CameraModeChannel` is a decoupled SO event channel (no singleton); the arbiter is the sole writer. Presentation only — no game-state/networking/vote-rule changes.

**Ask First:**
- The serialized face-axis params + offset/feel are visually verified by Poyo against the real seated camera before sign-off (the v1 axis guess clipped/faced wrong).

**Never:**
- No fixed/magic lift; no tilt that can clip the table; no FPS tilt outside Embodied; no per-frame billboard chase (compute the look-at pose once on enter).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected | Error Handling |
|----------|--------------|----------|----------------|
| Hover in Embodied | reticle on card, mode=Embodied | Card looks at camera + lifts to float above table (computed); no clip | — |
| Hover in Board/picker | mouse hover, mode≠Embodied | Old flat hover only (no tilt/leak) | — |
| Exit | unhover | Reverts to rest (rotation + lift back to 0) | — |
| Steep/shallow camera | card near/far | Lift recomputed from the rotated extent → always clears the table + offset | — |
| Channel unset | no SO wired | Treat as non-Embodied (flat hover); warn once | null-tolerant |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/.../CameraModeChannel.cs` (NEW SO) -- reusable event channel: `CameraMode Current`, `Set(mode)` raising `OnChanged`. Arbiter writes; elements read/subscribe.
- `Assets/Scripts/.../HoverFocusMath.cs` (NEW pure) -- given card world pose+size, camera pos, surface Y, offset, and local face-normal/up → returns the look-at world rotation + the lift (so the rotated card's lowest point = surfaceY + offset). EditMode-testable.
- `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs` -- gate on the channel: Embodied → compute pose via `HoverFocusMath` (Camera.main + card transform + size) and tween the "Hover" layer (local rotation + local lift); else the existing flat hover. Reverse on unhover.
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- serialize the channel; `channel.Set(_currentMode)` in `ApplyMode`.
- `Assets/Prefabs/Card.prefab` + `GameScene` + a `CameraModeChannel` asset -- wire the channel on the arbiter + the card (face-axis params + offset serialized for tuning).

## Tasks & Acceptance

**Execution:**
- [ ] `CameraModeChannel.cs` (NEW SO) -- `Current` + `Set` + `OnChanged`.
- [ ] `HoverFocusMath.cs` (NEW pure) -- look-at rotation (from face-normal/up params) + computed lift from rotated extent + offset; world↔local-scale aware. Pure.
- [ ] `HoverFocusMathTests.cs` (NEW EditMode) -- lift clears the surface at varied angles; rotation faces the camera; degenerate guards.
- [ ] `CardPlayerAnimation.cs` -- channel-gated FPS look-at+computed-lift hover vs flat; revert on unhover; serialized face-axis params + offset.
- [ ] `AvatarCameraArbiter.cs` -- write the channel in `ApplyMode`.
- [ ] `GameScene`/`Card.prefab`/asset -- create + wire the `CameraModeChannel`; verify (read-back).

**Acceptance Criteria:**
- Given the embodied Vote, when hovering a card, then it rotates to face the camera and lifts to float just above the table with NO part clipping the table, at any camera angle.
- Given a non-embodied phase (board/picker mouse hover), then only the flat hover plays — the FPS tilt never appears.
- Given unhover, then the card returns fully to rest.
- Given EditMode tests, then `HoverFocusMath` proves the lift clears the surface across angles and the rotation faces the camera.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- zero compile errors.
- `mcp__UnityMCP__run_tests` (EditMode) -- `HoverFocusMathTests` green; full suite unaffected.

**Manual checks (Poyo, before commit):**
- Seated Vote: card faces you + floats above the table, no clip, any angle; non-FPS phases show only the flat hover; tune face-axis params/offset if the facing/height is off.
