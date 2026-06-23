# Assets/UI Toolkit — flat-screen UI, UITK presentation layer (issue #63)

**Status: presentation authored, NOT yet wired.** These `.uxml` / `.uss` files are the front-loaded
UI Toolkit rebuild of the game's flat (screen-space) screens, decided in #63. They are inert assets
(no `.cs`, nothing in any scene) until a `UIDocument` references them — so they change nothing about
the running game yet. Wiring + compile + play-test happen in a **Unity MCP session** (editor required).

Cards and other world-space gameplay UI are **out of scope** (cards excluded per request; world-space
gated on Spike A).

> **Scope correction (verified 2026-06).** Only **Login / MainMenu / LobbyBrowser** are truly flat
> (pre-game, main-menu scene). **GameSettings** and **InfoTable** turned out to be **diegetic** — they
> render on the in-game 3D phone/tablet (`Smartphone/Apps/Lobby/…`; `RoleAttributionSettingTab` "Phone
> layer"), i.e. world-space. Their `.uxml`/`.uss` here are still reusable, but they must be wired as
> **world-space** panels (PanelSettings render mode = World Space + reticle interaction) and are therefore
> **gated on Spike A**, not the screen-space path described below.

## Layout
```
Theme/CorruptionTheme.uss     Shared design system (tokens + base classes, prefix cdp-). Reused by all screens.
Screens/Login.{uxml,uss}      Pseudo entry + loading + error (mirrors LoginMenu.cs).
Screens/MainMenu.{uxml,uss}   Host / Join / Quit + host form + join-by-code (mirrors UI.MainMenu).
Screens/LobbyBrowser.{uxml,uss}  Lobby table + refresh + connect + password modal (mirrors LobbySelectionPanel).
Screens/GameSettings.{uxml,uss}  Tabbed settings; Roles tab = one slider row per role (mirrors RoleAttributionSettingTab).
Screens/InfoTable.{uxml,uss}  Players x Roles deduction matrix, tri-state cells (mirrors InfoTableSystem / InfoRoleChecker).
```
Each screen UXML pulls the theme via `<Style src="../Theme/CorruptionTheme.uss" />` then its own sheet.
Data-driven screens (Lobby, Settings roles, InfoTable cells) include a **representative static example**
so they render meaningfully in UI Builder; at runtime the controller builds those rows/cells using the
same USS classes.

## Wiring TODO (do these in the Unity MCP session — editor required)
For each screen:
1. Create a **Screen Space Overlay** `PanelSettings` (one shared asset is fine; set sort order vs the
   existing uGUI Canvas so they layer correctly during the transition).
2. Add a `UIDocument` (Source Asset = the screen `.uxml`, Panel Settings = the asset above).
3. Port the existing controller to query elements (`root.Q<…>`) and register callbacks, replacing the
   `[SerializeField]` uGUI refs. Keep ALL existing game/network logic — these screens must stay
   functional (advance the game) exactly as today:
   - **Login** → `LoginMenu`: pseudo `TextField`, login `Button`, error `Label`, loading overlay; keep Steam auto-login + UGS auth.
   - **MainMenu** → `UI.MainMenu`: keep host/join flows (Facepunch/Relay), `LobbyManager`, `SwitchToGameScene`.
   - **LobbyBrowser** → `LobbySelectionPanel`: `ListView` bound to `LobbyManager.GetLobbies()`, 7s auto-refresh, password modal, join.
   - **GameSettings** → `RoleAttributionSettingTab`: one row per `RoleDataObject`; `SliderInt` writes via `GameSettingsManager.RequestSetRoleCount` (host-only interactable), refresh on `OnSettingsChanged`.
   - **InfoTable** → `InfoTableSystem`: build the matrix on `onGameStarted`; tri-state cells map to `CheckerType` (Sure/Maybe/SurelyNot) via classes `.cell--sure/--maybe/--not`, `.cell--conflict`, `.cell--locked`.
4. After each: `read_console` (no errors), then play-test the flow end to end (incl. networked paths).
5. Remove the old uGUI prefab/Canvas only once the UITK version is verified.

## Notes
- DOTween show/hide (`DoShowGroup`/`DoHideGroup`) → USS `transition` on `opacity` + `display`, or the
  UITK scheduler. Re-tuned at wiring.
- `cdp-` tokens are defined on `.cdp-root`; every screen root carries that class — keep it when editing.
