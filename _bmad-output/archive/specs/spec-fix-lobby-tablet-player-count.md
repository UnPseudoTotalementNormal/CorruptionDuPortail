---
title: 'Fix tablet lobby player count (wrong source + no live update)'
type: 'bugfix'
created: '2026-07-16'
status: 'done'
context: []
baseline_commit: '420aaa6f'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The tablet role-attribution app "Joueurs" tile shows the NGO connected-clients count (`1` for host-only), not the real lobby census. Simulated bots (`clientId >= 100`) are added to `LobbyPlayerInfoHolder.playerInfos` but never to NGO `ConnectedClientsIds`, so the tile reads `1` while the left panel reads `4`, and it never refreshes when a bot is spawned. The undercount also poisons the start-gate ("Pool trop petit") and preset filtering.

**Approach:** Make `GameLobbyRolesDataSource.GetPlayerCount()` read the replicated `LobbyPlayerInfoHolder.playerInfos.Count` (same census the left panel uses, correct on host and clients), and subscribe to `playerInfos.OnListChanged` so the tablet rebuilds live on player/bot add/remove — mirroring `ConnectedPlayerPanel`.

## Boundaries & Constraints

**Always:** Resolve the holder through `CompositionRoot.For(_nm).LobbyPlayerInfoHolder` (the route this class already uses); gate the subscription behind the existing async readiness wait so `playerInfos` exists first; unsubscribe every added handler in `OnDestroy`; null-tolerant (return 0 if holder/list unresolved).

**Ask First:** none expected.

**Never:** No game-state mutation (presentation-only data source); do not touch `LobbyPlayerInfoHolder`, the controller, or `ConnectedPlayerPanel`; do not change how bots are spawned or how the start-gate math works — only correct the count its inputs read.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Host + 3 simulated bots | `playerInfos.Count == 4` | `GetPlayerCount()` returns 4 | N/A |
| Bot spawned while tablet open | `AddDebugPlayer` → `playerInfos.OnListChanged` fires | `OnChanged` raised → controller rebuilds → tile + gate + presets recompute against new count | N/A |
| Holder not yet resolved | `_nm`/holder/`playerInfos` null | returns 0 | null-guard, no throw |
| Non-host client tablet | replicated `playerInfos` synced | returns replicated `.Count` (no `IsServer` branch) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs` -- the only file changed: `GetPlayerCount()` source, readiness wait predicate, subscribe/unsubscribe to `playerInfos.OnListChanged`
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs` -- reference only: `public NetworkList<PlayerInfo> playerInfos` census (bots enter via `AddDebugPlayer`)
- `Assets/Scripts/Network/ConnectedPlayerPanel.cs` -- reference pattern: counts `playerInfos.Count`, subscribes to `OnListChanged`
- `Assets/Scripts/GameLogic/CompositionRoot.cs` -- exposes `.LobbyPlayerInfoHolder` accessor used to resolve the holder
- `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` -- reference only: `OnChanged -> Rebuild -> GetPlayerCount()` consumer (no change)

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs` -- DONE. `WaitUntil` now gates on `LobbyPlayerInfoHolder` + non-null `playerInfos`; caches `_lobby`; `GetPlayerCount()` returns `_lobby?.playerInfos?.Count ?? 0` (IsServer branch dropped); subscribes `playerInfos.OnListChanged += OnPlayerListChanged` and unsubscribes in `OnDestroy`. NGO connect/disconnect subs + `OnClientChanged` removed as now-redundant. Compiles clean (validate_script 0/0).
- [x] Test -- NOT added (documented as manual check). No isolated harness exists for `GameLobbyRolesDataSource`: exercising `GetPlayerCount()` needs the full CompositionRoot lobby graph (GameSettingsManager + GameManager + RoleAttributionState + spawned holder) behind a private async resolve. A 2-NetworkManager integration test is disproportionate for a 4-line source fix and prone to the known PlayMode port-7777 bind flake. Verify via the manual playtest below.

**Acceptance Criteria:**
- Given a host lobby with the tablet open and 3 simulated players spawned, when the tablet rebuilds, then the "Joueurs" tile shows 4 (matching the left panel), and the "Pool trop petit" gate is computed against 4.
- Given the tablet is open, when a simulated player is added or removed, then the tile and start-gate update without any other interaction.
- Given the holder is not yet network-spawned, when `GetPlayerCount()` is called, then it returns 0 without throwing.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` (after edit) -- expected: no compile errors
- `mcp__UnityMCP__run_tests` (PlayMode, LobbyPlayerInfoHolder/LobbyRoles filter) -- expected: green

**Manual checks:**
- Playtest is Poyo's job (per project rule): host, open tablet, F1 ×3 → tile reads 4 and gate clears the false "Pool trop petit"; verify count follows further F1 presses live.

## Suggested Review Order

**Count source (the fix)**

- Entry point — the wrong source (NGO connected-clients) replaced by the replicated census.
  [`GameLobbyRolesDataSource.cs:79`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L79)

- Review patch — Unity-safe null check, not `?.`, so a destroyed holder can't throw during teardown.
  [`GameLobbyRolesDataSource.cs:85`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L85)

**Live refresh + lifecycle**

- Readiness gate now waits for the holder + its `playerInfos` before resolving.
  [`GameLobbyRolesDataSource.cs:48`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L48)

- Subscribe to `playerInfos.OnListChanged` — raises `OnChanged` on bot/player add/remove.
  [`GameLobbyRolesDataSource.cs:64`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L64)

- Symmetric unsubscribe (NGO connect/disconnect subs removed as now-redundant).
  [`GameLobbyRolesDataSource.cs:72`](../../Assets/Scripts/UI/LobbyRoles/GameLobbyRolesDataSource.cs#L72)
