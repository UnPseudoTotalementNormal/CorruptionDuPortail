# Story 8.3: Migrate the game-loop command consumers onto IGameLoop

Status: done

## Story

As a developer,
I want the consumers that drive the loop (`NextGameState`/`SetGameState` callers, `onGameStarted`/`onNewDayPassed` subscribers, `currentDay`/`hasGameStarted` readers) to depend on an injected `IGameLoop`,
so that GameManager's remaining fan-in is the narrow command surface, not the concrete type.

## Acceptance Criteria

1. **Command/event consumers migrated** per their lanes (GameStates already hold injected `gameManager` — they narrow to `IGameLoop` via the push or an interface-typed property; powers via the `Power` base resolve; scene components lane A; spawned lane C). Histogram floor: `onGameStarted` 10, state-transition calls ~13, `currentDay`/`hasGameStarted` 3 — re-derive at dev time.
2. **Server authority unchanged** — loop commands still execute server-side through the exact same code paths; the RPC dispatch (`CallStateMethodRpc`/`CallMethodAfterRpc`) stays GameManager-internal (network adapter role). The 5.1 wire-format guard must not move.
3. **NFR5:** any touched consumer's network-authority code relocated verbatim, never edited (D-NFR2).
4. **Batched, gated, guarded:** suite + fixture + 2.11a + goldens unchanged per batch; registry appended; both guards green.
5. **Fan-in census after:** grep census of remaining concrete-`GameManager` references recorded in Dev Agent Record (target: GameManager internals, the root, and recorded exceptions only).

## Tasks / Subtasks

- [x] **Task 1:** Inventory command-surface consumers (grep `NextGameState|PreviousGameState|SetGameState|onGameStarted|onNewDayPassed|currentDay|hasGameStarted` outside GameManager); classified by lane; froze batches (table below).
- [x] **Task 2:** Migrated per batch — GameStates + StateUI narrow via a `Loop => gameManager` property (no rewiring); 6 clean scene consumers got lane-A `[SerializeField] GameManager` + `Loop`/`Query` narrowing, MCP-wired + read-back. `IGameLoop` gained `WaitAFrameAndNextGameState` + `get;set;` on the two NetworkAction events (the `+=` idiom needs the setter). CharacterManager-entangled + prefab-only consumers deferred (recorded).
- [x] **Task 3:** Gates green — EditMode **162/162** (both DI guards, 2.11a-adjacent EM goldens, 5.1 wire-format), PlayMode **146/146** (2.11a sequence golden, multi-client fixture, power goldens). 4 PlayMode harnesses reflection-wired for the new field. 0 console errors.
- [x] **Task 4:** Fan-in census recorded (AC5, below); sprint-status → review; deferred-work updated.

## Dev Notes

- `ignoreGameLoop` interactions and state-transition ordering are the touchy parts — they are PINNED (2.11a journal + Domain `GameLoopMachine` tests). Trust the net; any movement = stop.
- Some consumers will need BOTH slices (`IGameLoop` + `IGameStateQuery`) — two deps is fine and correct (à-la-carte is the point); do not invent a combined interface.
- Staleness: authored 2026-06-11; Epic 7 + 8.1/8.2 will have reshaped consumer files. The Task 1 inventory is authoritative.

### Project Structure Notes

- Modified: consumer files per inventory, push/root extensions if needed, registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Server-authoritative state strict; clients propose via wrapped ServerRpc — unchanged. RPC ordering guarantees unchanged (no dispatch refactor here).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §1 histogram, §2.3] / [epics.md#Story 8.3]
- [Source: Assets/Scripts/GameLogic/GameManager.cs:304-376] — the dispatch that stays internal (5.1-guarded).
- [Source: _bmad-output/implementation-artifacts/8-2-*.md] — previous story: narrowing-property pattern.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Compile: 0 errors / 0 warnings after the final pass. Two intermediate compile fixes: `MessageManager` was missing `using UnityEngine;` (CS0246 SerializeField); `IGameLoop.onGameStarted/onNewDayPassed` were get-only → `+=` (NetworkAction `operator+` lowers to `action = action + handler`) needed `get;set;` (CS0200).
- Gate: **EditMode 162/162**, **PlayMode 146/146** (baseline held; 2.11a sequence golden `GameLoopTransitionOrderingTests` unchanged; 5.1 wire-format golden unchanged; multi-client fixture green).
- Guard #1 false-positive caught + fixed: the substring source-scan flagged the literals `GameManager.For(` (GameInfoRevealer comment) and `GameManager.instance` / `CharacterManager.instance` (PowerManager comment) — reworded the comments to drop the forbidden tokens (code was already clean).
- 12 transient PlayMode failures (bare power harnesses) when the new `gameManager` Awake/Start asserts fired: the harnesses provided GameManager via the `For(nm)` registry but not the serialized field. Faithful fix — reflection-wire `gameManager = _gameManager` next to the existing `characterManager` wiring in CorruptionTests / VisionPowerTests / PowerGoldenTraceTests (revealer) + EntrapmentPowerTests (PowerManager). Re-ran green.
- MCP scene wiring: GameManager component instanceID **98088** (GO 98084). Wired `gameManager` on AwakeningLight (98760), GameInfoRevealer (98780), MessageManager (98324), PowerManager (98814), RobotBoardInfo (98078), CorruptionBoardInfo (97892) → 98088; all 6 read-back verified (D-NFR3). Scene saved.

### Completion Notes List

**Task 1 — frozen inventory (re-grep, authoritative over the 2026-06-11 floor):**

| Consumer | Surface | Lane / route | Verdict |
|---|---|---|---|
| 10 GameStates (Lobby, GameIntroduction, RoleAttribution, Awakening, AwakeningRecap, Chaining, Vote, VoteRecap, VictoryConditionCheck, TakeDownThePortal) | `NextGameState`/`SetGameState`/`WaitAFrameAndNextGameState`/`onGameStarted` | lane B (already injected) — narrow via `protected IGameLoop Loop => gameManager` on `GameState` base | **Migrated** |
| AwakeningStateUI (StateUI) | `onGameStarted` | lane B push — narrow via `Loop` on `StateUI` base | **Migrated** |
| AwakeningLight | `onGameStarted` | lane A scene | **Migrated** + registered |
| GameInfoRevealer | `onGameStarted` + `GetGameStates` (was `GameManager.For(nm)`) | lane A scene | **Migrated** + registered |
| PowerManager | `onGameStarted` + `hasGameStarted` + `GetGameState` + `currentGameStateIndex` | lane A scene | **Migrated** + registered |
| MessageManager | `currentDay` | lane A scene | **Migrated** + registered |
| RobotBoardInfo | `onGameStarted` + `GetGameStates` | lane A scene | **Migrated** + registered |
| CorruptionBoardInfo | `onGameStarted` + `GetGameStates` | lane A scene | **Migrated** + registered |
| RoomFog, AnonymeMessageButton, InfoTableSystem, PowersBar | loop surface **+ `CharacterManager.instance`** | lane A possible | **Deferred → Epic 9** (residual CharacterManager locator → guard #1 would flag a half-migration; do both managers in one touch) |
| AwakeningRecapMessages (`currentDay`), CharacterAwakenTimer, RoleAttributionSettingTab/Object (`GetGameStates`) | query/currentDay | **prefab-only** (`find_gameobjects` = 0) | **Deferred → later UI/prefab pass** (lane A physically impossible) |
| ChainingManager, RoleTargetSystem | `GameManager.For(nm)` reads | system | **Deferred → Epic 10** |
| PBoundByInk, PDroolyHealing | `GameManager.For(nm)` query reads | power, lane C | **Deferred** (power query slice — not in the 8.3 command inventory; never claimed migrated) |
| ShutOffGameButton | `ShutOffGameRpc()` | — | **Out of scope** (non-loop control command, not on IGameLoop) |

- **Decision (refines the 8.2 record):** of 8.2's four deferred "mixed" board files, only **RobotBoardInfo + CorruptionBoardInfo** were truly clean (they read the injected `characterManager` field, not the locator) → migrated + registered here. **RoomFog + AnonymeMessageButton** genuinely still hold `CharacterManager.instance` → deferred to Epic 9 alongside InfoTableSystem + PowersBar. "Touch the scene object once" is impossible for these (they need two manager fields), so doing both managers in Epic 9's CharacterManager pass is the clean single-touch path. Honors Poyo's 8.2 "don't half-migrate" intent.
- **AC1:** GameStates/StateUI narrow via the internal `Loop => gameManager` property (D-NFR6) — concrete field kept (states still need `IsServer`/`gameStates`/`currentGameStateIndex`), only the command/event calls route through the slice. 6 scene consumers got lane-A `[SerializeField] GameManager` + `Loop` (and `Query` where they also read state).
- **AC1 (interface completion):** `IGameLoop` gained `UniTask WaitAFrameAndNextGameState()` (verbatim lift — TakeDownThePortalState's third command) and a setter on the two NetworkAction events (`+=`/`-=` lower to assignment; the 8.1 get-only form predated any interface-side subscriber). Behaviour-identical — the setter round-trips the same NetworkAction reference.
- **AC2/AC3 (server authority / NFR5):** zero change to the RPC dispatch — `CallStateMethodRpc`/`CallMethodAfterRpc`/`SwitchGameState` stay GameManager-internal; the 5.1 wire-format golden is unchanged. Every migrated call routes the SAME concrete GameManager via the field/property instead of the static — network-authority code relocated verbatim, never edited.
- **AC4:** registry `All += AwakeningLight, GameInfoRevealer, PowerManager, MessageManager, RobotBoardInfo, CorruptionBoardInfo`; both guards green (`GameManager` already in `InjectedManagerTypes`, so the new fields are wiring-checked automatically).
- **AC5:** fan-in census below.

**Fan-in census (AC5) — remaining concrete-`GameManager` references after 8.3:**
- **The root:** `CompositionRoot` (6 `GameManager.For(nm)` sites — the one sanctioned static; in `SceneWiredOnly`).
- **Injected lane-A fields (expected, guard-checked):** the 6 new `[SerializeField] GameManager` + GameStates'/StateUI's pushed `gameManager` + prior lane-A holders.
- **Recorded exceptions (still on the locator, with reasons):** `RoomFog` / `AnonymeMessageButton` / `InfoTableSystem` / `PowersBar` (`GameManager.instance`, CharacterManager-entangled → Epic 9); `AwakeningRecapMessages` / `CharacterAwakenTimer` / `RoleAttributionSettingTab` / `RoleAttributionSettingObject` (`GameManager.instance`, prefab-only → UI pass); `ChainingManager` / `RoleTargetSystem` (`GameManager.For(nm)` → Epic 10); `PBoundByInk` / `PDroolyHealing` (`GameManager.For(nm)` power query reads); `ShutOffGameButton` (`ShutOffGameRpc`, non-loop command). Production internals (`GameManager.cs`) and tests excluded.

### File List

**Modified (production — interface + adapter):**
- `Assets/Scripts/GameLogic/IGameLoop.cs` — `+UniTask WaitAFrameAndNextGameState()`; `onGameStarted`/`onNewDayPassed` → `get;set;`; `+using Cysharp.Threading.Tasks`.
- `Assets/Scripts/GameLogic/GameManager.cs` — explicit-impl setters for the two NetworkAction events.

**Modified (production — GameStates / StateUI narrowing via `Loop`):**
- `Assets/Scripts/GameLogic/GameState.cs` — `+protected IGameLoop Loop => gameManager`.
- `Assets/Scripts/GameLogic/StateUI.cs` — `+protected IGameLoop Loop => gameManager`.
- `Assets/Scripts/GameLogic/GameStates/{LobbyState,GameIntroductionState,RoleAttributionState,AwakeningState,AwakeningRecapState,ChainingState,VoteState,VoteRecapState,VictoryConditionCheckState,TakeDownThePortalState}.cs` — command/event calls routed through `Loop` (`NextGameState`/`SetGameState`/`WaitAFrameAndNextGameState`/`onGameStarted`).
- `Assets/Scripts/UI/StateUI/AwakeningStateUI.cs` — `onGameStarted` via `Loop`.

**Modified (production — lane-A scene consumers):**
- `Assets/Scripts/FX/AwakeningLight.cs` — `+[SerializeField] GameManager gameManager` + `Loop` + Start assert; `onGameStarted` rerouted.
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` — `+field` + `Loop` + `Query` + Start assert; `onGameStarted` + `GetGameStates` move off `GameManager.For(nm)` onto the field.
- `Assets/Scripts/GameLogic/PowerManager.cs` — `+field` + `Loop` + `Query` + Start assert; `onGameStarted`/`hasGameStarted`/`GetGameState`/`currentGameStateIndex` rerouted.
- `Assets/Scripts/MessageSystem/MessageManager.cs` — `+field` + `Loop` + Awake assert + `using UnityEngine`; `currentDay` rerouted.
- `Assets/Scripts/Board/UI/RobotBoardInfo.cs` — `+field` + `Query` + `Loop` + Start assert; `GetGameStates`/`onGameStarted` rerouted.
- `Assets/Scripts/Board/UI/CorruptionBoardInfo.cs` — `+field` + `Query` + `Loop` + Start assert; `GetGameStates`/`onGameStarted` rerouted.

**Modified (registry + scene):**
- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` — `All += AwakeningLight, GameInfoRevealer, PowerManager, MessageManager, RobotBoardInfo, CorruptionBoardInfo` (+`using FX; using MessageSystem;`).
- `Assets/Scenes/GameScene.unity` — wired `gameManager` on the 6 consumers → GameManager component.

**Modified (test harnesses — wire the new field):**
- `Assets/Scripts/Tests/PlayMode/{CorruptionTests,VisionPowerTests,PowerGoldenTraceTests}.cs` — `SetPrivateField(_revealer, "gameManager", _gameManager)`.
- `Assets/Scripts/Tests/PlayMode/EntrapmentPowerTests.cs` — `SetPrivateField(_powerManager, "gameManager", _gameManager)`.

**Docs:**
- `_bmad-output/implementation-artifacts/8-3-*.md` (this story); `sprint-status.yaml` (`8-3 → review`); `deferred-work.md` (Epic 9 / UI-prefab / Epic 10 deferrals + IGameLoop extension note).

### Change Log

- 2026-06-12 — Story 8.3 implemented: game-loop command/event consumers migrated onto `IGameLoop`. GameStates + StateUI narrow via a `Loop => gameManager` property (no rewiring); 6 clean scene consumers got lane-A `[SerializeField] GameManager` + `Loop`/`Query` narrowing (MCP-wired, read-back verified). `IGameLoop` completed with `WaitAFrameAndNextGameState` + event setters. RPC dispatch untouched (AC2), 5.1 wire-format + 2.11a sequence goldens unchanged. CharacterManager-entangled consumers deferred to Epic 9, prefab-only to a later UI pass (recorded). Registry + both guards green; EM 162 / PM 146. Status → review.
- 2026-06-12 — Code review (gds-code-review, 3 adversarial layers) PASS: Acceptance Auditor PASS (5/5 ACs); Edge Case Hunter (project access) verified every Blind Hunter concern SAFE or pre-existing. 0 patch + 2 defer + 6 dismissed. Status → done.

## Review Findings

Code review (gds-code-review — Blind Hunter / Edge Case Hunter / Acceptance Auditor), 2026-06-12. **Acceptance Auditor: PASS — all 5 ACs satisfied, fan-in census grep-accurate.** The Edge Case Hunter (with project read access) verified each Blind Hunter concern against the surrounding code and found **no real issue**. Outcome: 0 patch + 2 defer + 6 dismissed.

- [x] [Review][Defer] `onGameStarted` subscribe-without-unsubscribe across the migrated lane-A consumers (AwakeningLight/GameInfoRevealer/PowerManager/RobotBoardInfo/CorruptionBoardInfo) + GameStates/AwakeningStateUI — PRE-EXISTING: the `+=` existed on `GameManager.instance`/`.For(nm)` before; 8.3 only swapped the receiver, no `-=` was removed. Same lifecycle-hygiene bucket as 8.2's `OnValueChanged` leak → **Epic 11.4** (broadens its scope to `onGameStarted` subscribers; unsubscribe in `OnDestroy`/`OnNetworkDespawn` per type). Recorded in deferred-work.md.
- [x] [Review][Defer] `GameInfoRevealer` lane-A field drops per-NetworkManager resolution (was `GameManager.For(NetworkManager)`) — Edge-verified SAFE for now: production has a single NetworkManager (field ≡ `For(nm)` target), and the `MultiClientGameFixture` never instantiates GameInfoRevealer (CompositionRoot records it is NOT de-singletonised). Same per-NM-assumption family as the 5.0d façade entries. **Revisit if GameInfoRevealer is ever multi-NM-spawned / de-singletonised.** Recorded in deferred-work.md.

Dismissed (6): `IGameLoop.onGameStarted` `get;set;` "encapsulation regression" (the concrete `GameManager.onGameStarted` FIELD is already `public` and settable — the interface setter is NARROWER than the concrete surface, not broader, and is required for the verbatim `+=`/`-=` idiom; Edge verified `operator+` returns the same instance so the setter round-trips identically); `GameState.Loop`/`StateUI.Loop` NRE-before-injection + no assert parity (pure alias with identical null semantics to the prior direct `gameManager.X`; lane-B pushed deps don't assert by the 7.2 precedent — guard #2 can't wire-check an SO); AwakeningStateUI base-ordering dependency (verified `StateUI.SetupStateUI` stores `this.gameManager` before the `Loop` use); release-build `Assert` strip masking an unwired-field NRE in MessageManager's server RPC (the established lane-A convention since 6.1 — `SceneWiringGuard` is the production gate and is green; the field IS wired + read-back verified); PowerManager evaluates `Query` twice instead of caching (same field, no correctness/behaviour change); setter "foot-gun" + no teardown (defensive combination of the two above, no caller in the diff abuses it).
