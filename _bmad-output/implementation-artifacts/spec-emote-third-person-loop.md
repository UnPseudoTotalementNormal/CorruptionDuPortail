---
title: 'Emote self-feedback — orbit camera + modular loop/one-shot'
type: 'feature'
created: '2026-07-21'
status: 'done'
context: []
baseline_commit: 'fdc3115896ccf48d2d834783f1055f686292b33e'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** In the embodied vote the local player's own body is HIDDEN, and an emote is a one-shot
trigger. So the emoting player never perceives their own emote and it flashes by — they cannot tell
they emoted.

**Approach:** When the local player plays an emote, activate a DEDICATED third-person ORBIT camera
(mouse orbits AROUND the avatar without moving the character), reveal the local body, and play the
emote. Playback mode is modular per-emote: LOOP (holds until the player acts) or ONE-SHOT (plays once,
auto-returns) — the coucou LOOPS for now. The emote + orbit view end on the first stop action (any key,
any mouse button/click, any camera change — game state or nav — or leaving the tablet); mouse MOVEMENT
just orbits and never stops it. The emote is a networked state so everyone sees it hold, then stop.

## Boundaries & Constraints

**Always:**
- A NEW dedicated orbit Cinemachine camera, mouse-delta orbit around the local avatar; outranks the
  embodied + board cameras while active, stands down on exit (never removes them — same coexistence model
  as `AvatarEmbodiedCamera`).
- Head/body do NOT move with the orbit: freeze the embodied head-look during the emote (arbiter
  `SetLookEnabled(false)`) so no `SeatedYaw/SeatedPitch` is published — orbit is camera-only.
- MODULAR playback: `EmoteDefinition` carries a loop-vs-one-shot flag (coucou = loop now).
- Emote play/stop stays **server-authoritative** via the body `NetworkAnimator` (reuse the `PlayerAvatar` seam).
- Orbit camera + body reveal + stop-detection are **LOCAL presentation, owner-only** — no new game state
  / NetworkVariable, NOT NetworkBehaviours; routed through `AvatarCameraArbiter` (mirror `_emoteWheel`/`_tablet` hooks).
- Keep the pure `AvatarVisibilityController.ResolveVisible` static + its golden intact — reveal via an
  override at the apply site.
- Feel values (orbit distance/height/sensitivity, one-shot delay) are placeholder `[SerializeField]`, Poyo-tuned.

**Ask First:**
- Making the orbit view / body-reveal visible to OTHER clients (currently local-only).
- Adding board-camera graph nodes for the orbit camera (default: standalone priority camera, not a node).

**Never:**
- First-person paw/arm rig (the rejected alternative).
- Stopping the emote on mouse MOVEMENT (movement is orbit).
- `GetSafeRpcTarget`/bot routing (emote is a real-body cosmetic; bots have no body).
- Per-frame NetworkVariable writes; owner-write authority.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Play loop emote | Wheel released on coucou (embodied) | Server sets `EmoteId`+`Emoting=true`; orbit cam on; local body revealed; head frozen; emote loops for all | N/A |
| Play one-shot | Wheel released on a one-shot emote | Server fires the one-shot; orbit cam on for its duration then auto-returns | N/A |
| Orbit | Emoting, mouse moves | Camera orbits around the avatar; emote keeps playing; head does not turn | N/A |
| Stop — key/click | Emoting, owner presses any key or mouse button (fresh press) | Emote stopped; orbit cam off → first person; body hidden; look unfrozen | N/A |
| Stop — camera change | Emoting, game-state change OR board-camera nav OR tablet open/close | Same stop (arbiter drives it) | N/A |
| Start frame guard | The wheel-release frame that started the emote | Does NOT self-stop (only fresh presses AFTER start count) | N/A |
| Leave embodied mid-emote | Emoting, mode leaves Embodied/FreeRoam | Emote stopped, camera/visibility/look restored by arbiter | Null-tolerant if avatar torn down |
| Unwired animator | `_networkAnimator` null | Play/stop are silent no-ops (as today) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/EmoteDefinition.cs` -- add a `loops` flag (loop vs one-shot playback mode).
- `Assets/Scripts/Avatars/PlayerAvatar.cs` -- emote seam; loop path (`Emoting` bool + `EmoteId`) + one-shot path (trigger) + `StopEmote()`; select by the definition's mode.
- `Assets/Scripts/Avatars/AvatarEmoteOrbitCamera.cs` -- NEW: own `CinemachineCamera`, mouse-delta orbit (yaw/pitch + distance) around the bound local avatar; `SetActive` / priority stand-down like `AvatarEmbodiedCamera`.
- `Assets/Scripts/Avatars/EmotePlaybackController.cs` -- NEW local owner-only orchestrator: `Begin(EmoteDefinition)` / `End()`, `IsEmoting`, `EmoteStarted`/`EmoteStopped`; watches any-key/mouse-button (NOT mouse move) stop; auto-ends one-shots after their duration.
- `Assets/Scripts/Avatars/EmoteWheelInput.cs` -- on release route to `EmotePlaybackController.Begin(emote)` instead of `RequestEmote` directly.
- `Assets/Scripts/Avatars/AvatarVisibilityController.cs` -- add `SetLocalBodyVisibleOverride(bool)`; reveal owner body while emoting (mode != Board), static untouched.
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- subscribe to playback Started/Stopped (mirror `_emoteWheel`); on Started → orbit cam on + reveal + freeze look; on Stopped → restore; call `End()` on state/board-camera/tablet changes.
- `Assets/Animators/Cat_Avatar.controller` -- add `Emoting` bool + `EmoteId` int + `Emote` trigger: loop the emote clip while `Emoting`, one-shot via `Emote`→exit-time→Idle.
- `Assets/Scripts/Tests/Editor/EmoteStopConditionTests.cs` -- NEW EditMode: pure stop-condition (key/click yes, mouse-move no) + orbit-pose math goldens.

## Tasks & Acceptance

**Execution:**
- [x] `EmoteDefinition.cs` -- add `[SerializeField] bool loops` (default true) with accessor. *(also `oneShotSeconds`)*
- [x] `PlayerAvatar.cs` -- `RequestEmote(int id, bool loops)`: loop → `SetInteger(EmoteId)`+`SetBool("Emoting",true)`; one-shot → `SetInteger`+`SetTrigger("Emote")`. Add `StopEmote()` (owner → `StopEmoteRpc` → `SetBool("Emoting",false)`). Keep null-tolerance.
- [x] `AvatarVisibilityController.cs` -- add `_localBodyOverride` + `SetLocalBodyVisibleOverride(bool)`; owner body visible when override (incl. Board — owner-only self-feedback, others stay hidden).
- [x] `AvatarEmoteOrbitCamera.cs` -- NEW: bind local avatar (like `AvatarEmbodiedCamera.TryBind`), orbit pose from accumulated mouse delta around the avatar's chest/eye anchor, placeholder distance/height/sensitivity; `SetActive` raises/drops priority. *(pure math in `EmoteOrbit.cs`)*
- [x] `EmotePlaybackController.cs` -- NEW: `Begin(EmoteDefinition)` calls `avatar.RequestEmote(id, loops)` + raises `EmoteStarted`; `Update` ends on any fresh key/mouse-button press (skip start frame); one-shot auto-`End()` after duration; `End()` calls `StopEmote()` (loop only) + raises `EmoteStopped`.
- [x] `EmoteWheelInput.cs` -- serialize `EmotePlaybackController`; on release call `Begin(emote)` (fallback to direct `RequestEmote` if unwired).
- [x] `AvatarCameraArbiter.cs` -- serialize `EmotePlaybackController` + `AvatarEmoteOrbitCamera`; subscribe Started/Stopped → orbit-on+reveal+freeze-look / restore; call `End()` in `OnGameStateChanged`, `OnCurrentBoardCameraChanged`, tablet open/close, wheel open.
- [x] `EmoteSet.asset` -- coucou entry `loops: 1` (so it loops on load, not the missing-field default).
- [x] `EmoteStopConditionTests.cs` -- NEW EditMode goldens for the stop-condition helper + orbit-pose math. *(8/8 green)*
- [ ] **HANDOFF (Editor, Poyo)** `Cat_Avatar.controller` -- add `Emoting` bool; loop path Idle↔emote-state on `Emoting`; keep the one-shot trigger path. See **Handoff** below.
- [ ] **HANDOFF (Editor, Poyo)** GameScene -- create the orbit `CinemachineCamera` + `EmotePlaybackController` GameObjects and wire the new serialized refs. See **Handoff** below.

**Acceptance Criteria:**
- Given an embodied player releases the wheel on the coucou, when it plays, then a dedicated orbit camera activates, their own cat becomes visible, and the coucou loops.
- Given the emote is looping, when the player moves the mouse, then the camera orbits around the avatar and the character's head does not turn and the emote keeps playing.
- Given the emote is looping, when the player presses any key, clicks, changes camera/state, or exits the tablet, then the emote stops and the camera returns to first person.
- Given a one-shot emote definition, when played, then it plays once and auto-returns without waiting for an action.
- Given a remote client, when the local player emotes then stops, then they see the emote hold/play then return to idle (networked).
- Given `_networkAnimator` is unwired, when an emote is requested, then nothing happens (no exception).

## Spec Change Log

- **Review round 1 (3 adversarial reviewers).** Verdict PASS; 6 robustness patches applied, no loopback:
  (1) `EmoteWheelInput` fallback (playback unwired) forced to ONE-SHOT — a loop there set the server `Emoting`
  bool with no stop path. (2) `AvatarEmoteOrbitCamera.LateUpdate` guards `_camera == null` (Assert is stripped
  in release). (3) `AvatarCameraArbiter.OnEmoteStarted` gated on `_orbitCamera != null` (no camera → no reveal,
  avoids body clipping the FP view). (4) `EmotePlaybackController.OnDisable → End()` (defensive teardown).
  (5) `AvatarCameraArbiter.OnDestroy` stands the orbit camera down + clears the body override (teardown symmetry).
  (6) The local-body reveal now applies even at night (Board) for the OWNER only — self-feedback works whenever
  the wheel is reachable; others stay hidden (was: `mode != Board`, which orbited an empty body at night).
  Rejected as non-issues: `AvatarManager.For` already null-guards its arg; `_visibility` is an Awake-asserted
  mandatory ref; `anyKey` held-key edge; animator-unwired no-op; single-frame Begin churn; NetworkAnimator
  snapshot re-pick; `timeScale == 0`.

## Handoff — Editor (Poyo)

Code compiles clean, EditMode goldens green. The Animator graph + scene wiring + playtest are Editor work
(fragile to hand-edit in YAML, need visual + playtest verification):

**1. `Cat_Avatar.controller` (Animator window)**
- Add a **bool** parameter `Emoting` (names must match `PlayerAvatar`: `EmoteId` int, `Emote` trigger, `Emoting` bool — the first two already exist).
- Loop path: add a state (e.g. `WaveLoop`) using the coucou clip, with **loop time ON** (set on the clip import so the wave repeats). `Idle → WaveLoop` when `Emoting == true`; `WaveLoop → Idle` when `Emoting == false`. Keep the existing one-shot path (`Idle → Wave` on the `Emote` trigger, exit-time back to Idle) untouched — that's the modular one-shot route.

**2. GameScene wiring**
- New GameObject with a **`CinemachineCamera`** (bare, like the embodied one) + the **`AvatarEmoteOrbitCamera`** component; wire its `_camera` to that CinemachineCamera. Priority is code-driven (stands down at -100 when idle).
- New GameObject (or reuse the EmoteWheel input GO) with the **`EmotePlaybackController`** component.
- On **`AvatarCameraArbiter`**: wire `_emotePlayback` (the EmotePlaybackController) + `_orbitCamera` (the AvatarEmoteOrbitCamera).
- On **`EmoteWheelInput`**: wire `playback` (the EmotePlaybackController).
- Verify `PlayerAvatar._networkAnimator` is still wired (unchanged) — the loop rides the same NetworkAnimator.

**3. Feel tuning (Inspector, placeholder defaults)**
- `AvatarEmoteOrbitCamera`: distance / pivot height / yaw+pitch sensitivity / pitch clamp / start pitch.
- `EmoteDefinition.oneShotSeconds` per future one-shot emote.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected: 0 compile errors after each script change.
- `mcp__UnityMCP__run_tests` (EditMode, filter `EmoteStopConditionTests`) -- expected: green.

**Manual checks (Poyo playtest):**
- In an embodied vote, flick the wheel to coucou + release → own cat seen in third person doing coucou, looping; mouse orbits around it (head does not turn); any key/click/camera-change/tablet-exit snaps back to first person and stops the emote; other players see the coucou hold then stop.

## Suggested Review Order

**Networked emote seam (loop vs one-shot)**

- Entry point — how an emote begins/ends the self-feedback lifecycle.
  [`EmotePlaybackController.cs:40`](../../Assets/Scripts/Avatars/EmotePlaybackController.cs#L40)
- Server-authoritative play: loop bool vs one-shot trigger, null-tolerant.
  [`PlayerAvatar.cs:92`](../../Assets/Scripts/Avatars/PlayerAvatar.cs#L92)
- Per-emote loop/one-shot flag (data).
  [`EmoteDefinition.cs:28`](../../Assets/Scripts/Avatars/EmoteDefinition.cs#L28)

**Camera + visibility orchestration**

- Third-person orbit: mouse orbits the avatar, drives the Cinemachine pose each LateUpdate.
  [`AvatarEmoteOrbitCamera.cs:108`](../../Assets/Scripts/Avatars/AvatarEmoteOrbitCamera.cs#L108)
- Arbiter hooks Started/Stopped → orbit-on + reveal + freeze look; End() on every camera/state/tablet change.
  [`AvatarCameraArbiter.cs:137`](../../Assets/Scripts/Avatars/AvatarCameraArbiter.cs#L137)
- Owner body revealed during the emote (incl. night), static golden untouched.
  [`AvatarVisibilityController.cs:41`](../../Assets/Scripts/Avatars/AvatarVisibilityController.cs#L41)
- Wheel release routes the chosen emote through playback (one-shot fallback if unwired).
  [`EmoteWheelInput.cs:156`](../../Assets/Scripts/Avatars/EmoteWheelInput.cs#L156)

**Peripherals**

- Pure orbit-pose + stop-condition math.
  [`EmoteOrbit.cs:13`](../../Assets/Scripts/Avatars/EmoteOrbit.cs#L13)
- EditMode goldens (8 asserts).
  [`EmoteStopConditionTests.cs:13`](../../Assets/Scripts/Tests/Editor/EmoteStopConditionTests.cs#L13)
