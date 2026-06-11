# Story 6.1: Establish the injection seam + composition root on a worked example

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want a documented, tested dependency-injection seam proven on one real consumer rerouted off a static `GameManager.instance` lookup,
so that every later despaghetti story (Epic 7+) has a copy-paste pattern and a guard, instead of re-inventing the wiring.

This is **Epic 6 / D0** — the foundation of the whole despaghettification track (`refactor-architecture-despaghetti.md`). It changes almost no behaviour; its deliverable is a *convention* + a *guard* + one worked example, not a feature.

## Acceptance Criteria

1. **One consumer rerouted off the locator.** `LightManager` (`Assets/Scripts/Board/LightManager.cs`) no longer reads `GameManager.instance`. It receives its `GameManager` dependency through a `[SerializeField] private GameManager gameManager;` field, and both former `GameManager.instance` reads (`Start` subscription + `GetGameState` in `OnGameStateChanged`) go through the injected reference. No static `GameManager.instance` / `For(...)` lookup remains anywhere in `LightManager`.
2. **Composition root wires it.** The injected reference is wired at the composition root — the place where `LightManager` lives (GameScene, or its prefab if it is on one). After wiring, entering play and reaching the board behaves identically to today (light colour still tweens on state change).
3. **Unwired fails loud, not silent.** If the `gameManager` field is not wired, the consumer fails with a clear message at init (an `Assert`/explicit null-check in `Awake`/`Start`) instead of a mid-game `NullReferenceException`. This makes a missed wiring a deterministic, early failure — critical because DI in Unity moves the failure mode from "locator always resolves" to "reference might be unwired".
4. **Convention documented.** A short, referenceable **Injection Seam Convention** section is appended to `_bmad-output/refactor-architecture-despaghetti.md` covering: (a) `[SerializeField]` for scene/prefab-placed consumers (wired at the scene/prefab composition root); (b) `Initialize(deps)` / property-set for runtime-created objects, citing the **existing** precedents `GameManager.SetupGameStates` (`clonedGameState.gameManager = this`) and `StateUI.SetupStateUI(gameManager, this)`; (c) the rule that the consumer must hold NO static lookup after migration; (d) the NFR5 relocation rule (any `GetSafeRpcTarget`/`clientId`/`RpcParams` code is *moved verbatim*, never edited, and stays in the network adapter). Later stories cite this section instead of re-deriving it.
5. **Static-absence guard scaffolded.** A permanent EditMode guard test (modelled on `LeafPocoNoFacadeGuardTests`, `[Category("DiSeamGuard")]`) fails if a curated set of "migrated consumers" (seeded with `LightManager`) still references the `GameManager.instance` / `CharacterManager.instance` locator. The guard is the mechanism that lets Epic 7 migrate in bulk without regression — each migrated type is added to the curated set.
6. **Behaviour-preserving + green gate.** The full suite passes unchanged (**PlayMode 145 / EditMode 155** baseline) and a boot smoke check (enter play, reach the board, no exception, light still responds to a state change) is green. No golden moves.

## Tasks / Subtasks

- [ ] **Task 1: Reroute `LightManager` to an injected `GameManager`** (AC: 1, 3)
  - [ ] Add `[SerializeField] private GameManager gameManager;` to `LightManager`.
  - [ ] Replace `GameManager.instance.currentGameStateIndex.OnValueChanged += OnGameStateChanged;` (`Start`) and `GameManager.instance.GetGameState(_newValue)` (`OnGameStateChanged`) with the injected `gameManager`.
  - [ ] Add an early null-guard: in `Awake` (or at the top of `Start`), `Assert.IsNotNull(gameManager, "LightManager.gameManager is not wired — wire it in the composition root (GameScene/prefab).")` (use `UnityEngine.Assertions.Assert`). Do NOT fall back to `GameManager.instance` — that would re-introduce the locator (defeats the story).
  - [ ] **Preserve behaviour exactly:** keep the `Start`-time subscription, the `useLightColorOverride` branch, the DOTween `DOColor` calls, and the `Reset()` editor helper untouched. `LightManager` currently never unsubscribes `OnValueChanged` in `OnDestroy` — that is a **pre-existing** latent leak; do NOT "fix" it here (out of scope, would change behaviour/lifetime). Note it for Epic 11.
- [ ] **Task 2: Wire the dependency at the composition root** (AC: 2)
  - [ ] **Serialized-field safety (CRITICAL — applies to every story in this track):** add `gameManager` by **appending** it; do NOT rename, reorder, or retype the existing `mainLight` / `baseLightColor` / `colorTransitionDuration` fields — Unity serializes by field name, so renaming/reordering them **orphans their already-wired scene/prefab references silently**. A new `[SerializeField]` field is **null in every existing instance until explicitly wired** — adding the script field does NOT auto-link anything. (If a *rename* is ever unavoidable, use `[FormerlySerializedAs("old")]` to keep the wired ref.)
  - [ ] Locate **every** place `LightManager` lives: `mcp__UnityMCP__find_gameobjects` for the `LightManager` component (it may be on a GameScene object and/or a prefab, possibly multiple instances). Enumerate them all.
  - [ ] For **each** instance: set the `gameManager` field reference to the scene's `GameManager` via `mcp__UnityMCP__manage_gameobject` / `manage_components` (object-reference assignment); for a prefab, wire on the prefab asset via `manage_prefabs`. Save the scene/prefab.
  - [ ] **Verify each** wiring by re-reading the component back (`manage_components` get / `find_in_file` on the scene/prefab YAML) — an unset serialized reference is the #1 DI failure mode and is invisible until runtime. The Task 1 null-guard is the runtime backstop, but wiring must be confirmed at author time.
- [ ] **Task 3: Document the Injection Seam Convention** (AC: 4)
  - [ ] Append an `## Injection seam convention` section to `_bmad-output/refactor-architecture-despaghetti.md` (the spec, not a story file). Cover the four points in AC 4. Keep it tight — it is a reference card for Epics 7–12.
- [ ] **Task 4: Scaffold the static-absence guard** (AC: 5)
  - [ ] Create `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs`, `[Category("DiSeamGuard")]`, modelled on `LeafPocoNoFacadeGuardTests`. Curated array `MigratedConsumers = { typeof(LightManager) }`.
  - [ ] Assert each migrated consumer's compiled code contains no call to the locator getter. **Recommended mechanism (robust):** reflect each declared method's IL via `MethodBase.GetMethodBody().GetILAsByteArray()` and scan for a call to `GameManager.get_instance` / `CharacterManager.get_instance` (resolve the metadata token, or match the `call`/`callvirt` opcode against the getter's `MethodInfo` token). **Fallback (simplest, acceptable for the scaffold):** read the consumer's `.cs` source file and assert it contains no `GameManager.instance` / `CharacterManager.instance` substring — locate the file by `[CallerFilePath]` or a known relative path. Pick one, record why in Dev Notes; the IL approach is rename-safe, the source approach is trivial but path-coupled.
  - [ ] The test must FAIL if `LightManager` still referenced the locator (prove the guard bites before declaring it done).
- [ ] **Task 5: Validate + gate** (AC: 6)
  - [ ] `mcp__UnityMCP__read_console` after each change → zero compile errors before tests.
  - [ ] `mcp__UnityMCP__run_tests` EditMode + PlayMode. Confirm baseline **PM 145 / EM 155** (the new guard test raises EM by its test count; all existing tests still pass; no golden moves).
  - [ ] Boot smoke: enter play (`mcp__UnityMCP__manage_editor play`), reach the board, confirm no exception and the light responds to a state change, then stop. (If a full board boot is impractical in this session, at minimum confirm no NRE from `LightManager` init and record the manual smoke step for Poyo.)
  - [ ] Update sprint-status `6-1 → review` (or `done` post-review). Commit (English, conventional, body, `UX:` only if player-visible — here it is not; pure refactor).

## Dev Notes

### Why this story exists (don't skip — it frames every choice)

The despaghettification track replaces **Service Locator** (`GameManager.instance` / `CharacterManager.For(nm)`, an *implicit* global dependency) with **dependency injection** (the consumer *declares* what it needs). D0 does not migrate anything at scale — it builds the **one reusable seam + guard** that Epic 7 (the 78 `.characterManager` sites + 31 `.gameInfoRevealer` sites) then applies in bulk. Get the pattern right here; it is copied dozens of times.

The project already injects — this story **codifies** the existing pattern, it does not invent one:
- `GameManager.SetupGameStates()` (`GameManager.cs:218`) clones each `GameState` and pushes `clonedGameState.gameManager = this;` — composition-root push injection into runtime-created objects.
- `GameState.gameManager { get; set; }` (`GameState.cs:17`) — the injected reference, used everywhere instead of a locator.
- `StateUI.SetupStateUI(gameManager, this)` — explicit `Initialize`-style method injection one level down.

So the convention is: **`[SerializeField]`** for scene/prefab-placed objects (wired in the scene/prefab = the composition root), **`Initialize(...)` / property-set** for runtime-spawned objects (injected by whoever creates them, exactly as `SetupGameStates` does for states and `CharacterManager` will for powers in Epic 7).

### The worked example: `LightManager` (read it — `Assets/Scripts/Board/LightManager.cs`, 34 lines)

**Current state (what it does today):** a scene `MonoBehaviour` driving the room light. `Start()` subscribes `GameManager.instance.currentGameStateIndex.OnValueChanged += OnGameStateChanged`. `OnGameStateChanged` reads `GameManager.instance.GetGameState(_newValue)`, and either tweens `mainLight` to `baseLightColor` or to `_gameState.lightColorOverride` (DOTween `DOColor`) depending on `_gameState.useLightColorOverride`. `Reset()` is an editor-only helper. **It never unsubscribes** (pre-existing latent leak — leave it).

**What this story changes:** the two `GameManager.instance` reads become reads of an injected `[SerializeField] private GameManager gameManager;`. Nothing else.

**What must be preserved:** the subscription timing (`Start`), the override branch, the tween durations/colours, the `Reset()` helper. The light must behave byte-identically.

**Why `LightManager` is the chosen example:** lowest-risk representative of a *very* common pattern — ~10 board/FX/UI components (`RoomFog`, `BoardCameraManager`, `AwakeningLight`, `CharacterAwakenTimer`, `AnonymeMessageButton`, the `AwakeningRecap*` UIs…) subscribe to `GameManager.instance.currentGameStateIndex.OnValueChanged` the exact same way. Proving the seam here yields a template for all of them. It is a pure presentation `MonoBehaviour` (no NetworkBehaviour, no RPC, no NFR5 surface), so D0 stays low-risk; the network-sensitive consumers come later, gated by the multi-client fixture.

### The composition-root wiring (the fiddly part of Unity DI)

A `[SerializeField]` reference is null until something wires it. `LightManager` is scene-placed (confirm with `find_gameobjects`); wire its `gameManager` to the **scene's `GameManager`** in GameScene (it is scene-placed there per the de-singleton work). This is the literal "composition root": the scene is where the object graph is assembled. The null-guard (Task 1) is what converts a forgotten wiring into a loud, immediate failure instead of a silent runtime NRE — this is the single most important habit for the rest of the track, because Epic 7 will wire dozens of references.

If `find_gameobjects` shows `LightManager` lives on a **prefab**, wire it on the prefab (`manage_prefabs`) instead — same principle, different composition root.

### The static-absence guard (the thing that makes Epic 7 safe)

Model: `Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs` (story 5.2). That guard reflects over a curated `Type[]` and asserts structural properties. Here the property is "this type does not call the locator". Reflection over *fields/properties* (as the leaf guard does) is not enough — locator use is inside method bodies. Two viable mechanisms:
- **IL scan (recommended, rename-safe):** for each `MethodInfo`/`ctor` declared on the type, `GetMethodBody().GetILAsByteArray()`, walk the bytes for `call`/`callvirt` (0x28/0x6F) whose operand metadata token resolves (via `Module.ResolveMethod`) to `GameManager.get_instance` or `CharacterManager.get_instance`. Robust against source moves/renames.
- **Source scan (simplest, path-coupled):** read the type's `.cs` file and assert it contains no `GameManager.instance` / `CharacterManager.instance`. Trivial but breaks if the file moves; fine for the D0 scaffold.

Pick one, record the choice. The curated array starts as `{ typeof(LightManager) }`; Epic 7 stories append each migrated type. Prove the guard bites (temporarily point it at an un-migrated type or revert LightManager, see it fail) before declaring Task 4 done.

### NFR5 (state it even though LightManager has no RPC)

The convention must carry the rule forward: when a *later* consumer that holds `GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` / `RpcParams` code is migrated, that code is **relocated verbatim** into the network adapter, never edited. `LightManager` has none, so this story only documents the rule (AC 4d) for the consumers that do.

### Project Structure Notes

- Modified: `Assets/Scripts/Board/LightManager.cs` (inject), GameScene or a prefab (wiring + `.meta`/scene file), `_bmad-output/refactor-architecture-despaghetti.md` (convention section).
- New: `Assets/Scripts/Tests/Editor/DiSeamNoLocatorGuardTests.cs` (+ `.meta` via `refresh_unity`, never hand-write `.meta`).
- `Tests.Editor.asmdef` already runs EditMode tests; the new guard references `Game` types (`LightManager` is in the global namespace / `Game` asmdef). If a `LightManager` type-ref triggers CS0012 add the needed asmdef ref (precedent: the Domain-asmdef gotcha — `reference_domain_asmdef_autoref_tests`). `LightManager` is in the **global namespace** (no `namespace` in its file) — reference it as plain `LightManager` from the test, ensure the test asmdef sees the `Game` assembly.
- Branch: `refactor-despaghetti`. No `Dev`/`dev-refactor` direct work.

### Project Context Rules (from project-context.md)

- **`NetworkBehaviour` exposes deps via `Initialize(...)` or `[SerializeField]` — never `FindObjectOfType` / singleton lookup** (project-context.md:157). This story applies that rule to a `MonoBehaviour` too — `[SerializeField]`, no `FindObjectOfType`, no locator fallback.
- **Expose internals to tests via `InternalsVisibleTo`, never widen `public` for testability** (project-context.md:139). The guard reflects over public type metadata + IL; no `public` widening needed.
- **EditMode by default; push to PlayMode only when forced** (project-context.md:340). The guard is EditMode (pure reflection). The behaviour gate is the existing PlayMode suite.
- **Server authority / async = UniTask / audio = FMOD** — untouched here (LightManager is client-side presentation), but the convention must not violate them downstream.
- Commits: English, conventional, body always, no AI attribution; `UX:` line only if player-visible (this is not).

### References

- [Source: _bmad-output/refactor-architecture-despaghetti.md §2 (target), §4 (strategy), Epic 6/D0] — the convention this story establishes.
- [Source: _bmad-output/planning-artifacts/epics.md#Epic 6 (D0) / Story 6.1].
- [Source: Assets/Scripts/Board/LightManager.cs] — the worked example (the file being modified).
- [Source: Assets/Scripts/GameLogic/GameManager.cs:218 SetupGameStates + GameLogic/GameState.cs:17 gameManager] — the existing injection precedent the convention codifies.
- [Source: Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs] — the structural model for the static-absence guard.
- [Source: Assets/Scripts/GameLogic/GameManager.cs:42-49 For() / :29 instance] — the locator the consumer must stop using.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
