# Story 6.2: Add the SceneWiringGuard over injected references

Status: review

## Story

As a developer,
I want an EditMode guard that loads the composition roots and asserts every injected `[SerializeField]` dependency of every migrated consumer is actually wired,
so that a forgotten drag-drop fails CI deterministically instead of NRE-ing mid-game — the piece that makes lane A scale to 50+ consumers.

This is guard #2 of the **two-guards** pillar of the validated spec (`refactor-architecture-despaghetti.md` §5). Guard #1 (`DiSeamNoLocatorGuardTests`) checks the *type* (no locator in code); this guard checks the *binding* (the scene/prefab actually carries the reference). Together they close the "DI in Unity breaks silently" objection.

## Acceptance Criteria

1. **Guard exists and covers the migrated set.** `SceneWiringGuardTests` (EditMode, `[Category("SceneWiringGuard")]`) enumerates the curated migrated-consumers set, locates every instance of each type in `Assets/Scenes/GameScene.unity` (EditMode scene load) — and in prefab assets for prefab-placed types when those appear — and asserts each injected manager-typed `[SerializeField]` field is non-null.
2. **One shared registry for both guards.** The curated set is refactored out of `DiSeamNoLocatorGuardTests` into one shared registry consumed by BOTH guards. Epic 7+ stories append a migrated type once; both guards pick it up.
3. **The guard bites.** A synthetic/temporarily-unwired case turns the guard red before the story is declared done (prove the mechanism, like 6.1 did for the locator guard).
4. **EditMode only, isolated.** No PlayMode boot; the editor's scene state is restored after the test (no side effect on the user's open scene).
5. **`LightManager` is the first real subject.** Its GameScene wiring (done in 6.1) passes the guard.
6. **Green gate.** Full suite passes unchanged at baseline (PM 145 / EM 155 + guard tests). No golden moves.

## Tasks / Subtasks

- [x] **Task 1: Extract the shared migrated-consumers registry** (AC: 2)
  - [x] Create `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` (same `Tests.Editor` assembly, plain static class): `public static class DiSeamMigratedConsumers { public static readonly Type[] All = { typeof(LightManager) }; }`.
  - [x] Re-point `DiSeamNoLocatorGuardTests.MigratedConsumers` at the shared registry (delete its private array). Behaviour of guard #1 unchanged — its tests still pass.
- [x] **Task 2: Decide and implement "which fields are injected deps"** (AC: 1)
  - [x] Recommended mechanism (zero per-field maintenance): a static set `InjectedManagerTypes = { typeof(GameManager) }` (grows as the track injects more manager types: CharacterManager, GameInfoRevealer, …). A field counts as an injected dependency iff it is `[SerializeField]` (any visibility, walk base types too) AND its field type is (or derives from) a member of that set. This automatically EXCLUDES presentation refs like `LightManager.mainLight` (Light) and INCLUDES `LightManager.gameManager`.
  - [x] Record the mechanism choice in this story's Dev Agent Record (like 6.1 recorded its source-scan choice).
- [x] **Task 3: Implement `SceneWiringGuardTests`** (AC: 1, 4)
  - [x] Create `Assets/Scripts/Tests/Editor/SceneWiringGuardTests.cs`, `[Category("SceneWiringGuard")]`.
  - [x] Scene handling: capture `EditorSceneManager.GetSceneManagerSetup()` in `[OneTimeSetUp]`, `EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single)` for the assertions, `EditorSceneManager.RestoreSceneManagerSetup(...)` in `[OneTimeTearDown]`. If the working scene has unsaved changes, the open will prompt/fail — use `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo` is NOT testable; instead document that the guard requires a saved editor state and guard with a clear failure message. (PlayMode tests already tolerate scene churn; EditMode scene-load is cheap.)
  - [x] For each type in `DiSeamMigratedConsumers.All`: `UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None)` on the opened scene; for each instance, reflect its `[SerializeField]` fields (`GetFields(Instance|Public|NonPublic)` walking base types; a field is serialized if public-without-`[NonSerialized]` or has `[SerializeField]`), filter by `InjectedManagerTypes`, and assert the value is a non-null, non-destroyed `UnityEngine.Object` (use the Unity `== null` overload — a destroyed/missing ref must FAIL).
  - [x] Failure message names the GameObject path, the field, and the fix: "wire it in the composition root (GameScene/prefab), see refactor-architecture-despaghetti.md §3 lane A".
  - [x] Prefab support: if a migrated type has zero scene instances, scan prefab assets (`AssetDatabase.FindAssets("t:Prefab")` → `GetComponentsInChildren(type, true)`) before declaring "no instance found"; zero instances anywhere = FAIL (a migrated consumer must exist somewhere, else the curated set is stale).
- [x] **Task 4: Prove the guard bites** (AC: 3)
  - [x] Unit-level: factor the field-check into a pure helper (`FindUnwiredInjectedFields(Component)`) and feed it a synthetic component instance with a null `GameManager` field — assert it is flagged. (Mirrors `Guard_Bites_OnSyntheticLocatorUsage` from 6.1.)
  - [x] Integration-level (one-shot, manual or scripted): temporarily clear `LightManager.gameManager` on a scene copy or in-memory instance and watch the guard fail; restore. Record in Dev Agent Record that the bite was observed.
- [x] **Task 5: Validate + gate** (AC: 5, 6)
  - [x] `mcp__UnityMCP__read_console` → zero compile errors.
  - [x] `mcp__UnityMCP__run_tests` EditMode filtered `category: SceneWiringGuard` (must pass with LightManager wired), then `DiSeamGuard` (unchanged), then full EditMode + PlayMode. Baseline PM 145 / EM 155 + guard tests.
  - [x] Update sprint-status `6-2 → review`/`done`. Commit (English, conventional, body; no `UX:` — pure test infra).

## Dev Notes

### Why this guard is the load-bearing piece of lane A

Lane A (`[SerializeField]` + scene wiring) moves the failure mode from "locator always resolves" to "reference might be unwired". The 6.1 `Assert.IsNotNull` in `Awake` is the *runtime* backstop; this guard is the *CI* backstop — it catches the unwired reference **without entering play mode**, which is what makes wiring 70–100 references across Epics 7–12 tractable. Without it, every Epic 7 batch would need a manual boot-check per instance.

### Existing code to reuse / not reinvent

- **`DiSeamNoLocatorGuardTests`** (`Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs`, story 6.1): contains the curated array to extract (Task 1) and the "prove the guard bites" pattern to mirror (Task 4). Do not duplicate its source-scan logic — this guard checks bindings, not source.
- **`LeafPocoNoFacadeGuardTests`** (`Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs`, story 5.2): the structural model for reflection-based permanent guards (curated `Type[]`, per-type assertion loop, explicit failure messages).
- **`LightManager`** (`Assets/Scripts/Board/LightManager.cs`): the one migrated consumer today — fields: `mainLight` (Light, NOT an injected dep), `baseLightColor`, `colorTransitionDuration`, `gameManager` (GameManager, the injected dep). The `InjectedManagerTypes` mechanism must flag exactly `gameManager` and nothing else. `LightManager` is in the **global namespace**.
- GameScene path: `Assets/Scenes/GameScene.unity` (do NOT touch `GameScene_backup.unity` / `(old)MenuScene.unity` — legacy, never extended).

### Gotchas

- **Unity null:** use the `UnityEngine.Object` equality (a "missing" destroyed reference must count as unwired). Compare via `field.GetValue(component) as UnityEngine.Object` then `obj == null`.
- **Serialized-field detection must walk base types** (`BindingFlags.DeclaredOnly` per level, climbing `BaseType`) — private fields of a base class are not returned by a derived-type `GetFields` call.
- **Scene restore:** the user (or a later test) may have an unsaved scene open. `GetSceneManagerSetup`/`RestoreSceneManagerSetup` is the standard pattern; never leave GameScene open as a side effect.
- **`FindObjectsInactive.Include`** — a disabled GameObject still ships with the scene and still needs its wiring.
- **Domain reload disabled** in this project: no static mutable state in the guard (the registry array is `static readonly` — fine).
- This story creates **no runtime code** — `Tests.Editor` only. No asmdef change expected (`Tests.Editor` already references `Game`; precedent: 6.1's guard already references `LightManager`).

### Staleness check (authored 2026-06-11)

Story authored while only `LightManager` is migrated. If Epic 7 stories have landed since, the shared registry already exists or has more entries — extend, don't recreate; re-verify the baseline counts at dev time.

### Project Structure Notes

- New: `Assets/Scripts/Tests/Editor/SceneWiringGuardTests.cs`, `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` (+ `.meta` via `refresh_unity`, never hand-write `.meta`).
- Modified: `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` (re-point at shared registry only — do not change its forbidden-locators logic; 6.3 extends that).
- Branch: `refactor-despaghetti`.

### Project Context Rules (from project-context.md)

- EditMode by default; PlayMode only when forced (this is pure editor reflection + scene load → EditMode). [project-context.md: Testing Rules]
- Test classes suffix `Tests`, `[Category]` for filtering, file under `Tests/Editor/`. Both test asmdefs carry `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — expected.
- No `Thread.Sleep`/real-time waits; no static mutable carried across tests.
- Commits: English, conventional, body always, no AI attribution.

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §3 lane A, §5 guard #2] — the spec this story implements.
- [Source: _bmad-output/planning-artifacts/epics.md#Story 6.2].
- [Source: Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs] — guard #1, registry to extract, bite-proof pattern.
- [Source: Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs] — reflection-guard structural model.
- [Source: Assets/Scripts/Board/LightManager.cs] — first subject; field census above.
- [Source: _bmad-output/implementation-artifacts/6-1-establish-injection-seam-and-composition-root-on-a-worked-example.md] — previous story: serialized-field safety rules, wiring-verification habit, "prove the guard bites" precedent.

## Dev Agent Record

### Agent Model Used

claude-fable-5 (Claude Code)

### Debug Log References

- Guard run (filtered): EditMode `SceneWiringGuard` 3/3 + `DiSeamGuard` 2/2 — PASS.
- Full gate: EditMode 160/160 (baseline 155 + 2 DiSeamGuard + 3 SceneWiringGuard), PlayMode 145/145 (baseline unchanged). Zero console errors/warnings.
- **Incident found & fixed during Task 5:** `DiSeamNoLocatorGuardTests.cs` (story 6.1) was silently EXCLUDED from compilation — the asset was registered in the AssetDatabase (valid MonoScript, guid `da33a545…`) but absent from the Bee compilation source set of `Tests.Editor.dll`, so guard #1's 2 tests ran in NO suite (full EM totalled 158 without them; `category: DiSeamGuard` filter returned 0 tests). Targeted reimport and forced refresh did NOT invalidate the stale source set. Root cause consistent with the `.cs`+`.meta` having been written outside Unity's import pipeline during 6.1 (hand-minimal 2-line meta) without a real import ever registering it for compilation. Fix: delete via Unity (`manage_script delete`) + recreate with identical content + refresh → recompiled, type present, tests discovered. New guid is harmless (editor test script, referenced nowhere).

### Completion Notes List

- **Mechanism choice (Task 2, recorded per AC):** a field counts as an injected dependency iff it is serialized (public without `[NonSerialized]`, or `[SerializeField]`, walking base types `DeclaredOnly` per level) AND its field type is, or derives from, a member of `DiSeamMigratedConsumers.InjectedManagerTypes` (today `{ typeof(GameManager) }`). Zero per-field maintenance; on `LightManager` it flags exactly `gameManager` and excludes `mainLight` (Light), `baseLightColor`, `colorTransitionDuration`. `InjectedManagerTypes` is co-located in `DiSeamMigratedConsumers` so Epic 7+ stories maintain both curated sets (migrated types + injected manager types) in ONE file.
- **Shared registry (AC 2):** `DiSeamMigratedConsumers.All` extracted; `DiSeamNoLocatorGuardTests.MigratedConsumers` re-points at it (private array deleted, forbidden-locators logic untouched). Both guards now consume the same curated set.
- **Guard implementation (AC 1, 4):** EditMode-only; `[OneTimeSetUp]` captures `GetSceneManagerSetup()` (untitled entries filtered — not restorable), opens `GameScene.unity` Single, `[OneTimeTearDown]` restores (or falls back to a fresh default scene when the prior setup was untitled-only). Pre-check: a TITLED scene with unsaved changes fails the guard with a clear "save first" message instead of silently discarding user work; a dirty UNTITLED scratch scene is tolerated (indistinguishable from earlier EditMode tests leaking GameObjects into it). Unity-null comparison via the `UnityEngine.Object ==` overload so destroyed/missing refs count as unwired. Prefab fallback scan (`t:Prefab` → `GetComponentsInChildren(type, true)`) only when a type has zero scene instances; zero instances anywhere = FAIL (stale curated set).
- **Bite proven (AC 3), as two PERMANENT tests** (not a one-shot): `Guard_Bites_OnSyntheticUnwiredField` (synthetic MonoBehaviour with a deliberately-null `GameManager` field → flagged; its `Light` + `float` fields → NOT flagged) and `Guard_Bites_WhenRealConsumerFieldIsCleared_InMemory` (the REAL GameScene `LightManager.gameManager` cleared via reflection in memory — never saved — → flagged, then restored and re-verified clean). Bite observed red-path on both during the run.
- **LightManager passes (AC 5):** its 6.1 GameScene wiring satisfies the guard as-is.
- **Green gate (AC 6):** full suite at baseline + guards — EM 160 (155 + 5 guard tests, including the 2 DiSeamGuard tests now actually running again, see incident above), PM 145 unchanged. No golden moves.

### File List

- `Assets/Scripts/Tests/Editor/DiSeamMigratedConsumers.cs` (new) + `.meta`
- `Assets/Scripts/Tests/Editor/SceneWiringGuardTests.cs` (new) + `.meta`
- `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` (modified: registry re-point; file recreated through Unity to repair compilation-exclusion — new `.meta`/guid)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified: 6-2 → review)
- `_bmad-output/implementation-artifacts/6-2-add-the-scene-wiring-guard-over-injected-references.md` (this file)

## Change Log

- 2026-06-12 — Story 6.2 implemented: shared `DiSeamMigratedConsumers` registry (consumed by both guards), `SceneWiringGuardTests` (EditMode scene/prefab binding guard, 3 tests incl. 2 permanent bite-proofs), guard #1 re-pointed. Repaired silent compilation-exclusion of `DiSeamNoLocatorGuardTests.cs`. Gate: EM 160/160, PM 145/145.
