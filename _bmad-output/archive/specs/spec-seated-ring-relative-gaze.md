---
title: 'Seated ring — procedural seats + per-client rotation + relative networked gaze'
type: 'feature'
created: '2026-06-21'
status: 'done'
context: ['{project-root}/_bmad-output/project-context.md']
baseline_commit: '9e8ca2b5cc620a10459908fb64cb44ee5ec29a4b'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Seats are a hand-wired `List<Transform>` (4 fixed) on `AvatarManager`, so the table never adapts to the player count, and seated players are placed at *global* world seats — only one player can ever sit at the fixed "front of the table" interaction spot. During the embodied Vote the body is static (movement locked, only the local camera looks around), so there is no networked gaze: a player cannot see who is looking at them.

**Approach:** Replace the seat list with **one ring-center Transform + a serialized radius**; compute seats procedurally as an evenly-spaced circle, **count = current avatar count**. The ring's *relative* arrangement (who sits next to whom, by the replicated avatar-list order) is identical on every client, so each client **rigidly rotates the whole ring locally** to place its OWN avatar at the fixed front spot. Seated positions are therefore **computed locally, not networked**; the owner-authoritative `NetworkTransform` is suppressed during the Vote so each client drives every avatar's seated pose from the shared relative order. A new **relative-to-seat head yaw** is networked so others see who you are looking at — gaze is preserved because a rigid rotation is an isometry (it preserves the "A looks at B" relation).

## Boundaries & Constraints

**Always:**
- Ring arrangement derives ONLY from the replicated avatar-list order (`SeatIndexForClient`) + `ownerClientId` — both already consistent across clients. Relative offset `(targetIndex - localIndex + N) % N` is the single source of seat angle.
- Local player's seat = relative offset 0 = the fixed front spot, every client. Seats face the ring center.
- Networked gaze data is **relative to seat facing** (yaw + pitch floats), applied to the avatar's HEAD (EyePivot) for a rigged body+head, never an absolute world rotation. Published **on change only** (never per frame) and **interpolated** on remote clients (smooth head turn). Server-auth game state untouched (NFR3 — presentation only, no RPC per frame, no new `NetworkVariable<Vector3>`).
- Seated placement is purely cosmetic/local; suppress (disable) the avatar `NetworkTransform` only for the embodied window, restore it on exit. Symmetric enable/disable.
- Respect existing patterns: `IsLocalOrSimulated`, owner-gated writes, subscription symmetry, UniTask, FMOD untouched.

**Ask First:**
- The mechanism to suppress per-frame networked seated positions: toggling `NetworkTransform.enabled` during the Vote vs. another NGO mechanism. Confirm before relying on disable/re-enable behaviour mid-session.
- Whether seated **pitch** (up/down) is also networked, or **yaw only** is sufficient for "who you look at".
- Radius/front-angle source: serialized float + ring-center forward as the front direction (assumed) vs. a separate front-anchor Transform.

**Never:**
- No per-client *random* arrangement — the relative order must be identical everywhere (random would break gaze). 
- Do not network seated world positions, and do not keep calling `SeatAtSeat` to teleport-replicate the body during the Vote.
- Do not remove/disable board cameras or touch the GameState index (NFR1/NFR2). No bot avatars (clientId ≥ 100).
- No redesign of game mechanics — geometry/presentation only.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Local seat | N players, local at list index i | Local avatar seated at fixed front pose (offset 0), facing center, identical world pose for every local player | — |
| Even spread | N players | Seats at `front + k·(360/N)`, k = relative offset; equidistant | — |
| Remote consistency | I look at player X (offset k) | On X's client my avatar is at offset −k and my networked relative yaw+pitch turn my HEAD (EyePivot) toward X, interpolated smoothly | — |
| Count change | player joins/leaves mid-Vote | Ring recomputes for new N next frame; no networked teleport | — |
| No ring center wired | `_ringCenter == null` | `GetSeatPose` returns identity/no-op; warn once; no crash | Null-tolerant |
| N == 0 / not yet listed | avatar mid-spawn | Falls back to front pose; never divides by zero | Guard |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarManager.cs` -- replace `_seats`/`_spawnPoints` seat list with `_ringCenter` + `_ringRadius`; add pure `GetSeatPose(ulong target)` computing the local-frame ring pose; keep `GetSpawnPoint` (lobby) or fold into ring.
- `Assets/Scripts/Avatars/AvatarSeatingPresenter.cs` (NEW, local `MonoBehaviour`) -- while Embodied: each `LateUpdate` set every avatar's transform to `GetSeatPose(ownerClientId)` and apply its networked relative yaw to the `EyePivot`/head; suppress `NetworkTransform` on enter, restore on exit.
- `Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs` -- camera reads `GetSeatPose(localId)` instead of a seat `Transform`; the local owner publishes its clamped `_yaw` to the networked seated-yaw.
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- drive the seating presenter (enable on Embodied enter, disable on exit); stop the `SeatAtSeat` networked teleport route.
- `Assets/Scripts/Avatars/AvatarMovementController.cs` -- retire/neutralise networked `SeatAtSeat`; expose nothing that re-networks seated pose.
- `Assets/Scripts/Avatars/PlayerAvatar.cs` -- add `NetworkVariable<float>` seated yaw (owner-write, relative to seat facing); expose `EyePivot` (exists).
- `Assets/Prefabs/Avatars/PlayerAvatar.prefab` + `GameScene` -- wire `_ringCenter` + radius (replaces the 4 seat transforms); confirm `NetworkTransform` toggle path.
- `Assets/Scripts/Tests/.../AvatarEmbodiedModeTests.cs` -- update `SeatAtSeat_…ReplicatesViaTransform` (no longer networked); add EditMode tests for `GetSeatPose` geometry + relative-offset invariance.

## Tasks & Acceptance

**Execution:**
- [x] `AvatarManager.cs` -- swapped seat list for `_ringCenter`+`_ringRadius`+`_frontAngleDeg`; pure `GetSeatPose(target)` delegates to `SeatRingGeometry.Compute` (NEW pure helper) from `(targetIndex-localIndex+N)%N`, equidistant, facing center, null/zero guarded (warns once).
- [x] `SeatRingGeometry.cs` (NEW) -- pure EditMode-testable geometry (`SeatPose` struct + `Compute`).
- [x] `PlayerAvatar.cs` -- added owner-write `NetworkVariable<float> SeatedYaw` + `PublishSeatedYaw`.
- [x] `AvatarSeatingPresenter.cs` -- NEW local driver: on Embodied enter caches+disables each avatar `NetworkTransform`, each `LateUpdate` places all avatars + applies seated yaw to the body; on exit restores cached pose FIRST then re-enables NT.
- [x] `AvatarEmbodiedCamera.cs` -- consumes `GetSeatPose(localId)` (recomputed each frame); publishes local clamped yaw to `SeatedYaw`.
- [x] `AvatarCameraArbiter.cs` -- routes presenter on/off with the Embodied mode; removed the `SeatAtSeat` teleport route + one-shot re-arm.
- [x] `AvatarMovementController.cs` -- removed networked `SeatAtSeat`.
- [x] `GameScene` -- created `AvatarRingCenter` empty, wired `_ringCenter`+`_ringRadius=6` on AvatarManager, added+wired `AvatarSeatingPresenter` on the arbiter GO (read-back verified, scene saved). Prefab `NetworkTransform` left intact (suppressed at runtime).
- [x] Tests -- NEW `SeatRingGeometryTests` (7 geometry/invariance asserts); removed the obsolete `SeatAtSeat` replication test; wired the presenter into `AvatarEmbodiedModeTests` + `AvatarCameraArbiterTests` fixtures.

**Acceptance Criteria:**
- Given N seated players, when the Vote is entered, then on each client its own avatar sits at the identical fixed front pose and the others are equidistant around the ring by relative order.
- Given I aim at player X during the Vote, when X looks at their screen, then X sees my avatar's head turned toward X (networked relative yaw), and vice-versa.
- Given a player joins or leaves during the Vote, when the next frame renders, then the ring re-spreads for the new count with no networked position teleport.
- Given the Vote ends, when returning to Board, then `NetworkTransform` is restored and lobby/board behaviour is unchanged (NFR1).
- Given EditMode tests run, then `GetSeatPose` geometry + relative-offset invariance pass; the replication test reflects the now-local seated pose.

## Spec Change Log

### 2026-06-21 — review patches + human-renegotiated look (yaw → yaw+pitch, head, interpolation)

Adversarial review (3 reviewers) → patches only (no loopback; no intent_gap/bad_spec):
- **P1** `AvatarSeatingPresenter.OnDisable` → `RestoreAll` when active: a presenter deactivated/destroyed mid-Vote would otherwise leave every avatar's `NetworkTransform` disabled forever (bodies frozen, no sync).
- **P2** look published **on change only** (epsilon gate) — no per-frame `NetworkVariable` write (project-context bandwidth rule).
- **P3** presenter also disables each managed avatar's `CharacterController` — remote replicas keep theirs enabled otherwise; a direct transform write on the tight ring could depenetrate-eject ("avatar flying up" class).
- **P4** look reset on Vote entry (NaN sentinel forces first publish) — no stale gaze from the previous Vote.
- **P5** presenter no-ops until the local owned avatar is listed — avoids the `SeatIndexForClient` not-found sentinel placing bodies on colliding seats.
- **Guard** `AvatarEmbodiedCamera` re-checks `_manager`/`_boundAvatar` each frame — no deref of a destroyed manager on teardown.

Human renegotiation (Poyo, post-approval): networked look is **yaw + pitch** (rigged body+head), applied to the **HEAD (EyePivot)** not the body root, and **interpolated** on remote clients (`_headLerpSpeed`) so gaze turns smoothly despite sparse on-change publishing.

**KEEP:** pure `SeatRingGeometry` + EditMode invariance tests; restore-pose-then-re-enable-NT ordering; relative-look-only (never absolute world rotation).

**Human sign-off (LOW): RATIFIED by Poyo 2026-06-21.** `SeatedYaw`/`SeatedPitch` are owner-write `NetworkVariable`s — first in the project, a sanctioned exception to project-context's "no owner-write" rule (cosmetic gaze, no authority). Remaining gate before merge: live 3-client playtest.

## Design Notes

Gaze invariance: every client renders the SAME seated configuration up to a rigid rotation `R(δ_client)` chosen so the local avatar lands at the front spot. A rigid rotation preserves all angles, so "A points at B" holds on every client **iff** head direction is stored relative to seat facing (rotates with the frame) — hence the networked yaw is relative, never absolute world yaw.

Seat pose (local frame), per target avatar:
```
N = avatarCount; step = 360f / N
rel = (SeatIndexForClient(targetId) - SeatIndexForClient(localId) + N) % N
ang = frontAngle + rel * step                       // frontAngle = 0 along ring-center forward
dir = Quaternion.AngleAxis(ang, Vector3.up) * center.forward
pos = center.position + dir * radius
rot = Quaternion.LookRotation(center.position - pos) // face the table
```
Local player → rel 0 → front spot, every client. The presenter applies the networked relative yaw on top of `rot` (around `Vector3.up`) for the head/EyePivot.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change -- expected: zero compile errors before proceeding.
- `mcp__UnityMCP__run_tests` (EditMode, Avatars filter) -- expected: new `GetSeatPose` geometry/invariance tests green; updated replication test green.
- `mcp__UnityMCP__run_tests` (PlayMode, Avatars) -- expected: arbiter/embodied camera mode tests still green.

**Manual checks:**
- 3+ client session: each player sees itself at the front spot; relative neighbours match; turning to look at a player is visible to that player; no body jitter on Vote enter/exit.

## Suggested Review Order

**Geometry — the gaze-preserving core**

- Entry point: pure ring math (per-client rotation, equidistant, front spot); read this to grasp the whole design.
  [`SeatRingGeometry.cs:46`](../../Assets/Scripts/Avatars/SeatRingGeometry.cs#L46)
- The invariance that makes per-client rotation safe for gaze.
  [`SeatRingGeometryTests.cs:1`](../../Assets/Scripts/Tests/Editor/SeatRingGeometryTests.cs#L1)

**Manager API — wiring data into geometry**

- Local-frame seat pose by relative offset; null/zero guarded.
  [`AvatarManager.cs:306`](../../Assets/Scripts/Avatars/AvatarManager.cs#L306)

**Per-client placement + de-networking (highest risk)**

- Places all bodies locally + interpolates heads each frame.
  [`AvatarSeatingPresenter.cs:81`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L81)
- Head interpolation (smooth gaze despite sparse publish).
  [`AvatarSeatingPresenter.cs:125`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L125)
- Suppress NetworkTransform + CharacterController, cache pose.
  [`AvatarSeatingPresenter.cs:131`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L131)
- Restore-pose-then-re-enable ordering (avoids body pile-up).
  [`AvatarSeatingPresenter.cs:166`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L166)
- Teardown safety: restore if torn down while active.
  [`AvatarSeatingPresenter.cs:72`](../../Assets/Scripts/Avatars/AvatarSeatingPresenter.cs#L72)

**Gaze — networked look (owner-write; needs ratification)**

- Owner-write yaw+pitch NetworkVariables + publish helper.
  [`PlayerAvatar.cs:45`](../../Assets/Scripts/Avatars/PlayerAvatar.cs#L45)
- Publish on change only (bandwidth) + first-frame stale clear.
  [`AvatarEmbodiedCamera.cs:198`](../../Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs#L198)
- Camera consumes the seat pose for the front-spot first-person view.
  [`AvatarEmbodiedCamera.cs:205`](../../Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs#L205)

**Mode routing**

- Presenter on/off with the Embodied Vote; old SeatAtSeat route removed.
  [`AvatarCameraArbiter.cs:117`](../../Assets/Scripts/Avatars/AvatarCameraArbiter.cs#L117)
