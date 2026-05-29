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
- **Existing Logic:** The current sorting logic (Faction -> Awakening Order) is used to detect group transitions.
- **Component:** `CharactersBar.cs` updated to handle dynamic injection of labels and spacers.

## Tasks
- [x] Create `FactionLabelPrefab` with TextMeshPro and background image.
- [x] Create `SpacerPrefab` for visual "holes" between groups.
- [x] Add prefab references to `CharactersBar` component.
- [x] Update `CharactersBar.ResetCharactersBar` to detect faction transitions.
- [x] Instantiate spacers and faction labels at transition points.
- [x] Assign prefab references in the GameScene.

## Dev Agent Record
- Created `Assets/Art/Sprites/UI/FactionLabelBG.png`.
- Created `Assets/Prefabs/CharacterBar/FactionLabelPrefab.prefab`.
- Created `Assets/Prefabs/CharacterBar/SpacerPrefab.prefab`.
- Modified `CharactersBar.cs` to inject visual grouping logic.
- Linked prefabs to the `CharactersBar` object in `GameScene` (Instance ID: 87938).

