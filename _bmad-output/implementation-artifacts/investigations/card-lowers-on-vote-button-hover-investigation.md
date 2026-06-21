# Investigation: card lowers when the reticle reaches the "Voter" button

## Hand-off Brief
The card un-hovers (drops back toward rest) the moment the center reticle moves from the card body onto its own world "Voter" button. Confirmed root cause: `ReticleInteractor` tracks a SINGLE exclusive hover target and resolves the vote `CustomButton` (UI-first) as a target DISTINCT from the card, so switching to the button dispatches `OnPointerExit` to the card → `CardPlayerAnimation.UnHover` lowers it. The button is a child of the card; the interactor has no parent-stays-hovered-over-child notion. Confidence: **High**.

## Case Info
- Slug: card-lowers-on-vote-button-hover
- Reported: 2026-06-21 by Poyo (3 screenshots: raised/tilted card, lowers a bit when aiming the "Voter" button). The small coloured square in-shot is the reticle dot (over-state) → this is the FPS reticle path, look driven by the mouse.
- Area: first-person Vote reticle (recently added this session).

## Problem Statement
"Quand je passe ma souris sur le bouton Voter, ça baisse la carte un peu, pourquoi ?" — hovering a card raises+tilts it (look-at-camera hover); moving the reticle down onto the on-card "Voter" button makes the card sink.

## Evidence Inventory
| Item | Status | Cite |
|---|---|---|
| Reticle resolves UI (button) BEFORE the card collider | Confirmed | `Assets/Scripts/Reticle/ReticleInteractor.cs:120` (UI-first), `:148-156` (RaycastAll→button handler) |
| On target switch the interactor exits the OLD target | Confirmed | `Assets/Scripts/Reticle/ReticleInteractor.cs:101` `DispatchExit(_currentHandler)` then `:105` enter new |
| Card OnPointerExit lowers the card | Confirmed | `Assets/Scripts/Board/Card.cs:362-368` → `animationHandler.OnUnHover` |
| Unhover tweens rotation+lift back to 0 | Confirmed | `Assets/Scripts/Board/CardComponents/CardPlayerAnimation.cs` `UnHover` (DOLocalMoveY 0 + slerp to identity) |
| Button resolves as its own handler (not the card) | Confirmed | `ExecuteEvents.GetEventHandler<IPointerEnterHandler>` returns the nearest handler = the `CustomButton` itself (`UI/CustomButton.cs:16`), ≠ the Card |

## Source Code Trace
1. Reticle on the card body → `ResolveHandlerUnderReticle` physics-hits the card collider → handler = Card → `OnPointerEnter` → `CardPlayerAnimation.Hover` raises+tilts (look-at). `_currentHandler = Card`.
2. Reticle moves onto the "Voter" button → `ResolveWorldUiHandler` (UI-first, `ReticleInteractor.cs:120,143-159`) returns the `CustomButton`. New target ≠ Card.
3. `ReticleHover.Tick` reports `Exited = Card`, `Entered = Button`. Interactor runs `DispatchExit(_currentHandler=Card)` (`ReticleInteractor.cs:101`) → `Card.OnPointerExit` (`Card.cs:362`) → `CanUnZoomCard()` true → `OnUnHover` → **card lowers**. Then enters the button.

**Root cause (Confirmed):** the interactor's hover model is a single mutually-exclusive target. The vote button is a CHILD of the card but resolves as a separate handler, so aiming it counts as LEAVING the card → the card un-hovers. There is no "parent stays hovered while a child is the active target" rule.

## Hypotheses
- **#1 (user premise: "the button hover lowers the card")** — Status: **Confirmed** (mechanically: switching target to the button triggers the card's exit). Refutation attempt: could it be the FPS lift recomputing? No — `Hover`/lift only runs on enter, not per-frame; the drop is the `UnHover` from the exit. Refuted that alternative.

## Final Conclusion
**Confidence: High.** Aiming the on-card "Voter" button makes the reticle's single hover target switch from the card to the button, which dispatches `OnPointerExit` to the card and runs `UnHover` (lowers it). The button being a child of the card is not respected by the single-target interactor.

## Fix direction (out of scope here — `gds-quick-dev`)
Keep the parent CARD hovered while the reticle is on a DESCENDANT target (its button). Options, by mechanism:
- **Hierarchy-aware switch:** when the new resolved handler's transform is a child of the current handler (or both share the Card root), suppress the card `OnPointerExit` — keep the card "entered", and treat the button only as the click/sub-hover target.
- **Two-channel resolve:** resolve HOVER at the card level (walk up to the `Card`) and resolve CLICK separately at the topmost UI under the reticle (the button), so hovering the button never drops the card.
- **Cheapest:** before exiting `_currentHandler`, skip the exit if `newTarget.transform.IsChildOf(_currentHandler.transform)`.

## Reproduction
Embodied Vote, reticle on a card (it raises+tilts), then aim the on-card "Voter" button → the card sinks back toward rest. Deterministic.

## Status: Concluded (root cause Confirmed).
