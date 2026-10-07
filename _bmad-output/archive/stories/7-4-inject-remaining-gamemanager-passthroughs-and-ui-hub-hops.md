# Story 7.4: Inject the remaining GameManager pass-throughs (ChainingManager, bars) + the UI hub-hops

Status: review

## Story

As a developer,
I want the smaller pass-throughs (`ChainingManager`, `CharactersBar`, `PowersBar`) and the UI components' hub-hops injected,
so that the only thing left reaching for GameManager is its real game-loop/state surface.

## Acceptance Criteria

1. **Small pass-throughs injected.** `.chainingManager` / `.charactersBar` / `.powersBar` consumers (~4+ hits, histogram 2026-06-11) receive the dependency per their lane; `CompositionRoot` + `InjectedManagerTypes` extended accordingly.
2. **UI hub-hops rerouted (mechanical only).** UI components that hop through GameManager to reach managers (e.g. `InfoTableSystem` 10 instance-hits, `CardPickerManager`, `AnonymeMessageButton`, `MeIconCard`, bars' own files) get lane A fields. Scope fence: ONLY the hub-hop reroute — no view-model extraction, no UI redesign (that is Epic 12). A UI file whose hop targets the game-loop surface itself (`currentGameStateIndex`, `GetGameState`…) is NOT touched here (Epic 8.2 migrates those onto `IGameStateQuery`).
3. **After this story:** every remaining `GameManager` consumer uses its game-loop/state surface only — verified by grep inventory recorded in the story's Dev Agent Record (the input to 7.5's destructive deletion).
4. **Batched, gated, guarded** — suite + goldens unchanged per batch; serialized-field safety on every lane A wiring; registry appended.

## Tasks / Subtasks

- [x] **Task 1: Extend root + guard sets** (AC: 1) — `InjectedManagerTypes` += `CharactersBar`, `PowersBar` (so guard #2 wiring-checks the new lane-A fields). No new `CompositionRoot` accessor was needed for chaining/bars: no lane-C consumer resolves them (the only lane-C resolutions in scope are `CharacterManager` for `SendMessagePanel` and `GameInfoRevealer` for `CharactersBarObject`/static utils, both already exposed). GameStates receive chainingManager/charactersBar via the existing `SetupGameStates` lane-B push (extended).
- [x] **Task 2: Migrate small pass-through consumers** (AC: 1) — `chainingManager`/`charactersBar` rerouted off the GameManager pass-through per lane: GameStates (ChainingState/LobbyState/GameIntroductionState) via the SetupGameStates push; `powersBar` consumer (PowerUsageManager) + `charactersBar` consumer (FocusManager) via lane-A scene fields.
- [x] **Task 3: UI hub-hop sweep** (AC: 2) — every remaining `.characterManager` / `.gameInfoRevealer` hub-hop rerouted per lane (see Completion Notes for the lane matrix). Scene singletons → lane A ([SerializeField] + MCP-wired + read-back-verified); prefab-resident UI leaves → façade route (`CharacterManager.instance`) or composition-root route (revealer has no `.instance`); Card child → reads parent `Card.GameInfoRevealer`; `SendMessagePanel` (NetworkBehaviour) → lane C. Static utilities + POCO winning conditions → façade/composition-root (verify-don't-force, Epic 9/11). No view-model extraction (Epic 12 fence respected).
- [x] **Task 4: Closing inventory** (AC: 3) — grep over `GameManager.instance`/`For(...)` + `CharacterManager.instance`/`For(...)` confirms **no production consumer hops a GameManager pass-through member anymore**; every remaining `GameManager` external reference is game-loop/state surface only (`onGameStarted`, `hasGameStarted`, `currentGameStateIndex`, `GetGameState(s)`, `DoStateMethodRpc`, `ShutOffGameRpc`, `currentDay`) → Epic 8. Remaining `CharacterManager.instance` direct uses (CardPickerManager, MeIconCard, AnonymeMessageButton, DevIdentityController, …) are NOT GameManager pass-throughs → Epic 9. Gate: EditMode **162/162** + PlayMode **146/146** + both guards green. Sprint-status `7-4 → review`.

## Dev Notes

- This is the mop-up that makes 7.5 a pure deletion. Anything Task 4's inventory finds that is NOT game-loop surface = a missed reroute — fix it here, do not defer.
- `CharactersBar`/`PowersBar` are themselves scene UI objects: consumers get them lane A; the bars' OWN hub-hops (CharactersBar 3, PowersBar 6 instance-hits) are part of Task 3.
- UI files are presentation leaves — concrete serialized fields are fine (D-NFR6, verify-don't-force); no interfaces needed here.
- Staleness: hit counts are 2026-06-11 recon; the compiler + Task 4 inventory are authoritative.

### Project Structure Notes

- Modified: `CompositionRoot.cs`, consumer files per inventory, GameScene/prefab wiring, shared registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Serialized-field safety (append-only, MCP wiring, read-back verify). `Smartphone/` stays client-only if touched. No `UnityEvent` wiring introduced.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §1 histogram, §3, §7] / [epics.md#Story 7.4]
- [Source: _bmad-output/implementation-artifacts/7-3-*.md] — previous story: seam extensions in place.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow). Mechanical reroutes parallelised across disjoint file groups via subagents (Poyo-authorised); MCP scene wiring + registry + gate kept on the main thread.

### Debug Log References

- Guard #1 bit two literal-token comments (`CharacterManager.instance` in PowerUsageManager + SendMessagePanel comments) — same class of bite as 6.3's "CompositionRoot" comment; reworded to drop the `.instance` token.
- Guard #1 could not map `CardCorruptedText` → file (class lives in `CorruptedCardText.cs`); left off the registry (it holds no locator).
- PlayMode: ChainingManager + PowerManager eager Start-asserts false-failed bare-AddComponent harnesses — ChainingManager made null-tolerant (conditionally-used, off-registry, like 7.3 Power/BoardManager gameInfoRevealer); PowerManager reflection-wired in EntrapmentPowerTests (its field is used unconditionally in OnGameStarted).

### Completion Notes List

- **Lane matrix (how each consumer family was rerouted off the GameManager pass-through):**
  - **GameStates** (ChainingState/LobbyState/GameIntroductionState) → lane B: `GameState.chainingManager` + `GameState.charactersBar` added, pushed by `SetupGameStates` (alongside the 7.2/7.3 characterManager/gameInfoRevealer push).
  - **Scene singletons** → lane A `[SerializeField]`, MCP-wired in GameScene + read-back-verified: PowerUsageManager (characterManager+powersBar), PowerManager (characterManager, mixed), ChainingManager (characterManager+gameInfoRevealer, mixed), CharactersBar/PowersBar (characterManager, mixed), CorruptionBoardInfo/RobotBoardInfo (characterManager, mixed), SkipButton/MessageLeftText (characterManager, clean), FocusManager (charactersBar, clean), AwakeningLight (characterManager, mixed), InfoTableSystem (gameInfoRevealer, null-tolerant, mixed).
  - **NetworkBehaviour, NGO-spawnable** (SendMessagePanel) → lane C: resolves `CharacterManager` once in a new `OnNetworkSpawn` via the composition root.
  - **Prefab-resident UI leaves** (confirmed via `find_gameobjects` = 0 scene instances → lane A physically impossible): TakeDownThePortalTextTitle, CharacterAwakenTimer, AwakeningRecapCorruption, NoteRibbon, NoteChoosePanel, SelectPanelPlayer (Resources-loaded prefab), TooltipLinkParser → **façade route** `CharacterManager.instance` (behaviour-identical single-NM; proper injection deferred to Epic 12).
  - **Card child** (CardCorruptedText) → reads `Card.GameInfoRevealer` (new accessor) from its parent; **CharactersBarObject** (two creators: CharactersBar + NoteRibbon) → composition-root route for the revealer (no lane-B push fits two creators; revealer has no `.instance` façade).
  - **Static utilities** (TargetUtils, PowerEffectDispatcher, GameSnapshotBuilder) + **POCO winning conditions** (4× W*) → façade / composition-root: characterManager → `CharacterManager.instance` (or `CharacterManager.For(nm)` where an NM is in hand — GameSnapshotBuilder), gameInfoRevealer → `CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer`. Recorded verify-don't-force; proper interface injection is Epic 9/11. NFR5: reveal-RPC bodies untouched, only the access path changed.
  - **PPersonalBeacons.Awake** pre-spawn read (the 7.1-recorded exception) → `CompositionRoot.For(NetworkManager).CharacterManager` (the base Power.characterManager isn't resolved until OnNetworkSpawn); removes the last `GameManager.For(...).characterManager` hop. Proper fix Epic 11.
- **Registry (7.4):** `All` += PowerUsageManager, SkipButton, MessageLeftText, FocusManager (fully clean scene consumers, both guards). `NoLocatorOnly` += SendMessagePanel (lane C, no scene field). `InjectedManagerTypes` += CharactersBar, PowersBar. Mixed files (still holding a game-loop `GameManager` ref or a direct `CharacterManager.instance`) stay OFF the registry until Epic 8/9 — same convention as 7.1-7.3.
- **AC3 closing inventory is clean:** no production consumer hops `GameManager.{characterManager,gameInfoRevealer,chainingManager,charactersBar,powersBar}` anymore (verified by grep) → 7.5 can delete the pass-through fields. The remaining GameManager surface is game-loop/state only.

### File List

- **Modified (seam/registry):** `GameLogic/GameState.cs` (+chainingManager/charactersBar push fields), `GameLogic/GameManager.cs` (SetupGameStates push), `Board/Card.cs` (+`GameInfoRevealer` accessor), `Tests/Editor/DiSeamMigratedConsumers.cs`
- **Modified (gamestates):** `GameLogic/GameStates/{ChainingState,LobbyState,GameIntroductionState}.cs`
- **Modified (scene singletons, lane A):** `GameLogic/{PowerUsageManager,PowerManager,ChainingManager}.cs`, `Board/UI/CharacterBar/CharactersBar.cs`, `Board/UI/PowerBar/PowersBar.cs`, `Board/UI/{CorruptionBoardInfo,RobotBoardInfo,SkipButton}.cs`, `UI/Misc/MessageLeftText.cs`, `FocusSystem/FocusManager.cs`, `UI/InfoTable/InfoTableSystem.cs`, `FX/AwakeningLight.cs`
- **Modified (lane C):** `MessageSystem/SendMessagePanel.cs`
- **Modified (prefab/façade/route):** `UI/Misc/{TakeDownThePortalTextTitle,CorruptedCardText}.cs`, `Board/UI/CharacterBar/{CharacterAwakenTimer,CharactersBarObject}.cs`, `UI/StateUI/AwakeningRecap/AwakeningRecapCorruption.cs`, `NoteSystem/{NoteRibbon,NoteChoosePanel}.cs`, `UI/SpawnPanels/SelectPanelPlayer.cs`, `TooltipSystem/TooltipLinkParser.cs`
- **Modified (static/POCO):** `Characters/Powers/Target/TargetUtils.cs`, `Characters/Powers/PowerEffectDispatcher.cs`, `GameLogic/Snapshot/GameSnapshotBuilder.cs`, `Characters/Powers/PPersonalBeacons.cs`, `Characters/WinningConditions/{WOmniscienceHackedCharacter,WChosenChainedAllAnomaly,WMarginalIsChainedWin,WAnomalyCorruption}.cs`
- **Modified (scene):** `Assets/Scenes/GameScene.unity` (12 consumers wired to CharacterManager/GameInfoRevealer/CharactersBar/PowersBar)
- **Modified (harness):** `Tests/PlayMode/EntrapmentPowerTests.cs` (reflection-wire PowerManager.characterManager)
- **Modified (tracking):** `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-12 — Story 7.4: every remaining GameManager pass-through hop (characterManager / gameInfoRevealer / chainingManager / charactersBar / powersBar) injected per lane (A scene / B push / C OnNetworkSpawn / façade for prefab leaves & static utils). Registry + InjectedManagerTypes extended; GameScene wiring done. EM 162 / PM 146 green, both DI guards green. AC3 inventory clean → 7.5 unblocked for the destructive field deletion. Status → review.
