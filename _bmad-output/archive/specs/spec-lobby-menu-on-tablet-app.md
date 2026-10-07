---
title: 'GameSettings panel onto the tablet as a SmartphoneApp (modular lobby UI)'
type: 'feature'
created: '2026-06-20'
status: 'done'
baseline_commit: '204dbf35eb2ae653daca632b6ff1e5bef86de2d9'
context:
  - '{project-root}/_bmad-output/project-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/epics-player-embodiment.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** During the Lobby (first-person free-roam, Epic 13) the whole lobby menu renders as a Screen Space - Overlay canvas (`LobbyStateUI.prefab` under `StatesCanvas`). Poyo wants **only the `GameSettings` panel** (the tabbed settings panel — role attribution + the other-options tab) to live on the in-world tablet, where the host configures while free-roaming; the **connected-players list, Start Game, and game code stay a HUD** overlay. Crucially, this split must be **hyper-modular** — easy to move any piece between HUD and tablet later without rework.

**Approach:** Make every lobby UI piece a **host-agnostic view module** that resolves its own data (no `StateUI` push). Post-GameSettings-refonte the `GameSettings` views and the connected-players panel already self-resolve (`CompositionRoot` / `LobbyPlayerInfoHolder`); the one coupled piece, the Start button, is decoupled into a small `LobbyStartButton` that resolves `LobbyState` via `CompositionRoot`. **Extract the `GameSettings` panel into its own prefab** and host an instance in a new **Lobby `SmartphoneApp`** on the tablet's world-space `ScreenCanvas` (auto-raised on Lobby entry, closable). The lobby HUD overlay is **kept but trimmed** to connected-players + Start + game code. Because each piece self-resolves and visibility is the host's job, relocating a piece later is a **reparent**, not a code change.

## Boundaries & Constraints

**Always:**
- **Modularity is the headline requirement.** Each lobby piece (`GameSettings` panel, connected-players, Start, game-code) is a self-contained view that resolves its own deps and carries no dependence on its host. Moving a piece between the HUD overlay and a tablet `SmartphoneApp` must be a **reparent only** — no code edit. Visibility/show-hide is always the HOST's responsibility, never baked into the piece.
- The Lobby app is a SCENE object under `SmartphoneApps` (discovered by `SmartphoneController.Awake`), reusing the existing world-space `ScreenCanvas` + `PhoneCamera` + carousel + `TabletToggleInput`. No second surface, no new camera.
- The lobby HUD overlay is **kept** (still shown during the Lobby by the existing `StateUI` lifecycle), only **trimmed** — the `GameSettings` panel leaves it.
- Server authority preserved: Start stays host-gated (`IsServer`); role edits stay host-authoritative through `GameSettingsManager`; the tablet is client-local.
- Auto-open is a REACTION to `currentGameStateIndex.OnValueChanged` (lane-A `GameManager`→`IGameStateQuery`, subscribe in `Start`, unsubscribe in `OnDestroy`, mirrors `AvatarCameraArbiter`); never writes the index; only RAISES the tablet onto the Lobby app on Lobby entry (closable via the existing `TabletToggleInput`/carousel).

**Ask First:**
- Moving any of Start / connected-players / game-code onto the tablet too (default = they stay HUD; the modularity makes this a later reparent).
- Whether the Lobby app stays `isActive` only during the Lobby (default = yes; carousel skips it otherwise).

**Never:**
- Do not change the role-attribution mechanic, Start-Game validation, connected-players, or game-code logic — only the `GameSettings` host surface moves.
- Do not bake host/visibility knowledge into a piece (that breaks the modularity contract).
- Do not pixel-perfect the tablet layout unilaterally — fitting the panel onto the tablet screen is **design-owned (Poyo)**; deliver functional placement + flag it.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Enter Lobby | `currentGameStateIndex` → LobbyState | HUD overlay shows (players/Start/code); tablet raises + `GoToApp(lobbyApp)` showing the `GameSettings` panel | N/A |
| Leave Lobby | state → next | HUD hides (StateUI); Lobby app `isActive=false`; presentation returns to prior app | N/A |
| Lower tablet in Lobby | `ToggleTablet` press | Tablet closes; HUD unaffected; re-press re-opens | N/A |
| Host edits a role count | host drags a slider (tablet) | replicates via `GameSettingsManager` (unchanged) | clamp |
| Press Start (host / non-host) | tap Start (HUD) | host → `LobbyState.OnStartGameButtonPressed`; non-host → no-op | warn+abort if players > roles |
| Reparent a piece (future) | move a piece HUD⇄tablet | piece keeps working (self-resolves); only the new host's visibility applies | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scenes/GameScene.unity` — tablet: `SmartphoneController` (119918), `appParent`=`SmartphoneApps` (120400), `defaultApp`=`InfoTable`, apps `InfoTable`/`ChatUI`. New Lobby app goes here.
- `Assets/Prefabs/StateUI/LobbyStateUI.prefab` — the lobby HUD; under its `LobbyUI` child: `ConnectedPlayers`, `StartGame`, `GameSettings` (→ extract this one), `GameCode`. Root carries `LobbyUI : StateUI` (HUD show/hide lifecycle — kept).
- `Assets/Scripts/UI/StateUI/LobbyUI.cs` — `StartGame()` (host-gated via `owningGameState`) → logic moves to `LobbyStartButton`; `LobbyUI` stays the HUD `StateUI` host.
- `Assets/Scripts/UI/GameSettings/*` — `GameSettings` panel views (`GameSettingsUI`, `RoleAttributionSettingTab`, …), already host-agnostic (`CompositionRoot`, post-refonte). The `GameSettings` GameObject becomes the extracted prefab.
- `Assets/Scripts/Network/ConnectedPlayerPanel.cs` / `GameCodeText` — already host-agnostic (static holder / self-read); stay HUD, untouched.
- `Assets/Scripts/Smartphone/SmartphoneApp.cs` / `SmartphoneController.cs` — app container + carousel/open API to reuse.
- `Assets/Scripts/GameLogic/GameStates/LobbyState.cs` — `OnStartGameButtonPressed` (unchanged); `stateUIPrefab` stays (HUD still instantiated).
- `Assets/Scripts/Avatars/AvatarCameraArbiter.cs` — lane-A state-reaction precedent for the presenter.
- NEW `Assets/Scripts/Smartphone/Apps/Lobby/LobbyStartButton.cs` — host-agnostic Start (resolves `LobbyState` via `CompositionRoot`, host-gated).
- NEW `Assets/Scripts/Smartphone/Apps/Lobby/LobbyAppPresenter.cs` — lane-A driver: Lobby → activate+raise+`GoToApp`; else deactivate.
- NEW `Assets/Prefabs/GameSettings/GameSettingsPanel.prefab` — the extracted host-agnostic `GameSettings` panel.

## Tasks & Acceptance

**Execution:**
- [x] `LobbyStartButton.cs` — created: `StartGame()` host-gated, resolves `LobbyState` via `CompositionRoot.For(Singleton)` (`FirstOrDefault`-guarded). `[RequireComponent(Button)]` + self-wires its `onClick` in `Awake` (no Inspector UnityEvent, per project rule).
- [x] `LobbyUI.cs` — `StartGame()` removed; `LobbyUI` stays the trimmed HUD `StateUI` host. `LobbyStartButton` added to the `StartGame` GO in the prefab (self-wires). *Leftover: the button's old persistent `onClick → LobbyUI.StartGame` is now inert (method gone) — see deferred-work for the 1-click cleanup.*
- [x] `GameSettingsPanel.prefab` — extracted from `LobbyStateUI.prefab`'s `GameSettings` GameObject (`create_from_gameobject` + `unlink_if_instance`), then removed from `LobbyStateUI.prefab` (HUD keeps players/Start/code). Widgets still resolve via `CompositionRoot`.
- [x] `LobbyAppPresenter.cs` — created: lane-A `GameManager`(→`IGameStateQuery`) + `SmartphoneController` + lobby `SmartphoneApp` + `fallbackApp` refs; subscribe/prime `currentGameStateIndex.OnValueChanged`, unsubscribe in `OnDestroy`; Lobby TYPE → activate + `TryOpenPanel` + `GoToApp(lobbyApp)`; on exit re-home to `fallbackApp` if showing the lobby app (review F2). Resolves state via `GetGameState`.
- [x] `RoleAttributionSettingTab.cs` — build deferred via `UniTask.WaitUntil(graph ready)` + cancellation guard, so the scene-resident tablet tab doesn't race `GameManager.OnNetworkSpawn` (keeps the modular self-resolve contract).
- [x] GameScene (MCP) — `Lobby` app under `SmartphoneApps` (stretched, `CanvasGroup`, `SmartphoneApp` auto-wired, `LobbyAppPresenter` 4 refs wired + read-back); `GameSettingsPanel.prefab` instanced inside; carousel wired bidirectionally InfoTable↔ChatUI↔Lobby (review F1, via the `{instanceID}` set_property form). SceneWiringGuard green.
- [x] Tests — EditMode 228/228, PlayMode 165/166 (1 = pre-existing `AvatarSpawnTests` flake, passes isolated), 3 guards green. Manual lobby smoke = Poyo (UI, no new domain logic — automated UI test skipped per project rule).

**Acceptance Criteria:**
- Given the Lobby is entered, then the tablet raises onto a Lobby app showing the `GameSettings` panel, AND the HUD overlay shows connected players + Start + game code; the player can lower/swipe the tablet and bring it back.
- Given the host taps Start (HUD) with enough roles, then the loop advances; a non-host tap does nothing.
- Given any single lobby piece is reparented between the HUD canvas and the tablet `SmartphoneApp` (manually, no code change), then it still resolves its data and works — only the new host's visibility governs it.
- Given the full suite runs, then EditMode + PlayMode + the three DI guards stay green.

## Design Notes

- **The modularity contract (load-bearing for this story):** a lobby piece = a self-contained view that resolves its own deps (`ConnectedPlayerPanel`→`LobbyPlayerInfoHolder.instance`; `GameSettings` views→`CompositionRoot.For(Singleton)`; `LobbyStartButton`→`CompositionRoot`; game-code→self). It NEVER reads its host or drives its own show/hide. The host supplies visibility: the HUD overlay via the `LobbyUI` `StateUI` CanvasGroup lifecycle; the tablet via `SmartphoneApp` activation + `LobbyAppPresenter`. Result: moving a piece HUD⇄tablet is a reparent. Decoupling Start (the lone `owningGameState`-coupled piece) is what unlocks full modularity now even though Start stays HUD.
- **App = scene object, not runtime-instantiated:** `SmartphoneController.Awake` enumerates `appParent`'s `SmartphoneApp` children once; the Lobby app must pre-exist under `SmartphoneApps` (like `InfoTable`/`ChatUI`), hosting an instance of the extracted `GameSettingsPanel.prefab`.
- **Visual fit is Poyo's:** the panel was authored for a full-screen overlay; on the 1443×913 tablet `ScreenCanvas` it needs layout/scale tuning — delivered functional, polish is design-owned.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` after each change — expected: zero compile errors.
- `mcp__UnityMCP__run_tests` EditMode + PlayMode + the three guards — expected: green, no regression.

**Manual checks:**
- Host play: enter Lobby → tablet auto-raises onto the `GameSettings` panel; HUD shows players/Start/code; lower+re-raise tablet; swipe Lobby/Chat/InfoTable; edit a slider (replicates); press Start → advances. Non-host: Start no-op, sliders read-only. Reparent a piece HUD⇄tablet by hand → still works.

## Suggested Review Order

**State-driven presentation (start here)**

- The auto-open / re-home driver (mirrors `AvatarCameraArbiter`); review F1/F2 fixes live in `OnGameStateChanged`.
  [`LobbyAppPresenter.cs:59`](../../Assets/Scripts/Smartphone/Apps/Lobby/LobbyAppPresenter.cs#L59)

**Host-agnostic pieces (the modularity contract)**

- Start button: resolves `LobbyState` via `CompositionRoot`, self-wires its `Button` (no UnityEvent).
  [`LobbyStartButton.cs:33`](../../Assets/Scripts/Smartphone/Apps/Lobby/LobbyStartButton.cs#L33)
- The tablet tab defers its build until the network graph resolves (scene-object timing fix).
  [`RoleAttributionSettingTab.cs:41`](../../Assets/Scripts/UI/GameSettings/RoleAttributionSettingTab.cs#L41)

**Scene / prefab (inspect, not in the .cs diff)**

- GameScene `Lobby` app under `SmartphoneApps`: `SmartphoneApp` + `LobbyAppPresenter` (4 wired refs) + `GameSettingsPanel.prefab` instance; carousel InfoTable↔ChatUI↔Lobby.
- `LobbyStateUI.prefab` trimmed (no `GameSettings`); `LobbyStartButton` on the `StartGame` button.

## Review Change Log

- **F1 (HIGH, fixed):** the lobby app had no carousel neighbors → the player was trapped on it during the Lobby. Wired bidirectionally (InfoTable↔ChatUI↔Lobby) via the `{"instanceID": N}` `set_property` form (the bare-int form silently nulls the `SerializedDictionary`).
- **F2 (HIGH, fixed):** leaving the Lobby left the tablet stuck on the now-inactive lobby app. Added `fallbackApp` + on exit `GoToApp(fallbackApp)` when it is the shown app.
- **Async hardening:** `RoleAttributionSettingTab` re-checks cancellation after the `WaitUntil` before subscribing (no leaked handler on same-frame destroy).
- **F3 (MED, deferred):** the `StartGame` button's old persistent `onClick → LobbyUI.StartGame` is inert (method removed; `LobbyStartButton` self-wires) — a 1-click inspector cleanup, recorded in deferred-work.
- **Rejected:** presenter reading `currentGameStateIndex` at `Start` (mirrors the shipped `AvatarCameraArbiter`; lobby is index 0 so the primed value is correct); `.First()` on `GetGameStates` (states exist once `GameManager` is spawned, which the `WaitUntil` ensures).
