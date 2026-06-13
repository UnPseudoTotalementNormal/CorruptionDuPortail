# Story 1.1: Role bar sorted by faction + awakening order

Status: ready-for-dev

## Story

As a player,
I want the characters bar to display roles sorted by their awakening order (which naturally groups them by faction),
so that I can clearly see the progression of roles and their affiliations during the game.

## Acceptance Criteria

1. The `CharactersBar` must order roles based on their awakening order (layer index in `AwakeningState`).
2. If multiple characters share the same layer index, they must be sorted secondarily by their `RoleID` enum value, and tertiarily by their `ownerClientId` to guarantee a 100% stable, deterministic order.
3. The sorting must safely handle edge cases (e.g., characters/roles that are not found in the awakening order, like dead or 'unknown' roles) by placing them at the end of the list without throwing exceptions.
4. The sorting logic must be extracted into a pure method (e.g., `IEnumerable<Character> SortCharacters(IEnumerable<Character>)`).
5. The visual layout (separators, margins, custom UI containers) is OUT OF SCOPE for this story. Only the instantiation order of the GameObjects in the layout group needs to be changed.
6. The `CharacterAwakenTimer` already contains logic to find the awakening layer index. This logic MUST be extracted to a shared helper in `AwakeningState` (e.g., `public int GetAwakeningLayerIndex(Role role)`) to follow DRY principles. This helper should ideally cache results or avoid heavy LINQ allocations on every call if possible.

## Tasks / Subtasks

- [x] Task 1: Refactor Awakening Layer Index logic (AC: 4, 6)
  - [x] Subtask 1.1: Create a public helper method `int GetAwakeningLayerIndex(Role role)` in `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs`. Ensure it handles roles not present in the order gracefully (e.g., returning `int.MaxValue`).
  - [x] Subtask 1.2: Refactor `Assets/Scripts/Board/UI/CharacterBar/CharacterAwakenTimer.cs` to use this new helper.
- [x] Task 2: Implement deterministic sort for CharactersBar (AC: 1, 2, 3, 5)
  - [x] Subtask 2.1: Add `SortCharacters` method in `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs` that applies the sort: `OrderBy(GetAwakeningLayerIndex).ThenBy(RoleID).ThenBy(ownerClientId)`.
  - [x] Subtask 2.2: In `ResetCharactersBar`, replace the random sorting (`_characters.OrderBy(_ => UnityEngine.Random.value)`) with a call to `SortCharacters`.
- [x] Task 3: Validation and Tests
  - [x] Subtask 3.1: Write a unit test to verify that `SortCharacters` correctly orders characters by their awakening layer index, resolves ties via RoleID and ClientID, and pushes unknown/unawakened roles to the end safely.

## Dev Notes

- **Architecture:** The `CharactersBar` shows all roles present in the match. It does not track live player states (like dead or un-awakened). Factions and roles are static once the game is initialized.
- **Constraints:**
  - Poyo confirmed that a single `AwakeningLayerObject` CANNOT contain roles of different factions. Because of this, simply sorting by the awakening layer index will naturally group the roles by faction in contiguous runs.
  - Poyo confirmed that model "B" is correct: the primary sort is the awakening order. If a faction awakens at multiple different layers (e.g., 'Élu' early and 'Élu' late), they will appear multiple times in distinct groups, matching the UI mockup.
  - The sorting must remain pure so that a future UI story can easily insert visual separators or faction labels between the contiguous runs.

### Project Structure Notes

- Keep all game state logic strictly in `GameLogic` namespace scripts.
- The `AwakeningState` is shared state; querying it locally for sorting is safe and doesn't require RPCs.

### References

- Reference discussions regarding Sort Model B vs A: Awakening order determines layout priority, resulting in chronological display.

## Dev Agent Record

### Implementation Plan
1. Refactor `AwakeningState` to provide a centralized `GetAwakeningLayerIndex` method with a dictionary-based cache to avoid LINQ allocations during UI updates.
2. Update `CharacterAwakenTimer` to consume the new helper.
3. Implement `SortCharacters` in `CharactersBar` using a multi-level sort (Awakening Order -> RoleID -> ClientID).
4. Integrate the new sort into `ResetCharactersBar`.
5. Create EditMode unit tests to verify sorting logic stability.

### Debug Log
- Encountered missing assembly reference for `Unity.Netcode` in `Tests.Editor.asmdef`. Fixed by adding `Unity.Netcode.Runtime`.
- Fixed missing `using` directives in `AwakeningState.cs` and `CharactersBar.cs`.
- Resolved unit test failure for ClientID sorting by properly initializing `NetworkVariable<ulong>` in test characters.

### Completion Notes
- Implemented deterministic sort as per AC 1-3.
- Extracted logic to `AwakeningState` as per AC 6.
- Added 4 unit tests in `CharactersBarTests.cs` covering all sorting tiers and edge cases.
- All 57 project tests are green.

## File List
- `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs` (Modified)
- `Assets/Scripts/Board/UI/CharacterBar/CharacterAwakenTimer.cs` (Modified)
- `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs` (Modified)
- `Assets/Scripts/Tests/Editor/Tests.Editor.asmdef` (Modified)
- `Assets/Scripts/Tests/Editor/CharactersBarTests.cs` (New)

## Change Log
- Refactored awakening layer lookup logic.
- Implemented deterministic character sorting in UI bar.
- Added comprehensive unit tests for sorting logic.

## Status

Status: review
