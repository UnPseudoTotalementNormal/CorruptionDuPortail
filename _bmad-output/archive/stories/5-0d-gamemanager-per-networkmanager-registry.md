# Story 5.0d: `GameManager` per-NetworkManager registry + primary façade (the monster)

Status: done

## Story

As a developer,
I want the registry + façade pattern proven in 5.0c applied to `GameManager`, with its ~95 gameplay call sites migrated,
so that the largest singleton (161 usages / 67 files) no longer pins the whole game graph to one process-global instance.

## Acceptance Criteria

1. Registry + NM-aware guard + `For(NetworkManager)` land first as one commit, observably no-op in production — **reuse the exact design (A or B) chosen by the 5.0c probe**, do not re-decide.
2. Gameplay call sites (Powers/Characters/Board/GameLogic) migrate in gated batches; GameStates use their already-injected `gameManager` (zero static reads left in `GameLogic/GameStates/`).
3. The ~66 UI/presentation usages across 28 files stay on the `instance` façade (explicit non-goal; follow-up pass documented).
4. The 2.11a sequence golden and all Waves 1–3 goldens pass **unchanged** — if a golden moves, stop, revert the batch, investigate.
5. `instance` lifecycle for the primary NM is unchanged: claimed at Awake (or per Design B), nulled at `OnNetworkDespawn` (`GameManager.cs:85-88`).
6. Full EM + PM suite + console clean after every batch; one conventional commit per batch.

## Tasks / Subtasks

- [x] Task 1: Record test baseline (same procedure as 5.0a Task 1). — 155 EM + 138 PM, console clean.
- [x] Task 2: Read 5.0c's Dev Agent Record — inherit the Awake-guard design decision and any surprises (AC: 1). — Design B inherited (owner not visible at Awake).
- [x] Task 3: Batch 1 — registry + guard in `GameManager.cs`, no call-site change (AC: 1, 5). Gate + commit. — b754fc7.
- [x] Task 4: Batch 2 — GameStates: replace every `GameManager.instance` inside `GameLogic/GameStates/*.cs` with the injected `gameManager` property (AC: 2). Gate + commit. — df33136 (GameStates + GameInfoRevealer + ChainingManager).
- [x] Task 5: Batch 3 — Powers + WinningConditions (AC: 2). Gate + commit. — 90988d4 (48 migrated; TargetUtils/PowerEffectDispatcher/WinningConditions deferred on façade).
- [x] Task 6: Batch 4 — `Character.cs`, `PowerEffectDispatcher`, Board, remaining GameLogic NetworkBehaviours (AC: 2). Gate + commit. — fb2e5be (Character/BoardManager/RoleTargetSystem; Card + PowerManager + PowerUsageManager reclassified MonoBehaviour-on-façade).
- [x] Task 7: Verify UI untouched (`git diff --stat` contains no `UI/`, `FX/`, `TooltipSystem/`, `NoteSystem/`, `MessageSystem/`, `FocusSystem/` file) and goldens unchanged (AC: 3, 4). — CLEAN; 30 gameplay .cs only; suite green every batch.

## Dev Notes

### Current state of `GameManager.cs` (read fully before editing)

- `GameManager : NetworkBehaviour` (`GameManager.cs:27`), `public static GameManager instance { get; private set; }` (line 29 — property with private setter, unlike CharacterManager's field).
- `Awake()` (51-60): duplicate-destroy guard + claim + `onNewDayPassed += () => gameLoopCount++;` — **this listener attachment must stay in Awake and must still run for foreign replicas** (it mutates per-instance state, not static).
- `OnNetworkSpawn` (62-76): `SetupGameStates()`, server-side `currentGameStateIndex.Value = 0` + `OnStartStateServer` + disconnect callback, then client-side `OnStartStateClient`. The registry claim goes at the **very top**, before `SetupGameStates()`.
- `OnNetworkDespawn` (78-91): unsubscribes disconnect callback; nulls `instance` if self.
- Holds `currentGameStateIndex` (`NetworkVariable<int>`, line 39) — the exact variable stories 5.3/5.4 will move; do not touch its semantics.
- Holds the injected-deps fields the rest of the graph can use: `gameInfoRevealer`, `charactersBar`, `powersBar`, `chainingManager`, `characterManager` (lines 31-35).
- Field-initializer NetworkActions (48-49) — left unbound by 5.0b decision; do not touch.

### Batch 1 — registry + guard

Mirror 5.0c's Batch 1 exactly (same `s_byNetworkManager` dictionary, same `For()` with primary-falls-back-to-`instance`, same `SubsystemRegistration` static reset, same `OnNetworkSpawn` claim / `OnNetworkDespawn` + `OnDestroy` cleanup). Two GameManager-specific deltas:

1. `instance` is a `{ get; private set; }` property — keep it a property (callers bind to the getter; changing to a field is a source-compat break for none but keep the shape anyway).
2. `GameManager` has no `OnDestroy` override today (CharacterManager has one). Add one for registry cleanup; call `base.OnDestroy()` (NetworkBehaviour defines it virtual) — match CharacterManager's override style (`public override void OnDestroy()`).
3. In the foreign-replica path, **do not skip** the `onNewDayPassed += () => gameLoopCount++;` line — it is per-instance wiring, required on every replica.

### Batch 2 — GameStates (the easy 90%)

Inside any `GameLogic/GameStates/*State.cs`, `GameManager.instance` → `gameManager` (the injected property, `GameState.cs:17`, assigned in `SetupGameStates` before any lifecycle call). Known sites: `TakeDownThePortalState.cs` (3), `GameIntroductionState.cs` (1), `GameEndingState.cs` (1). Grep the folder to catch any not in this list (`Grep pattern: GameManager\.instance, glob: Assets/Scripts/GameLogic/GameStates/**`). After this batch that grep must return zero.

Also in this batch, the non-state GameLogic NetworkBehaviours: `GameInfoRevealer.cs` (8 uses) → `GameManager.For(NetworkManager)`; `ChainingManager.cs` (1) → `GameManager.For(NetworkManager)`.

### Batch 3 — Powers + WinningConditions

- All `Characters/Powers/P*.cs` + `Power.cs` + `PowerComponent.cs` + `TargetUtils.cs` + `PowerEffectDispatcher.cs`: `GameManager.instance` → `GameManager.For(NetworkManager)` (they are all NetworkBehaviours; verify each class declaration when you open the file). High-count files: `PDroolyHealing.cs` (6), `PCorruptingMark.cs` (4), `PHighPriorityBounty.cs` (4), `TargetUtils.cs` (4), `Power.cs` (4), `PowerEffectDispatcher.cs` (4).
- `Characters/WinningConditions/W*.cs` (5 uses across 4 files): these are **not** NetworkBehaviours (they are serializable condition classes migrated to snapshots in Epic 2). Open each, identify where the remaining `GameManager.instance` reads sit. If the enclosing method has access to a `GameManager` or a `NetworkBehaviour` context parameter, thread it. If not, **leave the read on the façade and list it in the Dev Agent Record under "deferred façade reads"** — do not invent new plumbing through serialized types; changing their fields touches wire format (guarded by `StateDispatchWireFormatGuardTests` for states, and `INetworkSerializable` shape matters for conditions).

### Batch 4 — Characters + Board

`Character.cs` (2), `Board/Card.cs` (2), `Board/BoardManager.cs` (5), `RoleTargetSystem/RoleTargetSystem.cs` (1) → `GameManager.For(NetworkManager)` (all NetworkBehaviours). `GameLogic/PowerUsageManager.cs` (2) and `GameLogic/PowerManager.cs` (6) are **MonoBehaviours** — leave on the façade (no NM context; they are scene-local, not replicated).

### Stay-on-façade list (28 UI/presentation files — DO NOT EDIT)

`Board/UI/**` (SkipButton, RobotBoardInfo, CorruptionBoardInfo, PowersBar, CharactersBar*, CharacterAwakenTimer), `UI/**` (InfoTableSystem, AnonymeMessageButton, SelectPanelPlayer, RoleAttributionSetting*, Misc/*, StateUI/**), `MessageSystem/**`, `TooltipSystem/TooltipLinkParser.cs`, `FX/**` (AwakeningLight, RoomFog), `NoteSystem/**`, `FocusSystem/FocusManager.cs`, `Board/LightManager.cs`, `Board/BoardCameraSystem/BoardCameraManager.cs`, `ChatSystem/**`. Plus `Assets/Scripts/Tests/PlayMode/GameManagerTests.cs` (2 uses — tests target the primary façade by design; leave them).

### Golden non-negotiables (AC: 4)

The 2.11a PlayMode sequence golden pins the `OnEndStateClient → write currentGameStateIndex.Value → OnStartStateClient` ordering. Your batches must not move any code relative to that write. The Batch 1 registry claim at the top of `OnNetworkSpawn` is BEFORE `SetupGameStates()` and the index write — fine. Nothing else in this story touches `OnNetworkSpawn` ordering. If `GameLoopMachine`-related or `StateDispatchWireFormat` tests move: revert, bisect.

### Gate procedure (per batch)

`refresh_unity` (compile=request) → `read_console` errors empty → full EM + PM at baseline → commit. The PlayMode suite is the real gate here (it boots the host harness through `GameManager`); EditMode alone proves nothing for this story.

### Commit message templates

Batch 1: `refactor(net): add per-NetworkManager registry to GameManager` — body: same design as CharacterManager (cite A/B), production no-op argument, issue #50.
Batch 2: `refactor(game): use injected gameManager in GameStates instead of the static instance` — body: injection existed since SetupGameStates; static reads were vestigial.
Batches 3-4: `refactor(net): resolve GameManager via For(NetworkManager) in <scope>`.
No `UX:` lines. No AI attribution.

### Project Structure Notes

- Prerequisite: 5.0c merged (design decision + pattern precedent).
- Branch: `epic5-network`.

### Project Context Rules (extracted from project-context.md)

- Server authority strict: `currentGameStateIndex` written server-side only — this story must not move/duplicate that write.
- Domain reload disabled → `SubsystemRegistration` reset for the new static dictionary.
- `OnNetworkSpawn` cross-object order not guaranteed — do not add cross-manager lookups into `Awake`.
- Hot paths (`Update` at `GameManager.cs:93-103` calls `GetGameState` every frame): do NOT route `Update` through `For()` dictionary lookups — `Update` uses instance members already (`GetGameState(currentGameStateIndex.Value)`), leave it.
- Tests: full suite via `mcp__UnityMCP__run_tests`; `read_console` after every change.
- Commits: English, conventional, body always, no AI attribution.

### References

- [Source: _bmad-output/refactor-architecture-desingleton.md#2 — core pattern; #4 slice 4]
- [Source: _bmad-output/planning-artifacts/epics.md#Story 5.0d]
- Blast radius: architecture doc §1 (161 usages / 67 files, measured 2026-06-11; ~66 UI / ~70 gameplay / ~22 GameLogic / 2 tests)
- Design precedent: story file `5-0c-charactermanager-per-networkmanager-registry.md` Dev Agent Record (probe conclusion)

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (gds-dev-story workflow)

### Debug Log References

**Task 1 — baseline (2026-06-11):** EditMode 155/155 passed, PlayMode 138/138 passed. Console clean. Matches the inherited 5.0c baseline.

**Task 2 — design inheritance:** Read `5-0c-charactermanager-per-networkmanager-registry.md` Dev Agent Record. Inherited **Design B** (NGO 2.6.0 probe conclusion, not re-decided per AC1): `NetworkObject.NetworkManagerOwner` is assigned AFTER `Object.Instantiate` returns (`NetworkSpawnManager.cs:881` vs `SpawnNetworkObjectLocally:1055`), so it is not visible in `Awake`. Consequence applied to `GameManager`: Awake claims the `instance` façade if-free only (no destroy); same-NM duplicate destruction + foreign-replica façade reconciliation + registry write moved to `OnNetworkSpawn` where `NetworkManager` is authoritative.

**Gates (per batch):** `refresh_unity` (compile=request) → `read_console` 0 errors → full EM + PM. Every batch held at **155 EM + 138 PM** (b754fc7, df33136, 90988d4, fb2e5be) and at story end. The PlayMode suite (which boots the host harness through `GameManager` and contains the 2.11a sequence golden, the Waves 1–3 goldens, and `StateDispatchWireFormatGuardTests`) never moved — AC4 satisfied.

### Completion Notes List

- **Batch 1 (b754fc7) — registry + guard, production no-op.** Added `s_byNetworkManager` dictionary + static `For(NetworkManager)` resolver (primary falls back to the Awake-claimed `instance`) + `SubsystemRegistration` play-restart reset to `GameManager.cs`. `instance` kept as a `{ get; private set; }` property (GameManager-specific delta vs CharacterManager's field). Awake reduced to claim-if-free; the per-instance `onNewDayPassed += () => gameLoopCount++;` listener stays in Awake and runs on **every** replica (it mutates per-instance state, not a static). `OnNetworkSpawn` does same-NM-duplicate destroy + foreign-replica reconciliation + registry claim **at the top, before `SetupGameStates()` and the server-side `currentGameStateIndex` write** — so the 2.11a ordering is untouched (AC4). Added an `OnDestroy` override (GameManager had none) calling `base.OnDestroy()`, plus `OnNetworkDespawn`/`OnDestroy` unregister-by-value for teardown-safe cleanup. AC1/AC5/AC6 satisfied. `Update`'s per-frame `GetGameState(currentGameStateIndex.Value)` was left on instance members (not routed through `For()`), per the hot-path rule.
- **Batch 2 (df33136) — GameLogic.** 3 GameStates (`TakeDownThePortalState` 3, `GameIntroductionState` 1, `GameEndingState` 1) switched to the already-injected `gameManager` property; `GameLogic/GameStates/` now has **zero** static `GameManager` reads (AC2). `GameInfoRevealer` (8) + `ChainingManager` (1) — both NetworkBehaviours already on `CharacterManager.For(NetworkManager)` since 5.0c — routed through `GameManager.For(NetworkManager)`.
- **Batch 3 (90988d4) — powers.** 48 reads across 21 NetworkBehaviours (19 `P*.cs` + `Power.cs` + `PowerComponent.cs`) → `GameManager.For(NetworkManager)`. Each class declaration verified as `: Power` / `: PowerComponent` before editing.
- **Batch 4 (fb2e5be) — characters + board.** 8 reads across `Character` (2), `BoardManager` (5), `RoleTargetSystem` (1) → `For(NetworkManager)`. The `BoardManager` Start-subscribe / Despawn-unsubscribe pair resolves to the same `characterManager` under one NM, preserving symmetry + null-guard.
- **AC3 / UI untouched (Task 7):** `git diff --name-only e0942d9..HEAD` contains no `UI/`, `FX/`, `TooltipSystem/`, `NoteSystem/`, `MessageSystem/`, `FocusSystem/` file — 30 gameplay `.cs` only.
- **Migration tally:** 70 of 161 static reads migrated (Batch 2: 14, Batch 3: 48, Batch 4: 8). 91 remain on the `instance` façade, all behaviour-identical to before.
- **Deferred-on-façade (verify-and-reclassify, NOT force-the-pattern):**
  - `TargetUtils.cs` (static class, 4) — no `NetworkManager` of its own; threading an NM through `GetTargetsForCharacters`/`GetTargetsForRoles`/`IsTargetValid` is a public-API change rippling to all callers. (5.0c had pencilled this in for 5.0d assuming an NM context; on opening, the static-class kind governs.)
  - `PowerEffectDispatcher.cs` (static class, 4) — same; its `ResolveTarget` also still defers `CharacterManager` to the dispatcher de-singletonisation (5.0c open item). Threading an NM through `Dispatch(...)` is out-of-scope public-API churn.
  - `WinningConditions/W*.cs` (serializable, 5: WAnomalyCorruption 1, WChosenChainedAllAnomaly 1, WMarginalIsChainedWin 1, WOmniscienceHackedCharacter 2) — the legacy parameterless `CheckCondition()` has no NM/context parameter to thread; the migrated `CheckCondition(GameSnapshot)` overloads read no manager. Adding plumbing would touch the `WinningCondition` virtual signature and the snapshot differential harness (out of scope; per the story's "do not invent new plumbing through serialized types").
  - `Board/Card.cs` (MonoBehaviour, 2) + `GameLogic/PowerManager.cs` (6) + `GameLogic/PowerUsageManager.cs` (2) — scene-local MonoBehaviours with no NM context; stay on façade (consistent with 5.0c's treatment of `Card`). The story's Batch 4 list assumed `Card` was a NetworkBehaviour; the class kind governs.
  - The remaining 68 reads are UI / presentation / `GameManagerTests` — the explicit non-goal (AC3), untouched.
- **Behaviour-preservation argument:** production has exactly one scene-placed `GameManager`; `For(Singleton)` resolves to the registry entry or falls back to the Awake-claimed `instance`, so `For(...) === instance` in every production state. The only delta (Design B) is that a same-NM duplicate would be destroyed in `OnNetworkSpawn` instead of `Awake` — one frame later, defensive-only, unobservable with a single manager.
- **# REVIEW-REQUIRED:** largest blast radius of the de-singleton stories (161 uses / 67 files). Run `/gds-code-review` (or cheap local `/code-review`) before merging. Suite green (155 EM + 138 PM) and goldens unchanged, but registry/lifecycle + bot-flow-adjacent paths warrant a fresh-context pass.

### File List

- `Assets/Scripts/GameLogic/GameManager.cs` (Batch 1 — registry + For + Design B guard + OnDestroy + reset)
- `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs` (Batch 2)
- `Assets/Scripts/GameLogic/GameStates/GameIntroductionState.cs` (Batch 2)
- `Assets/Scripts/GameLogic/GameStates/GameEndingState.cs` (Batch 2)
- `Assets/Scripts/GameLogic/GameInfoRevealer.cs` (Batch 2)
- `Assets/Scripts/GameLogic/ChainingManager.cs` (Batch 2)
- `Assets/Scripts/Characters/Powers/Power.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PowerComponents/PowerComponent.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PAutoCorruption.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PBlessing.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PBoundByInk.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PChainedByTheShadows.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PClandestineObservation.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PCorruptingMark.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PCorruptionInsight.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PCorruptionParanoia.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PCorruptionKnowledge.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PEmbraceOfShadows.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PDroolyHealing.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PLackOfAffection.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PHighPriorityBounty.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PEyeOfTheVoid.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PVisionOfTheImpossible.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PTruthChains.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PReincarnation.cs` (Batch 3)
- `Assets/Scripts/Characters/Powers/PPersonalBeacons.cs` (Batch 3)
- `Assets/Scripts/Characters/Character.cs` (Batch 4)
- `Assets/Scripts/Board/BoardManager.cs` (Batch 4)
- `Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs` (Batch 4)

### Change Log

- 2026-06-11: Implemented Story 5.0d in 4 gated batches (commits b754fc7, df33136, 90988d4, fb2e5be on `epic5-network`). 70/161 static `GameManager.instance` reads migrated to `GameManager.For(NetworkManager)` / injected `gameManager`; 91 remain on the façade (15 gameplay deferred + 8 Mono gameplay + 68 UI/tests). Suite green at baseline every batch; goldens unchanged. Status → review.

### Review Findings

gds-code-review (2026-06-11, 3 adversarial layers: Blind Hunter / Edge Case Hunter / Acceptance Auditor). Acceptance Auditor: AC1–AC6 all satisfied, 70/161 tally exact, deferred-on-façade list correct, 28 DO-NOT-EDIT files untouched, no partial migration. Outcome: 0 decision-needed, 1 patch, 2 deferred, ~10 dismissed (production-no-op false positives + already-matched-precedent). The behaviour-preservation guarantee was confirmed against source: `For(NetworkManager.Singleton)` falls back to the Awake-claimed `instance` (`GameManager.cs:48`, `:96-99`), so `For(...) ≡ instance` in every production window (single scene-placed GameManager, single NetworkManager). The subagents' large NRE list assumes a 2nd in-process NetworkManager, which does not exist yet (5.0/5.0e blocked).

- [x] [Review][Patch] `BoardManager.OnNetworkDespawn` resolves `GameManager.For(NetworkManager)` three times in a guard-then-deref idiom; snapshot it into one local so the null-guard is authoritative for the unsubscribe deref (behaviour-identical on the single main-thread today; pre-empts a latent TOCTOU once 5.0e introduces a real 2nd NetworkManager) [Assets/Scripts/Board/BoardManager.cs:56-61] — APPLIED; re-gated 155 EM + 138 PM green, goldens unchanged
- [x] [Review][Defer] Two-NetworkManager spawn-order interleaving leaves a transient window where the Singleton façade (`instance`) is null — a foreign-NM replica nulls it in `OnNetworkSpawn` before the primary reclaims it [Assets/Scripts/GameLogic/GameManager.cs:106-150] — deferred to story 5.0e coexistence gate; no 2nd in-process client exists yet, production is a no-op
- [x] [Review][Defer] `For(null)` shutdown fallback (`null == NetworkManager.Singleton ? instance : null`) can hand back the primary `instance` instead of a clean null when both the caller's NM and `NetworkManager.Singleton` are null mid-teardown [Assets/Scripts/GameLogic/GameManager.cs:48] — deferred to 5.0e; no regression vs the old static `instance` (identical shutdown exposure), but verify under two-client teardown
