# Investigation — LobbyRoles UITK sections overlap

## Hand-off Brief
The lobby faction sections overlap vertically (ÉLU over ANOMALIE, MARGINAL over ÉLU, footer over MARGINAL). Root cause is NOT card height: the UITK root `.lobby-roles` is a **fixed-height column** (`height: 100%` of the 912px RT) with **no scroll**, and its `content` child holds all 14 role cards whose natural height far exceeds 912px. With UITK's default `flex-shrink: 1` and no `ScrollView`, the flexbox **compresses the overflowing children to fit the panel**, so sections render beyond their shrunk boxes and the next sibling is pulled up on top. Fix = make the sections area a vertical `ScrollView` (and pin the non-scrolling bands with `flex-shrink: 0`).

## Case Info
- Area: `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs`, `Assets/UI/Screens/LobbyRoles/LobbyRoles.uss`, `Assets/Scripts/UI/Cards/RoleCardElement.cs`
- Harness: `Assets/Scenes/Spikes/TabletOnlySpike.unity` (RT 1440×912)
- Status: Concluded — root cause Deduced, High confidence
- Slug: lobby-uitk-section-overlap

## Problem Statement
Faction sections (`BuildSection` = header Label + row-wrap grid of card units) overlap vertically. Reported after cards were given a fixed 5:7 height; the overlap persisted, disproving "card height not reserved" as the cause.

## Evidence Inventory
| Evidence | Grade | Cite |
|---|---|---|
| Root is a fixed-height column | Confirmed | `Assets/UI/Screens/LobbyRoles/LobbyRoles.uss:5` → `.lobby-roles { width:100%; height:100%; flex-direction:column; }` |
| No `flex-shrink` declared anywhere in the screen USS | Confirmed | grep `flex-shrink` in `LobbyRoles.uss` → 0 hits |
| No `ScrollView` / no `overflow` on the sections container | Confirmed | grep `ScrollView`/`overflow` in `LobbyRolesUitkController.cs` + `LobbyRoles.uss` → 0 hits for a scroll container |
| Root stacks 5 flex children incl. `content` | Confirmed | `LobbyRolesUitkController.cs:154-158` (`_root.Add(presetBar/tabs/tally/content/footer)`) |
| `content` is a plain VisualElement (default flex-shrink=1) | Confirmed | `LobbyRolesUitkController.cs:218` (`var content = new VisualElement();`) |
| 14 roles rendered as 5:7 cards (~279px + steppers) across 2–3 rows per faction ⇒ intrinsic content height ≫ 912px | Deduced | pool = `RoleAttributionState` (14 keys) + `CardWidth=200 ⇒ 279px height` |

## Confirmed Findings
1. The panel is a **fixed 912px-tall column** with **no vertical scroll**.
2. The overflowing `content` band has the **default `flex-shrink: 1`**, so UITK's flexbox shrinks it below its natural height to make all five root children fit 912px.
3. A shrunk `content` box does not clip its sections (no `overflow: hidden` on it), so the sections/grids paint beyond the box and the following siblings (next section, then the footer) are laid out on top → the observed overlap.

## Deduced Conclusion (High confidence)
Symptom is produced at the **root-column level, not the card level**. Making cards taller (the earlier "fix") increased the content's natural height → more shrink → same/worse overlap, which matches the report. This is the known project trap: a **fixed-height UITK panel with content taller than the panel needs an explicit ScrollView**; otherwise flex-shrink compresses and siblings overlap.

## Refutation attempted
- "It's the async card height" → Refuted: overlap persisted after explicit deterministic height (`RoleCardElement.SetWidth`), and the footer (a fixed-size sibling) also overlaps — a card-only cause would not move the footer.
- "A section is missing `flex-shrink:0`" → partially true, but the primary missing piece is a **scroll container**; without it, pinning shrink just pushes overflow off-panel instead of overlapping.

## Fix direction (for gds-quick-dev — NOT applied here)
1. Put the faction sections inside a **vertical `ScrollView`** that takes the remaining height: `flex-grow:1; flex-shrink:1;` with internal scrolling. (Mirrors the InfoTable overflow approach.)
2. Pin the non-scrolling bands (`preset bar`, `tabs`, `tally`, `footer`) with **`flex-shrink: 0`** so they keep their height and only the sections area scrolls.
3. No hardcoded pixel heights — the ScrollView + flex-shrink pinning is layout-driven and adapts to any role count / RT size.
4. Optional runtime confirmation: inspect `resolvedStyle.height` of `content` vs its children in Play mode (UI Toolkit Debugger / visual tree) to see the compression directly.

## Status
Concluded — root cause identified. Implementation deferred (no code changed by this investigation; the earlier uncommitted `SetWidth`/`CardWidth` change is orthogonal and should be revisited during the fix).
