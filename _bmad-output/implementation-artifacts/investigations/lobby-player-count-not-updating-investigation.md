# Investigation: Tablet lobby "Joueurs" count doesn't match / update with connected players

## Hand-off Brief

1. **What happened.** The tablet role-attribution UITK app shows `Joueurs: 1` while the left connected-players panel shows `Connected Players: 4` (host + 3 simulated bots) — the tablet counter reads the wrong source and never refreshes when bots are added. **Confirmed.**
2. **Where the case stands.** Root cause Confirmed by code trace: `GameLobbyRolesDataSource.GetPlayerCount()` reads NGO `ConnectedClientsIds.Count` (host only = 1), but the authoritative census is `LobbyPlayerInfoHolder.playerInfos` (includes simulated bots = 4).
3. **What's needed next.** One-file fix in `GameLobbyRolesDataSource` — count `LobbyPlayerInfoHolder.instance.playerInfos` and subscribe to its `OnListChanged`. Trivial; route to `gds-quick-dev`.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-07-16                                                                 |
| Status           | Concluded                                                                  |
| System           | Unity 6000.2.6f2, NGO, tablet UITK lobby role-attribution app              |
| Evidence sources | In-editor screenshot; source code (`GameLobbyRolesDataSource`, `ConnectedPlayerPanel`, `CharacterManager`, `LobbyPlayerInfoHolder`, `DevIdentityController`) |

## Problem Statement

User (Poyo): "je crois que le nombre de joueur s'update pas sur l'ui du lobby ?" — with a screenshot showing the left panel at `Connected Players: 4` (hfgh + Simulated 1/2/3) but the tablet "Joueurs" tile at `1`, and the footer error `Pool trop petit : 1 rôle(s) en moins que de joueurs`.

Premise verified: the tablet count is genuinely wrong (shows 1, should be 4) AND stale (doesn't move when bots spawn). Both stem from one root cause.

## Evidence Inventory

| Source                              | Status    | Notes                                                                 |
| ----------------------------------- | --------- | --------------------------------------------------------------------- |
| Screenshot                          | Available | 4 vs 1 discrepancy; footer "1 rôle(s) en moins"                       |
| `GameLobbyRolesDataSource.cs`       | Available | tablet count source + change subscriptions                            |
| `ConnectedPlayerPanel.cs`           | Available | left panel count source (the correct one)                             |
| `CharacterManager.cs`               | Available | simulated-bot spawn + id assignment                                   |
| `LobbyPlayerInfoHolder.cs`          | Available | `AddDebugPlayer` → `playerInfos.Add`                                   |

## Confirmed Findings

### Finding 1: Tablet reads NGO connected-clients, not the player census

**Evidence:** `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs:76-83`

```csharp
public int GetPlayerCount()
{
    if (_nm == null) return 0;
    return _nm.IsServer ? _nm.ConnectedClientsIds.Count : _nm.ConnectedClients.Count;
}
```

**Detail:** As host, this returns `ConnectedClientsIds.Count`. Simulated bots are NOT real NGO transport connections, so they never appear in `ConnectedClientsIds`. With host-only + 3 bots the value is 1. The author already flagged this as a shortcut in the comment ("A client-accurate count would read a replicated player holder — refine at GameScene wiring…").

### Finding 2: The correct count lives in `LobbyPlayerInfoHolder.playerInfos` (includes bots)

**Evidence:** `Assets/Scripts/Network/ConnectedPlayerPanel.cs:36-38`

```csharp
var _playerInfos = LobbyPlayerInfoHolder.instance.playerInfos;
playerCountText.text = $"Connected Players: {_playerInfos.Count.ToString()}";
```

**Detail:** The left panel (showing 4, the value the user expects) counts the `playerInfos` NetworkList, which is the authoritative lobby census.

### Finding 3: Simulated bots land in `playerInfos`, never in NGO connected clients

**Evidence:** `Assets/Scripts/Characters/CharacterManager.cs:402-410`, `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:195-206`

```csharp
// CharacterManager.SpawnSimulatedPlayer()
ulong _debugId = 100 + (ulong)instance.GetCharacters().Count(_c => !_c.isFake && _c.ownerClientId.Value >= 100);
LobbyPlayerInfoHolder.instance.AddDebugPlayer(_debugId, $"Simulated {_debugId - 99}");

// LobbyPlayerInfoHolder.AddDebugPlayer()
playerInfos.Add(new Network.Player.PlayerInfo { playerClientId = _clientId, ... });
```

**Detail:** Bots get id `100+n` (the `clientId >= 100` simulated range per CLAUDE.md) and are added only to `playerInfos`. NGO's `ConnectedClientsIds` is untouched → the two counters diverge by exactly the bot count.

### Finding 4: Tablet never refreshes on bot add/remove

**Evidence:** `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs:56-58`

```csharp
_manager.OnSettingsChanged += Raise;
_nm.OnClientConnectedCallback += OnClientChanged;
_nm.OnClientDisconnectCallback += OnClientChanged;
```

**Detail:** `OnChanged` fires on manager settings changes and real NGO connect/disconnect only. Adding a simulated bot fires neither → the tablet "Joueurs" tile stays frozen (the "s'update pas" the user observed). `ConnectedPlayerPanel` avoids this by subscribing to `playerInfos.OnListChanged` (`ConnectedPlayerPanel.cs:20`).

## Source Code Trace

| Element       | Detail                                                                              |
| ------------- | ----------------------------------------------------------------------------------- |
| Error origin  | `GameLobbyRolesDataSource.GetPlayerCount()` — `GameLobbyRolesDataSource.cs:76-83`   |
| Trigger       | Spawn simulated players (F1 → `DevIdentityController` → `CharacterManager.SpawnSimulatedPlayer`) or any real join |
| Condition     | Bots increment `playerInfos` but not `ConnectedClientsIds`; count reads the latter, and no `playerInfos.OnListChanged` subscription → wrong value + stale |
| Related files | `ConnectedPlayerPanel.cs`, `LobbyPlayerInfoHolder.cs`, `CharacterManager.cs:402`, `LobbyRolesUitkController.cs` |

## Conclusion

**Confidence:** High — Confirmed root cause, deterministic.

The tablet "Joueurs" tile is both **wrong** (reads NGO `ConnectedClientsIds`, which excludes simulated bots) and **stale** (no subscription to the census list that bots mutate). The left panel is right because it reads `LobbyPlayerInfoHolder.playerInfos` and listens to its `OnListChanged`. Single source of truth mismatch.

Downstream symptom: the footer gate `Σforced ≤ players ≤ Σmax` uses this same undercount, so "Pool trop petit / 1 rôle(s) en moins que de joueurs" and preset filtering (`ForPlayerCount(GetPlayerCount())`) are all computed against 1 instead of 4 — the fix corrects all of them at once.

## Recommended Next Steps

### Fix direction

In `GameLobbyRolesDataSource` (one file):

1. **Count source** — replace `GetPlayerCount()` body with `LobbyPlayerInfoHolder.instance.playerInfos.Count` (resolve the holder via `CompositionRoot.For(_nm).LobbyPlayerInfoHolder`, guard null → 0). This matches `ConnectedPlayerPanel` and includes bots + is correct on non-host tablets (replicated NetworkList).
2. **Refresh** — subscribe to `playerInfos.OnListChanged` → `Raise()` in `ResolveWhenReadyAsync` (and unsubscribe in `OnDestroy`), so the tile, the start-gate, and preset lists update live on bot/player change. The `OnClientConnected/Disconnect` NGO subs become redundant for the count but can stay harmless.

Note: `LobbyPlayerInfoHolder` isn't spawned until network is up — keep it inside the existing `WaitUntil` readiness gate (add it to the wait predicate).

### Diagnostic

None needed — root cause Confirmed. Optional: add an EditMode/PlayMode test asserting `GetPlayerCount()` tracks `playerInfos.Count` after `AddDebugPlayer`.

## Reproduction Plan

1. Host the lobby, open the tablet role-attribution app.
2. Press F1 three times (spawn 3 simulated players).
3. Observe: left panel `Connected Players: 4`, tablet `Joueurs: 1` and unchanged across the spawns. Footer shows spurious "Pool trop petit".

## Side Findings

- The undercount also feeds `GetActivePresetIndex()` / `GetPresets()` via `ForPlayerCount(GetPlayerCount())` (`GameLobbyRolesDataSource.cs:136,162,172`) — presets are filtered for the wrong player count until fixed. **Confirmed.**
- On a genuine non-host client the old code path (`_nm.ConnectedClients.Count`) is also unreliable for lobby census; reading the replicated `playerInfos` fixes host and client uniformly. **Deduced.**
