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
Assets/Scripts/                         ★ All gameplay C# (245 .cs files)
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
│   ├── Role.cs / RoleID.cs / RoleDataObject.cs   Role definitions (SO-backed)
│   ├── FactionType.cs / CharacterType.cs
│   ├── Powers/                         ★ One P*.cs per power (PBlessing, PAutoCorruption, ...)
│   │   └── Interfaces/                 Power contracts
│   └── WinningConditions/              Victory predicates per faction
├── ChatSystem/                         In-game chat
├── CustomAttributes/                   C# attribute helpers
├── Editor/                             Custom inspectors + tooling (Game.Editor.asmdef)
├── Extensions/                         Pure utilities (★ unit-tested in Tests/Editor)
├── FX/                                 Visual effects
├── Facepunch/                          Steam transport bundle
├── FixedStrings/                       Constant string tables
├── FocusSystem/                        UI focus management
├── GameLogic/                          ★ Core game loop
│   ├── GameManager.cs                  Top-level controller
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
├── Tests/                              ★ Unit + PlayMode tests
│   ├── Editor/                         Pure C# tests, NSubstitute (16 files)
│   │   ├── ActionStackTests, *ExtensionsTests, ValidatorTests,
│   │   │   NoteManagerTests, LocalPlayerInfoTests,
│   │   │   NetworkSerializableObjectTests, ReflectionHelperTests,
│   │   │   TooltipLinkParserTests, ...
│   │   └── Tests.Editor.asmdef
│   └── PlayMode/                       Networked tests via NetworkTestHelper
│       ├── BoardTests, ChainingManagerTests, ChatManagerTests,
│       │   CorruptionTests, EntrapmentPowerTests, GameManagerTests,
│       │   LobbyManagerTests, PowerTests, ...
│       ├── NetworkTestHelper.cs        ★ Multi-client simulation harness
│       └── Tests.PlayMode.asmdef
├── TooltipSystem/                      Hover tooltips
├── UI/                                 Shared UI widgets
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
- **Network entry:** `Assets/Scripts/Network/Services/UnityServicesInit.cs` (Unity Services boot) + `NetworkTransportDetector.cs` (transport selection).
- **Tests:** Unity Test Runner (EditMode + PlayMode) via `mcp__UnityMCP__run_tests`.
