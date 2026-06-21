---
title: 'Board-mode avatar hiding (centralized visibility) + seated camera eye-height fix'
type: 'feature'
created: '2026-06-21'
status: 'done'
context: ['{project-root}/_bmad-output/project-context.md']
baseline_commit: 'a0f462e'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** (A) Avatars are visible in every camera mode; the design is "you only see each other during the day" — so in **Board** mode (everything that is not the lobby free-roam nor the seated Vote) **all** avatar bodies should be hidden. Today nothing hides them in Board. (B) In the seated embodied Vote the first-person camera sits at the floor ("eyes at the ground"): `AvatarEmbodiedCamera` adds a hardcoded `_eyeOffset` (≈1.2) to the seat position, but avatars are scaled ×6, so the real eye height is ~10 units up — the camera is ~9 units too low. The lobby camera is correct because it copies the avatar's eye-anchor transform pose directly.

**Approach:** (A) Introduce a single visibility authority driven by the arbiter's `CameraMode`: Board → hide every avatar; FreeRoam/Embodied → show every avatar except the LOCAL one (first-person). Remove the per-camera local hide/show so renderer visibility has exactly one owner (no cross-mode conflict). (B) Drive the seated camera's POSITION from the avatar's dedicated eye-anchor transform (`PlayerAvatar.EyePivot` — a bare, NON-animated child, so the rigged model's animations never shake the camera), like the lobby camera; keep the clamped look rotation. This makes eye height scale-correct with no magic number.

## Boundaries & Constraints

**Always:**
- Exactly ONE owner of avatar `Renderer.enabled` (the new visibility controller). Cameras no longer toggle renderers.
- Visibility policy is purely a function of the arbiter `CameraMode` + `IsOwner`: Board → all hidden; FreeRoam/Embodied → hidden iff local owner.
- Seated camera eye position comes from a NON-animated transform on the avatar (the `EyePivot`), never the rigged model/bones. Keep the clamped yaw/pitch look unchanged.
- Presentation-only (NFR3) — no game state, no networking changes, no RPC. Null-tolerant; handle late-spawning avatars.
- Respect subscription symmetry, arbiter NFR1/NFR2 (never write the state index, never remove board cameras), and existing patterns.

**Ask First:**
- If centralizing visibility would require changing how the first-person cameras BIND the local avatar (beyond dropping the renderer toggle), confirm before expanding scope.

**Never:**
- No per-camera renderer hiding left behind (would fight the controller).
- Do not parent the scene camera under a runtime-spawned avatar; keep the copy-pose-each-LateUpdate pattern.
- No change to seating geometry, gaze, or the NetworkTransform suppression from the prior story.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Board mode | resolved mode = Board | Every avatar (local + remote) renderers OFF | — |
| Lobby | FreeRoam | All visible except local (first-person) | — |
| Seated Vote | Embodied | All visible except local; remotes seated/visible | — |
| Late spawn | avatar appears mid-mode | Picks up current policy next frame | Null-tolerant |
| Mode change | Board→Vote→Board | Renderers re-applied each transition; no stuck-hidden/visible avatar | — |
| Seated eye height | Embodied, avatar scaled ×N | Camera at the EyePivot world height (eye level), not the floor | Fallback to seat+offset if EyePivot unwired |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarVisibilityController.cs` (NEW) -- single visibility authority; `SetMode(CameraMode)` from the arbiter; each LateUpdate applies the policy to every avatar (caches renderer arrays, handles late spawns).
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- serialize + Assert the controller; call `_visibility.SetMode(_currentMode)` in `ApplyMode`.
- `Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs` -- camera POSITION from `_boundAvatar.EyePivot.position` (scale-correct), keep clamped look; remove `HideBoundModel`/`ShowBoundModel`/`_boundRenderers`/`OnDestroy` show + the `_eyeOffset` floor bug (keep an offset only as a null-EyePivot fallback).
- `Assets/Scripts/Avatars/AvatarFollowCamera.cs` -- remove `HideBoundModel`/`ShowBoundModel`/`_boundRenderers` (local hide now centralized); keep eye-pose copy.
- `GameScene` -- add the `AvatarVisibilityController` component, wire it on the arbiter (MCP, read-back verified).
- `Assets/Scripts/Tests/.../Avatars*` -- wire the controller into the arbiter fixtures (new Awake assert); add a visibility-policy EditMode test if pure-extractable.

## Tasks & Acceptance

**Execution:**
- [x] `AvatarVisibilityController.cs` -- NEW: `SetMode`; pure `ResolveVisible(mode, isOwner)` (Board→hide all; else hide iff owner); LateUpdate apply over `GetAvatars()` with cached renderers; null-tolerant.
- [x] `AvatarCameraArbiter.cs` -- serialize `_visibility` + Assert; drive `SetMode(_currentMode)` in `ApplyMode`.
- [x] `AvatarEmbodiedCamera.cs` -- position from `_boundAvatar.EyePivot` (fallback seat+offset); stripped renderer hide/show + OnDestroy show.
- [x] `AvatarFollowCamera.cs` -- stripped renderer hide/show; kept eye-pose copy.
- [x] `GameScene` -- added + wired `AvatarVisibilityController` on the arbiter GO (read-back verified, saved).
- [x] Tests -- wired controller into both arbiter fixtures (Awake assert); NEW `AvatarVisibilityPolicyTests` (6 asserts); camera-mode tests green.

**Acceptance Criteria:**
- Given Board mode, when it is active, then every avatar body is hidden (local and remote).
- Given the lobby (FreeRoam) or the seated Vote (Embodied), when active, then all avatars are visible except the local one (first-person).
- Given the seated Vote, when the camera positions, then the eye is at the avatar's eye height (from the EyePivot), not the floor.
- Given mode transitions Board↔Vote↔Lobby, then no avatar is stuck hidden or stuck visible, and no renderer is double-owned.
- Given EditMode/PlayMode tests run, then arbiter camera-mode tests stay green and the new visibility wiring compiles + passes.

## Design Notes

Eye-height fix mirrors `AvatarFollowCamera.LateUpdate` (it copies `EyePivot.position`). The seated camera keeps its own clamped look:
```
_camera.transform.SetPositionAndRotation(
    _boundAvatar.EyePivot.position,                    // scale-correct, non-animated anchor
    _seat.Rotation * Quaternion.Euler(_pitch, _yaw, 0f));
```
Centralized visibility removes the duplicated local hide from both cameras so the local avatar's renderers have ONE owner across all mode transitions — the prior split (each camera toggling on activate/deactivate) would fight a Board "hide all" on the local body.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change -- expected: zero compile errors.
- `mcp__UnityMCP__run_tests` (PlayMode, Avatars) -- expected: arbiter/embodied camera-mode tests green with the new controller wired.
- `mcp__UnityMCP__run_tests` (EditMode, Avatars) -- expected: existing + any new policy test green.

**Manual checks:**
- Multi-client: in Board nobody is visible; in Lobby/Vote others are visible and self is hidden; seated, the camera is at eye level (not the floor); transitions don't strand visibility.
