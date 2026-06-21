---
title: 'Procedural rigged head-look with body follow-through'
type: 'feature'
created: '2026-06-21'
status: 'done'
baseline_commit: '0da2c5e'
context:
  - '{project-root}/_bmad-output/project-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-seated-ring-relative-gaze.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The cat's rendered head is rigid. The first-person camera tilts, but the body every OTHER player sees is a frozen-necked cat: the head bone never pitches, and yaw snaps the whole body instantly. Where a player is looking — the core "attention" tell of a social-deduction game — is invisible.

**Approach:** Aim the rigged Head bone procedurally (LateUpdate, over the Animator) from the avatar's networked head-look — the existing owner-write `SeatedYaw`/`SeatedPitch`, generalized to "head direction relative to body". Free-roam: the owner's look LEADS with the head (clamped to a cone) while the body yaw EASES to catch up, returning the head toward 0 local yaw; the same channel feeds remotes so everyone sees the tilt/turn + body follow-through. Seated Vote: the head turns up to its clamp, the body stays locked at the seat.

## Boundaries & Constraints

**Always:**
- Mutate the Head bone ONLY in `LateUpdate` (after Animator/NetworkAnimator) so the aim layers over the Wave clip.
- Presentation-only (NFR3): no game state, no per-frame RPC. Ride the EXISTING owner-write `SeatedYaw`/`SeatedPitch` — owner publishes, EVERY client (owner + remotes) applies to the bone.
- Body yaw stays owner-auth on `NetworkTransform`; only the OWNER eases its body yaw, gated by `IsOwner` (bots never own avatars — plain `IsOwner` is correct).
- Clamp head yaw to a cone and pitch to a range; framerate-independent easing (`1 - Mathf.Exp(-k·dt)`). All feel values `[SerializeField]`, Poyo-tuned.
- Keep `EyePivot` as the camera anchor; in free-roam it carries the SAME yaw offset + pitch as the head, so the camera follows the look while the body lags.

**Ask First:**
- Adding ANY new networked variable beyond reusing `SeatedYaw`/`SeatedPitch` (the ratified owner-write cosmetic-gaze exception). Broadening it to free-roam is the same category — confirm before a 2nd owner-write NV.
- Making the body catch up in the SEATED Vote (spec keeps the seated body locked at the seat; catch-up is free-roam only).

**Never:**
- No server-authoritative head aim, no RPC-per-frame, no IK package — direct bone rotation only.
- No idle/secondary motion (breathing, ears, whiskers, tail, blink, bow sway) — that is deferred Goal 2 (`cat-avatar-juice`).
- Do NOT rename or remove `SeatedYaw`/`SeatedPitch` (the seated-ring spec and serialized prefab data depend on them).
- No `AudioSource` (FMOD only; no audio in this goal anyway).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Free-roam pitch | Owner looks up/down | Head bone pitches within ±pitchClamp; owner camera tilts; remotes show the same tilt | N/A |
| Free-roam yaw lead+catch-up | Owner holds a turn | Head yaw offset rises to the cone, then body yaw eases toward the look so head-local yaw returns ~0 | N/A |
| Seated Vote | Owner looks (clamped) | Head bone turns up to the seated clamp; body stays locked at the seat pose | N/A |
| Remote replica | `SeatedYaw`/`Pitch` change, no local input | Head bone interpolates smoothly toward the networked look; no body catch-up driven locally | N/A |
| Head bone unwired | `_headBone == null` | Component no-ops | Log once, no NRE |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarHeadLook.cs` -- NEW. Reads `PlayerAvatar.SeatedYaw/SeatedPitch` + the head bone; LateUpdate aims the bone (owner + remotes, both modes).
- `Assets/Scripts/Avatars/PlayerAvatar.cs` -- add `[SerializeField] Transform _headBone` + `HeadBone` accessor; `SeatedYaw/Pitch` semantics broadened to "head-look vs body" (doc only).
- `Assets/Scripts/Avatars/AvatarMovementController.cs` -- free-roam look rework: accumulate look-yaw, write the clamped head yaw-offset + pitch to `EyePivot`, ease body yaw toward look, publish via `PlayerAvatar.PublishSeatedLook`.
- `Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs` -- already publishes `SeatedYaw/Pitch` (seated); confirm unchanged.
- `Assets/Scripts/Avatars/AvatarSeatingPresenter.cs` -- its `EyePivot` writes become redundant for the visible head (the bone is now driven); leave body/NT suppression intact, do not double-drive the head bone.
- `Assets/Prefabs/Avatars/Cat_Avatar.prefab` + `PlayerAvatar.prefab` -- add `AvatarHeadLook`, wire `_headBone` to `BW Rig/Root/Spine1/Spine2/Spine3/Head`, set `PlayerAvatar._headBone`.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Avatars/AvatarHeadLook.cs` + `AvatarLookMath.cs` -- LateUpdate head-bone aimer; pure aim/clamp/ease math extracted to the `static AvatarLookMath` helper (unit-testable, no scene).
- [x] `Assets/Scripts/Avatars/PlayerAvatar.cs` -- documented the broadened gaze semantics on `SeatedYaw/Pitch`. (PLACEMENT CHANGE: the `_headBone` ref lives on `AvatarHeadLook` in the cat model prefab, not on PlayerAvatar — see Design Notes.)
- [x] `Assets/Scripts/Avatars/AvatarMovementController.cs` -- split look-yaw from body-yaw: clamped head offset to `EyePivot`, body-yaw catch-up ease, sparse `PublishSeatedLook`. Added `[SerializeField] _headYawCone`, `_bodyCatchUpSpeed`.
- [x] `Assets/Prefabs/Avatars/Cat_Avatar.prefab` -- added `AvatarHeadLook`, wired `_headBone` to `BW Rig/.../Spine3/Head` (read back: fileID 4477502950927753904). Nested `CatVisual` in `PlayerAvatar.prefab` inherits it.
- [x] `Assets/Scripts/Tests/Editor/AvatarHeadLookMathTests.cs` -- 5 EditMode tests (cone/pitch clamp, shortest-path + monotonic ease, catch-up converges head offset → 0). 239/239 EditMode green.

**Acceptance Criteria:**
- Given a remote replica, when its `SeatedYaw/SeatedPitch` change, then its rigged head bone interpolates smoothly toward that look with no local input read.
- Given the seated Vote, when the owner looks, then the head bone turns up to the seated clamp and the body remains at the seat pose.
- Given free-roam, when the owner holds a yaw turn, then the head leads to the cone and the body eases until the head-local yaw returns toward 0.
- Given `_headBone` is unwired, then the component logs once and no-ops (no exception).

## Spec Change Log

- **2026-06-21 — review patches (no spec loopback; intent/spec held).** Three adversarial reviewers (blind / edge-case / acceptance) found the acceptance criteria + boundaries fully met, no intent_gap / bad_spec. Auto-fixed patches applied to the code (not the spec): (1) **stale-Vote-gaze** — `AvatarMovementController` now NaN-arms its publish + re-seeds heading/pitch + neutral-publishes on `SetMovementEnabled`, so leaving a look mode re-centres the rigged head (no more head stuck cranked in the Lobby / on the Board); (2) **bone aim** rewritten as an intrinsic root-frame `Euler(pitch,yaw,0)` delta (matches the eye-pivot convention; no pitch/yaw axis coupling); (3) **NaN sanitize** on the untrusted owner-write look; (4) **first-frame seed** of the displayed aim (no swing-in for late joiners); (5) **explicit `[DefaultExecutionOrder(100)]`** on `AvatarSeatingPresenter` so the after-presenter ordering is enforced, not implicit; (6) `_lookYaw` bounded via `Mathf.Repeat`. One finding deferred (seated `EyePivot` ease now redundant with the bone path — tuning hygiene, see deferred-work).

## Design Notes

Axis calibration is the one fiddly part: the Blender-exported head bone's local axes may not map cleanly to yaw=Y / pitch=X. Aim in the avatar-root frame and convert, rather than guessing local euler:

```
// after Animator; _root = avatar root, _aim = target look in root space
Quaternion target = Quaternion.AngleAxis(yawDeg, _root.up) *
                    Quaternion.AngleAxis(pitchDeg, _root.right) * _headBone.rotation;
_headBone.rotation = Quaternion.Slerp(_headBone.rotation, target, 1 - Mathf.Exp(-k*dt));
```

v1 OVERRIDES the head bone (Wave's head keys are superseded); additive blending over the clip is a later tuning pass. Reusing `SeatedYaw/Pitch` for free-roam is the same ratified cosmetic owner-write category — only one mode publishes at a time (arbiter-exclusive), so the channel never has two writers.

**Placement deviation from the Code Map (implementation decision):** `AvatarHeadLook` and its `_headBone` reference live on the **cat model prefab** (`Cat_Avatar.prefab`), not on the outer `PlayerAvatar` root. Rationale: the head bone is inside the nested model, so a same-prefab local ref is stable and reusable (the cat carries its own head-look); a cross-prefab ref from the outer root into the nested instance would be fragile to wire and re-derive. `AvatarHeadLook` resolves the `PlayerAvatar` (look channel + body frame) via `GetComponentInParent`, and no-ops when the model has no PlayerAvatar parent. Net effect on behaviour is identical to the Code Map; only the component/ref location moved.

**Publish is sparse:** the owner pushes the head-look onto the NetworkVariable only past a 0.25° threshold (mirrors `AvatarEmbodiedCamera`), so a still/slow look does not write every frame; remote smoothing is `AvatarHeadLook`'s ease.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after each script change) -- expected: no compile errors.
- `mcp__UnityMCP__run_tests` (EditMode, filter `AvatarHeadLookMath`) -- expected: all green.

**Manual checks:**
- Two clients (host + 1): a remote cat's head visibly pitches with the other player's look and turns with body follow-through in free-roam; in the seated Vote the head turns while the seated body stays put.

## Suggested Review Order

**Free-roam: head leads, body follows (the feel)**

- Entry point — the look split: accumulate heading, clamped head offset to the eye, body yaw catch-up, sparse publish.
  [`AvatarMovementController.cs:191`](../../Assets/Scripts/Avatars/AvatarMovementController.cs#L191)

- Mode hygiene — re-seed + NaN-arm on free-roam entry, neutral-publish on exit (fixes stale-Vote-gaze / Board head).
  [`AvatarMovementController.cs:145`](../../Assets/Scripts/Avatars/AvatarMovementController.cs#L145)

- The two Poyo-tuned feel knobs (cone + catch-up speed).
  [`AvatarMovementController.cs:43`](../../Assets/Scripts/Avatars/AvatarMovementController.cs#L43)

**Rigged head aim (presentation, all clients)**

- The head-bone aimer: NaN-sanitize, first-frame seed, eased angles, intrinsic root-frame Euler delta over the clip.
  [`AvatarHeadLook.cs:54`](../../Assets/Scripts/Avatars/AvatarHeadLook.cs#L54)

- Pure, unit-tested math (clamp / shortest-path ease / head offset).
  [`AvatarLookMath.cs:33`](../../Assets/Scripts/Avatars/AvatarLookMath.cs#L33)

- Explicit execution order so the head aims against the already-snapped seat body.
  [`AvatarSeatingPresenter.cs:33`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L33)

**Networked channel (no new state)**

- The reused owner-write `SeatedYaw/Pitch`, now documented as the shared head-look channel.
  [`PlayerAvatar.cs:45`](../../Assets/Scripts/Avatars/PlayerAvatar.cs#L45)

**Supporting**

- EditMode golden for the math (cone/pitch clamp, wrap, catch-up → 0).
  [`AvatarHeadLookMathTests.cs:54`](../../Assets/Scripts/Tests/Editor/AvatarHeadLookMathTests.cs#L54)

- Prefab wiring: `AvatarHeadLook` + `_headBone` on the cat model (inherited by the nested `CatVisual`).
  [`Cat_Avatar.prefab`](../../Assets/Prefabs/Avatars/Cat_Avatar.prefab)
