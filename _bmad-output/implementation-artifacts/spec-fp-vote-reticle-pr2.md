---
title: 'FP Vote reticle (PR2): hover lift+turn, jitter-proof static envelope, votable button'
type: 'feature'
created: '2026-06-21'
status: 'done'
context: ['{project-root}/_bmad-output/project-context.md']
baseline_commit: '7a32fba'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** PR1 gave the first-person Vote a center reticle that hovers cards and dispatches clicks, but a seated player still can't comfortably READ a card lying flat far across the table, and the on-card vote button isn't a reticle target. We also must add the dramatic hover and make it JITTER-PROOF (the lift must not walk the card out from under the reticle in an infinite loop) and CLIP-PROOF (the card must not pass through the table).

**Approach (Sally, UX — PR2 of 2):** On hover the card **hinges up at its FAR table-edge** — rise for clearance, then rotate to a FIXED readable tilt (~60°) facing the seated camera — so the near edge rises toward the player and nothing dips below the table. Jitter is killed STRUCTURALLY: hover-DETECTION geometry lives on the card ROOT (never on the animated "Hover" compositor layer) as a **static two-phase envelope** — a small resting footprint to ENTER, an expanded latch box spanning rest→lifted to STAY — so the raycast target never moves and the lifted card can't oscillate. The on-card vote button gets a collider and sits lower-center on the tilted face so the reticle can click it; the Skip control becomes a world-space reticle target.

## Boundaries & Constraints

**Always:**
- Hover-DETECTION geometry is rigidly parented to the card ROOT and STATIC; the lift/turn animates ONLY the "Hover" compositor layer.
- ENTER on the resting footprint; STAY while the ray is anywhere in the latch envelope (footprint + lifted/tilted bounds + the card's child graphics incl. the vote button); EXIT only when the ray leaves the envelope, via the existing `CanUnZoomCard()` gate + PR1's exit dwell.
- Hinge pivot = the card's FAR table-edge (Hover layer pivot positioned there); rise THEN rotate; fixed ~60° tilt computed once on enter toward the camera (not a per-frame billboard). Nothing crosses the table plane.
- Suppress the hover-lift while the "Flip" layer is animating; resume after.
- Reuse the existing DOTween compositor layers + `IPointer*` flow; the reticle/dispatch from PR1 is unchanged. Presentation only — no game-state/networking/vote-rule changes. FMOD for cues.

**Ask First:**
- Exact tilt angle, lift height, hinge pivot offset, envelope box size, button placement/size — FEEL/geometry to eyeball in the editor against the real seated camera. Confirm if the readable angle should differ for far vs near cards.

**Never:**
- Do not animate the detection geometry; do not make detection depend on the lifted (moving) visual.
- No billboard chase; no table clipping; no change to vote rules.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected | Error Handling |
|----------|--------------|----------|----------------|
| Hover | ray on resting footprint | Card hinges up to readable tilt facing player; no table clip | — |
| Aim lifted button | ray on lifted card/button (in air) | Inside latch envelope → stays hovered; button clickable | — |
| Leave | ray exits envelope | After dwell + `CanUnZoomCard`, card returns to rest | — |
| Mid-flip | "Flip" layer active | Hover-lift suppressed until flip done | no layer fight |
| Two adjacent | ray on the seam | Latch keeps current; switch via PR1 debounce | no strobe |
| Own card | hover self | Lifts to read; not votable (existing) | denied SFX |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs` -- replace flat `Hover`/`UnHover` (DOScale + DOLocalMoveY) with the far-edge HINGE: rise then `DOLocalRotate` to the fixed readable tilt toward the camera, on the "Hover" layer; reverse on unhover.
- `Assets/Scripts/Board/Card.cs` -- gate `OnPointerEnter` hover when the "Flip" layer is animating (suppress-during-flip); `CanUnZoomCard()` already gates exit.
- `Assets/Prefabs/Card.prefab` -- (EDITOR geometry) position the "Hover" layer pivot at the FAR table-edge; add a STATIC root-parented hover-detection envelope (resting footprint + expanded latch box spanning rest→lifted), NOT under the Hover layer; add a BoxCollider to the vote `CustomButton` sized to its rect; place the button lower-center on the tilted face.
- `Assets/Scripts/UI/StateUI/VoteStateUI.cs` + scene -- Skip becomes a world-space reticle-targetable button (collider + reuse handler), off the card cluster.
- `Assets/Scripts/Reticle/ReticleInteractor.cs` -- unchanged (already dispatches to whatever the static envelope/button resolves to).

## Tasks & Acceptance

**Execution:**
- [ ] `CardPlayerAnimation.cs` -- far-edge hinge lift+turn on the "Hover" layer (rise → rotate to fixed tilt toward `Camera.main`); reverse on unhover; tunable consts.
- [ ] `Card.cs` -- suppress hover-lift while "Flip" layer animates.
- [ ] `Card.prefab` (editor) -- Hover-layer pivot at far edge; static footprint + latch envelope collider on root; vote-button collider + lower-center placement on tilted face.
- [ ] `VoteStateUI.cs` + scene (editor) -- world-space Skip button targetable by the reticle.
- [ ] Tests -- EditMode for any extracted pure geometry (tilt/pivot math, envelope membership) where feasible.

**Acceptance Criteria:**
- Given Embodied Vote, when the reticle crosses a card's resting footprint, then the card hinges up to a fixed readable tilt facing the player with no part passing through the table.
- Given a hovered (lifted) card, when the player aims the lifted card or its vote button, then it stays hovered (ray inside the static envelope) and never oscillates; the vote button clicks via the reticle.
- Given the reticle on the world Skip button, when confirmed, then the existing skip flow runs.
- Given a card mid-flip, then hover-lift is suppressed until the flip completes.

## Implemented (revised, Poyo-narrowed 2026-06-21)

Poyo dropped the table-edge hinge (the card sits far from the table edge) and narrowed the task to "just tilt the card so the player sees its face a minimum"; the vote-button collider was done by Poyo separately. Sally's revised clean + MCP-feasible solution (no pivot rig, no new GameObject) was implemented in `CardPlayerAnimation`:
- On hover, the "Hover" compositor layer pitches about its OWN local X (`HOVER_PITCH_X = -40°`) so the face tips toward the seated player, lifting first (`HOVER_DISPLACEMENT_Y = 0.4`) on the SAME eased tween so the lower edge never sinks through the table. Reversed on unhover. Scale 1.15 kept.
- Fixed angle (not per-card billboard): a clamped seated camera reads a fixed raise universally; trivially tunable consts (flip the sign if it tips the wrong way; raise the lift for a taller card).
- Jitter stays impossible: the reticle raycasts the STATIC detection collider on the card root; only the Hover layer tilts — detection never moves.
- The full far-edge hinge + static latch envelope from the frozen section is superseded by this simpler approach; EditMode 256/256 green.

## Design Notes

No-jitter proof (Sally): detection geometry is on the card ROOT (never the animated Hover layer); the latch box already encloses BOTH rest and lifted/tilted poses, so once entered, looking anywhere on the lifted card keeps the ray inside → EXIT can't fire from the lift. Small-footprint-to-enter / large-envelope-to-stay hysteresis means no pose where "hovered" moves the target off the volume that sustains it. Far-edge hinge: rotating about the far edge raises the near edge toward the player; the far edge stays planted on the table → no downward sweep. Much of PR2 is editor geometry (pivot, envelope, button) to eyeball against the real seated camera.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change -- zero compile errors.
- `mcp__UnityMCP__run_tests` (EditMode/PlayMode, Avatars + Reticle) -- existing suites stay green.

**Manual checks:**
- Seated Vote: cards hinge up readable with no table clip; aiming the lifted card/button never oscillates; vote + skip via world buttons record; own card not votable.
