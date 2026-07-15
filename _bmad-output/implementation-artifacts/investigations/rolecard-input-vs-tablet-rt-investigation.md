# Investigation — RoleCard overlay input stolen by the tablet RT panel

## Hand-off Brief
When the RoleCard detail overlay is opened from the tablet lobby grid, its scrim doesn't block and its close ✕ is
hard/impossible to click — but lowering the tablet (TAB) makes the ✕ clickable. Root cause: the screen-space RoleCard
panel (`PS_ScreenOverlay`) and the RT tablet panel (`PS_LobbyRoles_RT`) both have `m_SortingOrder: 0`, and the RT
presenter's `ScreenToPanel` maps tablet-region screen clicks into the RT panel (always live in the harness because its
`_ownerApp` is null), so the RT panel intercepts the pointer over the tablet area where the modal + ✕ live. Fix: give
`PS_ScreenOverlay` a higher sortingOrder than the RT panels so the open overlay wins input (and falls through when
collapsed).

## Case Info
- Area: UITK panel input routing — screen-space overlay vs tablet RenderTexture panels
- Files: `Assets/UI/PanelSettings/PS_ScreenOverlay.asset`, `PS_LobbyRoles_RT.asset`, `PS_InfoTable_RT.asset`; `Assets/Scripts/UI/LobbyRoles/LobbyRolesRtPresenter.cs`
- Status: Concluded — root cause Confirmed, High confidence
- Slug: rolecard-input-vs-tablet-rt

## Problem Statement (user, verbatim)
"ça bloque pas le clic sur le background et en plus j'ai du mal à cliquer sur la croix comme si y'avait un truc, mais si je baisse la tablette (avec TAB) je peux cliquer sur la croix sans problème."

## Evidence Inventory
| Evidence | Grade | Cite |
|---|---|---|
| RoleCard overlay panel and both RT tablet panels all have equal sort order | Confirmed | `PS_ScreenOverlay.asset:30` `m_SortingOrder: 0`, `PS_LobbyRoles_RT.asset:30` `m_SortingOrder: 0`, `PS_InfoTable_RT.asset:30` `m_SortingOrder: 0` |
| The RT presenter maps screen clicks into the RT panel, gated only on the owner app being open | Confirmed | `LobbyRolesRtPresenter.cs:97-114` (`ScreenToPanel`); gate `if (_ownerApp != null && !_ownerApp.IsOpen) return invalid;` at `:101` |
| In the harness the RT presenter's `_ownerApp` is null ⇒ ScreenToPanel is ALWAYS live | Confirmed | Scene wiring: `SpikeLobbyRolesUITK` LobbyRolesRtPresenter `_ownerApp` = null |
| ScreenToPanel returns NaN when the pointer is outside the RawImage rect | Confirmed | `LobbyRolesRtPresenter.cs:108-111` (u/v out of [0,1] → NaN) |
| RoleCard is modal only via its scrim root pickingMode (Position when open, Ignore when collapsed) | Confirmed | `RoleCardController.cs:128,153` |

## Confirmed Findings
1. Two UITK runtime panels overlap on screen in the tablet region: the screen-space RoleCard (`PS_ScreenOverlay`) and
   the RT tablet (`PS_LobbyRoles_RT`). Both at `sortingOrder 0`.
2. The RT panel actively claims pointer positions inside the RawImage rect via `SetScreenToPanelSpaceFunction`. With
   equal sort order, that claim competes with the RoleCard scrim for the same screen pixels.
3. In the harness the RT presenter's `_ownerApp` is null, so its ScreenToPanel never yields on "app closed" — it maps
   every tablet-region click into the RT panel, stealing it from the RoleCard modal.
4. Lowering the tablet (TAB) moves the RawImage out from under the pointer → ScreenToPanel returns NaN → the RT panel
   stops claiming → clicks fall through to the RoleCard scrim/✕. This is exactly the reported TAB behaviour.

## Deduced Conclusion (High confidence)
Input-routing conflict between two equal-`sortingOrder` UITK panels. The RT tablet panel intercepts the pointer over
its RawImage region (where the modal + ✕ are drawn), so the RoleCard scrim can't block and the ✕ can't be hit while
the tablet is up. Not a RoleCard bug — a panel-ordering issue.

## Refutation attempted
- "The ✕ button itself is mis-placed / not pickable" → Refuted: the ✕ works when the tablet is lowered, so the button
  is fine; the difference is purely whether the RT panel is under the pointer.
- "The scrim pickingMode is wrong" → Refuted: the scrim is Position when open (`RoleCardController.cs:153`); it simply
  never receives the event because the RT panel consumes it first.

## Fix direction (for gds-quick-dev — NOT applied here)
Primary (clean, general, benefits GameScene too):
- Raise `PS_ScreenOverlay.m_SortingOrder` above the RT panels (e.g. 10). UITK routes pointer events to the highest
  panel that picks something at the position; when the RoleCard is open its scrim (Position) captures; when collapsed
  its root is Ignore, so input falls through to the RT tablet. One-value asset change.

Note: gating the RT ScreenToPanel on `_ownerApp.IsOpen` does NOT solve it — in GameScene the tablet IS open while in
use, so the RT panel is legitimately live; sort order is the correct lever. (Optionally, also set the harness
presenter's `_ownerApp` for parity, but that is not the fix.)

## Reproduction
Play TabletOnlySpike → swipe to the LobbyRoles slot → tap a card → the overlay opens; with the tablet UP the scrim
doesn't dismiss and the ✕ is hard to click; press TAB to lower the tablet → the ✕ clicks fine.

## Status
Concluded — Confirmed root cause. Fix VERIFIED by playtest (Poyo, 2026-07-15): setting `PS_ScreenOverlay.m_SortingOrder = 10`
(above the RT panels at 0) fully restored the scrim block + the ✕ click with the tablet up. Since `PS_ScreenOverlay` is
shared, the fix applies globally (GameScene RoleCard included).
