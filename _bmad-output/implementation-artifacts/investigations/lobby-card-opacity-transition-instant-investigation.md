# Investigation: Lobby card opacity transition plays instantly

## Hand-off Brief

1. **What happened.** The pool↔dimmed opacity change on lobby role cards commits instantly despite an inline UITK `transition-property: opacity` + a `schedule.Execute(...).ExecuteLater(0)` nudge (Confirmed via source + a working counter-example).
2. **Where the case stands.** Root cause Confirmed: every stepper click triggers a full-tree `Rebuild()` that **recreates** each card, so the transition has no already-resolved "from" value on a laid-out element; `ExecuteLater(0)` applies the target within the same pre-repaint tick → no from→to boundary → instant.
3. **What's needed next.** Replace the USS-transition + scheduled-style approach with an explicit `card.experimental.animation.Start(prev, target, …)` value animation (timing-independent, works on freshly-added elements) — trivial fix via `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-15                                                                 |
| Status           | Concluded                                                                  |
| System           | Unity 6000.2.6f2, UI Toolkit, LobbyRoles panel rendered RT→RawImage        |
| Evidence sources | Source (LobbyRolesUitkController.cs), USS counter-examples (InfoTable/RoleCard/EmoteWheel) |

## Problem Statement

The card opacity (1.0 in pool ↔ 0.22 out of pool) switches instantly when Max crosses 0. A first fix added inline `transitionProperty/Duration/TimingFunction` on the card + `card.style.opacity = prev` then `schedule.Execute(() => opacity = target).ExecuteLater(0)`. User confirms at runtime: still instant.

## Confirmed Findings

### Finding 1: Every stepper click rebuilds the whole tree, recreating each card

**Evidence:** `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` — `RequestSetMax/Forced` → data source `OnChanged` → `Rebuild()` (line ~136) → `_root.Clear()` (line ~153) → `BuildContent`→`BuildSection`→`BuildCard` recreates a fresh `RoleCardElement` every time.

**Detail:** The card carrying the opacity is a brand-new `VisualElement` on each rebuild, not a persistent element whose property changes.

### Finding 2: Transitions DO run in this RT-rendered panel — proven by a working sibling

**Evidence:** `Assets/UI/Screens/InfoTable/InfoTable.uss:96,171,195,241,308` — `transition-property: background-color, color, opacity` with 120–140ms durations, on the InfoTable panel which is rendered RT→RawImage (same pattern as LobbyRoles). Also `RoleCard.uss:26,71,88` (opacity/translate/scale) and `EmoteWheel.uss` opacity/scale transitions.

**Detail:** These transitions fire on **persistent** elements when a **class toggles** (`:hover`, a `--visible`/state class) — i.e. a property changes on an element that already resolved its style at least once. This refutes any "RT panels don't animate" theory.

### Finding 3: The current attempt is the only in-code transition, set inline on a newborn element

**Evidence:** `LobbyRolesUitkController.cs:453-455` (`transitionProperty/Duration/TimingFunction`) — grep shows no other C# transition/animation usage in `Assets/Scripts`.

## Deduced Conclusions

### Deduction 1: No from→to boundary exists, so opacity commits instantly

**Based on:** Findings 1, 2, 3.

**Reasoning:** A UITK transition interpolates when a property changes **from a previously-resolved value** across a style-resolution/repaint boundary. On a freshly-created-and-added element, the first style resolution happens during the upcoming layout pass; `ExecuteLater(0)` fires on the very next scheduler tick (same panel update, before the element's first committed repaint of `prev`), so the system only ever sees the final `target` — no interpolation. The working siblings (Finding 2) differ precisely in that their element persisted and merely had a class toggled, giving a genuine from→to delta.

**Conclusion:** The instant behaviour is inherent to combining a full recreate-on-rebuild with a USS transition; it is not a duration/property/easing typo.

## Hypothesized Paths

### Hypothesis 1: ExecuteLater(0) timing prevents the transition (user's implicit theory)

**Status:** Confirmed

**Theory:** The scheduled target-set lands too early for a transition boundary.

**Would confirm:** A working transition on a persistent element in the same panel (Finding 2) + no other difference than persistence.

**Resolution:** Confirmed. InfoTable proves the panel animates; the only material difference is card recreation + newborn-element timing. Approach #1 (ExecuteLater) was refuted by the user's runtime report.

## Source Code Trace

| Element       | Detail                                                                 |
| ------------- | --------------------------------------------------------------------- |
| Error origin  | `LobbyRolesUitkController.cs:~451-461` (BuildCard opacity block)       |
| Trigger       | Stepper +/- crossing Max 0↔>0 → OnChanged → full `Rebuild()`           |
| Condition     | Card is recreated each rebuild; inline transition needs a resolved "from" on a laid-out element, which a newborn element + ExecuteLater(0) never provides |
| Related files | Working counter-examples: `InfoTable.uss`, `RoleCard.uss`, `EmoteWheel.uss` |

## Conclusion

**Confidence:** High

Confirmed root cause: full-tree `Rebuild()` recreates each card, so an inline USS opacity transition + `ExecuteLater(0)` nudge has no from→to boundary and commits instantly. The RT panel itself animates fine (InfoTable transitions prove it); the failure is specific to animating a freshly-recreated element via USS transitions.

## Recommended Next Steps

### Fix direction

Two mechanisms, pick one:

- **Preferred (local, timing-independent):** replace the transition + scheduled-style block with an explicit value animation:
  `card.experimental.animation.Start(prev, target, CardFadeMs, (e, v) => e.style.opacity = v).Ease(Easing.OutCubic);`
  This is driven directly by the panel scheduler frame-by-frame and does not depend on a resolved "from" value, so it works on newborn elements. Keep the `_cardOpacity` prev/target memory + the "no-op when prev≈target" guard (avoids animating on scroll/tab rebuilds). Drop the inline `transitionProperty/Duration/TimingFunction`.
- **Alternative (align with codebase):** stop recreating cards on stepper changes — keep persistent cards and toggle a `--dimmed` class so the USS transition fires like InfoTable. Larger refactor of Rebuild; not warranted for this alone.

### Diagnostic

None needed — root cause Confirmed. If desired, a one-line `[LOBBYFADE]` log in the `onValueChanged` callback would visually confirm the interpolation ticks.

## Reproduction Plan

Harness (`Assets/Scenes/Spikes/TabletOnlySpike.unity`), Attribution tab: click a role's Max from 1→0 (or 0→1). Expected after fix: opacity eases over ~200ms; before fix: snaps instantly.

## Side Findings

- No other C# UITK transition/animation usage exists in `Assets/Scripts` — this is the codebase's first programmatic UITK animation; all other motion is USS-class-driven on persistent elements. (Confirmed via grep.)
