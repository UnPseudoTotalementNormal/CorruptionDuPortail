---
title: 'Steam build: Steam lobbies, Steam Datagram Relay, Steam profile'
status: 'implemented'
date: '2026-10-11'
---

# Steam build

## Goal (owner request, 2026-10-11)

- Players of the Steam build connect through Steam's relay servers.
- CI sends two builds to Discord: a **Relay** build (Unity Relay, several copies on one PC for tests) and a **Steam** build.
- App id = Spacewar (480) until the Steam page exists.
- Steam build: server browser kept (as today), the Steam profile replaces the login menu.

## Decisions

- **One codebase, two flavors** (`Domain/OnlineFlavor`, `Network/GameFlavor`). The Steam build is the Relay build plus
  the `CDP_STEAM` define, added by `CdpBuild` to that build only (`BuildPlayerOptions.extraScriptingDefines`).
  ProjectSettings carry no flavor: the Editor, tests and autoplay builds stay Relay. Editor play of the Steam flavor:
  menu `Tools/Corruption du Portail/Build Steam en Play mode` (EditorPrefs, needs Steam running).
- **Facepunch.Steamworks kept** (already vendored, covers SDR sockets, lobbies, avatars, overlay). Steamworks.NET would
  mean rewriting the transport for no gain.
- **Steam build = no UGS at all.** Steam lobbies (`SteamMatchmaking`) are the server browser, the Steam Datagram Relay
  (`FacepunchTransport`: `ConnectRelay` / `CreateRelaySocket`) carries the game, the Steam persona is the profile.
- **Lobby backend abstraction**: `LobbyManager` keeps the session bookkeeping (current lobby, heartbeat, polling, error
  channel) over `ILobbyBackend` = `UgsLobbyBackend` (Relay) or `SteamLobbyBackend` (Steam). Menus see `GameLobby`.
- **Steam lobby specifics**: Spacewar lobbies are shared by every game on 480, so every query filters on
  `cdp_game=CorruptionDuPortail`. Steam has no join code: the host publishes a 6-char `LobbyCode` (no look-alikes).
  Steam has no lobby password: the joiner sends it in `ConnectionPayload.LobbyPassword` (format 3, formats 1-2 still
  read) and the host's `ConnectionApprovalGate` checks it (`LobbyPasswordGate`, "Mot de passe incorrect."). A password
  lobby joined by code is refused client-side (no field to type it). Game started = lobby not joinable (hidden).
- **Rejoin** only offers a game of this build's stack (`OnlineFlavorRules.CanRejoin`): both builds share PlayerPrefs.
- **Login**: Steam build skips the form, `LocalPlayerInfoHolder.CreateSteamClientData` (name + SteamID64, which now
  reaches every peer through the connection payload for later avatar display). Steam closed = blocking notice with a
  "Réessayer" button. The old UGS-username/password-from-SteamID sign-in (password derivable from a public SteamID) is
  gone.
- **Transport patches** (`[CdP patch]` in `FacepunchTransport`): no longer owns/shuts down the Steam client
  (`SteamRuntime` does), forgets sockets and clients on `Shutdown`, drains 256 messages per poll, lingers on a remote
  disconnect so the rejection reason arrives.
- **CI** (`Build.yml`): `announce` → `build` matrix (relay, steam; `fail-fast: false`; game-ci
  `buildMethod: CdpBuild.BuildFromCommandLine`, `-cdpFlavor`) → `notify` (changelog once, one Discord embed with two
  direct artifact links; one failed flavor = warning embed with the other link). The Steam leg checks
  `steam_appid.txt` + `steam_api64.dll` in the output.

## Friend invites (feat/steam-friend-invites, 2026-10-11)

- While in a joinable Steam lobby, the rich presence `connect` = `+connect_lobby <id>` (`SteamJoinRequest`): friends get
  "Rejoindre la partie" on our Steam profile. Cleared on leave and once the game starts (host on lock, members on
  their next lobby poll).
- `SteamInvites` turns `OnGameLobbyJoinRequested` (accepted invite), `OnGameRichPresenceJoinRequested` and the
  `+connect_lobby` launch arguments into one pending request. Mid-game it is refused with a message, never kept.
- `MainMenu` takes it and runs `LobbySelectionPanel.JoinFromSteamInvite`: lobby data fetched by id
  (`SteamLobbyBackend.GetAsync`, `cdp_game` tag checked: Spacewar is shared), then the browser's own join (game started,
  password prompt).
- Lobby UI: "Inviter des amis" under the game code (clone of StartGame without its behaviour) opens the Steam overlay
  invite dialog; overlay disabled = message pointing to the Steam friends list.
- **Spacewar limit**: when the game is NOT running, Steam launches app 480 (Spacewar), not our exe, so a cold
  "Rejoindre la partie" only works once the game has its own app id. With the game already open it works now.

## Playtest checklist (Poyo; needs two PCs / two Steam accounts)

1. Steam build, Steam running: no login form, own Steam name in the lobby.
2. Steam closed: notice + "Réessayer" works after starting Steam.
3. Host → the other PC sees the game in the list (name, language, players), joins, plays a full game.
4. Join by the 6-char code; code typed lower case with spaces still works.
5. Password lobby: wrong password → "Mot de passe incorrect."; right one → joins; by code → refusal message.
6. Started game disappears from the list; quitting mid-game → "Rejoindre" works within the grace delay.
7. Friend invite: game open on both PCs, "Inviter des amis" (overlay) → the friend accepts → joins the lobby
   (password asked if set). Friend list → "Rejoindre la partie" on a friend in a lobby → joins. Once started: no
   "Rejoindre la partie", and an invite accepted mid-game shows a message.
8. Relay build unchanged (login, list, code, 2 copies on one PC); it never lists Steam games.

## Follow-ups (not done)

- Steam avatar display (design-owned placement; SteamID64 already replicated in `PlayerInfo.playerSteamId`).
- Rich presence text ("En salon 4/15") needs localization tokens in the Steamworks app admin: own app id first.
- Own app id: replace `GameValues.STEAM_APP_ID`, add `SteamClient.RestartAppIfNecessary`, stop shipping
  `steam_appid.txt`, SteamPipe upload from CI.
- Trust: the host could check the payload's SteamID against the Steam-authenticated connection identity
  (`ConnectionInfo.Identity`) before showing avatars.
