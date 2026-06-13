# Story 7.2: Inject CharacterManager into GameStates, Board, and Character

Status: review

## Story

As a developer,
I want the remaining `.characterManager` consumers (GameStates, board components, `Character`) to receive it through their lanes,
so that all non-power `.characterManager` locator hops are gone.

## Acceptance Criteria

1. **GameStates (lane B).** GameStates currently hop via their injected `gameManager.characterManager` (an injected-reference pass-through — still a hub-hop targeted by D1). `SetupGameStates` (the existing composition-root push, `GameManager.cs:218` area) pushes `characterManager` directly (new property alongside `gameManager`, or extend to a small services struct — pick once, record). GameStates consume the direct ref; no `gameManager.characterManager` chain remains in any `GameState`/`StateUI`.
2. **Board components (lane A).** Scene-placed board consumers (`BoardManager` 5 For-hits, `Card.cs` 6 instance-hits, others surfaced by the compiler) get `[SerializeField] private CharacterManager characterManager;` — append-only, every instance MCP-wired + read-back verified, `Awake` assert, no fallback.
3. **Character (lane C).** `Character.cs` (5 For-hits) resolves once in `OnNetworkSpawn` via the root; field-consumed thereafter.
4. **NFR5 verbatim.** `CharacterManager`-side bot flow untouched; any consumer-side `GetSafeRpcTarget`/`IsLocalOrSimulated` code relocated verbatim if a method moves (none expected — only access paths change).
5. **Batched, gated, guarded.** Compiler-enumerated batches; suite + fixture + goldens unchanged per batch; migrated types appended to the shared registry; SceneWiringGuard covers every new lane A field.

## Tasks / Subtasks

- [x] **Task 1: GameStates push extension** (AC: 1) — added `GameState.characterManager` (pushed by `SetupGameStates` alongside `gameManager`, and into `StateUI` via `GameState.OnStateCreated`). Rerouted `gameManager.characterManager` → `characterManager` in 10 GameStates + 2 StateUIs (GameIntroductionUI, AwakeningStateUI). 2.11a sequence golden unmoved.
- [x] **Task 2: Character lane C** (AC: 3) — `Character.characterManager` resolved once in `OnNetworkSpawn` via the composition root. NO assert (Character already guards on a null manager — preserved); dropped the now-vestigial `GameManager.For(nm) == null` guard (L66) since the only GameManager use it protected is gone.
- [x] **Task 3: Board lane A/B** (AC: 2) — `BoardManager` = lane A `[SerializeField] characterManager`, MCP-wired in GameScene + read-back verified; assert moved to Start (first use) so PlayMode harnesses can wire by reflection after AddComponent. **`Card` lane decision (recorded precedent):** prefab-instantiated, its CharacterManager target is a scene object → lane A impossible (prefab can't ref scene) and not NGO-spawned → **lane B** (sole creator `BoardManager.AddNewCard` pushes via `Card.Initialize`, the local-identity subscription deferred Awake→Initialize). Card still holds a `GameManager.instance.gameInfoRevealer` hop → mixed, NOT registered until 7.3.
- [x] **Task 4: Sweep + gate** (AC: 5) — registry: `All` += Character, BoardManager; Card/mixed left for 7.3. SceneWiringGuard covers BoardManager.characterManager. Full EditMode **162/162** + PlayMode **146/146** + guards 7/7 + boot smoke clean. Fixed the PlayMode harnesses the new injected fields/asserts touched (see Completion Notes). Sprint-status `7-2 → review`.

## Dev Notes

- **The nuance that makes this story:** GameStates' hops go through an INJECTED `gameManager` — guard #1 won't flag them (no static). They are still D1 targets because 7.5 deletes the `characterManager` pass-through field itself; migrate them here or 7.5's compile errors will enumerate them anyway. Doing it now keeps 7.5 small.
- **`Card.cs` (6 `.instance` hits)** is the densest board consumer — check whether Cards are scene-placed or prefab-instantiated at runtime (pooled?): scene/prefab-placed → lane A on the prefab asset only if the ref target is also prefab-internal — otherwise the ref targets a scene object and Cards must take lane B (push at creation/pool-checkout) or lane C if NGO-spawned. **Decide per the creation mode, record the decision.** This is the first consumer where the lane decision is non-obvious — the recorded decision becomes the precedent for pooled objects.
- Staleness: re-derive consumer lists at dev time (7.1 will have moved power-adjacent code).

### Project Structure Notes

- Modified: `GameManager.cs` (SetupGameStates push), GameStates/StateUIs touched, `Character.cs`, board files, GameScene (lane A wiring), shared registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Serialized-field safety (append-only + MCP wiring + verify) — D-NFR3, load-bearing on Task 3.
- Networked init in `OnNetworkSpawn` (Character); `OnNetworkDespawn` unsubscribes stay as-is.
- EditMode-first testing; fixture for replicated behavior.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3, §7] / [epics.md#Story 7.2]
- [Source: Assets/Scripts/GameLogic/GameManager.cs SetupGameStates] — the lane B push to extend.
- [Source: Assets/Scripts/Characters/Character.cs] — lane C subject (5 For-hits).
- [Source: Assets/Scripts/Board/Card.cs] — the lane-decision precedent case (6 instance-hits).
- [Source: _bmad-output/implementation-artifacts/7-1-*.md] — previous story: base-field pattern, batch cadence.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Claude Code, gds-dev-story workflow).

### Debug Log References

- Full EditMode 162/162; full PlayMode 146/146; DiSeamGuard + SceneWiringGuard 7/7; boot smoke clean.
- Two PlayMode iterations fixed harness fallout from the new injected fields/asserts (recorded below).

### Completion Notes List

- **Scope split honoured.** 7.2 = GameStates/StateUIs (lane B) + BoardManager (lane A) + Card (lane B) + Character (lane C). The broad UI/Chat/Message/Note/FX/WinningConditions/PowerManager `.characterManager` locators belong to 7.3 (gameInfoRevealer) / 7.4 (UI hub-hops) and were left untouched.
- **GameStates lane B.** `GameState.characterManager` is pushed by the existing composition-root `SetupGameStates` clone-push (alongside `gameManager`), and propagated to `StateUI` by `GameState.OnStateCreated`. These were never static-locator users (they hub-hopped through the injected `gameManager`), so they are NOT in the guard registry — this just removes the pass-through chain ahead of 7.5 deleting `GameManager.characterManager`.
- **Character lane C, null-tolerant.** Unlike the powers, `Character` already guarded on a null CharacterManager (it can spawn before the manager in degenerate cases), so the resolved field is NOT asserted — preserving behaviour. `For(nm)` is a stable per-NM singleton, so caching once equals the old per-call re-resolution. Dropped the vestigial `GameManager.For(nm) == null` guard whose only purpose was protecting the now-removed GameManager access.
- **Card — the recorded lane precedent for prefab/pooled objects.** A Card is `Instantiate`d from a prefab by the SOLE creator `BoardManager.AddNewCard`; its CharacterManager dependency targets a SCENE object. Lane A is physically impossible (a prefab cannot serialize a scene-object reference); it is not NGO-spawned, so lane C does not apply. → **lane B**: the creator pushes CharacterManager via `Card.Initialize`, and the `onLocalIdentityChanged` subscription is deferred from Awake to Initialize (same frame, before use). This is the precedent for future pooled/prefab consumers.
- **BoardManager assert at Start, not Awake (recorded deviation).** Lane A normally asserts in Awake (LightManager). BoardManager is `AddComponent`-created bare in many PlayMode harnesses, and AddComponent runs Awake synchronously, so the field cannot be wired before an Awake assert. The assert moved to Start (the first use of the field); SceneWiringGuard remains the authoritative CI wiring check.
- **Harness fallout fixed (the test touches):** (a) GameState harnesses that wired only the old `gameManager.characterManager` now also set the state's injected `characterManager` (`RoleAssignmentGoldenMasterTests`, `VoteInsertionOrderTests`, `VoteTallyGoldenMasterTests`); (b) BoardManager harnesses now wire the serialized `characterManager` by reflection right after AddComponent (`BoardTests`, `CorruptionTests`, `EntrapmentPowerTests`, `PowerGoldenTraceTests`, `VisionPowerTests`). One ordering bug caught and fixed: `VoteTallyGoldenMasterTests` had assigned `_voteState.characterManager` before `_characterManager` was created — moved after.
- **Behaviour-preserving:** 2.11a sequence golden + Epic 4 power goldens + all vote/role goldens green; no golden moved.

### File List

- **Modified (lane B GameStates seam):** `Assets/Scripts/GameLogic/GameState.cs`, `Assets/Scripts/GameLogic/StateUI.cs`, `Assets/Scripts/GameLogic/GameManager.cs` (SetupGameStates push)
- **Modified (GameStates):** `GameLogic/GameStates/{AwakeningState,VoteState,RoleAttributionState,TakeDownThePortalState,GameEndingState,ChainingState,GameIntroductionState,LobbyState,VictoryConditionCheckState,VoteRecapState}.cs`
- **Modified (StateUIs):** `UI/StateUI/{GameIntroductionUI,AwakeningStateUI}.cs`
- **Modified (lane C):** `Assets/Scripts/Characters/Character.cs`
- **Modified (Board lane A/B):** `Assets/Scripts/Board/BoardManager.cs`, `Assets/Scripts/Board/Card.cs`
- **Modified (registry):** `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs`
- **Modified (scene wiring):** `Assets/Scenes/GameScene.unity` (BoardManager.characterManager)
- **Modified (test harnesses):** `Tests/PlayMode/{BoardTests,CorruptionTests,EntrapmentPowerTests,PowerGoldenTraceTests,VisionPowerTests,RoleAssignmentGoldenMasterTests,VoteTallyGoldenMasterTests}.cs`, `Tests/PlayMode/GameLogic/GameStates/VoteInsertionOrderTests.cs`
- **Modified (tracking):** `_bmad-output/implementation-artifacts/sprint-status.yaml`

### Change Log

- 2026-06-12 — Story 7.2: CharacterManager injected into GameStates/StateUIs (lane B push), BoardManager (lane A), Card (lane B, recorded prefab precedent), and Character (lane C). Card + mixed gameInfoRevealer holders deferred to 7.3. EM 162 / PM 146 green. Status → review.
