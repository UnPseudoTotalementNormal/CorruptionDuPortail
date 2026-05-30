# Story: Visual grouping for CharactersBar by awakening layer

**Status:** `review`
**Reference:** Story 1.1 (Role bar sorted by faction + awakening order)

## Context
Logical sorting for the `CharactersBar` is already implemented. However, the UI currently displays character icons contiguously without clear boundaries between different awakening layers (factions). A visual separation is needed to match the Canva mockup aesthetic and improve readability.

## Goal
Implement visual separators or spacing between character groups in the `CharactersBar` to clearly distinguish different awakening layers.

## Acceptance Criteria
1. [x] **Visual Separation:** The `CharactersBar` must insert a visual gap or a separator object between characters belonging to different awakening layers.
2. [x] **Consistency:** Spacing between groups must be consistent and configurable via the component's inspector or a central UI configuration.
3. [x] **Implementation Method:** The solution should ideally use a separator prefab (e.g., a simple line or themed object) or dynamic padding/spacing adjustments within the LayoutGroup.
4. [x] **Dynamic Updates:** Grouping and separators must remain correct during gameplay, including cases where characters are eliminated (the gap should correctly shift to remain between groups of surviving characters).
5. [x] **Visual Quality:** The result must match the "separated groups" aesthetic defined in the Canva mockup.

## Technical Notes
- **Input Reference:** `clipboard-1780078637169.png` (Current contiguous display issue).
- **Existing Logic:** The sorting logic (Awakening Order -> RoleID -> ClientID) feeds a consecutive-run grouping (`GroupConsecutiveByFaction`). Per design decision, a new faction group starts every time the faction changes in awakening order — so a faction awakening at two non-contiguous layers yields two separate groups (awakening order always wins over faction merging).
- **Component:** `CharactersBar.cs` builds one `FactionGroupPrefab` container per faction.

## Implementation Approach (as built)
Visual grouping is achieved with a **nested container**, not separate spacer/label objects:
- Each faction renders a `FactionGroupPrefab` instance: a `VerticalLayoutGroup` with a faction title (`TextMeshProUGUI`) on top and a `CharactersContainer` (`HorizontalLayoutGroup`) holding the character icons below.
- Group-to-group separation is produced by the parent `HorizontalLayoutGroup.spacing` (`interGroupSpacing`), driven from the `CharactersBar` inspector — no dedicated spacer object.
- Faction title text is set to the `FactionType` name on the prefab's embedded TMP.

## Tasks
- [x] Create `FactionGroupPrefab` (VerticalLayoutGroup: title TMP + `CharactersContainer` HorizontalLayoutGroup).
- [x] Add `factionGroupPrefab` + spacing fields to the `CharactersBar` component.
- [x] Update `CharactersBar.ResetCharactersBar` to group sorted characters by faction and instantiate one container per group.
- [x] Drive inter-group / intra-group / title spacing from inspector fields.
- [x] Assign `factionGroupPrefab` in the GameScene.

## Dev Agent Record
- Created `Assets/Prefabs/CharacterBar/FactionGroupPrefab.prefab`.
- Modified `CharactersBar.cs` to sort, group by faction, and instantiate per-faction containers.
- Linked `factionGroupPrefab` to the `CharactersBar` object in `GameScene` (Instance ID: 87938).

### Cleanup (post-review)
- Removed dead artifacts created during exploration but never wired: `FactionLabelPrefab.prefab`, `SpacerPrefab.prefab` (empty stub, no RectTransform), `Assets/Art/Sprites/UI/FactionLabelBG.png`, and the unused `factionLabelPrefab` field (code + GameScene reference).
- Hardening: `CharactersBar` re-fetches `AwakeningState` on every rebuild (no stale reference across rematches); warnings replace silent fallbacks when `factionGroupPrefab` / `CharactersContainer` is missing.
- Verified: clean compile, 57/57 EditMode tests passing.

