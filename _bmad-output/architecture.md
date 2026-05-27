# Architecture — Corruption du Portail

## Executive summary

Server-authoritative multiplayer Unity game using **Netcode for GameObjects (NGO)** over **Facepunch Steam transport**. The host owns all game-state mutations; clients propose via `ServerRpc`s. Cinematics and powers run asynchronously through **UniTask**.

The signature architectural feature is the **Gateway RPC** layer: the host can locally simulate additional player identities (`clientId >= 100`) to debug full lobbies without spinning up multiple Steam clients.

## Technology stack

See [project-overview.md](./project-overview.md#tech-stack-summary).

## Architecture pattern

**Layered server-authoritative networking** with feature-first module decomposition.

```
GameManager
   └── GameState Router (state machine)
        └── NetworkGatewaySystem  (RPC interception for simulated clients)
             └── CharacterManager (identity, role distribution)
                  └── PowerManager (per-role abilities)
```

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

State transitions are dictated by the server. Each state lives under `Assets/Scripts/GameLogic/GameStates/`.

## Critical patterns (load-bearing — break the game if ignored)

### 1. `GetSafeRpcTarget(clientId)` — always wrap RPC targets

If `clientId >= 100` the target is a simulated bot. The host intercepts the call locally instead of sending it across the wire. Calling `ServerRpc/ClientRpc` directly with a raw client id silently breaks the bot-debug flow.

### 2. `IsLocalOrSimulated(clientId)` — never use plain `IsLocalClient`

The host may legitimately act on behalf of a simulated identity. `IsLocalClient` returns false for those identities and produces wrong branches.

### 3. Strict server authority

All gameplay-state mutation happens server-side. Clients submit `ServerRpc` proposals; the server validates and broadcasts.

## Feature-based module layout

### Core gameplay
- `GameLogic/` — `GameManager`, state router, `ChainingManager`, `PowerManager`, `PowerUsageManager`, `GameInfoRevealer`, vote, victory, state UI.
- `Characters/` — `Character`, `CharacterManager`, `Role`, `RoleDataObject`, `RoleID`, `FactionType`, `Powers/` (one `P<Name>.cs` per power), `WinningConditions/`.
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
- `Misc/`, `CustomAttributes/`, `FixedStrings/`, `PolymorphicPropertyDrawer/`, `Inputs/`.

## Async / animation conventions

- **All async code** uses `UniTask`, `UniTaskVoid` — never `System.Threading.Tasks.Task`.
- **Tweens**: DOTween + UniTask integration (`AwaitForComplete`).
- **Long-running cancellable flows**: `CancellableTaskHandler` (see `Board/CardComponents/`).

## Audio

FMOD only for gameplay. `AudioSystem/GameAudioManager` is the single entry point. Banks live under `Assets/FMODBanks/`.

## Networking transport

- **Production:** Facepunch (Steam) — `Facepunch/com.community.netcode.transport.facepunch.asmdef`.
- **Local debug:** simulated-player gateway (no need for multiple Steam clients).
- **Lobby/auth:** Unity Services (Multiplayer, Authentication).

## Architecture-relevant gotchas

- Repo is partially French — code identifiers are English, but commit messages and some comments are French. Match surrounding style.
- `.csproj` files at root are **auto-generated** by Unity from `.asmdef`. Edit the `.asmdef`, never the csproj.
- `Library/`, `Temp/`, `Logs/`, `TestResults/`, `obj/` — Unity-generated, never edit or commit.
- Test assemblies use `defineConstraints: ["UNITY_INCLUDE_TESTS"]` — compile only with Editor test runner.
