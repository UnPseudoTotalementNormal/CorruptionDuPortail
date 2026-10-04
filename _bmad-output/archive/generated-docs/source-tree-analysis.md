# Source Tree Analysis

## Top-level layout

```
CorruptionDuPortail/
├── Assets/                    # Unity asset root — everything game-side
│   ├── Adaptive Performance/  # Mobile perf scaling package data
│   ├── AddressableAssetsData/ # Addressables groups/settings
│   ├── Art/                   # Visual assets (textures, models, materials)
│   ├── Editor/                # Editor-only content
│   ├── FMODBanks/             # FMOD audio banks (read by GameAudioManager)
│   ├── Plugins/               # Native plugins / DLLs
│   ├── Prefabs/               # Reusable game objects
│   ├── Resources/             # Resources.Load fallback content
│   ├── Samples/               # Imported package samples
│   ├── Scenes/                # Unity scenes (entry points below)
│   ├── ScriptableObjects/     # SO instances (role data, settings)
│   ├── Scripts/               # ★ All C# source — feature-first layout
│   ├── Settings/              # URP/render settings
│   ├── Shaders/               # Custom shaders (HLSL/ShaderGraph)
│   ├── StreamingAssets/       # Runtime-streamed assets
│   ├── Tests/                 # (mirrors Scripts/Tests for asset linkage)
│   └── InputSystem_Actions.inputactions  # Input bindings
├── Packages/                  # Unity package manifests + lock
│   └── manifest.json          # ★ Dependency list — edit here, not csproj
├── ProjectSettings/           # Unity project config (committed)
│   └── ProjectVersion.txt     # → 6000.2.6f2
├── .github/workflows/         # CI: Build.yml, unity-tests.yml (disabled), Dev* notif workflows
├── _bmad/                     # BMad install (this skill suite)
├── _bmad-output/              # BMad-generated artifacts (this folder)
├── docs/                      # GDS `project_knowledge` slot (currently empty)
├── CLAUDE.md                  # ★ Primary AI-agent contract — points to _bmad-output/
└── (auto-generated csproj/sln, ignored)
```

> Folders **not to touch / not to commit**: `Library/`, `Temp/`, `Logs/`, `TestResults/`, `obj/`.

## Scenes (entry points)

| Scene | Role |
|---|---|
| `Assets/Scenes/BootScene.unity` | First scene loaded; bootstraps NGO + Unity Services |
| `Assets/Scenes/MainMenu.unity` | Main menu |
| `Assets/Scenes/GameScene.unity` | Active gameplay scene |
| `Assets/Scenes/(old)MenuScene.unity` | Legacy menu — kept for reference |
| `Assets/Scenes/GameScene_backup.unity` | Backup snapshot |

Scene routing is handled by `Assets/Scripts/SceneSwitcher.cs`.

## Scripts annotated tree

```
Assets/Scripts/                         ★ All gameplay C# (~344 .cs; ~272 runtime, ~72 tests)
├── Domain/                             ★ Pure POCO decision core (own asmdef, no UnityEngine)
│   ├── GameLoopMachine.cs              State-loop advance/rewind arithmetic
│   ├── GameSnapshot.cs / CharacterSnapshot.cs   Immutable state value-objects
│   ├── VoteTally.cs                    Vote counting/resolution
│   ├── ChainingResolver.cs            Chaining ordering/resolution
│   ├── VictoryEvaluator.cs + IWinningCondition(Evaluator).cs   Win-condition eval
│   ├── RoleDistributor.cs             Deterministic role assignment
│   ├── PowerResolver.cs / PowerUsability.cs / EffectDescriptor.cs
│   ├── ChatChannelPolicy.cs / CardLayout.cs / TooltipLinkFormatter.cs
│   ├── IRandomProvider.cs / SeededRandomProvider.cs   Randomness port (seedable)
│   └── FactionType.cs / WinningTeam.cs   Shared domain enums
├── ArrowSystem/                        On-board pointer arrows
├── AudioSystem/                        FMOD wrapper — GameAudioManager
├── Board/                              3D card rendering + animations
│   ├── BoardManager.cs                 Top-level board orchestrator
│   ├── Card.cs                         Single card abstraction
│   ├── CardComponents/                 Sub-behaviors per card (incl. CancellableTaskHandler)
│   ├── CardEffects/                    Visual effect bindings
│   ├── BoardCameraSystem/              Cinemachine wiring
│   ├── LightManager.cs                 Stage lighting
│   └── UI/                             Board overlay UI
├── Characters/                         Identity, factions, powers
│   ├── Character.cs                    Per-player networked actor
│   ├── CharacterManager.cs             Roster + role distribution
│   ├── CharacterManager.cs             Roster + role distribution (thin adapter)
│   ├── ICharacterQuery.cs / ICharacterCommand.cs   ★ Narrow injected slices (Epic 9)
│   ├── Role.cs / RoleID.cs / RoleDataObject.cs   Role definitions (SO-backed)
│   ├── FactionType.cs / CharacterType.cs
│   ├── Powers/                         ★ One P*.cs per power (PBlessing, PAutoCorruption, ...)
│   │   ├── Interfaces/                 Power contracts
│   │   └── PowerEffectDispatcher.cs / PowerEffectTrace.cs   Effect application
│   └── WinningConditions/              Victory predicates per faction (W*.cs, eval via Domain)
├── ChatSystem/                         In-game chat
├── CustomAttributes/                   C# attribute helpers
├── Editor/                             Custom inspectors + tooling (Game.Editor.asmdef)
├── Extensions/                         Pure utilities (★ unit-tested in Tests/Editor)
├── FX/                                 Visual effects
├── Facepunch/                          Steam transport bundle
├── FixedStrings/                       Constant string tables
├── FocusSystem/                        UI focus management
├── GameLogic/                          ★ Core game loop
│   ├── GameManager.cs                  Game-loop adapter (impl IGameLoop/IGameStateQuery)
│   ├── CompositionRoot.cs              ★ THE one surviving static — DI seam (For(nm))
│   ├── IGameLoop.cs / IGameStateQuery.cs   ★ Narrow injected slices (Epic 8)
│   ├── UnityRandomProvider.cs          Production IRandomProvider impl
│   ├── Snapshot/GameSnapshotBuilder.cs Builds Domain GameSnapshot from live state
│   ├── GameState.cs / GameStateSettings.cs
│   ├── GameStates/                     One class per game phase
│   ├── ChainingManager.cs              Chaining phase logic
│   ├── PowerManager.cs                 Server-side power execution
│   ├── PowerUsageManager.cs            Per-game-state usage budgets
│   ├── GameInfoRevealer.cs             Reveals info to clients
│   ├── StateUI.cs                      State-bound UI hooks
│   └── Validation/                     Input validators
├── Inputs/                             Input System action bindings
├── MessageSystem/                      In-world / UI messages
├── Misc/                               Misc utilities
├── Network/                            ★ NGO bootstrap + gateway
│   ├── Player/                         LocalPlayerInfo, PlayerInfo
│   ├── Services/                       LobbyManager, UnityServicesInit
│   ├── GameCode.cs                     Lobby code helpers
│   ├── NetworkDictionary.cs            Networked dict primitive
│   ├── NetworkSerializableObject.cs    Custom serialization base
│   ├── NetworkTransportDetector.cs     Switches Steam ↔ local
│   ├── NetworkBehaviourReferenceWrapper.cs
│   ├── ConnectedPlayerPanel.cs / LobbyPlayerInfoHolder.cs
│   └── IsServerTest.cs
├── NoteSystem/                         Smartphone notes
├── PolymorphicPropertyDrawer/          Inspector helper for polymorphic SOs
├── Rendering/                          Render-pipeline-aware code (own asmdef)
├── RoleTargetSystem/                   Role targeting UI/logic
├── Smartphone/                         In-game 2D smartphone OS hub
├── Test/                               (placeholder / staging)
├── Tests/                              ★ Unit + PlayMode tests (~205 EM / ~148 PM cases)
│   ├── Editor/                         Pure C# tests, NSubstitute (~37 files)
│   │   ├── Domain POCO tests (golden masters): VictoryEvaluator, VoteTally,
│   │   │   ChainingResolver, RoleDistributor, GameLoopMachine, PowerResolver, ...
│   │   ├── DI guards: DiSeamNoLocatorGuardTests, SceneWiringGuardTests,
│   │   │   StaticSingletonCensusGuardTests, LeafPocoNoFacadeGuardTests
│   │   ├── *ExtensionsTests, ValidatorTests, TooltipLinkParserTests, ...
│   │   └── Tests.Editor.asmdef
│   └── PlayMode/                       Networked tests via NetworkTestHelper (~35 files)
│       ├── BoardTests, ChainingManagerTests, ChatManagerTests, GameManagerTests,
│       │   PowerTests, snapshot differential / oracle tests, ...
│       ├── MultiClientGameFixture      ★ host + real client + simulated bot, in-process
│       ├── NetworkTestHelper.cs        ★ Multi-client simulation harness
│       └── Tests.PlayMode.asmdef
├── TooltipSystem/                      Hover tooltips
├── UI/                                 Shared UI widgets
├── Domain/CorruptionDuPortail.Domain.asmdef   ★ Pure POCO assembly (no UnityEngine)
├── Game.asmdef                         ★ Main runtime assembly
├── GameAssetHolder.cs                  Global asset references
├── GameSceneOnlineChecker.cs           Online state guard
├── GameValues.cs                       Tunable constants
├── DontDestroyOnLoadComponent.cs       Persistence helper
├── ReflectionHelper.cs                 Reflection utilities (tested)
├── SceneSwitcher.cs                    Scene transitions
└── TransformFollower.cs                Transform follow helper
```

## Critical folder summary

| Folder | Why it matters | Owner system |
|---|---|---|
| `Domain/` | Pure decision logic (no UnityEngine — purity-guarded). New testable logic goes here, adapter stays thin | Domain core |
| `GameLogic/CompositionRoot.cs` | The one sanctioned static — DI seam; lane-C consumers resolve via `For(nm)` in `OnNetworkSpawn` | Composition |
| `GameLogic/GameStates/` | Adding a phase = new state class here + register in GameManager routing | Core loop |
| `Characters/Powers/` | Adding a power = new `P*.cs` + interface impl + role binding | Roles/powers |
| `Network/` | All RPC/gateway code — must respect `GetSafeRpcTarget` / `IsLocalOrSimulated` | Networking |
| `Tests/Editor/` | Pure logic — add tests here when changing `Extensions/`, validators, parsers | QA |
| `Tests/PlayMode/` | Multi-client flows — add tests here for new powers, network changes | QA |
| `AudioSystem/` | All gameplay sound goes through `GameAudioManager` (FMOD) | Audio |
| `Smartphone/` | Big self-contained system; treat as its own sub-app | UI hub |

## Entry points

- **Bootstrap:** `Assets/Scenes/BootScene.unity` → loads `MainMenu.unity` → `GameScene.unity`.
- **Code entry:** `Assets/Scripts/GameLogic/GameManager.cs` (server-side game lifecycle).
- **DI seam:** `Assets/Scripts/GameLogic/CompositionRoot.cs` (scene-placed in GameScene; resolves the service graph per `NetworkManager`).
- **Network entry:** `Assets/Scripts/Network/Services/UnityServicesInit.cs` (Unity Services boot) + `NetworkTransportDetector.cs` (transport selection).
- **Tests:** Unity Test Runner (EditMode + PlayMode) via `mcp__UnityMCP__run_tests`.
