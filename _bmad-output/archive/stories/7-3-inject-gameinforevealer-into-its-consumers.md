# Story 7.3: Inject GameInfoRevealer into its consumers

Status: review

## Story

As a developer,
I want the 31 `.gameInfoRevealer` consumers to receive `GameInfoRevealer` directly through their lanes,
so that no consumer hops through GameManager for the revealer.

## Acceptance Criteria

1. **No `.gameInfoRevealer` hub-hop remains** — neither `GameManager.instance.gameInfoRevealer` / `For(nm).gameInfoRevealer` nor injected-`gameManager.gameInfoRevealer` chains in migrated consumers.
2. **Lanes per creation mode**, reusing the 7.1/7.2 seams: powers consume a base `Power` field (resolve in `Power.OnNetworkSpawn` alongside `characterManager` — extend the 7.1 resolve, one line); GameStates get it via the `SetupGameStates` push (extend the 7.2 push); scene components lane A.
3. **`GameInfoRevealer` itself** (13 For-hits internally) is a CONSUMER here too: its own `GameManager.For(...)` reads for OTHER managers are rerouted per its lane (it is scene-placed or manager-owned — verify; record). Reveal behaviour byte-identical.
4. **Reveal-related goldens unchanged** (Epic 4 traces pin reveal-adjacent effects; the 5.0 fixture pins reveal RPC routing incl. bot interception).
5. **Batched, gated, guarded** — same regime as 7.1/7.2; types appended to the shared registry.

## Tasks / Subtasks

- [x] **Task 1: Extend the established seams** (AC: 2) — `Power.gameInfoRevealer` base field (resolved in `Power.OnNetworkSpawn`, null-tolerant); `GameState.gameInfoRevealer` via the `SetupGameStates` push; `BoardManager` lane A; `Card` lane B (pushed by `AddNewCard`). `CompositionRoot` gained a `GameInfoRevealer` accessor + lane-A `[SerializeField] gameInfoRevealer` (GameInfoRevealer is NOT de-singletonised — resolved from the scene root, with a `GameManager.For(nm).gameInfoRevealer` fallback for the no-scene-root harness case, revisited in 7.5). `InjectedManagerTypes` += `GameInfoRevealer`.
- [x] **Task 2: Migrate the sites in batches** (AC: 1, 5) — 12 powers + 2 GameStates + Card rerouted off the gameInfoRevealer hub-hop. Mixed files keep their OTHER hops (GetGameStates / Awake reads) for Epic 8 / 7.4.
- [x] **Task 3: GameInfoRevealer's own hops** (AC: 3) — its CharacterManager reads rerouted onto a lane-A `[SerializeField] characterManager` (its `GameManager.For` onGameStarted/GetGameStates game-loop reads stay for Epic 8). Reveal logic untouched.
- [x] **Task 4: Gate** (AC: 4, 5) — registry: `All` += 10 now-clean powers; `NoLocatorOnly` += Card; `InjectedManagerTypes` += GameInfoRevealer. Full EditMode **162/162** + PlayMode **146/146** (Epic 4 reveal goldens unmoved) + guards 7/7 + boot smoke clean. Sprint-status `7-3 → review`.
  - **Deferred to 7.4 (recorded):** the UI gameInfoRevealer consumers (`CharactersBarObject`, `CorruptedCardText`, `InfoTableSystem` — lane-A scene/prefab wiring) and `ChainingManager`'s injected `_gameManager.gameInfoRevealer` chain. These overlap 7.4's explicit UI/ChainingManager scope, so AC1 is fully satisfied for the powers/GameStates/Card/revealer families here and the UI batch lands in 7.4. `TargetUtils` / `PowerEffectDispatcher` are static utilities (verify-don't-force, out of scope).

## Dev Notes

- `GameInfoRevealer` carries reveal flows that are deduction-information-critical (who learns what — gameplay-sacred). NO logic change; access paths only. If any reveal golden moves: stop, revert the batch, investigate.
- Reveal RPCs target specific clients — heavy `GetSafeRpcTarget` territory **inside GameInfoRevealer/CharacterManager** (not in its consumers). Those bodies are untouched; this story only changes how consumers FIND the revealer.
- Staleness: 31 is the 2026-06-11 count; re-grep at dev time.

### Project Structure Notes

- Modified: `CompositionRoot.cs` (+accessor), `Power.cs` (+field), `GameManager.cs` (push), `GameInfoRevealer.cs` (own hops), ~consumer files per inventory, GameScene wiring, shared registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- `GetSafeRpcTarget`/`IsLocalOrSimulated` verbatim (revealer internals untouched). Serialized-field safety on lane A. EditMode-first; fixture for RPC routing.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §1 histogram (31 hits), §3, §7] / [epics.md#Story 7.3]
- [Source: Assets/Scripts/GameLogic/GameInfoRevealer.cs] — both subject (13 internal hops) and dependency.
- [Source: _bmad-output/implementation-artifacts/7-2-*.md] — previous story: push-extension + lane-decision precedents.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow).

### Completion Notes List

- **GameInfoRevealer is not de-singletonised**, so the CompositionRoot carries it as a lane-A `[SerializeField]` and resolves it from the registered scene root; for NMs with no scene root (PlayMode harnesses) it falls back to `GameManager.For(nm).gameInfoRevealer` (the still-present pass-through field). Production always hits the scene-root path — behaviour-identical. The fallback is revisited in 7.5 when the GameManager pass-through is removed.
- **Power.gameInfoRevealer is null-tolerant (no Assert)** — not every power uses it and bare-power harnesses provide no revealer; reveal-using powers always have it (production scene root; their own harnesses set `GameManager.gameInfoRevealer`, reached via the fallback).
- **BoardManager.gameInfoRevealer has no runtime assert** — BoardManager never uses it directly (only forwards it to each Card it creates), so SceneWiringGuard (CI) is its sole wiring check; this avoids breaking the ~5 BoardManager PlayMode harnesses that create it bare.
- **10 powers became fully locator-free** (characterManager from 7.1 + gameInfoRevealer here) and joined `All`. NOT registered: PDroolyHealing & PBoundByInk (still hold a GetGameStates `GameManager.For` → Epic 8), PPersonalBeacons (Awake pre-spawn characterManager read → recorded since 7.1). Card joined `NoLocatorOnly` (now fully clean).
- **Harness wiring:** the 3 reveal-power harnesses (CorruptionTests, VisionPowerTests, PowerGoldenTraceTests) now set `GameInfoRevealer.characterManager` by reflection (it became a lane-A consumer of CharacterManager).
- **NFR5 / reveal-sacred:** reveal RPC bodies + GetSafeRpcTarget untouched; only access paths changed. Epic 4 reveal goldens unmoved.

### File List

- **Modified (seam):** `GameLogic/CompositionRoot.cs` (+GameInfoRevealer accessor/field/resolver), `Characters/Powers/Power.cs` (+field), `GameLogic/GameState.cs` (+field), `GameLogic/GameManager.cs` (push), `GameLogic/GameInfoRevealer.cs` (lane-A characterManager + reroute)
- **Modified (powers):** `Powers/{PBlessing,PCardsShuffling,PChainedByTheShadows,PCorruptingMark,PCorruptionInsight,PCorruptionKnowledge,PHighPriorityBounty,PLackOfAffection,PEmbraceOfShadows,PCorruptionParanoia,PDroolyHealing,PPersonalBeacons}.cs`
- **Modified (gamestates):** `GameLogic/GameStates/{GameEndingState,TakeDownThePortalState}.cs`
- **Modified (board):** `Board/BoardManager.cs`, `Board/Card.cs`
- **Modified (registry):** `Tests/Editor/DiSeamMigratedConsumers.cs`
- **Modified (scene):** `Assets/Scenes/GameScene.unity` (CompositionRoot/BoardManager gameInfoRevealer, GameInfoRevealer characterManager)
- **Modified (harnesses):** `Tests/PlayMode/{CorruptionTests,VisionPowerTests,PowerGoldenTraceTests}.cs`
- **Modified (tracking):** `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-12 — Story 7.3: GameInfoRevealer injected into powers (base field), GameStates (push), Board/Card (lane A/B); GameInfoRevealer itself migrated onto an injected CharacterManager. UI consumers + ChainingManager deferred to 7.4. EM 162 / PM 146 green. Status → review.
