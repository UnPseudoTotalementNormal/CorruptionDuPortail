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

Concrete classes:

- `LobbyState`
- `GameIntroductionState`
- `RoleAttributionState`
- `AwakeningState` / `AwakeningRecapState`
- `ChainingState`
- `TakeDownThePortalState`
- `VoteState` / `VoteRecapState`
- `VictoryConditionCheckState`
- `GameEndingState`

Settings driving phase behavior live in `GameStateSettings.cs`. State-bound UI hooks via `StateUI.cs`. Routing performed by `GameManager` → `GameState` enum.

## Identity / player state

| Type | Path | Scope |
|---|---|---|
| `LocalPlayerInfo` | `Network/Player/LocalPlayerInfo.cs` | Local-only data (preferences, local id) |
| `PlayerInfo` | `Network/Player/PlayerInfo.cs` | Networked player data |
| `LobbyPlayerInfoHolder` | `Network/LobbyPlayerInfoHolder.cs` | Lobby-phase aggregate |
| `Character` | `Characters/Character.cs` | In-game player avatar (networked) |
| `CharacterManager` | `Characters/CharacterManager.cs` | Server-side roster + role distribution |

## Power / role state

- `Role` + `RoleDataObject` (ScriptableObject) + `RoleID` define static role data.
- `PowerManager` executes powers server-side.
- `PowerUsageManager` tracks per-game-state usage budgets (e.g., "once per night").
- Each power is its own class under `Characters/Powers/` (`PBlessing`, `PAutoCorruption`, `PCorruptingMark`, ...).
- `FactionType` + `WinningConditions/` decide victory.

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

## Lobby + services state

- `Network/Services/LobbyManager.cs` — Unity Services Lobby integration.
- `Network/Services/UnityServicesInit.cs` — auth + services bootstrap.
- `Network/Services/LobbyCreationSettings.cs` — typed lobby parameters.
- `GameCode.cs` — invite / join-code helpers.
- `NetworkTransportDetector.cs` — selects Steam (Facepunch) vs local transport at runtime.

## UI-side state

Per-system, not centralized:
- `Smartphone/` keeps its own UI state for chat / notes / info tabs.
- `FocusSystem/` tracks current focus target.
- `RoleTargetSystem/` tracks targeted roles.
- `TooltipSystem/` is event-driven.

## Persistence

- ScriptableObjects (`Assets/ScriptableObjects/`) hold designer-tunable static data (roles, settings).
- `GameValues.cs` keeps tunable runtime constants.
- `DontDestroyOnLoadComponent.cs` persists specific GameObjects across scene loads.
- `GameAssetHolder.cs` exposes shared asset references project-wide.

No external save backend at scan time; runtime state is in-memory only.
