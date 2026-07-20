# Investigation: client joining a host lands desynced in the lobby

## Hand-off Brief

`LobbySelectionPanel.JoinLobby` calls the **raw** `UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene")`
*after* the newly added `JoinHandshake.WaitForConnectedOrTimeout()` — but that wait only returns once NGO has already
synchronized the client into GameScene. The client therefore hard-reloads GameScene outside NGO, wiping every
replicated NetworkObject in it while the (DontDestroyOnLoad) NetworkManager stays connected: a movable body with no
embodied camera on the client, and a host census stuck at 1 because the client's `LobbyPlayerInfoHolder` replica is
destroyed before it can answer `AskForPlayerInfoRpc`. Regression introduced by commit `026b0bd7` (today 14:44) which
inserted the wait before an already-existing raw `LoadScene`.

## Case Info

- Date: 2026-07-20
- Branch: `Dev`
- Reporter: Poyo
- Repro context: Multiplayer Play Mode, editor = host, "2 player build" client (`Builds/PlayModeScenarios/…`, built 18:11 — **not** stale)
- Status: **Concluded** — root cause Confirmed, fix not applied

## Problem Statement (user, verbatim, treated as hypothesis)

> un client qui rejoint un host ne fonctionne pas du tout, son corps est créé dans le lobby et il peut le bouger mais
> il n'a pas la caméra d'embodied c'est buggé, + y'a marqué qu'y a qu'un seul connected player (que l'host) […] on est
> dans le lobby donc la game n'a pas commencé, y'avait pas ce problème avant je crois

## Evidence Inventory

| Category | State | Note |
|---|---|---|
| Source code | Available | Confirmed citations below |
| Version control | Available | `026b0bd7` diff is the regression carrier |
| Editor console (host) | Partial | Only 10 entries retained; no errors, nothing incriminating |
| Client player log | **Missing** | `AppData/LocalLow/DefaultCompany/CorruptionDuPortail (Client)/` is empty — MPPM streams client logs into the editor console, which had already rolled over |
| Client build freshness | Available | `Assembly-CSharp.dll` 18:11 vs last commit 18:03 → build is current, mismatch ruled out |

## Confirmed Findings

1. `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:336` loads the scene with the **raw Unity** API:
   `UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene")` — not `NetworkManager.SceneManager.LoadScene`.
2. `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:327` now awaits `JoinHandshake.WaitForConnectedOrTimeout()`
   immediately before it. Added by `026b0bd7`; the raw `LoadScene` line itself is older.
3. `Assets/Scripts/Network/JoinHandshake.cs:25-27` states the invariant explicitly: *"A joining client's
   `IsConnectedClient` flips true only at NGO SynchronizeComplete, i.e. **AFTER GameScene finished loading**"*.
4. `Assets/Scenes/BootScene.unity:614` — `EnableSceneManagement: 1`. NGO drives client scene sync.
5. `Assets/Scripts/UI/MainMenu.cs:434` — the host / join-by-code path uses `NetworkManager.Singleton.SceneManager.LoadScene`
   (NGO), which on a client is a no-op `ServerOnlyAction`. That path is **not** destructive; only the lobby-list path is.
6. `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:90-115` — the host census grows only via the round trip
   server `OnClientConnected` → `AskForPlayerInfoRpc` (to that client) → client `SavePlayerInfoRpc` → `playerInfos.Add`.
   The panel count at `Assets/Scripts/Network/ConnectedPlayerPanel.cs:38` renders that list.

## Deduced Conclusions

- The client reaches `LoadScene` **already synchronized and already in GameScene**. The raw single-mode load destroys
  every NGO-spawned NetworkObject living in that scene, while `NetworkManager` (DontDestroyOnLoad) keeps the socket up.
  The host therefore still counts the client as connected, but the client is a zombie: fresh non-networked scene
  objects, no spawn state, no embodiment wiring → *"body moves, no embodied camera"*.
- The server fires `OnClientConnected` at the very moment sync completes — i.e. the same frame the client starts its
  destructive reload. The client's `LobbyPlayerInfoHolder` replica is destroyed before/around `AskForPlayerInfoRpc`, so
  `SavePlayerInfoRpc` never lands → `playerInfos.Count` stays 1 (host only) → *"Connected Players: 1"*.
- Before `026b0bd7` the raw `LoadScene` fired right after `StartClient()`, i.e. **before** NGO sync, so NGO's own
  synchronization arrived afterwards and set the scene up correctly. The bug is purely an **ordering regression**.

## Hypotheses

| # | Hypothesis | Status | Resolution |
|---|---|---|---|
| 1 | Stale client build vs host code (MPPM) | **Refuted** | client `Assembly-CSharp.dll` timestamped 18:11, after the last commit (18:03) |
| 2 | ConnectionApproval asymmetry (known past trap) | **Refuted** | the client is approved and synchronized — the body spawns; an approval failure would reject the join outright |
| 3 | Census RPC path broken by a recent lobby commit (`e3498f16` / `34da61d4`) | **Refuted** | those touch composition presets / a label only; `LobbyPlayerInfoHolder` untouched since the ready-system merge |
| 4 | Raw `SceneManager.LoadScene` after full NGO sync wipes client state | **Confirmed** | findings 1-6 + the invariant documented in `JoinHandshake.cs:25-27` |

## Source Code Trace

- **Error origin:** `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:336`
- **Trigger:** joining from the **lobby list** panel (not the join-by-code path)
- **Condition:** `EnableSceneManagement = 1` + the handshake wait returning only post-`SynchronizeComplete`
- **Related:** `Assets/Scripts/Network/JoinHandshake.cs:42-110`, `Assets/Scripts/UI/MainMenu.cs:157-171,430-437`,
  `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:90-115`

## Fix direction

The client must **not** load GameScene itself — NGO already did. In `JoinLobby`, after a successful handshake, keep
`GameCode.gameCode = lobby.LobbyCode;` and drop the `SceneManager.LoadScene("GameScene")` call (hide the loading group
instead). Optionally align with `MainMenu` by routing through the same helper so both paths cannot drift again.

Caveat worth checking during the fix: `GameCode.gameCode` is assigned *after* sync, so anything in GameScene reading it
during spawn already ran. Verify whether it must be set before `StartClient`.

## Follow-up: 2026-07-20 (fix applied, second runtime observation)

The first fix attempt still hid the loading `CanvasGroup` after a successful handshake. Runtime evidence from the
client build:

```
Failed to join lobby: Object reference not set to an instance of an object.
  at UnityEngine.CanvasGroup.set_interactable
  at Extensions.CanvasGroupExtensions.DoHideGroup
  at UI.Lobby.LobbySelectionPanel.ReportJoin… LobbySelectionPanel.cs:373
  at UI.Lobby.LobbySelectionPanel.JoinLobby   LobbySelectionPanel.cs:352
  at UI.Lobby.LobbySelectionPanel.OnConnectButtonClickedAsync LobbySelectionPanel.cs:133
```

**This is decisive confirmation of the root cause.** A `CanvasGroup` can only be destroyed by its scene being
unloaded — so by the time the handshake returns, NGO has *already* switched the client to GameScene. The user's
counter-hypothesis ("the LoadScene is needed") is therefore **Refuted** by runtime evidence.

Cascade the NRE caused: NRE on the UI hide → caught by `JoinLobby`'s generic catch → `AbortJoin` →
`NetworkManager.Shutdown()` + `LeaveLobby()` → a healthy connection torn down and the player bounced to the menu →
second NRE inside `AbortJoin`'s own hide.

Applied:
- success path touches **no** UI and no scene API — just `return true`
- `HideLoadingSafe()` null-guard for every hide reached from an async continuation
- `JoinLobby`'s catch refuses to run `AbortJoin` when `IsConnectedClient` — a stray exception can no longer kill an
  established session

## Reproduction Plan

1. Host from the editor, stay in the lobby.
2. Client build: join **via the lobby list** (not by code) → reproduces.
3. Client build: join **by code** from MainMenu → expected to work (control case that isolates the panel path).
4. After the fix: both paths show `Connected Players: 2` and the joiner gets the embodied camera.
