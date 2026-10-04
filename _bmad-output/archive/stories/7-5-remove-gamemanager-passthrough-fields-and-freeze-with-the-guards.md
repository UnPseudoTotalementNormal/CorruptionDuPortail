# Story 7.5: Remove the GameManager pass-through fields + freeze with the guards

Status: done

## Story

As a developer,
I want the pass-through accessors (`characterManager`, `gameInfoRevealer`, `chainingManager`, `charactersBar`, `powersBar`) removed from GameManager,
so that the hub cannot quietly come back (D-NFR4 — never both paths as permanent debt).

Epic 7's closing move: the strangler's "remove the old path" step.

## Acceptance Criteria

1. **Destructive deletion.** The five pass-through fields/accessors are DELETED from `GameManager`. The compiler enumerates every straggler (CS0117/CS1061); each is REROUTED per its lane (never patched back, never re-exposed).
2. **GameManager's wiring of those managers survives where genuinely needed**: if GameManager itself legitimately *uses* (not exposes) one of them internally, it keeps a private injected ref — exposure is what dies, not GameManager's own dependency.
3. **Surface check:** after deletion, `GameManager`'s public surface = game-loop/state machine + NGO plumbing only (paste the public-member census into Dev Agent Record — it is Epic 8.1's input).
4. **Guards frozen.** Shared registry covers every Epic 7 migrated type; both guards green; the 6.3 For-rules make a re-introduced hub-hop in ANY migrated type red.
5. **Green gate:** full suite + fixture + boot smoke-test unchanged at baseline. No golden moves. The 7.4 closing inventory is re-run and shows zero pass-through references.

## Tasks / Subtasks

- [x] **Task 1: Delete + compile-enumerate** (AC: 1) — deleted the five members; `read_console` listed 27 stragglers, ALL in `Tests.PlayMode` (zero production stragglers → 7.1-7.4 fully migrated prod). Rerouted each per lane (no re-exposure).
- [x] **Task 2: Internal-use audit** (AC: 2) — the four GameManager uses internally (`characterManager`, `gameInfoRevealer`, `chainingManager`, `charactersBar`) demoted `public` → `[SerializeField] private`, SAME field name (scene binding verified intact by YAML read-back); `powersBar` had no internal use and was deleted entirely (its scene ref is now an orphan key Unity ignores).
- [x] **Task 3: Census + freeze** (AC: 3, 4) — public-member census captured below; both DI guards green (EM); registry already complete (7.5 is a deletion, no new migrated consumer).
- [x] **Task 4: Gate** (AC: 5) — full suite EM 162 / PM 146 + fixture + boot smoke green at baseline, no golden moves; sprint-status `7-5 → review` (epic-7 → done flip flagged to Poyo — left `in-progress` to match the 7.1-7.4 review board); commit pending.

## Dev Notes

- **The serialization trap is the whole risk here:** the five members are (likely) `[SerializeField]`-backed on the GameScene GameManager object. Deleting the FIELD orphans scene data silently (acceptable — data no longer needed) but deleting/renaming a field GameManager still NEEDS internally would wipe its binding. Task 2's rule: demote public → `[SerializeField] private`, SAME field name, zero scene rewiring needed. Verify each by read-back anyway.
- If a straggler turns out to be in code that cannot take injection (static util, serializable VO) — verify-don't-force: record it, route it through the root in its narrowest scope, or leave a documented exception. Do not contort.
- This commit is the epic's point of no return — re-read `feedback_review_required_gate`: not tagged REVIEW-REQUIRED (compiler-enumerated mechanical change, full net), but treat the gate seriously: zero red, zero console warnings.

### Project Structure Notes

- Modified: `GameManager.cs` (surface shrink), straggler files (if any), registry. Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- Rename serialized field w/o `[FormerlySerializedAs]` = silent data wipe — Task 2's same-name rule. `[SerializeField] private`, never public fields.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §7 step 4-5 (destructive deletion + remove old path)] / [epics.md#Story 7.5]
- [Source: _bmad-output/implementation-artifacts/7-4-*.md] — previous story: the closing inventory this story consumes.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story)

### Debug Log References

- Post-deletion compile (`read_console`): **27 errors, all `Tests.PlayMode`** (CS1061 on `characterManager`/`gameInfoRevealer`). **Zero production stragglers** — proves 7.1-7.4 migrated all production consumers off the hub.
- Final compile after reroute: **0 errors / 0 warnings**.
- Gate: EditMode **162/162**, PlayMode **146/146** (baseline EM 162 / PM 146 — unchanged, no golden moves). Boot smoke (`manage_editor` play on GameScene): **0 errors, no CompositionRoot assert**.
- Scene binding read-back (`GameScene.unity`, GameManager &704980999): `gameInfoRevealer`/`charactersBar`/`chainingManager`/`characterManager` all still bound to non-zero fileIDs; `powersBar` line now an orphan key (deleted field) Unity ignores. No silent data wipe.

### Completion Notes List

- **AC1 (destructive deletion):** the five `public` pass-throughs deleted from `GameManager`. Compiler enumerated every straggler; each rerouted per lane, none patched back / re-exposed.
- **AC2 (internal-use survives):** GameManager genuinely uses four of them internally — `characterManager` (RPC target resolution `GetSafeRpcTarget`, disconnect handling, `SetupGameStates` push), `gameInfoRevealer`/`chainingManager`/`charactersBar` (`SetupGameStates` push to cloned states). These became `[SerializeField] private` with the SAME field name → name-based scene binding preserved (verified). `powersBar` had no internal use → deleted outright.
- **AC3 (surface check):** post-deletion **GameManager public-member census** (Epic 8.1 input):
  - `static GameManager instance { get; private set; }`, `static GameManager For(NetworkManager)` — NGO de-singleton plumbing
  - `SerializedDictionary<GameState,GameStateSettings> gameStates`, `NetworkVariable<int> currentGameStateIndex { get; private set; }`, `bool ignoreGameLoop`, `int gameLoopCount { get; private set; }`, `int currentDay`, `bool hasGameStarted`, `NetworkAction onGameStarted`, `NetworkAction onNewDayPassed`
  - `OnNetworkSpawn` / `OnNetworkDespawn` / `OnDestroy` (NGO lifecycle)
  - `SetGameState(GameState)`, `SetGameState(Type)`, `WaitAFrameAndNextGameState()`, `NextGameState(bool)`, `PreviousGameState()`, `GetGameState(int)`, `GetGameStates(Type)`, `GetGameStateIndex(GameState)`, `GetClosestPreviousState<T>()`
  - `DoStateMethodRpc(...)` ×2, `ShutOffGameRpc()` (RPC dispatch)
  - → **game-loop / state-machine + NGO plumbing only. Zero manager pass-throughs.**
- **AC4 (frozen):** both guards green (EM). Registry already covers every Epic 7 migrated type (7.5 deletes the hub, adds no consumer). The hub is now frozen at TWO levels: the compiler (private/deleted ⇒ external `GameManager.instance.characterManager` no longer compiles) AND `DiSeamNoLocatorGuard` (source-scan on migrated types). Closing-inventory sweep: every residual `.gameInfoRevealer`/`.characterManager`/etc. reference is a comment, a class's own field, or GameManager's own internal push — zero live pass-through reads.
- **AC5 (green gate):** full suite + fixture + boot smoke unchanged at baseline; 7.4 closing inventory re-run → zero pass-through references.
- **CompositionRoot:** removed the `ResolveGameInfoRevealer` GameManager fallback (the explicit "revisited in 7.5" crutch). Production always has a registered scene-placed root; the fallback was a PlayMode-harness-only path. The 3 revealer harnesses now register their own `CompositionRoot` via `NetworkTestHelper.RegisterCompositionRoot` (production-faithful).
- **Test reroute pattern:** PlayMode harnesses wired GameManager's now-private deps via the existing `ReflectionHelper.SetPrivateField(_gameManager, "characterManager"|"gameInfoRevealer", …)` (Tests.PlayMode has no `InternalsVisibleTo`, so reflection — already the repo's harness idiom).
- **Epic-7 status:** functionally complete (the GameManager-as-locator hub is dismantled + frozen). Left sprint `epic-7: in-progress` rather than auto-flipping to `done`, since 7.1-7.4 sit at `review` — flip to `done` is Poyo's call once the 7.x slice is accepted/merged.

### File List

**Production (behaviour-affecting surface):**
- `Assets/Scripts/GameLogic/GameManager.cs` — deleted 5 public pass-throughs (4 → `[SerializeField] private` same-name, `powersBar` removed); dropped now-unused `using Board.UI.PowerBar;`.
- `Assets/Scripts/GameLogic/CompositionRoot.cs` — removed the GameManager `gameInfoRevealer` fallback in `ResolveGameInfoRevealer`.

**Test infrastructure:**
- `Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs` — added `RegisterCompositionRoot(...)` helper (+`Characters`/`GameLogic` usings).

**Test stragglers rerouted (reflection-wire of GameManager's private deps):**
- 3 revealer harnesses (characterManager + gameInfoRevealer reflection + CompositionRoot registration + teardown): `VisionPowerTests.cs`, `CorruptionTests.cs`, `PowerGoldenTraceTests.cs`.
- characterManager reflection-write only: `PowerTests.cs`, `BoardTests.cs`, `ChatManagerTests.cs`, `EntrapmentPowerTests.cs`, `VoteTallyGoldenMasterTests.cs`, `RoleAssignmentGoldenMasterTests.cs`, `SnapshotDifferentialTests.cs`, `GameSnapshotBuilderLosslessnessTests.cs`, `Characters/WinningConditionHarnessFidelityTests.cs`, `Characters/WinningConditionGoldenMasterTests.cs`, `SnapshotOracle/WinningConditionSnapshotOracleTests.cs`, `GameLogic/GameStates/AwakeningStateIsolationTests.cs`, `GameLogic/GameStates/VoteInsertionOrderTests.cs`, `Migration/WOmniscienceMigrationTests.cs`, `Migration/WAnomalyMigrationTests.cs`, `Migration/WChosenMigrationTests.cs`, `Migration/WMarginalMigrationTests.cs`.
- characterManager reflection-write + read reroute to local: `Migration/VictoryLoopRepointTests.cs`.
- characterManager reflection-write (PascalCase host vars): `Desingleton/CoexistenceGateTests.cs`, `Desingleton/MultiClientGameFixture.cs`.

**Docs:**
- `_bmad-output/implementation-artifacts/7-5-remove-gamemanager-passthrough-fields-and-freeze-with-the-guards.md` (this story)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (`7-5 → review`)

### Change Log

- 2026-06-12 — Story 7.5 implemented: deleted the 5 GameManager pass-through accessors (the strangler's "remove old path", D-NFR4). Four demoted to `[SerializeField] private` (GameManager's own deps), `powersBar` removed. Zero production stragglers (compiler-proven); 23 PlayMode harnesses + the CompositionRoot fallback rerouted. EM 162 / PM 146 + boot smoke green, no golden moves. Status → review.
- 2026-06-12 — Code review (gds-code-review, 3 layers) PASS: all 5 ACs satisfied, no hard violations. Applied 1 hardening patch (RegisterCompositionRoot Singleton assert); the registry-miss diagnostic was reconsidered and dropped (would cry wolf on null-tolerant powers) → deferred. Re-gate green. Status → done.

## Review Findings

Code review (gds-code-review — 3 adversarial layers: Blind Hunter / Edge Case Hunter / Acceptance Auditor), 2026-06-12. **Acceptance Auditor: all 5 ACs satisfied, zero hard violations.** Outcome: 1 patch (applied) + 3 defer + 5 dismissed.

- [x] [Review][Patch] Enforce RegisterCompositionRoot call-after-StartHost contract [Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs] — calling before StartHost would silently register a root that resolves for nobody (CompositionRoot.Awake binds to NetworkManager.Singleton). Added `Assert.IsNotNull(NetworkManager.Singleton, …)`. **Applied.**
- [x] [Review][Defer] Diagnose silent-null on CompositionRoot registry-miss [Assets/Scripts/GameLogic/CompositionRoot.cs] — Blind + Edge flagged the removed fallback's failure mode (registry miss → null revealer → NRE) as the top latent concern. A `LogWarning` was prototyped then **reverted**: `Power.gameInfoRevealer` is null-tolerant by design (7.3), so `Power.OnNetworkSpawn` resolving null in a bare harness is legitimate — the warning would fire (cry wolf) in known-good tests like PowerTests/EntrapmentPowerTests. Latent future-test-authoring concern only (current tree verified safe, production always has a root). Logged in deferred-work.md.
- [x] [Review][Defer] CompositionRoot double-registration is silent (last-writer-wins) [Assets/Scripts/GameLogic/CompositionRoot.cs:93] — deferred, pre-existing 6.3 registry design; backstops adequate per Edge Case Hunter. Logged in deferred-work.md (Epic 8/10).
- [x] [Review][Defer] VoteState/GameState pushed deps are public fields [Assets/Scripts/Tests/PlayMode/VoteTallyGoldenMasterTests.cs] — deferred, pre-existing lane-B push target (7.2 design), not a hub. Logged in deferred-work.md.

Dismissed (5): `powersBar` orphan (no live reader — compiler + grep verified); `using System.Reflection` (still used by `CallMethodAfterRpc`); residual private-field readers (clean compile proves none — CS0122 otherwise); pre-spawn wiring ordering (identical to pre-7.5, reflection-set vs field-set is same timing); `ReflectionHelper.SetPrivateField` silent-no-op on a field rename (already caught by `CompositionRoot.Awake` non-null asserts in the Editor test runner — the green PM 146 run proves wiring resolves).
