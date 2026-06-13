# Story 9.1: Extract ICharacterQuery + migrate the read-side consumers

Status: review

## Story

As a developer,
I want character lookups behind an injected `ICharacterQuery`,
so that read-only consumers see only the query slice.

## Acceptance Criteria

1. **Surface from usage, not invention:** grep-census of `CharacterManager` member usage by consumers (read side: `GetCharacter`, character-list queries, lookups — exact list at dev time from the census) → `ICharacterQuery` in `Game` asmdef; `CharacterManager` implements; signatures verbatim.
2. **Inert first commit** (interface + implements, zero call-site change, suite byte-identical) — then read-consumer migration in batches: consumers already holding an injected `CharacterManager` (Epic 7's work) narrow to the interface (field type stays concrete where serialized — lane A narrowing-property pattern; lane B/C consumers can take the interface directly).
3. **`CompositionRoot.CharacterQuery`** typed accessor added.
4. **No command-surface leak:** read consumers compile against `ICharacterQuery` ONLY (the narrowing property's type makes the compiler enforce it).
5. **Gated:** suite + fixture unchanged per batch; registry/guards green.

## Tasks / Subtasks

- [x] **Task 1:** Member-usage census of `CharacterManager` consumers done (grep of the injected field + the `instance`/`For` locator); read vs command partitioned; `ICharacterQuery` member list frozen (Completion Notes).
- [x] **Task 2:** `ICharacterQuery` extracted (Game asmdef, via `create_script`), `CharacterManager` implements it (all 6 members implicit — inert), `CompositionRoot.CharacterQuery` accessor added (instance + Services struct). Zero call-site change; EditMode **162/162** byte-identical, PlayMode unchanged at 146 (interface-only). **Ready as the AC2 "inert first commit".**
- [x] **Task 3:** Migrate read consumers in batches (narrowing properties / interface-typed fields per lane). DONE — 22 production files narrowed to `ICharacterQuery`; the MIXED + ENTANGLED + static-locator sets left untouched per the frozen census (→ 9.2 / later batches). Detail in Completion Notes.
- [x] **Task 4:** Gates; sprint-status; commit. DONE — EditMode **162/162**, PlayMode **146/146** (both baselines held, behaviour-identical). No registry/guard edits (consumers were already Epic-7-injected; no `*.instance`/`.For(` introduced).

## Dev Notes

- After Epic 7, consumers already HOLD CharacterManager injected — this story narrows the TYPE they compile against. Mostly find-and-narrow, low risk; the partition census (Task 1) is the real work.
- `GetSafeRpcTarget`/`IsLocalOrSimulated` are NOT query members — they are adapter internals consumed by command flows; they must NOT appear in `ICharacterQuery` (9.2's territory decides their exposure — likely none: they stay internal to the adapter).
- Staleness: authored 2026-06-11 pre-Epic-7; the consumer set will look different. Census first, always.

### Project Structure Notes

- New: `Assets/Scripts/Characters/ICharacterQuery.cs`. Modified: `CharacterManager.cs` (declaration), `CompositionRoot.cs`, consumer files. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Interfaces `IPascalCase`; EditMode-first; fixture for replicated checks; mock NGO surface via interface (this interface BECOMES the mockable read surface for EditMode tests — a deliverable side-benefit).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2.3, §8 Epic 9] / [epics.md#Story 9.1]
- [Source: Assets/Scripts/Characters/CharacterManager.cs] — the surface source (509 LOC, fan-in 37).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Inert extraction compile: 0 errors (CharacterManager : ICharacterQuery satisfied implicitly → no CS0535). `ICharacterQuery.cs` created via `create_script` (avoids the silent-compile-exclusion that bites `Write`-tool new .cs). Gate: EditMode **162/162**; PlayMode unchanged at 146 (interface-only, zero call-site change).

### Completion Notes List

**Task 1 — frozen census + member partition.**

`ICharacterQuery` (the READ slice, members lifted verbatim — exactly what consumers call):
- `Character GetCharacter(ulong, bool=true)`
- `List<Character> GetCharacters(bool=true)`
- `Character GetLocalCharacter(bool=true)`
- `ulong GetLocalClientId()`
- `event Action<List<Character>> onCharactersListUpdated`
- `event Action onLocalIdentityChanged`

**Excluded from the query slice** (→ `ICharacterCommand` / story 9.2, or adapter-internal): `GetSafeRpcTarget`, `IsLocalOrSimulated` (Dev Notes: adapter internals consumed by command flows), `AddNewCharacter`, `RemoveCharacter`, `CreateNewFakeCharacter`, `GivePowerToCharacter`, `RemovePowerFromCharacter`, `GiveRoleToCharacterRpc`, `AskForUpdateAllCharactersRpc`, `SpawnSimulatedPlayer`, `SetPossessedIdentity`, `RegisterSpawnedCharacter`, `GetCharacterAsync` (no external consumer), `static instance`/`For`.

**Consumer partition (for the Task 3 batches):**
- **PURE-READ (9.1 narrow-to-slice — already injected from Epic 7, no command, no foreign locator):** `BoardManager`, `SendMessagePanel` (lane C), `AwakeningLight`, `GameInfoRevealer`, `Card` (lane B), `SkipButton`, `RobotBoardInfo`, `PowerUsageManager`, `CorruptionBoardInfo`, `GameIntroductionUI`, `AwakeningStateUI`, `MessageLeftText`, `CharactersBar`; pure-read GameStates `AwakeningState`, `TakeDownThePortalState`, `VictoryConditionCheckState`, `GameEndingState`, `GameIntroductionState`, `ChainingState`, `VoteRecapState` (narrow via a `protected ICharacterQuery CharacterQuery => characterManager` on the `GameState` base — the field stays concrete because the MIXED states share it).
- **MIXED → 9.2 (command surface, can't narrow to read-only):** all powers (the `Power` base field touches `GetSafeRpcTarget`/`AskForUpdateAllCharactersRpc`), GameStates `VoteState` (AskForUpdate), `RoleAttributionState` (Create/Give), `LobbyState` (Add/Remove), `ChainingManager` (AskForUpdate), `GameManager` (GetSafeRpcTarget/AskForUpdate).
- **ENTANGLED → "both managers in one touch" (the 8.3 deferral):** `RoomFog`, `AnonymeMessageButton`, `InfoTableSystem`, `PowersBar` still hold `GameManager.instance` (8.3-deferred IGameLoop/IGameStateQuery) AND `CharacterManager.instance` (read). They register clean only once BOTH managers are migrated — do the GameManager lane-A (8.3-style) + the CharacterManager read-narrowing together so each scene object is wired once.
- **Static-locator pure-read leaves (not field-injected yet):** `NoteChoosePanel`, `NoteRibbon`, `SelectPanelPlayer`, `AwakeningRecapCorruption`, `MeIconCard`, `CardPickerManager`, `TakeDownThePortalTextTitle`, `VoteStateUI`, `ChatWindow`, prefab-only timers, `TargetUtils` (static) — these read via `CharacterManager.instance`; lane decided per object in a later batch / Epic 12 (prefab-only → lane impossible).

**Task 2 — inert extraction (done, committed `334e124`).** `ICharacterQuery` in `Assets/Scripts/Characters/`; `CharacterManager : NetworkBehaviour, ICharacterQuery` (implicit); `CompositionRoot.CharacterQuery` typed accessor on both the instance accessors and the `Services` value resolver. Inert — no consumer rerouted.

**Task 3 — read-consumer narrowing (done).** The 6-member read slice is now what the pure-read consumers compile against. Pattern per lane (D-NFR6 internal-narrowing, AC4 enforced by the property/field type):

- **Lane A (9 files) — concrete `[SerializeField]` field kept (Unity can't serialize an interface), reads routed through a private `ICharacterQuery CharacterQuery => characterManager;`:** `BoardManager` (incl. the `Card.Initialize` hand-off, see below), `CorruptionBoardInfo`, `RobotBoardInfo`, `SkipButton`, `CharactersBar`, `MessageLeftText`, `AwakeningLight`, `GameInfoRevealer`, `PowerUsageManager`. Property named `CharacterQuery` (not `Query`) to avoid colliding with the `IGameStateQuery Query` the dual-manager consumers already hold from 8.2/8.3, and to mirror `CompositionRoot.CharacterQuery`. The Unity-lifetime null-guards/`Assert.IsNotNull` stay on the concrete field (correct `==`-overload semantics); only the read/event surface goes through the slice.
- **Lane B/C (2 files) — field retyped to the interface directly** (never serialized): `Card` (lane B, field `ICharacterQuery characterManager`; `Initialize(ICharacterQuery, …)`; the only caller `BoardManager.AddNewCard` now passes `CharacterQuery`), `SendMessagePanel` (lane C, resolves `CompositionRoot.For(nm).CharacterQuery`). The `!= null` checks shift from Unity-lifetime to reference semantics, but the only sites are event `-=` cleanup on managed-alive objects — no native access, behaviour-identical (and strictly safer for the teardown unsubscribe).
- **Base-class slice for the shared field (2 bases) → routed subclasses:** `GameState` and `StateUI` each gained `protected ICharacterQuery CharacterQuery => characterManager;` (field stays concrete `public` because the MIXED states share it). Routed pure-read subclasses: GameStates `ChainingState`, `AwakeningState`, `GameEndingState`, `GameIntroductionState`, `TakeDownThePortalState`, `VoteRecapState`, `VictoryConditionCheckState`; StateUIs `GameIntroductionUI`, `AwakeningStateUI`.

**Deliberately NOT touched (per frozen census):** MIXED (command surface, → 9.2) `LobbyState`, `RoleAttributionState`, `VoteState`, `ChainingManager`, `GameManager`, all `Power`-base powers, `Character`, `PowersBar`, `PowerManager`; ENTANGLED (`GameManager.instance` + `CharacterManager.instance`, both-managers-one-touch batch) `RoomFog`, `AnonymeMessageButton`, `InfoTableSystem`, `PowersBar`; static-locator pure-read leaves (Epic 12 / prefab-lane batch). Confirmed by a post-edit grep of `characterManager.` — every survivor is one of those sets, a test's own `_characterManager`, a comment, or the two now-interface-typed fields.

**Gate (Task 4):** EditMode 162/162, PlayMode 146/146 — both baselines byte-for-byte unchanged. No `DiSeamMigratedConsumers`/guard edits: every migrated consumer was already registered when Epic 7 injected its `CharacterManager`, and the narrowing introduces no `*.instance`/`.For(` (guard #1 clean) and changes no `[SerializeField]` wiring (guard #2 / SceneWiringGuard unaffected — the concrete fields are untouched).

### File List

**Added (production):**
- `Assets/Scripts/Characters/ICharacterQuery.cs` — the read slice (6 members).

**Modified (production) — Task 2 (inert, `334e124`):**
- `Assets/Scripts/Characters/CharacterManager.cs` — `: ICharacterQuery` (implicit impl).
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — `CharacterQuery` accessor (instance + `Services` struct).

**Modified (production) — Task 3 (read-consumer narrowing, 22 files):**
- Bases: `Assets/Scripts/GameLogic/GameState.cs`, `Assets/Scripts/GameLogic/StateUI.cs` — `protected ICharacterQuery CharacterQuery`.
- Lane A: `Assets/Scripts/Board/BoardManager.cs`, `Assets/Scripts/Board/UI/CorruptionBoardInfo.cs`, `Assets/Scripts/Board/UI/RobotBoardInfo.cs`, `Assets/Scripts/Board/UI/SkipButton.cs`, `Assets/Scripts/Board/UI/CharacterBar/CharactersBar.cs`, `Assets/Scripts/UI/Misc/MessageLeftText.cs`, `Assets/Scripts/FX/AwakeningLight.cs`, `Assets/Scripts/GameLogic/GameInfoRevealer.cs`, `Assets/Scripts/GameLogic/PowerUsageManager.cs`.
- Lane B/C: `Assets/Scripts/Board/Card.cs`, `Assets/Scripts/MessageSystem/SendMessagePanel.cs`.
- Routed subclasses: `Assets/Scripts/UI/StateUI/GameIntroductionUI.cs`, `Assets/Scripts/UI/StateUI/AwakeningStateUI.cs`, `Assets/Scripts/GameLogic/GameStates/ChainingState.cs`, `.../AwakeningState.cs`, `.../GameEndingState.cs`, `.../GameIntroductionState.cs`, `.../TakeDownThePortalState.cs`, `.../VoteRecapState.cs`, `.../VictoryConditionCheckState.cs`.

**Docs:**
- `_bmad-output/implementation-artifacts/9-1-*.md` (this story); `sprint-status.yaml` (`9-1 → review`).

## Change Log

| Date | Change |
|---|---|
| 2026-06-12 | Task 1 census + Task 2 inert `ICharacterQuery` extraction (committed `334e124`). |
| 2026-06-12 | Task 3 read-consumer narrowing (22 files, 3 lanes + 2 bases); Task 4 gate EM 162/162 + PM 146/146 (baselines held). Status → review. |
