---
title: 'First-person Vote reticle (PR1): center-dot targeting restores card/button voting'
type: 'feature'
created: '2026-06-21'
status: 'done'
context: ['{project-root}/_bmad-output/project-context.md']
baseline_commit: '4881c81'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Vote phase is now a first-person SEATED camera (`AvatarEmbodiedCamera`, OS cursor locked + hidden). Players vote by looking at other players' world-space cards on the table, but there is NO cursor and NO raycaster feeding the cards'/buttons' existing `IPointer*` handlers — so hovering a card and clicking its world vote button (or skipping) is currently impossible in first-person.

**Approach (designed with Sally, UX — PR1 of 2):** Add a **center-screen reticle** that raycasts from the camera forward each frame and dispatches `OnPointerEnter/Exit/Click` to the EXISTING `IPointer*` handlers on `Card`, the on-card vote `CustomButton`, and a reticle-targetable world Skip button — via `ExecuteEvents`, with NO changes to `Card.cs`/`CustomButton.cs` logic. This restores hovering + voting + skipping in first person, reusing the current hover/click animations and the server-authoritative `VoteState` flow as-is. (PR2 — deferred — replaces the flat hover with the dramatic far-edge lift+turn and adds the static two-phase anti-jitter envelope.)

## Boundaries & Constraints

**Always:**
- Reuse the existing `IPointer*` contract — synthesize a `PointerEventData` (position = screen center) and dispatch via `ExecuteEvents`. Do NOT rewrite `Card`/`CustomButton` handling; the existing hover (`CardPlayerAnimation`) + vote RPC flow fire through the dispatched events unchanged.
- One center-screen ray per frame from the embodied camera forward; resolve the hit to its `IPointer*` handler (`GetComponentInParent`). Confirm input → `OnPointerClick` on the current target.
- Reticle + its raycaster are active ONLY while Embodied (driven by the arbiter, like `_seatingPresenter` / `_visibility`); fully inactive in Board/FreeRoam.
- Targets are resolved by Physics colliders: the card's existing `Collider` child for hover; the vote + Skip buttons get matching box colliders so the same ray hits them.
- Server-authoritative vote/skip flow (`VoteState` RPCs) and all vote RULES (self-vote, re-vote, eliminated/chained gating) are UNTOUCHED. Audio = FMOD. Cursor stays locked/hidden.

**Ask First:**
- Confirm input binding: reuse the existing Look/Player map "Fire"/left-click action vs a dedicated Interact action. Confirm before adding a new input action.

**Never:**
- No free OS cursor during Vote. No `GraphicRaycaster`/event-camera plumbing on every card canvas if the uniform physics-collider + `ExecuteEvents` path suffices (keep it simple).
- No hover-lift redesign, no anti-jitter envelope, no table-clip fix here — that is PR2 (deferred). PR1 keeps the existing flat hover, whose detection collider is already static.
- Don't affect Board/other phases (NFR1) — reticle exists only while Embodied.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Hover a card | Embodied Vote, ray hits card collider | Existing hover fires (`OnPointerEnter` → lift/zoom); reticle over-state; FMOD tick | — |
| Move off | ray leaves card | `OnPointerExit` (after small dwell) → existing un-hover | — |
| Vote | reticle on vote button + confirm | `OnPointerClick` → existing `VoteState` vote RPC | invalid (eliminated/chained) → existing disabled handling, denied SFX |
| Skip | reticle on world Skip button + confirm | existing skip-vote flow | — |
| Own card (idx 0) | hover/confirm self | existing not-votable handling; no RPC | denied SFX |
| Two targets resolved | ray hits overlapping colliders | nearest hit wins; switch only after the new target is held briefly (dwell) | no strobe |
| Not Embodied | Board / FreeRoam | reticle + raycaster inactive; no dispatch | — |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarEmbodiedCamera.cs` -- first-person camera; source of the center-forward ray.
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` -- route the reticle active iff Embodied (mirror `_seatingPresenter`/`_visibility` wiring + Assert).
- `Assets/Scripts/Board/Card.cs` -- existing `IPointerEnter/Exit/Click` (reused, not modified).
- `Assets/Scripts/Board/UI/VoteCanvas/VoteCanvas.cs` + `Assets/Scripts/UI/CustomButton.cs` -- vote button reused as a reticle target (needs a collider sized to its rect).
- `Assets/Scripts/UI/StateUI/VoteStateUI.cs` -- the Skip control becomes a world-space, reticle-targetable button (collider + existing handler).
- `Assets/Prefabs/Card.prefab` -- add a box collider to the vote button hit area (card hover collider already exists, static).
- NEW `Assets/Scripts/Reticle/ReticleInteractor.cs` -- center-screen ray, target resolution + dwell, `ExecuteEvents` dispatch, confirm input. Active only while Embodied.
- NEW `Assets/Scripts/Reticle/ReticleHover.cs` (pure) -- per-frame target hysteresis (enter, exit-dwell, switch). EditMode-testable.
- NEW `Assets/Scripts/Reticle/ReticleHUD.cs` -- center dot + over-state; FMOD enter/confirm/denied cues (debounced).

## Tasks & Acceptance

**Execution:**
- [x] `ReticleHover.cs` (NEW, pure) -- hysteresis (exit-dwell + switch-debounce, accumulates from first sighting). EditMode-tested (5 cases).
- [x] `ReticleInteractor.cs` (NEW MonoBehaviour) -- each frame `Physics.Raycast` from camera center; resolve hit → `IPointer*` via `ExecuteEvents.GetEventHandler`; feed `ReticleHover`; dispatch `pointerEnter/Exit` and, on confirm input, `pointerClick` via `ExecuteEvents` (screen-center `PointerEventData`). Arbiter-driven `SetActive`; releases hover on deactivate.
- [x] `ReticleHUD.cs` (NEW) -- screen-center dot, idle/over states (scale+color); show/hide with active.
- [x] `AvatarCameraArbiter.cs` -- serialize + Assert `_reticle`; `SetActive(_currentMode == Embodied)` in `ApplyMode`.
- [x] `GameScene` -- added `ReticleInteractor` on the arbiter GO (wired `_camera`=CameraBrain, `_hud`); `ReticleCanvas`+`Dot`+`ReticleHUD` (dot raycastTarget off); arbiter `_reticle` wired (read-back verified, saved).
- [x] Tests -- EditMode `ReticleHoverTests` (5, green); arbiter PlayMode fixtures wired `_reticle` (3, green).
- [→ PR2] `Card.prefab`/`VoteCanvas` vote-button collider + world Skip button + comfortable aiming — moved to PR2: aiming a small flat button only becomes ergonomic once the card LIFTS, so the button-as-reticle-target belongs with the lift. Appended to `deferred-work.md`.
- [→ Poyo] Wire `_confirmAction` (InputActionReference: Fire/click/gamepad — Ask First) and optionally narrow `_targetMask` (currently all; non-handler hits already resolve to no-target). HUD dot sprite/scale polish.

**Acceptance Criteria:**
- Given Embodied Vote, when the reticle crosses a card collider, then the card's existing hover fires and the reticle shows its over-state.
- Given the reticle on the vote (or Skip) world button, when the player confirms, then the existing `VoteState` vote/skip flow runs unchanged (server records it).
- Given the reticle drifts between overlapping/adjacent targets, then there is no rapid enter/exit flicker (dwell + switch debounce).
- Given Board/FreeRoam, then no reticle targeting or dispatch occurs.
- Given EditMode tests, then `ReticleHover` proves stable enter/exit and correct target switching.

## Design Notes

`ExecuteEvents` reuse keeps `Card`/`CustomButton` untouched: the interactor is just a new raycast SOURCE feeding the same `IPointer*` API a GraphicRaycaster would. Pointer position is forced to screen center (the locked/hidden cursor has no position). PR1 deliberately keeps the existing flat hover (its detection collider is already static, so no jitter); the dramatic lift+turn and the static two-phase latch envelope that makes the bigger lift jitter-proof are PR2 (see `deferred-work.md`).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change -- expected: zero compile errors.
- `mcp__UnityMCP__run_tests` (EditMode) -- expected: `ReticleHover` tests green.
- `mcp__UnityMCP__run_tests` (PlayMode) -- expected: reticle-dispatch test green; existing Vote/Avatar tests unaffected.

**Manual checks:**
- Seated Vote, multi-card: the center dot hovers cards, voting via the on-card world button records, the world Skip button works, own card not votable, and nothing targets outside Embodied.
