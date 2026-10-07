# Architecture — Corruption du Portail

## Executive summary

Server-authoritative multiplayer Unity game using **Netcode for GameObjects (NGO)** over **UTP + Unity Relay** (dtls → wss fallback) or the vendored **Facepunch Steam** transport. The host owns all game-state mutations; clients propose via `ServerRpc`s. Cinematics and powers run asynchronously through **UniTask**.

Two signature architectural features:

1. **Gateway RPC** layer — the host can locally simulate additional player identities (`clientId >= 100`) to debug full lobbies without spinning up multiple Steam clients.
2. **Layered POCO core + CompositionRoot DI** — a behaviour-preserving refactor (Epics 1–12, completed 2026-06) extracted a pure, Unity-free **Domain** layer of tested decision logic and replaced the singleton/God-Object spaghetti with a single composition root and narrow injected interfaces. The runtime is now **thin NGO/Mono adapters over tested POCOs**, with dependencies declared and injected through three lanes rather than resolved by global lookup.

## Technology stack

See [project-context.md](./project-context.md#stack-versions--packagesmanifestjson-projectsettingsprojectversiontxt).

## Architecture pattern

**Layered server-authoritative networking** with feature-first module decomposition. Three layers, bottom-up:

```
┌─ Composition ─────────────────────────────────────────────┐
│  CompositionRoot (THE one surviving project static)        │
│   • scene-placed in GameScene; per-NetworkManager registry │
│   • For(nm) → typed accessors: GameLoop, GameStateQuery,   │
│     CharacterQuery, CharacterCommand, BoardManager, …       │
├─ Adapters (thin MonoBehaviour / NetworkBehaviour) ─────────┤
│  GameManager · CharacterManager · BoardManager · powers ·  │
│  GameStates · StateUI · ChatManager …                       │
│   • lifecycle, RPC plumbing (GetSafeRpcTarget), NetworkVar │
│   • implement narrow interfaces (IGameLoop, ICharacterQuery)│
├─ Domain (pure POCO — no UnityEngine) ──────────────────────┤
│  GameLoopMachine · GameSnapshot · VoteTally · RoleDistributor│
│  VictoryEvaluator · ChainingResolver · PowerResolver · …    │
│   • all decision logic, all EditMode-tested                 │
└────────────────────────────────────────────────────────────┘
```

Dependencies flow **downward only**: adapters depend on Domain POCOs; nothing in Domain references Unity or the adapters. Consumers reach other systems through **injected narrow interfaces**, not `instance`/`For(nm)` locators.

## The Domain layer — pure POCO decision core

`Assets/Scripts/Domain/` (`CorruptionDuPortail.Domain.asmdef`) holds **19 framework-free types**. A permanent purity guard keeps `UnityEngine` out of this assembly; every type is unit-tested in EditMode. Adapters **compute nothing** load-bearing — they call a Domain POCO and apply the returned decision.

| Domain type | Responsibility |
|---|---|
| `GameLoopMachine` (+ `GameLoopTransition`) | Pure advance/rewind index arithmetic for the state loop (adapter owns the `NetworkVariable` and fires events) |
| `GameSnapshot` / `CharacterSnapshot` | Immutable value-object views of live game/character state |
| `VoteTally` | Vote counting / resolution |
| `ChainingResolver` | Chaining-phase ordering + resolution |
| `VictoryEvaluator` + `IWinningCondition` / `IWinningConditionEvaluator` | Win-condition evaluation over a snapshot |
| `RoleDistributor` | Deterministic role assignment |
| `PowerResolver` / `PowerUsability` / `EffectDescriptor` | Power resolution + per-state usability + effect descriptors |
| `ChatChannelPolicy` | Chat channel/visibility decisions |
| `CardLayout` | Board card layout math |
| `TooltipLinkFormatter` | Tooltip rich-text link formatting |
| `IRandomProvider` / `SeededRandomProvider` | Randomness port — seedable for deterministic tests |
| `FactionType` / `WinningTeam` | Shared domain enums |

The live adapters build a `GameSnapshot` via `GameLogic/Snapshot/GameSnapshotBuilder.cs`, hand it to a Domain evaluator, and apply the result. `IRandomProvider` is backed by `UnityRandomProvider` in production and `SeededRandomProvider` under test, making role distribution and shuffles reproducible.

## Dependency injection — CompositionRoot + three lanes

There is **one surviving project static**: `CompositionRoot` (scene-placed in GameScene). The previous **24 singletons collapsed to 1**. Every migrated consumer **declares** its dependency and depends on a **narrow interface**, not a whole manager or a global lookup. No DI framework — the injection *mechanism* is chosen by **how the object is created**, decided once per type:

| Lane | Who | Mechanism | Failure if unwired |
|---|---|---|---|
| **A — scene/prefab-placed** | LightManager, RoomFog, cameras, scene UI, CompositionRoot itself | `[SerializeField]` concrete field, wired in scene/prefab | init `Assert` + **SceneWiringGuard** (CI) |
| **B — created by our code** | powers (server spawn), GameStates, StateUI | `Initialize(deps)` / property-push by the creator | `Assert` inside `Initialize` |
| **C — NGO-spawned client replicas** | `Character`, `P*` powers, `LobbyPlayerInfoHolder` | resolve **once** in `OnNetworkSpawn` from `CompositionRoot.For(NetworkManager)` | `Assert` in `OnNetworkSpawn` |

`CompositionRoot.For(nm)` returns a lightweight `Services` value-resolver (a `readonly struct`) delegating to the per-`NetworkManager` manager registries (`GameManager.For(nm)` / `CharacterManager.For(nm)`). Production has one NM, so `For(Singleton)` resolves the scene-wired managers — behaviour-identical to the old locator. The multi-client test fixture's second in-process NM resolves its own graph with zero fixture changes.

### Narrow interfaces (the real decoupling)

Each God Object's surface was split into intent-named interfaces a caller depends on à la carte. A class implementing several is fine — **callers see only the slice they use**:

| Interface | File | Slice |
|---|---|---|
| `IGameLoop` | `GameLogic/IGameLoop.cs` | game-loop commands (advance, next-state) |
| `IGameStateQuery` | `GameLogic/IGameStateQuery.cs` | read-only state-machine queries (`currentGameStateIndex`, …) |
| `ICharacterQuery` | `Characters/ICharacterQuery.cs` | character reads (`GetCharacter(s)`, local identity, list-update events) |
| `ICharacterCommand` | `Characters/ICharacterCommand.cs` | character spawn/mutation commands |

`GameManager` and `CharacterManager` survive as **thin network adapters** implementing these interfaces — no longer grab-bags. Their `instance` accessors are recorded façades read only by context-less static machinery (see exceptions below).

### Three CI guards (permanent)

- **`DiSeamNoLocatorGuardTests`** (`[Category("DiSeamGuard")]`, source scan) — a migrated consumer must never reference `GameManager.instance` / `CharacterManager.instance` / `*.For(`. `CompositionRoot` may appear **only inside `OnNetworkSpawn`** (the lane-C whitelist).
- **`SceneWiringGuardTests`** (EditMode) — loads GameScene + prefab composition roots, asserts every injected `[SerializeField]` of every migrated consumer is non-null. Turns a forgotten drag-drop into red CI instead of a silent runtime NRE.
- **`StaticSingletonCensusGuardTests`** (`[Category("StaticAbsenceGuard")]`) — scans the game assembly and fails on any non-whitelisted static `instance`/`Instance`. Whitelist = the recorded survivors, each with a reason.

### Recorded permanent exceptions (verify-don't-force)

Not every reference has an injection context. These legitimately stay, each recorded with a reason:

- **`GameManager.instance` / `CharacterManager.instance`** — read only by context-less static machinery: serializable winning-condition POCOs (`WAnomalyCorruption`, `WChosenChainedAllAnomaly`, `WMarginalIsChainedWin`, `WOmniscienceHackedCharacter`), the `TargetUtils` static class, the `PowerEffectDispatcher` static, and the network test fixtures.
- **Global-service façades (not de-singletonised):** `GameAudioManager` (FMOD), `InputManager`, `LobbyManager` — global services with no lifecycle, served as-is. The remaining replicated singletons (`ChatManager`, `RoleTargetSystem`, `BoardManager`, `ChainingManager`, `StatesCanvas`, `MessageManager`, `LobbyPlayerInfoHolder`, `SelectionFlowService`, `FocusManager`) are served **through the root** — consumers read injected fields, the root forwards to the one instance.
- **Index-ownership adapter boundary:** `GameManager` keeps owning the `currentGameStateIndex` `NetworkVariable` + the `OnEnd → NV write → OnStart` sequencing. Replication state must live on a `NetworkBehaviour`; `GameLoopMachine` computes only the arithmetic. This is a **designed permanent boundary, not a strangler façade**.

> Full rationale, the 24→1 ledger, and per-system census: [refactor-architecture-despaghetti.md](./refactor-architecture-despaghetti.md) (+ companions [refactor-architecture-poco.md](./refactor-architecture-poco.md), [refactor-architecture-desingleton.md](./refactor-architecture-desingleton.md)).

## Game loop (`GameState`)

| State | Class | Role |
|---|---|---|
| Lobby | `LobbyState` | Pre-game player join/ready |
| Introduction | `GameIntroductionState` | Intro cinematic |
| RoleAttribution | `RoleAttributionState` | Server assigns roles |
| Awakening | `AwakeningState` | Role-specific night actions |
| AwakeningRecap | `AwakeningRecapState` | Cinematic recap |
| Chaining | `ChainingState` | Players accuse / chain |
| TakeDownThePortal | `TakeDownThePortalState` | Endgame mechanic |
| Vote | `VoteState` | Group vote |
| VoteRecap | `VoteRecapState` | Vote results display |
| VictoryConditionCheck | `VictoryConditionCheckState` | Decide game end |
| GameEnding | `GameEndingState` | Outro cinematic |

State transitions are dictated by the server. Each state lives under `Assets/Scripts/GameLogic/GameStates/`. The transition **arithmetic** lives in the `GameLoopMachine` Domain POCO; `GameManager` (the adapter) owns the index `NetworkVariable` and fires the events — the load-bearing `OnEnd → NV write → OnStart` ordering is pinned by a sequence golden master.

## Critical patterns (load-bearing — break the game if ignored)

### 1. `GetSafeRpcTarget(clientId)` — always wrap RPC targets

If `clientId >= 100` the target is a simulated bot. The host intercepts the call locally instead of sending it across the wire. Calling `ServerRpc/ClientRpc` directly with a raw client id silently breaks the bot-debug flow. (This code is NFR5-protected — relocated verbatim during the refactor, never edited.)

### 2. `IsLocalOrSimulated(clientId)` — never use plain `IsLocalClient`

The host may legitimately act on behalf of a simulated identity. `IsLocalClient` returns false for those identities and produces wrong branches.

### 3. Strict server authority

All gameplay-state mutation happens server-side. Clients submit `ServerRpc` proposals; the server validates and broadcasts.

### 4. No-locator rule (post-refactor)

A migrated consumer depends on an **injected** narrow interface (lane A/B/C), never on `GameManager.instance` / `CharacterManager.instance` / `*.For(nm)`. The only sanctioned static lookup is `CompositionRoot.For(nm)`, and only inside `OnNetworkSpawn` (lane C). A missed wiring fails **loud and early** via init-time `Assert.IsNotNull` with **no locator fallback** — guarded in CI by the three guards above.

## Feature-based module layout

### Domain core
- `Domain/` — pure POCO decision logic, own asmdef, no UnityEngine. The tested heart (see Domain layer above).

### Core gameplay
- `GameLogic/` — `GameManager`, `CompositionRoot`, state router + `GameStates/`, `ChainingManager`, `PowerManager`, `PowerUsageManager`, `GameInfoRevealer`, `IGameLoop`/`IGameStateQuery`, `Snapshot/GameSnapshotBuilder`, `UnityRandomProvider`, vote, victory, state UI.
- `Characters/` — `Character`, `CharacterManager`, `ICharacterQuery`/`ICharacterCommand`, `Role`, `RoleDataObject`, `RoleID`, `FactionType`, `Powers/` (one `P<Name>.cs` per power + `PowerEffectDispatcher`/`PowerEffectTrace`), `WinningConditions/`.
- `Board/` — `BoardManager`, `Card`, `CardComponents/`, `CardEffects/`, `BoardCameraSystem/`, `LightManager`, board UI.

### Networking
- `Network/Player/` — `LocalPlayerInfo`, `PlayerInfo`.
- `Network/Services/` — `LobbyManager`, `LobbyCreationSettings`, `UnityServicesInit`.
- `Network/` (top level) — `GameCode`, `NetworkDictionary`, `NetworkSerializableObject`, `NetworkTransportDetector`, `NetworkBehaviourReferenceWrapper`, `LobbyPlayerInfoHolder`, `ConnectedPlayerPanel`.
- `Facepunch/` — Steam transport bundle.

### In-game UI hub
- `Smartphone/` — 2D in-game smartphone OS (chat, notes, info tables, role target).
- `ChatSystem/`, `NoteSystem/`, `MessageSystem/`, `TooltipSystem/`, `FocusSystem/`, `RoleTargetSystem/`, `ArrowSystem/`.

### Presentation / IO
- `AudioSystem/` — FMOD wrapper, `GameAudioManager` is the entry point. **Never use `AudioSource` for gameplay sounds.**
- `FX/` — visual effects.
- `Rendering/` — render-pipeline-aware code (own asmdef).
- `Inputs/` — Input System bindings.
- `UI/` — shared UI widgets.

### Foundation
- `Extensions/` — pure utility extensions (heavily unit-tested under `Tests/Editor/`).
- `Misc/`, `CustomAttributes/`, `FixedStrings/`, `PolymorphicPropertyDrawer/`.

## Async / animation conventions

- **All async code** uses `UniTask`, `UniTaskVoid` — never `System.Threading.Tasks.Task`.
- **Tweens**: DOTween + UniTask integration (`AwaitForComplete`).
- **Long-running cancellable flows**: `CancellableTaskHandler` (see `Board/CardComponents/`).

## Audio

FMOD only for gameplay. `AudioSystem/GameAudioManager` is the single entry point (a recorded global-service façade, not injected). Banks live under `Assets/FMODBanks/`.

## Networking transport

- **Production:** Facepunch (Steam) — `Facepunch/com.community.netcode.transport.facepunch.asmdef`.
- **Local debug:** simulated-player gateway (no need for multiple Steam clients).
- **Lobby/auth:** Unity Services (Multiplayer, Authentication).

## Architecture-relevant gotchas

- **Serialized-field safety (silent-breakage rule):** Unity serializes by field name. **Append** new injected `[SerializeField]` fields; never rename/reorder/retype existing ones — that orphans already-wired references silently. A new field is null in every existing instance until explicitly wired via MCP; verify by reading the reference back. Use `[FormerlySerializedAs]` if a rename is unavoidable.
- **Domain purity:** `CorruptionDuPortail.Domain` must not reference `UnityEngine` — a purity guard enforces it. New decision logic goes here with EditMode tests; the adapter stays thin.
- Repo is partially French — code identifiers are English, but commit messages and some comments are French. Match surrounding style.
- `.csproj` files at root are **auto-generated** by Unity from `.asmdef`. Edit the `.asmdef`, never the csproj.
- `Library/`, `Temp/`, `Logs/`, `TestResults/`, `obj/` — Unity-generated, never edit or commit.
- Test assemblies use `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — compile only with the Editor test runner.
