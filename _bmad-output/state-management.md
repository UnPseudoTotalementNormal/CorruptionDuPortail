# State Management

> Game-project state management = **networked game state**, not a UI store. This document covers the authoritative state model and replication patterns.

## Authority model

**Server-authoritative.** All mutations to game state happen on the host. Clients submit proposals via `ServerRpc`s, the server validates, applies, and broadcasts via `ClientRpc` or `NetworkVariable` updates.

## State machine (game loop)

The active phase is held by `GameManager` and dispatched through state objects under `Assets/Scripts/GameLogic/GameStates/`. Each state encapsulates its own logic and transition rules.

```
Lobby
  → Introduction
    → RoleAttribution
      → Awakening
        → AwakeningRecap
          → Chaining
            → TakeDownThePortal (conditional)
              → Vote
                → VoteRecap
                  → VictoryConditionCheck
                    → (loop to Chaining) or → GameEnding
```

Concrete classes: `LobbyState`, `GameIntroductionState`, `RoleAttributionState`, `AwakeningState` / `AwakeningRecapState`, `ChainingState`, `TakeDownThePortalState`, `VoteState` / `VoteRecapState`, `VictoryConditionCheckState`, `GameEndingState`.

Settings driving phase behavior live in `GameStateSettings.cs`. State-bound UI hooks via `StateUI.cs`. Routing performed by `GameManager` → `GameState` enum.

### Index ownership (refactor boundary)

The transition **arithmetic** (advance/rewind, new-day/game-started flags) lives in the `GameLoopMachine` Domain POCO (`Domain/GameLoopMachine.cs`, EditMode-tested). `GameManager` (the adapter) **owns the `currentGameStateIndex` `NetworkVariable<int>`** — it writes it server-side in `SwitchGameState` and performs the load-bearing `OnEnd → NV write → OnStart` sequencing pinned by a sequence golden master. This split is a **designed, permanent network-adapter boundary**: replication state must live on a `NetworkBehaviour`, so the POCO computes the decision and the adapter applies it. It is *not* a strangler façade awaiting removal.

## Identity / player state

| Type | Path | Scope |
|---|---|---|
| `LocalPlayerInfo` | `Network/Player/LocalPlayerInfo.cs` | Local-only data (preferences, local id) |
| `PlayerInfo` | `Network/Player/PlayerInfo.cs` | Networked player data |
| `LobbyPlayerInfoHolder` | `Network/LobbyPlayerInfoHolder.cs` | Lobby-phase aggregate (NGO-spawned) |
| `Character` | `Characters/Character.cs` | In-game player avatar (networked, NGO-spawned) |
| `CharacterManager` | `Characters/CharacterManager.cs` | Server-side roster + role distribution |

`CharacterManager` is a **thin network adapter** split into two narrow interfaces consumers depend on à la carte:

- **`ICharacterQuery`** (`Characters/ICharacterQuery.cs`) — reads: `GetCharacter(s)`, `GetLocalCharacter`, `GetLocalClientId`, `onCharactersListUpdated`, `onLocalIdentityChanged`.
- **`ICharacterCommand`** (`Characters/ICharacterCommand.cs`) — spawn/mutation commands.

The bot-gateway code (`GetSafeRpcTarget`, `IsLocalOrSimulated`, `clientId >= 100`) stays **concrete** on `CharacterManager` (NFR5 — relocated verbatim, never edited).

## Snapshot model (Domain)

Read-side decision logic operates on **immutable value snapshots**, not live `NetworkBehaviour`s:

- `GameLogic/Snapshot/GameSnapshotBuilder.cs` builds a `GameSnapshot` (+ `CharacterSnapshot[]`) from live state.
- Domain evaluators (`VictoryEvaluator`, `VoteTally`, `ChainingResolver`, …) take the snapshot and return a decision; the adapter applies it.
- This is what makes win-conditions, vote tallies, and chaining resolution **unit-testable in EditMode** without booting the network.

## Power / role state

- `Role` + `RoleDataObject` (ScriptableObject) + `RoleID` define static role data.
- `RoleDistributor` (Domain POCO) assigns roles **deterministically** via the injected `IRandomProvider` (`UnityRandomProvider` in production, `SeededRandomProvider` under test).
- `PowerManager` executes powers server-side; `PowerResolver` / `PowerUsability` / `EffectDescriptor` (Domain) hold the resolution + per-state usability decisions.
- `PowerUsageManager` tracks per-game-state usage budgets (e.g., "once per night").
- Each power is its own class under `Characters/Powers/` (`PBlessing`, `PAutoCorruption`, `PCorruptingMark`, …); powers are NGO-spawned and resolve their injected deps in `OnNetworkSpawn` (lane C). Effect application routes through `PowerEffectDispatcher`.
- `FactionType` + `WinningConditions/` (with `VictoryEvaluator` + `IWinningCondition`/`IWinningConditionEvaluator`, Domain) decide victory.

## Replication primitives

| Primitive | File | Use |
|---|---|---|
| `NetworkDictionary` | `Network/NetworkDictionary.cs` | Generic networked dictionary |
| `NetworkSerializableObject` | `Network/NetworkSerializableObject.cs` | Base class for serializable payloads |
| `NetworkBehaviourReferenceWrapper` | `Network/NetworkBehaviourReferenceWrapper.cs` | Stable cross-client reference to a NetworkBehaviour |

## The gateway / simulated-client layer

The host can run additional player identities locally for solo debug of full lobbies. Simulated identities carry `clientId >= 100`.

- **`GetSafeRpcTarget(clientId)`** — wrap every RPC target. For `clientId >= 100`, the host intercepts the RPC instead of sending over the wire.
- **`IsLocalOrSimulated(clientId)`** — replaces `IsLocalClient` everywhere the host may act on behalf of a simulated id.

Direct `ServerRpc/ClientRpc` calls **without** `GetSafeRpcTarget` silently break the bot-debug flow.

## Dependency resolution (CompositionRoot)

State is no longer reached via global singletons. The **one surviving project static**, `CompositionRoot` (scene-placed in GameScene), exposes typed accessors over a per-`NetworkManager` registry:

- `CompositionRoot.For(nm)` → `Services` resolver: `.GameLoop`, `.GameStateQuery`, `.CharacterQuery`, `.CharacterCommand`, `.GameInfoRevealer`, `.BoardManager`, `.ChatManager`, … (full list in `GameLogic/CompositionRoot.cs`).
- NGO-spawned consumers (lane C) resolve **once** in `OnNetworkSpawn`; scene/prefab consumers (lane A) hold injected `[SerializeField]` fields; code-created consumers (lane B) are pushed deps by their creator.
- Production has one NM, so `For(Singleton)` returns the scene-wired managers — behaviour-identical to the old locator. See [architecture.md](./architecture.md#dependency-injection--compositionroot--three-lanes).

## Lobby + services state

- `Network/Services/LobbyManager.cs` — Unity Services Lobby integration (global-service façade, menu-phase).
- `Network/Services/UnityServicesInit.cs` — auth + services bootstrap.
- `Network/Services/LobbyCreationSettings.cs` — typed lobby parameters.
- `GameCode.cs` — invite / join-code helpers.
- `NetworkTransportDetector.cs` — selects Steam (Facepunch) vs local transport at runtime.

## UI-side state

Per-system, not centralized:
- `Smartphone/` keeps its own UI state for chat / notes / info tabs.
- `FocusSystem/` tracks current focus target.
- `RoleTargetSystem/` tracks targeted roles (`ChatChannelPolicy` in Domain holds the chat-visibility decision logic).
- `TooltipSystem/` is event-driven (`TooltipLinkFormatter` in Domain formats rich-text links).

`OnValueChanged` subscriptions are the decoupled observer layer; the refactor injected the *source* of each subscription but left the subscription code in place. Every subscription has a matching unsubscribe in the component's teardown mirror (`OnDestroy` / `OnNetworkDespawn`).

## Persistence

- ScriptableObjects (`Assets/ScriptableObjects/`) hold designer-tunable static data (roles, settings).
- `GameValues.cs` keeps tunable runtime constants.
- `DontDestroyOnLoadComponent.cs` persists specific GameObjects across scene loads.
- `GameAssetHolder.cs` exposes shared asset references project-wide.

No external save backend at scan time; runtime state is in-memory only.
