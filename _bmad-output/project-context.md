---
project_name: 'Corruption Du Portail'
user_name: 'Poyo'
date: '2026-06-13'
sections_completed:
  ['technology_stack', 'engine_rules', 'architecture_di', 'performance_rules', 'organization_rules', 'testing_rules', 'platform_rules', 'anti_patterns']
status: 'complete'
rule_count: 300
refactor_baseline: 'post Epics 1–12 / PR #53 (Dev) — EditMode 205/205 · PlayMode 148/148'
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing game code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

| Layer | Tech | Version |
|---|---|---|
| Engine | Unity | 6000.2.6f2 (exact) |
| Render | URP | 17.2.0 |
| Networking | Netcode for GameObjects (NGO) | 2.6.0 |
| Transport | Facepunch (Steam) | bundle asmdef |
| Async | UniTask | (manifest) |
| Tweens | DOTween + UniTask integration | `AwaitForComplete` |
| Audio | FMOD | banks in `Assets/FMODBanks/` |
| Input | Unity Input System | 1.14.2 |
| UI | uGUI 2.0 + TMP + SoftMaskForUGUI | 2D smartphone + 3D board |
| Cameras | Cinemachine | 3.1.5 |
| Mocks (tests) | NSubstitute (tnrd) | EditMode |
| Assets | Addressables | 2.7.2 (+ Android 1.0.6) |
| Services | Unity Services (Core, Multiplayer 1.1.2, Auth) | Lobby/auth |
| MCP | CoplayDev/unity-mcp | LLM editor automation |

Main branch: `Dev` (PR target). Current scan branch: `BMAD-Setup`.

### Version constraints (load-bearing)

- Unity Editor: **6000.2.6f2 exact** — Hub must auto-select. No upgrade without team coordination.
- URP 17.2.0 bound to Unity 6.x — do not downgrade.
- NGO 2.6.0 + Multiplayer Tools 2.2.6 — paired; bump together.
- Facepunch transport: vendored under `Assets/Scripts/Facepunch/` with own asmdef — not via UPM.
- UniTask + DOTween: required for `AwaitForComplete` integration — do not replace with `Task` or coroutines.
- FMOD: banks compiled from external FMOD Studio — never regen at runtime.

### Edge cases AI must guard against

- Never bump a single package in `Packages/manifest.json` without paired bump (NGO ↔ Multiplayer Tools; Addressables ↔ Addressables Android; URP ↔ Unity version).
- Never add `com.community.netcode.transport.facepunch` to manifest — Facepunch is **vendored** under `Assets/Scripts/Facepunch/` with its own asmdef.
- Never use `Task`-returning DOTween helpers (`AsyncWaitForCompletion`) — only UniTask variant (`AwaitForComplete`) preserves cancellation.
- Never reference editor-only packages (Multiplayer Center, Multiplayer Playmode, MCP for Unity) from runtime asmdefs.
- Never write FMOD `EventReference` for event not present in `Assets/FMODBanks/` — runtime `EVENT_NOTFOUND`, not caught at compile.
- `ProjectSettings/ProjectVersion.txt` is source of truth for Editor version — do not edit; if mismatch detected, halt and surface to user.

## Critical Implementation Rules

### Unity / NGO lifecycle

| Rule | Why | Escape hatch |
|---|---|---|
| Networked init in `OnNetworkSpawn()`, not `Awake`/`Start` | Network ids unassigned until spawn | `Awake` OK for **non-networked** init (components, pools) |
| Networked teardown in `OnNetworkDespawn()`, not `OnDestroy` | Despawn fires **before** Destroy; unsubscribe `NetworkVariable.OnValueChanged` here | None |
| `NetworkObject.Spawn()` server-only | Client-side spawn = silent desync | None |
| `NetworkObject.Despawn(destroy: true)` server, never raw `Destroy()` on NetworkObject | Clients keep ghost | Client must never `Destroy()` a NetworkObject |
| Cross-object `OnNetworkSpawn` order **not** guaranteed | Race on spawn-time deps | Defer via `OnClientConnectedCallback` or explicit init phase |

### NGO ownership / authority

- `IsOwner` ≠ write authority on `NetworkVariable`. Mutate server-side only; clients propose via `ServerRpc`.
- Default `NetworkVariable<T>.WritePerm = Server`. Changing to `Owner` opens cheat surface — never in this project.
- `OnValueChanged(prev, next)` fires **after** `.Value` already mutated — no previous outside callback.
- Never write `NetworkVariable` from client `OnNetworkSpawn` — late joiners receive initial state via snapshot, not callback.
- Use `FixedString*` for string `NetworkVariable` — managed `string` allocates per sync.
- Mutate `NetworkVariable` **by event, never per frame** — saturates bandwidth at tick rate (~30Hz).
- Position/rotation → `NetworkTransform` (interpolated), not custom `NetworkVariable<Vector3>`.

### RPC ordering

- Order guaranteed **only** between two RPCs of same type, same target, same sender.
- RPC sent during `OnNetworkSpawn` may arrive before target client has spawned the object → gate with `IsSpawned` or defer one frame.
- `GetSafeRpcTarget` fixes bot routing, **not** ordering — never assume.

### Domain reload (silent killer)

- Project may have **Enter Play Mode** with domain reload disabled (fast iteration). Then **all `static` fields and events survive across Play Mode sessions**.
- Any mutable `static` cache **must** register `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` to reset.
- `static event` subscribers must detach in `OnDestroy` or remain as zombie handlers.

### Serialization

- Always `[SerializeField] private` (or `protected`) — never `public` for inspector fields. `public` couples AI to "API surface".
- Renaming a serialized field **without `[FormerlySerializedAs("oldName")]` silently wipes prefab/scene value** — no compile warning.
- Complex RPC payloads → inherit `NetworkSerializableObject` (`Network/NetworkSerializableObject.cs`).
- Cross-client NetworkBehaviour refs → wrap in `NetworkBehaviourReferenceWrapper` — raw refs do not replicate.
- Polymorphic lists serialized in inspector (e.g. `List<IPower>`) → use `[SerializeReference]`, else Unity loses concrete type on reload.
- Designer-tunable static data → `ScriptableObject` under `Assets/ScriptableObjects/`.

### ScriptableObject runtime mutation

- Mutating a SO at runtime **persists to disk in Editor** (not in build) → silent designer-data corruption + Editor/build divergence.
- SOs are **read-only at runtime**. For mutable state, clone via `Instantiate(so)` at startup.

### Assembly definitions

| asmdef | Role | May depend on |
|---|---|---|
| `CorruptionDuPortail.Domain` | Pure POCO core (`noEngineReferences: true`, `references: []`) | **nothing** — compiler forbids UnityEngine/Netcode/FMOD/DOTween |
| `Game` | Runtime gameplay | `Domain`, Facepunch, third-party UPM |
| `Game.Editor` | Editor tooling | `Game`, UnityEditor.* |
| `Game.Rendering` | URP-bound code | `Game`, RP packages |
| `Tests.Editor` | EditMode tests (NSubstitute) | `Game`, `Game.Editor` |
| `Tests.PlayMode` | Multi-client tests | `Game`, `NetworkTestHelper` |

- **`Game` references `Domain`, never the reverse.** Never add a reference to the `Domain` asmdef to make an import compile — that inverts the layer. Relocating a type into `Domain`? The **test** asmdefs need an *explicit* `Domain` reference (`autoReferenced` does not reach them — CS0012 otherwise).
- **`Game` never references `Game.Editor` nor `Game.Rendering`** — silent build-player break, caught only in CI.
- Tests asmdefs depend on `Game`, never inverse. Helpers shared across tests → dedicated `Tests.Shared` asmdef, not `Game`.
- Render-pipeline code (URP `Volume`, `RendererFeature`, `ScriptableRenderPass`) → `Game.Rendering`, never `Game`.
- `.asmdef` JSON or Inspector — never edit auto-generated `.csproj`.
- `Tests.*` carry `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — only compile under Editor test runner.

### Architecture & Dependency Injection (post-refactor — Epics 1–12, the load-bearing shape)

> The whole game was de-spaghettified into a layered, injected architecture (PR #53, merged `Dev` 2026-06-13). **Read `_bmad-output/refactor-architecture-despaghetti.md` before touching any manager, DI, or adding a singleton** — the three CI guards fail a re-introduced locator. Baseline: EditMode 205/205, PlayMode 148/148.

#### Layering — Domain POCO core + thin adapters

- **Domain core:** `Assets/Scripts/Domain/`, asmdef `CorruptionDuPortail.Domain` (`noEngineReferences: true`, `references: []`). 19 pure POCO types — decision logic only (`VictoryEvaluator`, `VoteTally`, `ChainingResolver`, `GameLoopMachine`, `RoleDistributor`, `PowerResolver`, snapshots, narrow interfaces). The compiler *structurally* forbids `UnityEngine`/`Unity.Netcode`/FMOD/DOTween — **not even `Vector3`/`Mathf`/`Color`/`[SerializeField]`**. Need one? Pass the primitive in, or use a POCO equivalent. **Never add a reference to the `Domain` asmdef to fix a compile error** (`Game → Domain` only, never the reverse).
- **Thin adapters:** `MonoBehaviour`/`NetworkBehaviour` shrink to lifecycle + RPC plumbing + Unity glue. New decision logic → a Domain POCO with EditMode tests; keep the adapter thin (Humble Object).
- **A POCO returns a DECISION; it never applies it.** State transitions, `NetworkVariable` writes, `OnStart`/`OnEnd` sequencing are the adapter's job. A POCO that mutates network/engine state directly **freezes every client — no exception thrown**. If a Domain method feels like it should "do" something, return *what should be done*.

#### Dependency injection — three lanes (lane = HOW the object is created, decided once per type)

**Decision procedure for a new class — answer in order, stop at first YES:**

1. **NetworkBehaviour spawned as an NGO replica** (`Character`, `P*` powers, `LobbyPlayerInfoHolder`)? → **Lane C.**
2. **MonoBehaviour placed in a scene or on a prefab** (exists before runtime)? → **Lane A.**
3. **POCO / plain class our code `new`s or owns**? → **Lane B.**
4. **None of the above** (e.g. a `MonoBehaviour` `AddComponent`'d at runtime that is *not* an NGO replica)? → **STOP — do not guess, flag for a human.**

> Lane = **creation mode, not folder**. Two `P*` powers can be different lanes — a `P*` is Lane C **only if `OnNetworkSpawn` is actually called on it**. **Never mix `[SerializeField]` deps and `Initialize(deps)` on the same type — one lane per type.**

| Lane | Field type | Mechanism / where injected | Unwired failure |
|---|---|---|---|
| **A** scene/prefab-placed | **concrete** (interfaces aren't serializable) | `[SerializeField]`, wired in the scene/prefab asset via MCP | init `Assert` + **SceneWiringGuard** |
| **B** created by our code | **interface** (the testable lane) | `Initialize(deps)` / property-push by the creator (e.g. `SetupGameStates` clones + pushes into each `GameState`) | `Assert` in `Initialize` |
| **C** NGO-spawned replica | **interface slice** | resolve once in `OnNetworkSpawn` from `CompositionRoot.For(NetworkManager)`, store in fields | `Assert` in `OnNetworkSpawn` |

- **Depend on the narrow slice, never the manager or a locator.** `IGameLoop` / `IGameStateQuery` / `ICharacterQuery` / `ICharacterCommand` / `IRevealService` — only the slice you use; a class may implement several. Lane C resolves the **slice** off `Services`, never `Services.GameManager` (that resurrects the locator).
- Concrete consumed fields you'll see in code: powers read base fields `Power.chainingManager` / `roleTargetSystem` / `selectionFlowService` / `focusManager` / `lobbyPlayerInfoHolder` / `chatManager` (lane C). GameStates get `GameState.boardManager` / `statesCanvas` / `chainingManager` / `selectionFlowService` pushed by `SetupGameStates` (lane B). Card children read `Card.CharacterQuery` / `Card.GameInfoRevealer`; `StateUI` subclasses read base `StateUI.CharacterQuery`.

#### CompositionRoot — the ONE sanctioned project static

- `CompositionRoot.For(NetworkManager)` returns a `Services` (`readonly struct`) with **typed accessors** (no `Dictionary<Type,object>` bag), per-`NetworkManager`. It carries **no `instance` member** — `For(nm)` is the only entry.
- **Resolve once, in `OnNetworkSpawn`, store in a field.** Never call `For(nm)` from `Awake`/`Start`/a field initializer/a constructor (the root may not be registered for this replica's NM yet), nor from `Update`/a hot path/per-RPC (`For()` is a registry lookup, not free).
- **Resolve against `base.NetworkManager` / `NetworkObject.NetworkManager`, NEVER `NetworkManager.Singleton`.** The second in-process NM (`MultiClientGameFixture`) has no scene root; `.Singleton` resolves the wrong graph or null. `NetworkManager.Singleton` is the NGO idiom every LLM writes by reflex — it silently breaks multi-NM.
- **Do not read another spawned object's resolved fields inside your own `OnNetworkSpawn`** — cross-object spawn order is not guaranteed; defer cross-replica reads past spawn.

#### Fail loud — no locator fallback

- `Assert.IsNotNull(dep, "<dep> not wired")` immediately after each `For(nm)` resolution and each `[SerializeField]` read (in `Awake`/`Start`/`Initialize`/`OnNetworkSpawn`). A missed wiring must crash at the boundary, not three frames later in a stack-less NRE.
- **A `?? GameManager.instance` / `?? CharacterManager.instance` fallback is forbidden** — it silently re-introduces the locator the guards exist to kill.

#### Serialized-field safety (lane A — silent-breakage rule)

Unity serializes by **field name**. **Append** a new injected `[SerializeField]`; never rename / reorder / retype an existing one (orphans already-wired refs silently — no compile error). A new field is **null in every existing instance until explicitly wired**. After adding one — **before committing** — enumerate every instance (`find_gameobjects`), wire each via MCP (scene `manage_components`, prefab `manage_prefabs`), and **verify by reading the reference back**. Compile-green + tests-green do **not** cover this; `SceneWiringGuard` does — run it. **Never defer a wireable consumer** (a deferred wire is an untraceable playtest NRE). Rename unavoidable → `[FormerlySerializedAs("old")]`.

#### NFR5 — network-authority code is relocated, never edited

`GetSafeRpcTarget` / `IsLocalOrSimulated` / `clientId >= 100` move **verbatim** into the network adapter. Never edit, refactor, "simplify", or re-derive from memory — the bot-debug flow + `MultiClientGameFixture` gate them. A new RPC without `GetSafeRpcTarget` reddens no compile guard — it reddens `MultiClientGameFixture` in PlayMode. **Every new RPC ships with a fixture case.**

#### Permanent boundaries — NOT strangler façades (do not "finish removing" them)

- `GameManager` **owns** `currentGameStateIndex` (`NetworkVariable`) + the `OnEnd → write NV → OnStart` sequencing — replication state must live on a `NetworkBehaviour`; this is its designed adapter role. `GameLoopMachine` (POCO) computes only the arithmetic. Never write the index elsewhere.
- Opt-out global façades (keep `instance`, intentionally OUT of `ForbiddenLocators`): `GameAudioManager`, `LobbyManager`, `InputManager`.
- `GameManager.instance` / `CharacterManager.instance` survive as **recorded verify-don't-force façades**, read only by context-less static machinery (`W*` win-rules, `TargetUtils`, `PowerEffectDispatcher`) + test fixtures. **Grandfathered, not an invitation** — a NEW `.instance` call from injectable code is forbidden; don't route the existing callers through the root for "purity" either.

#### The three CI guards (a violation = red, not a silent runtime bug)

| Guard | `[Category]` | Fails when |
|---|---|---|
| `DiSeamNoLocatorGuardTests` | `DiSeamGuard` | a migrated type references `GameManager.instance` / `CharacterManager.instance` / `*.For(` (lane C `CompositionRoot` allowed **only** inside `OnNetworkSpawn`) |
| `SceneWiringGuardTests` | `SceneWiringGuard` | any injected `[SerializeField]` of a migrated consumer is null in GameScene/prefab |
| `StaticSingletonCensusGuardTests` | `StaticAbsenceGuard` | a non-whitelisted `static instance`/`Instance` exists |

- **A type is "migrated" only when all three are true, in order:** (a) zero locator outside `OnNetworkSpawn`; (b) every injected `[SerializeField]` wired on every instance; (c) added **once, last** to the shared `DiSeamMigratedConsumers` registry (both seam guards read it). Add to the set **last** — adding before (a)+(b) reddens SceneWiringGuard; omitting the set leaves `DiSeamGuard` **green-but-wrong** (false negative — worse than a failure). The registry is **lane C consumers only**; lanes A/B never appear (no phantom entries).
- **Never add a new `static instance`/`Instance`.** Need a shared service → inject it (lane B) or resolve via `CompositionRoot.For(nm)` (lane C). **Never whitelist your own new type to pass the census** — the whitelist is a frozen-debt ledger, not an escape hatch.
- **Run guards by category before pushing** (`run_tests` filtered to `DiSeamGuard` / `SceneWiringGuard` / `StaticAbsenceGuard`) — don't wait for the full suite to discover a red seam.

#### Subscription symmetry (lane variant — full rule in refactor-architecture-despaghetti §5b)

Every event / `NetworkVariable` subscription has an unsubscribe in the teardown mirror. **Lane decides where:** a NetworkBehaviour **Spawned** (lane C) unsubscribes in `OnNetworkDespawn`; one **Instantiated** (`StateUI` subclasses — `Instantiate`d, never `Spawn`ed, so `OnNetworkDespawn` never fires) unsubscribes in `OnDestroy`. Both: **cache the resolved target**, unsubscribe from the *same* reference, guard the deref (`if (cached != null) ...`).

#### Behaviour-preserving — golden masters

- `GameSnapshotBuilder.FromLiveState` must run **synchronously before any `await`** — a `NetworkVariable` can tear across a frame and move a golden with no real behaviour change. Read `hackedCharacterClientId` off the **live `POmniscience`** instance, never a serialized `Role`.
- A `[Category("GoldenMaster")]` master that **moves** = hidden behaviour disturbed. **STOP, find the cause, never "re-bless"** — and it is not a unilateral call, escalate.

### Async / cancellation

- `UniTask` / `UniTaskVoid` only — never `System.Threading.Tasks.Task`, never new `IEnumerator` coroutines.
- Every `.Forget()` in a `MonoBehaviour` **must** chain `this.GetCancellationTokenOnDestroy()`. Otherwise tasks survive destruction → NRE on dead refs.
- Long cancellable flows → `CancellableTaskHandler` (see `Board/CardComponents/`); token threaded through chain.
- Tweens: `tween.AwaitForComplete(cancellationToken)` (UniTask integration). Never `tween.AsyncWaitForCompletion()` (`Task`).

### DOTween hygiene

- `tween.SetLink(gameObject)` OR `DOTween.Kill(this)` in `OnDestroy`/`OnDisable` — else callback on null transform.
- UI tweens that must run during `Time.timeScale = 0` (pause menus): `SetUpdate(true)`.
- Gameplay state is **never** the result of a tween. Tweens are visual; source of truth lives in plain fields / `NetworkVariable`.

### Editor-only code

- `Editor/` folder for inspectors, property drawers, menu items (`Assets/Scripts/Editor/`, `PolymorphicPropertyDrawer/`).
- `#if UNITY_EDITOR ... #endif` inside runtime files OK for `OnDrawGizmos`, `OnValidate`, `ContextMenu` buttons.
- **`UNITY_INCLUDE_TESTS` ≠ `UNITY_EDITOR`** — PlayMode runner can ship as standalone. Testability hooks (internals exposed, reset helpers) → `#if UNITY_INCLUDE_TESTS`. Editor-only UI → `#if UNITY_EDITOR`. Mixing the two = green local / red CI.
- Expose internals to tests via `[assembly: InternalsVisibleTo("Tests.Editor")]` / `[assembly: InternalsVisibleTo("Tests.PlayMode")]` — never widen `public` for tests.

### Asset loading

- Gameplay dynamic loads → **Addressables** (`AsyncOperationHandle` + UniTask). Keep handle; `Release()` in `OnDestroy`/`OnNetworkDespawn` — refcount stuck else.
- Boot-time singleton config OK via `Resources` if not gameplay-dynamic.
- Global shared refs through `GameAssetHolder.cs`; never duplicate refs across managers.
- Persist GameObjects across scenes via `DontDestroyOnLoadComponent`, not manual `DontDestroyOnLoad()`.

### FMOD lifetime

- All gameplay sound through `AudioSystem/GameAudioManager` (FMOD). Never `AudioSource`.
- One-shots via `RuntimeManager.PlayOneShot` — auto-managed.
- Persistent `EventInstance` (music, ambience) → explicit `.release()` in teardown. Else native leak invisible to GC.
- `GameAudioManager` must be **no-op-safe** when FMOD uninitialized (Tests.Editor has no FMOD runtime).

### Testability constraints on gameplay code (design rules, not test rules)

- `NetworkBehaviour` exposes deps via `Initialize(...)` or `[SerializeField]` — never `FindObjectOfType` / singleton lookup in `OnNetworkSpawn`. Otherwise PlayMode tests must boot full scene.
- Pure decision logic (role distribution, power resolution, vote tally, victory check, validators) lives in **non-`MonoBehaviour` / non-`NetworkBehaviour`** classes — testable under Tests.Editor without booting.
- No branching on `Application.isPlaying` / `Application.isEditor` / `NetworkManager.Singleton.IsHost` in gameplay paths — pass through the Gateway abstraction so simulated bots match real host.
- No `Thread.Sleep` / `WaitForSeconds` for gameplay sync — wrap time behind a `ITimeProvider`-style abstraction.
- No `TEST_ForceState` backdoors — if a test needs a state, the production `Initialize` API is incomplete; refactor it.

### Render pipeline

- URP only. No Built-in shaders. Custom shaders under `Assets/Shaders/` target URP via ShaderGraph or HLSL.

### Performance Rules

#### Frame budget targets

- **Desktop/Steam:** 60fps target → 16.6ms/frame budget.
- **Mobile (Adaptive Performance enabled):** 60fps target, 30fps floor → 33ms/frame budget under thermal throttling.
- **Network tick rate:** ~30Hz (`NetworkConfig.TickRate`). RPCs batched per tick — never fire RPC per `Update()` frame.
- Adaptive Performance changes `QualityLevel` at runtime → don't cache `QualitySettings` values; subscribe `AdaptivePerformance.WarningLevelEvent`.

#### Hot path — zero-allocation discipline

`Update`, `FixedUpdate`, NGO tick callbacks, `OnGUI` = hot. Inside, never:

- `GetComponent<T>()` — cache in `Awake`/`OnNetworkSpawn`.
- `Camera.main` — `FindObjectWithTag` internal, ~0.5ms. Cache once.
- `transform.position`/`rotation` read 2+ times — local var; each access marshalls C++ ↔ C#.
- `string` concatenation, `$"..."` interpolation, `.ToString()` on value types — allocs → GC hitch. Use `StringBuilder` reused, or gate logs with `[Conditional("UNITY_EDITOR")]`.
- LINQ (`.Where`, `.Select`, `.ToList`, etc.) — allocates iterator + delegate. Rewrite as `for` indexed loop.
- `foreach` over `IEnumerable` typed as interface — boxing of enumerator. `foreach` on concrete `List<T>` is fine in modern Unity.
- `new` on reference types — pool via `Stack<T>` or `ObjectPool<T>` (`UnityEngine.Pool`).
- `Debug.Log` outside `#if UNITY_EDITOR` / `[Conditional]` — string formatting cost ships to player.

#### Networking bandwidth

- `NetworkVariable<T>` mutated by event, never per frame.
- Position/rotation → `NetworkTransform` with interpolation. Do not roll a custom `NetworkVariable<Vector3>` per frame.
- Large payloads (>1KB) → consider chunking; NGO message size limits apply.
- Simulated bots (`clientId >= 100`) still go through `GetSafeRpcTarget` — RPC dispatch is local but **not free**; same per-tick budget applies.

#### Memory & GC

- No allocations in steady-state gameplay. Profile with Memory Profiler / `Profiler.BeginSample`.
- Pool: cards, particles, UI list items, RPC payload structs (when reused).
- `List<T>.Clear()` reused over `new List<T>()`.
- Closures capturing locals → allocs. Prefer static lambdas + state passed explicitly.

#### UI / Canvas

- Smartphone UI = 2D Canvas-heavy. **Split canvases by update frequency**: static (background) vs dynamic (chat, notes) — any change rebuilds the whole canvas.
- `Canvas.willRenderCanvases` is the rebuild cost — monitor in Profiler.
- TMP `text.SetText(stringBuilder)` over `text.text = string` to avoid alloc.
- Disable off-screen canvases (`enabled = false`) — keeps mesh in memory but skips raycast/rebuild.

#### Asset loading hitches

- Preload at **lobby phase**: heavy prefabs (cards, role art), FMOD banks (`FMODUnity.RuntimeManager.LoadBank`), Addressables (`Addressables.DownloadDependenciesAsync`).
- Never `Addressables.LoadAssetAsync` mid-frame in gameplay state — schedule during state-transition cinematics.
- `Resources.Load` is synchronous → blocks frame. Avoid in gameplay path.

#### URP

- One `Volume` per scene preferred; nested volumes blend per-frame.
- `RendererFeature` cost adds per camera per frame — keep gameplay camera lean; debug features behind `#if UNITY_EDITOR`.
- Shader variants: avoid `_KEYWORD` proliferation — explodes build size + first-frame compile hitches.

#### DOTween scaling

- `DOTween.Init(useSafeMode: true, recycleAllByDefault: true, logBehaviour: LogBehaviour.ErrorsOnly)` once at boot.
- Bulk `DOTween.KillAll()` on scene unload — else dangling tweens touch destroyed transforms.

#### Profiling workflow

- Suspected hitch → `mcp__UnityMCP__manage_profiler` to capture frame; `mcp__UnityMCP__read_console` for GC warnings.
- Baseline before any "optimization" PR — un-profiled changes are speculation.

### Code Organization Rules

#### Folder structure (feature-first)

- All gameplay C# under `Assets/Scripts/<SystemName>/`. New system = new folder, never sprinkled across existing ones.
- One concern per folder. If a feature is a "system", it gets its own folder + (optionally) its own sub-asmdef.

#### Where new code goes

| Adding... | Goes in | Pattern |
|---|---|---|
| New game phase | `GameLogic/GameStates/` | Class `<Name>State.cs` + register in `GameManager` + extend `GameState` enum |
| New role power | `Characters/Powers/` | Class `P<Name>.cs` (prefix `P`) + impl `Characters/Powers/Interfaces/` + bind in `RoleDataObject` |
| New role | `Characters/` + SO under `Assets/ScriptableObjects/` | Add `RoleID` enum entry + `RoleDataObject` instance + faction bind |
| New victory condition | `Characters/WinningConditions/` | Predicate hooked from `VictoryConditionCheckState` |
| New RPC flow | `Network/` or system folder | Wrap target with `GetSafeRpcTarget`; payload via `NetworkSerializableObject` |
| Pure utility | `Extensions/` | Static class, unit-tested under `Tests/Editor/` |
| URP renderer feature / Volume | `Rendering/` | Stays out of `Game.asmdef` |
| Editor inspector / drawer | `Editor/` or `PolymorphicPropertyDrawer/` | Lives in `Game.Editor.asmdef` |
| Const string | `FixedStrings/` | No magic string literals in gameplay (typo on FMOD/NGO event = silent runtime miss) |
| Tunable runtime constant | `GameValues.cs` | Designer can tune w/o code |

> Note the **prefix vs suffix asymmetry** — Powers use prefix `P<Name>`, States/Managers/DataObjects use suffix. AI agents frequently invert.

#### In-game UI placement (Smartphone vs *System vs UI)

Three-question test before creating a new UI module:

1. Does the player tap the in-game phone to open it? → `Smartphone/Apps/<Name>/`
2. Is it a layer that can appear over anything (phone open or not)? → `<Name>System/` at top level
3. Is it a button/slot/tooltip widget reused by >2 systems with no domain logic? → `UI/`

**Content vs host pattern:** the system owns the data and gameplay logic; Smartphone owns the screen that renders it. `ChatApp` inside `Smartphone/` is a view that subscribes to `ChatSystem`. Putting logic in `Smartphone/Apps/<X>/` loses server authority and breaks multiplayer.

#### Module boundaries (load-bearing)

- **`Network/` is the sole NGO surface.** No `NetworkBehaviour`, `[ServerRpc]`, `[ClientRpc]`, or `NetworkVariable` outside `Network/` and the designated `*Manager` classes. Disperse them and `GetSafeRpcTarget` gets forgotten silently.
- **`Smartphone/` is client-only.** Reads game state and emits `ServerRpc` (wrapped). **Never mutates game state directly** — simulated bots desync silently.
- **Dependency direction (declared):** `Powers/` may reference `GameLogic/` interfaces, never the reverse. `Smartphone/` consumes domain interfaces, never concrete `*Manager` types. `Tests.*` depend on `Game`, never the inverse.
- **`Rendering/` quarantine:** URP-specific code (Volume, RendererFeature, ScriptableRenderPass) stays in `Game.Rendering.asmdef`. Never sneaks into `Game.asmdef`.
- **ScriptableObjects are pure data.** No `Instantiate`, no `FindObjectOfType`, no scene-side-effects in a `*DataObject`. SOs hold values + decision logic only.
- **No `UnityEvent` in Inspector for gameplay wiring.** Use typed C# events or a typed mediator. `UnityEvent` couplings are invisible to the compiler and survive every refactor as ghosts.

#### Naming conventions

- Classes / methods / properties: `PascalCase`. English identifiers always.
- Private fields: `_camelCase`. Use `[SerializeField] private` to expose to Inspector without widening visibility — never `public` fields.
- Constants: `PascalCase`. `const` if compile-time; `static readonly` if runtime.
- Interfaces: `IPascalCase` (`IPower`, `IWinningCondition`).
- Powers: prefix `P` — `PBlessing`, `PAutoCorruption`. Greppable + clear domain.
- States: suffix `State` — `LobbyState`, `ChainingState`.
- Managers (server-authoritative): suffix `Manager` — one per system.
- Data SOs: suffix `DataObject`. Under `Assets/ScriptableObjects/`.
- Test classes: suffix `Tests`; file mirrors path of subject under `Tests/Editor/` or `Tests/PlayMode/`.

#### Encapsulation

- Default visibility = `internal` or `private`. Promote to `public` only when crossing asmdef boundary.
- Cross-test internal access: `[assembly: InternalsVisibleTo("Tests.Editor")]` / `Tests.PlayMode`. Never widen to `public` for testability.

#### File layout

- One top-level type per file. File name = class name. **Load-bearing for AI navigability.**
- Partial classes only when Unity-generated (Input System actions, etc.).
- Nested helper types OK if `private` and small; else extract to own file in same folder.

#### Language

- Code identifiers: English only.
- Inline comments: French OK if surrounding code is French. Match surrounding style — never mix mid-file.

#### Folders never to commit / edit

- `Library/`, `Temp/`, `Logs/`, `TestResults/`, `obj/` — Unity-generated.
- Root `.csproj` / `.sln` — regenerated from `.asmdef`. `.gitignore` covers; never force-add.

#### Scene files

- Entry scenes: `BootScene`, `MainMenu`, `GameScene`. Scene routing via `SceneSwitcher.cs`.
- Legacy scenes (`(old)*.unity`, `*_backup.unity`) — not loaded by router; do not delete without coordination, do not extend.
- New scenes must include Main Camera + Directional Light.

### Testing Rules

#### When to write tests

| Trigger | Required? |
|---|---|
| New power (`P<Name>.cs`) | Yes — Tests.Editor for pure logic, Tests.PlayMode if RPC flow |
| New role + role distribution change | Yes — both EditMode + PlayMode |
| New game state / phase transition | Yes — PlayMode |
| New RPC / network flow | Yes — PlayMode multi-client |
| New validator / parser / extension | Yes — Tests.Editor |
| Bug fix with subtle root cause | **Yes, red-first** — regression test that reproduces the bug, then fix |
| Pure Unity boilerplate / wiring | Skip |
| New UI screen with no domain logic | Skip; covered manually |

#### EditMode vs PlayMode — choose correctly

| Concern | Tests.Editor | Tests.PlayMode |
|---|---|---|
| Pure C# logic (extensions, validators, parsers, decision functions) | ✅ | ❌ overkill |
| Single-process MonoBehaviour with no NGO | ✅ via NSubstitute | ❌ |
| Anything touching `NetworkBehaviour`, `NetworkVariable`, RPC | ❌ | ✅ via `NetworkTestHelper` |
| Multi-client / host + bot flows | ❌ | ✅ |
| Async chains (`UniTask`) | ✅ if no Unity coroutine | ✅ if needs frame ticks |
| FMOD / Addressables runtime | ❌ (no runtime) | ✅ |

Rule of thumb: **start in EditMode**. Push to PlayMode only when forced by NGO, real frame loop, or runtime services.

#### File layout & naming

- File path **mirrors source**: `Assets/Scripts/<System>/Foo.cs` → `Assets/Scripts/Tests/Editor/<System>/FooTests.cs` (or `Tests/PlayMode/<System>/`).
- Test class suffix `Tests` — `<TargetClass>Tests`. One class per subject.
- Test method name = scenario: `Foo_Bar_ReturnsBaz_WhenQux()`. Greppable when triage from CI.
- Both test asmdefs carry `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — only compile under Editor test runner. Expected.

#### NSubstitute (EditMode)

- Mock NGO surface via interface, never via raw `NetworkBehaviour` subclass.
- `Substitute.For<IFoo>()` — pass mocks through `Initialize(...)` (not via `FindObjectOfType`).
- `Received().Method(...)` for behavior assertions; `Returns(...)` for stubs.
- No `DateTime.Now` / `Time.time` — wrap in `ITimeProvider` and substitute.

#### PlayMode (NetworkTestHelper)

- All multi-client tests route through `NetworkTestHelper` (`Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs`). It spawns host + N simulated clients with `clientId >= 100`.
- Never call `NetworkManager.Singleton.StartHost()` directly in a test — bypasses the harness.
- RPCs in tests still wrap targets with `GetSafeRpcTarget` — the harness validates the bot path.
- `[UnityTest]` returns `IEnumerator` for Unity-side ; UniTask alternative: `[Test] public async Task ...` with `UniTask.ToCoroutine` pattern if needed.
- Yield with `null` (one frame) or `new WaitForFixedUpdate()` — **never `WaitForSeconds`**, makes tests time-dependent → CI flakes.

#### Test isolation

- No `static` mutable carried across tests. If a singleton is unavoidable, expose `ResetForTests()` behind `#if UNITY_INCLUDE_TESTS` and call in `[SetUp]`.
- `[TearDown]` releases Addressables handles, kills DOTween tweens (`DOTween.KillAll()`), and despawns NetworkObjects spawned by the test.
- Each test must pass independently and in any order. Order-dependence = flaky pipeline.

#### Bug fix workflow

1. Write failing test that reproduces the bug. Verify red.
2. Fix in source. Verify green.
3. Commit message: `fix(<scope>): <bug>` + body explaining why the test catches it.

#### Running tests

- `mcp__UnityMCP__run_tests` — preferred. Filter by `testMode: EditMode|PlayMode`, by `assembly:` name, or by `category:` if marked with `[Category("...")]`.
- After any code change: `mcp__UnityMCP__read_console` first (catch compile errors), then targeted test run.
- After feature complete: full suite. Zero red before review.
- CI: `unity-tests.yml` exists but currently gated `if: false`. Tests must still pass locally before PR.

#### What NOT to do in tests

- `Thread.Sleep`, `WaitForSeconds`, real-time waits — flakes guaranteed.
- DOTween or animation timing as test sync — visual layer, not source of truth.
- `Debug.Log` left in committed tests — noisy CI.
- `[Ignore("flaky")]` without an issue link — silent rot.
- Test that asserts on `Application.isPlaying` / editor-only branches — wrap behind interface and assert behavior instead.
- Boot the full GameScene to test one decision — extract pure logic, test in EditMode.

### Platform & Build Rules

#### Target platforms

- **Primary:** Windows / Steam (Facepunch transport in production).
- **Secondary:** Mobile (Android — Addressables Android variant + Adaptive Performance + `com.unity.feature.mobile` installed). iOS reachable via same stack.
- `NetworkTransportDetector.cs` selects transport at runtime — Steam (Facepunch) for shipped builds, local simulated gateway for solo debug.

#### Build pipeline

| Action | Tool | Notes |
|---|---|---|
| Local player build | `mcp__UnityMCP__manage_build` | Preferred. CLI Unity is **not** the primary workflow. |
| CI build | `.github/workflows/Build.yml` (game-ci) | Runs on push to `Dev` |
| Run tests | `mcp__UnityMCP__run_tests` | Local. CI `unity-tests.yml` exists but gated `if: false`. |

- Build target switching may invalidate Library/ — never delete it manually, let Unity rebuild.
- Addressables content build must precede player build for mobile (Android variant). Skipping yields runtime "asset not found".

#### CI / Discord notifications (load-bearing for commit format)

- `Build.yml` posts build status + **commit body** to Discord. Subject-only commits = silent team.
- `DevPush.yml`, `DevPROpened.yml`, `DevMergeNotif.yml` — push / PR / merge → Discord.
- Workflow yaml in `.github/workflows/` — never edit on a feature branch without flagging; CI changes need their own PR.

#### Platform-specific code

- `#if UNITY_STANDALONE_WIN` / `UNITY_ANDROID` / `UNITY_IOS` / `UNITY_EDITOR` for compile-time branches.
- Steam-specific code (`Facepunch.Steamworks`) **must** be gated. On non-Steam builds, transport falls back to local gateway. Never assume `SteamClient.IsValid` true.
- Mobile-specific paths (touch input, thermal throttling) → guard with `UNITY_ANDROID || UNITY_IOS` and pair with Editor stub so test runner doesn't crash.
- Editor-only packages (`com.unity.multiplayer.center`, `com.unity.multiplayer.playmode`, `com.coplaydev.unity-mcp`) **must not** be referenced from runtime asmdefs — they ship `Editor`-only assemblies; runtime ref = "type missing in player" at build.

#### Input

- Unity Input System 1.14.2. All bindings live in `Assets/InputSystem_Actions.inputactions`.
- New action → add to `.inputactions` asset, regenerate C# wrapper, consume via `Inputs/` system. Never read raw `Input.GetKey` (legacy Input Manager).
- Multi-platform action maps: ensure touch / gamepad / keyboard bindings all present for any new action.

#### Steam (Facepunch) integration

- Steam app id + lobby config: check `Network/Services/UnityServicesInit.cs` + `Facepunch/` bundle. Never hard-code app id in new code; read from existing config surface.
- Steam lobby create/join routed through `LobbyManager.cs` + `LobbyCreationSettings.cs`. Do not bypass — Unity Services lobby + Steam lobby must stay in sync.
- `GameCode.cs` — invite / join-code helpers. Use these; do not invent a parallel code scheme.

#### Mobile constraints

- Adaptive Performance: subscribe `AdaptivePerformance.WarningLevelEvent` for thermal throttling.
- Battery / bandwidth: lobby phase preloads — avoid network bursts mid-game.
- Touch input must coexist with keyboard / gamepad — no platform-exclusive UI flows.
- Screen resolution: smartphone OS UI scales via Canvas Scaler reference resolution; new screens must declare safe-area awareness.

#### Build artifacts

- Player builds output under `Builds/` (gitignored). Never commit binary builds.
- `BuildReport` saved by game-ci — consult on CI failure rather than rebuilding locally first.
- FMOD banks: re-export from FMOD Studio before build if events changed. Stale banks → `EVENT_NOTFOUND` at runtime in built player.

#### Versioning

- Bump `ProjectSettings/ProjectSettings.asset` `bundleVersion` only via PR — touches Steam build, mobile store version. Coordinate.
- Never bump `ProjectVersion.txt` (Unity Editor version) without team coordination.

### Critical Don't-Miss Rules

#### The silent killers (read first)

Six rules where breaking compiles cleanly, tests pass locally, and the bug surfaces only in multiplayer or in player builds:

1. **`GetSafeRpcTarget(clientId)` on every RPC target.** `clientId >= 100` is a simulated bot — host intercepts locally. Raw `ServerRpc/ClientRpc` calls silently break bot-debug flow.
2. **`IsLocalOrSimulated(clientId)` instead of `IsLocalClient`.** Host may act on behalf of simulated identity; plain `IsLocalClient` returns false → wrong branch silently.
3. **Server-authoritative state.** Mutate game state on server only. Clients propose via `ServerRpc`. Client-side mutation = cheat surface + desync.
4. **`UniTask` / `UniTaskVoid` only.** Never `System.Threading.Tasks.Task`. Every `.Forget()` in MonoBehaviour chains `this.GetCancellationTokenOnDestroy()`.
5. **FMOD via `AudioSystem/GameAudioManager` only.** Never `AudioSource` for gameplay audio. Banks live in `Assets/FMODBanks/`.
6. **Depend on injected slices, never `instance`/`For`.** Take an injected `IGameLoop`/`ICharacterQuery`/… field; `CompositionRoot.For(nm)` only inside `OnNetworkSpawn` (lane C), resolved against `base.NetworkManager` not `NetworkManager.Singleton`. A re-introduced locator compiles clean but fails `DiSeamGuard`. See **Architecture & Dependency Injection** above.

#### Commit conventions (load-bearing — surfaced to Discord)

- **Never** add Claude / AI as commit author or co-author. No `Co-Authored-By: Claude ...` trailers. No AI attribution in commits or PRs.
- **English only** — subject and body.
- **Always include a body** explaining the *why*. Never subject-only, even for `ci` / `chore` / `docs`.
- Subject = Conventional Commits (`feat(scope):`, `fix(scope):`, `chore:`, `ci:`, `docs:`, `refactor:`, `test:`).
- Body adds a `UX:` line when the change affects player experience. Skip `UX:` for pure infra/CI/tooling — still write a descriptive body.
- Commit body posted to Discord by `Build.yml` — the `UX:` line doubles as real-time team briefing.

Format:

```
feat(ui): animate role picker entrance/exit

Tween the role card in and out on a dedicated layer so the reveal is progressive.

UX: the player sees their role appear gradually — reduces confusion during role distribution.
```

#### Branch / PR conventions

- Main branch: **`Dev`** (PR target — not `main`/`master`).
- Branch name = `<type>/<short-description>` (e.g. `feat/blessing-power`, `fix/vote-recap-desync`).
- PRs are reviewed; CI `Build.yml` must be green before merge.
- Force-push to `Dev` is forbidden. Force-push to feature branch only after coordination if rebased.

#### Anti-patterns (compile clean, break silently)

| Anti-pattern | Why it fails | Right way |
|---|---|---|
| `[ServerRpc]` direct call with raw `clientId` | Bot debug flow breaks | Wrap with `GetSafeRpcTarget(clientId)` |
| `if (IsLocalClient)` for host logic | False for simulated identities | `IsLocalOrSimulated(clientId)` |
| Mutate `NetworkVariable.Value` from client | Server authority violation, silent on Host | `ServerRpc` proposal |
| `Task` / `async void` / `Task.Run` | Breaks UniTask cancellation chain | `UniTask` / `UniTaskVoid` + `Forget()` |
| `.Forget()` without `GetCancellationTokenOnDestroy()` | Task survives GO destruction → NRE | Chain the token |
| `AudioSource.Play()` for gameplay sfx | Bypasses FMOD mixer, no ducking | `GameAudioManager` API |
| `public` serialized field | Couples to "API surface", breaks encapsulation | `[SerializeField] private` |
| Rename serialized field w/o `[FormerlySerializedAs]` | Silent prefab/scene data wipe | Add the attribute first commit, remove a release later |
| `FindObjectOfType` in `OnNetworkSpawn` | Forces full-scene boot for tests + race | Inject via `Initialize(...)` / `[SerializeField]` |
| Mutating `ScriptableObject` at runtime | Persists to disk in Editor → designer data corruption | Clone via `Instantiate(so)` |
| `Resources.Load` for gameplay dynamic asset | Synchronous, blocks frame | `Addressables.LoadAssetAsync` + UniTask |
| Hard-coding magic string for FMOD event / NGO message | Typo = silent runtime miss | `FixedStrings/` |
| Editing `.csproj` / `.sln` | Auto-regenerated from `.asmdef` | Edit the `.asmdef` |
| Bumping single package in `manifest.json` (e.g. NGO without Multiplayer Tools) | Version mismatch silent | Paired bump |
| `GameManager.instance`/`CharacterManager.instance`/`*.For(` in a migrated type | Re-introduces locator → `DiSeamGuard` red | Inject the slice; root only inside `OnNetworkSpawn` |
| New `static instance`/`Instance` | `StaticAbsenceGuard` red | Inject (lane B) or `CompositionRoot.For(nm)` (lane C); never whitelist your own type |
| `NetworkManager.Singleton` to resolve a service in a replica | Wrong graph under multi-NM fixture, silent | `base.NetworkManager` + `CompositionRoot.For(NetworkManager)` |
| POCO writes `currentGameStateIndex` / triggers a transition | Clients freeze, no exception | Return a decision; the adapter applies it |
| Adding a reference to the `Domain` asmdef to fix a compile error | Inverts the layer (`Domain → Game`) | Pass the primitive / use a POCO equivalent |
| New `[SerializeField]` committed unwired | Null in every instance → untraceable playtest NRE | Wire every instance via MCP + read-back before commit (`SceneWiringGuard`) |
| `Co-Authored-By: Claude ...` in commit | Project policy violation | Remove |

#### Gotchas (engine-specific surprises)

- **Domain reload disabled** — statics survive across Play Mode sessions. Use `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`.
- **`OnNetworkSpawn` cross-object order not guaranteed** — defer via `OnClientConnectedCallback` if dep on another spawn.
- **`OnNetworkDespawn` before `OnDestroy`** — unsubscribe `NetworkVariable.OnValueChanged` in Despawn.
- **`NetworkManager.Singleton == null` in `OnDestroy`** during shutdown — null-check.
- **CI `unity-tests.yml` gated `if: false`** — local test run still required pre-PR.
- **`(old)*.unity` / `*_backup.unity` scenes** exist — not loaded, don't extend.
- **Repo is partially French** — code English, comments may be French — match surrounding style.

#### When in doubt

- Read `CLAUDE.md` first (load-bearing patterns + commit rules).
- Before touching managers, DI, or adding a singleton: read `_bmad-output/refactor-architecture-despaghetti.md` (+ `-poco.md`, `-desingleton.md`) — the post-2026-06 injected shape; the three guards fail a re-introduced locator.
- Read relevant `_bmad-output/<system>.md` doc before non-trivial changes.
- Read 2–3 neighbor files in the same folder before adding new code (conventions imitable).
- `mcp__UnityMCP__read_console` after every code change.
- `mcp__UnityMCP__run_tests` filtered to the changed assembly before declaring done.
- Ask the user when destructive (delete files, force-push, drop tests) — never silently bypass.

---

## Usage Guidelines

**For AI Agents:**

- Read this file before implementing any game code.
- Follow ALL rules exactly as documented.
- When in doubt, prefer the more restrictive option.
- Start with the **Silent Killers** subsection — it's the highest-leverage 5 rules.
- Use `CLAUDE.md` + this file + `_bmad-output/index.md` as the entry-point triad.

**For Humans:**

- Keep this file lean and focused on agent needs.
- Update when technology stack changes (`Packages/manifest.json`, `ProjectVersion.txt`).
- Review quarterly for outdated rules — remove anything that has become obvious from the code itself.
- Refresh via `/gds-generate-project-context` after structural changes (new system, asmdef split, render pipeline change).

Last Updated: 2026-06-13 (refreshed post-refactor — added Architecture & Dependency Injection section: Domain POCO layer, three-lane injection, CompositionRoot, the three CI guards; patched asmdef table, anti-patterns, silent killers)
