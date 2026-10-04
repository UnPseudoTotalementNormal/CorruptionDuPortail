---
title: 'Legless cat hop locomotion (arc + squash + up-beat pitch)'
type: 'feature'
created: '2026-06-21'
status: 'done'
baseline_commit: '8ebdac5'
context:
  - '{project-root}/_bmad-output/project-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-cat-avatar-idle-motion.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The cat model has NO LEGS (robe/cone base, no feet) and there is no walk clip — so when it moves in the free-roam Lobby it just slides/skates across the floor with zero sense of locomotion.

**Approach:** A LOCAL, deterministic `AvatarHopMotion` drives the cat's VISUAL ROOT (the `CatVisual` transform it sits on) in a hop cycle — a vertical arc + squash & stretch + a small forward pitch on the up-beat — paced by DISTANCE TRAVELLED of the networked root (so cadence scales with speed and the body rests flat on the ground when stopped). Everything is derived from the already-replicated root position → every client sees the same hop with zero new netcode. It writes ONLY the visual-root transform (single writer); idle bone motion and head-look compose on top, untouched.

## Boundaries & Constraints

**Always:**
- Drive ONLY the cat visual-root's OWN transform — the GameObject this component sits on (`CatVisual` / `Cat_Avatar` root): `localPosition.y` (arc), `localScale` (squash/stretch), `localRotation` (up-beat forward pitch). SINGLE-WRITER: never the spine/bones (idle owns those), never the networked avatar root (movement owns that).
- Pace by the PLANAR distance travelled of the networked root (`GetComponentInParent<PlayerAvatar>().transform`): `phase += planarDist / strideLength`. Cadence scales with speed; phase FREEZES when still. Deterministic-local — read the replicated position only; NO new NetworkVariable/RPC.
- Ease the hop AMPLITUDE toward 0 when ~stopped so the cat rests FLAT on the ground (no floating mid-arc); arc is a half-sine clamped ≥ 0 (never dips below the floor).
- Execution order 160 (after idle 150, before head-look 200) so head-look still re-aims the head on the pitched body. All feel values `[SerializeField]`, Poyo-tuned, SUBTLE. Live on the model prefab; `GetComponentInParent<PlayerAvatar>` for the root; no-op-safe standalone.

**Ask First:**
- Adding ANY networked state (this is meant to be fully derived from the replicated transform).
- A resting idle-hop while standing still (this pass rests flat when stopped) — separate feel decision.

**Never:**
- No legs / stride / foot IK (no leg bones), no walk-clip authoring.
- No landing FMOD this pass (content-gated: needs an authored FMOD event + a positional one-shot — deferred), no accel-lean / turn-bank / bow-jolt (polish wave, deferred).
- No driving the `EyePivot`/camera (no first-person bob), no `AudioSource`, no touching idle bones or the head bone.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Moving | Avatar travels | Visual root rises/falls in a half-sine arc; cadence ∝ planar speed; squash at ground, stretch at apex; small forward pitch on the up-beat | N/A |
| Stopped | speed ≈ 0 | Amplitude eases to 0, body rests FLAT on the ground; phase frozen (no jump on resume) | N/A |
| Fast vs slow | High vs low speed | More vs fewer hops per metre is constant; hops-per-second scales with speed | N/A |
| Remote replica | Replicated position moves, no local input | Hops in sync, derived from the replicated position | N/A |
| Standalone (no PlayerAvatar) | Model used without the networked parent | Uses its own transform as the root ref; no exception | No-op-safe |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarHopMotion.cs` -- NEW. Exec 160; distance-paced hop on the visual-root transform (arc + squash + up-beat pitch + amplitude ease).
- `Assets/Scripts/Avatars/AvatarHopMath.cs` -- NEW. Pure helpers (`Arc(phase)` half-sine≥0, squash/stretch factors, framerate-independent amplitude ease) — EditMode-testable.
- `Assets/Scripts/Avatars/AvatarIdleMotion.cs` -- reference: owns spine/ears/whiskers/bow bones; hop must NOT write those (drives the parent visual-root transform instead).
- `Assets/Prefabs/Avatars/Cat_Avatar.prefab` -- add `AvatarHopMotion` (only the tuning fields; no wiring — root resolved via parent).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Avatars/AvatarHopMath.cs` -- pure: `Arc(phase)` (half-sine ≥0), `SquashStretch(phase)` (±1 ground/apex), `EaseAmplitude(current, target, k, dt)`.
- [x] `Assets/Scripts/Avatars/AvatarHopMotion.cs` -- component: networked-root planar distance → phase (teleport-guarded); writes visual-root `localPosition.y` (arc × amplitude), `localScale` (squash/stretch × amplitude), `localRotation` (up-beat pitch × arc); base captured at Awake (accumulation-free); `[DefaultExecutionOrder(160)]`; amplitude eases to 0 (rest flat) when stopped.
- [x] `Assets/Prefabs/Avatars/Cat_Avatar.prefab` -- added `AvatarHopMotion` via Unity MCP (drives its own transform; root resolved via parent). 0 compile errors.
- [x] `Assets/Scripts/Tests/Editor/AvatarHopMathTests.cs` -- 4 EditMode tests: arc grounded/apex, arc never negative, squash/stretch ±1, amplitude ease in+out. 247/247 EditMode green.

**Acceptance Criteria:**
- Given the avatar moves, when it travels, then the visual root rises/falls in a half-sine arc whose cadence scales with planar speed, squashing at the ground and stretching at the apex.
- Given the avatar stops, when speed ≈ 0, then the hop amplitude eases to 0 and the cat rests flat on the ground (no floating mid-arc).
- Given a remote replica, when it moves, then it hops in sync derived from the replicated position, with no input read.
- Given head-look is active, when the body pitches on the up-beat, then the head still aims at the gaze target (head-look composes on top).

## Spec Change Log

- **2026-06-21 — review patches (no loopback; acceptance fully compliant).** Three reviewers (blind/edge/acceptance) found the frozen contract honored. Patches: (1) **teleport guard is now SPEED-based** (`>25 m/s`, not raw 2 m) so it catches a one-frame jump of ANY size — this stops the seated-Vote body from bobbing when the seating presenter snaps the root, and kills the spawn/respawn/ring-respread hop-pop. (2) **First-frame skip** (`_initialized`, re-armed in `OnEnable`) seeds `_prevRootPos` without a jolt. (3) **`OnDisable` restores** the base pose so a disabled hop never leaves the cat frozen mid-bounce. (4) **Squash clamped** (`Max(0.01, …)`) so a mis-tuned `_squash ≥ 1` can't invert the scale. (5) **Tuning defaults** `_strideLength 0.6→3`, `_hopHeight 0.15→0.1` (script + prefab) — 0.6 against the ~12 m/s move speed buzzed at ~20 hops/s. Head-look/idle composition and standalone no-op were confirmed handled.

## Design Notes

The visual root = `CatVisual` (the nested cat model under the networked `PlayerAvatar`); the component drives its OWN transform, so the bounce is purely cosmetic-local and never feeds the networked position or the bow-spring's angular-velocity guard. `EyePivot` is a child of the networked root (NOT of `CatVisual`), so the hop does not bob the first-person camera — only the third-person body bounces. Half-sine arc `max(0, sin(phase·π))` lands flat (never below floor). Amplitude eased by an `isMoving` factor (speed threshold) → a clean rest-on-ground stop that also absorbs Sally's "settle". Single-writer on the visual-root transform means idle (bones) and head-look (head bone) layer cleanly with no contention.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected: no compile errors.
- `mcp__UnityMCP__run_tests` (EditMode, filter `AvatarHopMath`) -- expected: all green.

**Manual checks:**
- Move an avatar: it hops, cadence scaling with speed; stop: it settles flat (no float); a second client sees the same hop; the head-look still reads while the body bounces.

## Suggested Review Order

**The hop cycle**

- Entry point — LateUpdate: distance→phase, speed-based teleport guard, amplitude ease, single-writer transform write.
  [`AvatarHopMotion.cs:84`](../../Assets/Scripts/Avatars/AvatarHopMotion.cs#L84)

- Teleport guard + first-frame skip (why the seated Vote body never bobs and spawns don't pop).
  [`AvatarHopMotion.cs:98`](../../Assets/Scripts/Avatars/AvatarHopMotion.cs#L98)

- Lifecycle restore — disable leaves the cat at its base pose, not mid-bounce.
  [`AvatarHopMotion.cs:73`](../../Assets/Scripts/Avatars/AvatarHopMotion.cs#L73)

**Pure math (unit-tested)**

- Half-sine arc + squash/stretch + amplitude ease.
  [`AvatarHopMath.cs:15`](../../Assets/Scripts/Avatars/AvatarHopMath.cs#L15)

**Supporting**

- EditMode golden (arc grounded/apex/never-negative, squash±1, ease in+out).
  [`AvatarHopMathTests.cs:14`](../../Assets/Scripts/Tests/Editor/AvatarHopMathTests.cs#L14)

- Prefab: `AvatarHopMotion` on the cat model (tuned `_strideLength 3`, `_hopHeight 0.1`).
  [`Cat_Avatar.prefab`](../../Assets/Prefabs/Avatars/Cat_Avatar.prefab)
