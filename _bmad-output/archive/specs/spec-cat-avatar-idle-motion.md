---
title: 'Cat avatar idle secondary motion (breathing, ears, whiskers, bow)'
type: 'feature'
created: '2026-06-21'
status: 'done'
baseline_commit: 'dbb3086'
context:
  - '{project-root}/_bmad-output/project-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-rigged-head-look-body-followthrough.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Outside the looping Wave clip and the new head-look, the cat is dead — a perfectly still rig reads as AFK/disconnected, which poisons the social reads the game lives on. It needs constant subtle life.

**Approach:** One LOCAL, cosmetic `AvatarIdleMotion` component (no networking — every client sims its own, phase-offset per avatar so a table of cats never moves in lockstep) that, each LateUpdate AFTER the Animator, layers procedural secondary motion onto the rig: gentle **breathing** on the spine, low-amplitude **ear** idle twitch/sway, subtle **whisker** quiver, and a **bow** spring follow-through driven by the avatar's angular velocity (which sells the head-turn from the head-look feature). Goal-2 "ambient" scope: ears are idle-only, NOT gaze-reactive.

## Boundaries & Constraints

**Always:**
- LATE-bind the rig bones by NAME convention at Awake (`Spine1/2/3`, `Ear*`, `Whisker_*`, `Bow_*`, `Bell`) via `GetComponentsInChildren` — NO per-bone Inspector wiring (avoids the fragile serialized-array pitfalls). Null-tolerant: a missing group is skipped + warned once.
- Run in `LateUpdate` (after Animator) ADDITIVELY over the clip — perturb each bone's freshly-animated `localRotation` (and breathing may add a tiny chest scale), never hard-overwrite a bone's whole pose. Execution order < `AvatarHeadLook` (200) so the head-look still wins on the head bone — use `[DefaultExecutionOrder(150)]` (after `AvatarSeatingPresenter` 100).
- Purely cosmetic & LOCAL (NFR3): no `NetworkVariable`, no RPC, no game state. Each client runs the sim independently.
- ANTI-SYNC: seed every looping phase from a per-avatar hash (`GetInstanceID`) so breathing/ears/whiskers do not beat in unison across avatars.
- PIVOT CORRECTNESS (Poyo): rotate each appendage about its OWN joint origin. Drive the ROOT/base bone of each chain (`Ear1.*`, the first `Whisker_*_01.*`, the first `Bow_*_01.*`, `Spine*`) so the rotation pivots where the part attaches — never a mid/tip bone, and never about the avatar root or world origin. Use `localRotation` (pivots at the bone's local origin). PREFER rotation over scale; if breathing uses any chest scale, keep it tiny and on a bone whose children won't visibly distort (else use a small spine pitch instead). When in doubt, rotate the base joint only.
- All amplitudes / speeds are `[SerializeField]`, Poyo-tuned placeholders. Motion must stay SUBTLE (degrees, not flailing).

**Ask First:**
- Adding ANY networked/replicated state (the whole feature is meant to be local-only — flag if a sub-motion seems to need replication).
- A distance/LOD cull (skip whiskers/bow for far avatars) — only if profiling shows the per-bone work matters at ~10 avatars.

**Never:**
- No blink (the model has 0 blendshapes / no eyelid mesh — art-gated, stays deferred) and no tail (no tail bone exists).
- No gaze-reactive ears (Poyo chose ambient-only for this pass), no FMOD/audio (separate deferred idea), no `AudioSource`.
- Do NOT touch the head bone (owned by `AvatarHeadLook`) or the Animator/Wave clip; no per-frame network writes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Idle breathing | Avatar standing, clip playing | Spine rises/falls subtly, layered over the clip | N/A |
| Many avatars | ~10 visible | Each breathes/twitches on its own phase (per-instance seed) — no lockstep | N/A |
| Bow follow-through | Avatar turns head/body fast | Bow lags then settles (spring), beat behind the motion | N/A |
| Bow at rest | No angular velocity | Bow rests at its neutral pose (spring converges to 0) | N/A |
| Missing bone group | A name group not found | That sub-motion is skipped | Warn once, no NRE |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarIdleMotion.cs` -- NEW. The LateUpdate idle layer; auto-finds bone groups, applies breathing/ears/whiskers/bow.
- `Assets/Scripts/Avatars/AvatarIdleMath.cs` -- NEW. Pure helpers (bounded sine offset, damped-spring step, per-seed phase) — EditMode-testable.
- `Assets/Scripts/Avatars/AvatarHeadLook.cs` -- reference only: order 200 must stay > AvatarIdleMotion's 150 (head wins on the head bone).
- `Assets/Prefabs/Avatars/Cat_Avatar.prefab` -- add `AvatarIdleMotion` (auto-find means no bone wiring; only the tuning fields).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Avatars/AvatarIdleMath.cs` -- pure helpers: `PhaseSeed(instanceId)` (deterministic, spread) + `DampedSpring(current, ref velocity, target, stiffness, damping, dt)`. (Breath/whisker offsets are plain `Mathf.Sin` inline — no helper needed.)
- [x] `Assets/Scripts/Avatars/AvatarIdleMotion.cs` -- component: name-based bone discovery at Awake (Spine*/Ear1.*/Whisker_*_01.*/Bow_*_01.*+Bell); LateUpdate applies the four ADDITIVE sub-motions (post-multiply the base bone's localRotation → pivots at the joint), phase-offset per instance; `[DefaultExecutionOrder(150)]`; warn-once on missing groups.
- [x] `Assets/Prefabs/Avatars/Cat_Avatar.prefab` -- added `AvatarIdleMotion` via Unity MCP (auto-find → no bone wiring). Runtime group-found check deferred to the manual playtest (edit-mode Awake doesn't run).
- [x] `Assets/Scripts/Tests/Editor/AvatarIdleMathTests.cs` -- 4 EditMode tests: phase seed deterministic + in range + differs across ids; spring converges; perturbed spring overshoots-then-settles. 243/243 EditMode green.

**Acceptance Criteria:**
- Given an idle avatar, when the clip plays, then the spine breathes subtly on top of the animation (no fight, no pop).
- Given ~10 avatars, when idle, then their breathing/ear phases are visibly out of sync (per-instance seed).
- Given the avatar turns sharply, when it stops, then the bow lags and settles to neutral (spring), not instantly.
- Given a bone group is absent, then that sub-motion is skipped, logged once, and the rest still run (no exception).

## Spec Change Log

- **2026-06-21 — review patches (no loopback; intent held).** Three adversarial reviewers passed the acceptance criteria/boundaries. Patches: (1) **REST-RELATIVE application** — the idle bones (ears/whiskers/bow/spine) are NOT keyed by the gameplay clips, so a per-frame `localRotation *= Euler(...)` would COMPOUND and wind them to garbage; capture each bone's rest at Awake and write `rest * Euler(offset)` (accumulation-free; still pivots at the joint; for these clip-untouched bones it IS the intended additive idle). (2) **Bow whip guard** — a seat-snap (AvatarSeatingPresenter teleports the root yaw at order 100, before this at 150) injected a huge fake angular velocity; now ignore teleport-sized yaw jumps (>45°/frame), clamp the spring `dt` and velocity. (3) **Bell removed** from the bow group — it is outside the frozen base-bone set (`Bow_*_01.*`); a bell channel is a future renegotiation. (4) **Ear Perlin decorrelated** (distinct axis coords + stronger L/R offset). (5) **Doc** corrected re head-look/breath inheritance.

## Design Notes

All motion is ADDITIVE rest-relative on the BASE bone of each chain so it pivots at the joint: `baseBone.localRotation = restRotation * Quaternion.Euler(offset)` (rest captured at Awake; post-multiply → the offset is in the bone's local frame, pivoting at the bone origin). Rest-relative (not `*=`) because these bones are clip-untouched, so a frame-to-frame multiply would accumulate. `offset` is small (sub-~10°). Driving the base bone makes the whole appendage swing from where it attaches (ear from the skull, whisker from the cheek, bow from the knot); never perturb a mid-chain bone (kinks the part) or compose the rotation in world/root space (pivots off-origin). Breathing drives one phase; whiskers reuse it (scaled) so they quiver with the breath. Ears get their own slow Perlin/sine wander plus a rare flick. The bow is a damped spring per bow-chain bone, its target = rest, perturbed by the root's frame-to-frame angular velocity — so a head/body turn (from the head-look feature) throws the bow and it trails back. Per-instance phase seed = `AvatarIdleMath.PhaseSeed(GetInstanceID())` keeps 10 cats desynced.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected: no compile errors; no "bone group missing" warnings on the wired prefab.
- `mcp__UnityMCP__run_tests` (EditMode, filter `AvatarIdleMath`) -- expected: all green.

**Manual checks:**
- Enter Play with several avatars: each breathes/twitches on its own beat; turning the head makes the bow trail and settle; the head-look still reads correctly (idle does not override the head aim).

## Suggested Review Order

**Idle layer (the four sub-motions)**

- Entry point — LateUpdate: rest-relative breathing/whiskers/ears + the guarded bow spring, all pivoting at the joint.
  [`AvatarIdleMotion.cs:104`](../../Assets/Scripts/Avatars/AvatarIdleMotion.cs#L104)

- Bone discovery + REST capture (name convention; accumulation-free base for the idle).
  [`AvatarIdleMotion.cs:74`](../../Assets/Scripts/Avatars/AvatarIdleMotion.cs#L74)

- Bow spring robustness — teleport (seat-snap) guard + dt/velocity clamp (the riskiest bit).
  [`AvatarIdleMotion.cs:139`](../../Assets/Scripts/Avatars/AvatarIdleMotion.cs#L139)

**Pure math (unit-tested)**

- Damped spring (bow follow-through) + per-instance phase seed (anti-lockstep).
  [`AvatarIdleMath.cs:27`](../../Assets/Scripts/Avatars/AvatarIdleMath.cs#L27)

**Supporting**

- EditMode golden (spring converges / overshoots-then-settles; seed deterministic + spread).
  [`AvatarIdleMathTests.cs:49`](../../Assets/Scripts/Tests/Editor/AvatarIdleMathTests.cs#L49)

- Prefab: `AvatarIdleMotion` added to the cat model (auto-find → no bone wiring).
  [`Cat_Avatar.prefab`](../../Assets/Prefabs/Avatars/Cat_Avatar.prefab)
