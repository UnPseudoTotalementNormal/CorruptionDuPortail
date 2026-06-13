# Story 8.1: Extract IGameLoop + IGameStateQuery; GameManager implements (zero behaviour change)

Status: review

## Story

As a developer,
I want the two intent interfaces extracted from GameManager's real surface, implemented by GameManager with no call-site change yet,
so that the narrowing starts with a provably inert commit.

## Acceptance Criteria

1. **Interfaces match the measured surface** (histogram 2026-06-11 + the 7.5 public-member census — the census is authoritative at dev time):
   - `IGameLoop`: `NextGameState` / `PreviousGameState` / `SetGameState` / `currentDay` / `hasGameStarted` / `onGameStarted` / `onNewDayPassed`.
   - `IGameStateQuery`: `GetGameState` / `GetGameStates` / `GetGameStateIndex` / `GetClosestPreviousState` / `currentGameStateIndex` (read access).
   Exact member signatures lifted from `GameManager` as-is — no signature "improvement".
2. **Placement:** interfaces live in the `Game` assembly (they expose `GameState` / `NetworkVariable<int>` — placing them in `Domain` would violate its `noEngineReferences` purity; this is by design, recorded).
3. **GameManager implements both; zero call-site change in this commit.** Full suite passes byte-identical.
4. **Root exposes the slices:** `CompositionRoot` gains `IGameLoop GameLoop` / `IGameStateQuery GameStateQuery` typed accessors (delegating to the resolved GameManager).
5. Console clean; no golden moves; sprint-status updated.

## Tasks / Subtasks

- [x] **Task 1:** Authored both interfaces in `Assets/Scripts/GameLogic/` (`IGameLoop.cs`, `IGameStateQuery.cs`), signatures lifted verbatim from the 7.5 census, each member XML-doc'd with its intent.
- [x] **Task 2:** `GameManager : NetworkBehaviour, IGameLoop, IGameStateQuery` — all members already existed and satisfy the interfaces implicitly EXCEPT `onGameStarted`/`onNewDayPassed`, which are public `NetworkAction` FIELDS (5.0b). A field can't implicitly implement a same-named interface property, so they are wrapped with explicit interface implementations (`NetworkAction IGameLoop.onGameStarted => onGameStarted;`). The fields are untouched. (`currentDay`/`hasGameStarted` are already get-only properties; `currentGameStateIndex`'s public getter satisfies the read-only interface property despite its private setter.)
- [x] **Task 3:** Root accessors (AC 4) — `CompositionRoot` gains `IGameLoop GameLoop` / `IGameStateQuery GameStateQuery` on both the instance accessors and the `Services` value-resolver struct, delegating to `GameManager.For(nm)` (which now implements both).
- [x] **Task 4:** Gate — console clean (0 errors/warnings); full suite bit-identical EM 162 / PM 146; sprint-status updated; commit pending.

## Dev Notes

- **Inert means inert:** if any test moves on this commit, something other than "add interface" happened — revert and re-inspect.
- `currentGameStateIndex` is a `NetworkVariable<int>` — `IGameStateQuery` exposes it as-is (read+`OnValueChanged` subscription need). Do not wrap it in events here; subscribers keep their exact pattern (8.2 just changes how they FIND the source).
- `onGameStarted`/`onNewDayPassed` are `NetworkAction`-backed (5.0b) — expose through the interface as their existing public type; no rebind.
- Staleness: authored 2026-06-11 before Epic 7 landed; the 7.5 census may shrink/adjust the member lists. The HISTOGRAM is the floor, the census is the truth.

### Project Structure Notes

- New: `Assets/Scripts/GameLogic/IGameLoop.cs`, `IGameStateQuery.cs`. Modified: `GameManager.cs` (declaration line + possible explicit impls), `CompositionRoot.cs`. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Interfaces `IPascalCase`; one top-level type per file; `internal`-by-default does not apply (crossing consumer boundaries within Game — keep `public`).
- Domain purity (NFR2/D-NFR placement decision above).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.3 narrow interfaces, §8 Epic 8] / [epics.md#Story 8.1]
- [Source: _bmad-output/implementation-artifacts/7-5-*.md] — the public-member census (authoritative input).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Final compile: **0 errors / 0 warnings**.
- Gate: EditMode **162/162**, PlayMode **146/146** — bit-identical to the pre-8.1 baseline (no test moved, no golden moved). Inert commit confirmed.
- **Silent-compile-exclusion hit + remedy:** the two interface files created with the Write tool were registered in AssetDatabase but EXCLUDED from the `Game` assembly source set → `CS0246: IGameLoop/IGameStateQuery could not be found` (GameManager/CompositionRoot couldn't see same-assembly types). `refresh_unity force` did NOT fix it. Remedy (per the known project gotcha): delete + recreate via Unity (`delete_script` then `create_script`) — fixed; clean compile after. GameManager compiling with `: IGameLoop, IGameStateQuery` is itself the type-discovery proof.
- **Env note (not code):** the first PlayMode gate attempt was eaten by a Unity Profiler hang ("Parsing Profiler Data: 0/0 KB processed", modal dialog froze the editor + MCP bridge). Caused by an open Profiler window recording each play session, NOT by this change. Resolved by closing the Profiler window; PM then ran clean.

### Completion Notes List

- **AC1 (interfaces match the measured surface):** signatures copied verbatim from the 7.5 census. `IGameLoop` = `NextGameState(bool=false)` / `PreviousGameState()` / `SetGameState(GameState)` / `SetGameState(Type)` / `currentDay` / `hasGameStarted` / `onGameStarted` / `onNewDayPassed`. `IGameStateQuery` = `GetGameState(int)` / `GetGameStates(Type)` / `GetGameStateIndex(GameState)` / `GetClosestPreviousState<T>() where T:GameState` / `currentGameStateIndex` (read). No signature "improvement".
- **AC2 (placement):** both in the `Game` assembly (`Assets/Scripts/GameLogic/`) — they expose `GameState` / `NetworkAction` / `NetworkVariable<int>`, so Domain placement would violate its `noEngineReferences` purity. By design, recorded in the file headers.
- **AC3 (implements, zero call-site change):** GameManager declares both; every member is implicitly satisfied except the two `NetworkAction` fields, wrapped via explicit interface implementation. No existing call site touched. Full suite byte-identical.
- **AC4 (root slices):** `CompositionRoot.GameLoop` / `.GameStateQuery` added on the instance accessors AND the `Services` struct, delegating to `GameManager.For(nm)` (implicit upcast; null when no manager resolves).
- **AC5 (green gate):** console clean, EM 162 / PM 146 unchanged, no golden moves, sprint-status updated.

### File List

**New (Game assembly):**
- `Assets/Scripts/GameLogic/IGameLoop.cs` — game-loop command slice.
- `Assets/Scripts/GameLogic/IGameStateQuery.cs` — game-state read slice.

**Modified:**
- `Assets/Scripts/GameLogic/GameManager.cs` — class declaration `+ IGameLoop, IGameStateQuery`; two explicit interface impls wrapping the `onGameStarted`/`onNewDayPassed` fields.
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `GameLoop` / `GameStateQuery` accessors on the instance accessors + the `Services` struct.

**Docs:**
- `_bmad-output/implementation-artifacts/8-1-extract-igameloop-and-igamestatequery-gamemanager-implements.md` (this story)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`8-1 → review`)

### Change Log

- 2026-06-12 — Story 8.1 implemented: extracted `IGameLoop` + `IGameStateQuery` from GameManager's real surface (signatures verbatim from the 7.5 census), GameManager implements both (two explicit-impl wrappers for the NetworkAction fields, rest implicit), `CompositionRoot` exposes both slices. Provably inert: zero call-site change, EM 162 / PM 146 bit-identical, no golden moves. Status → review.
