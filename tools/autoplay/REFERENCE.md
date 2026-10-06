# Autoplay — feature reference (Corruption du Portail)

Everything the autoplay framework can do today, in one place. How to *use* it (goal → run → evidence → answer):
the `autoplay` skill (`.claude/skills/autoplay/SKILL.md`). How to *add* to it: `Packages/com.unpseudo.autoplay/EXTENDING.md`.
Headless editor + Unity CLI workflow: `tools/HEADLESS_UNITY.md`.

Keep this file exact: a lever, event or check that is added, renamed or removed is updated here in the same commit.

## Layers

| Layer | Where | Owns |
|---|---|---|
| Package (game-agnostic) | `Packages/com.unpseudo.autoplay/Runtime` | runner, journal, captures + state export, animation recorder, command line, dev-build bootstrap, window guard, virtual mouse / keyboard (`AutoplayVirtualInput`) |
| Package tools | `Packages/com.unpseudo.autoplay/Tools~` | `unityctl.sh`, launchers, `run_scenario.py`, `campaign.py`, `compare_runs.py`, `contact_sheet.py` |
| Game adapter | `Assets/Scripts/Autoplay` | `CdpAutoplayGame` (levers, host/client roles), `AutoplayDriver` (bot brain; real-input partials `AutoplayDriverRealInput`, `AutoplayDriverTour`, `AutoplayDriverLobby`), `AutoplayUiLocator`, `AutoplayMenuTour`, `AutoplayInputMask`, `AutoplaySelectionAutopilot`, `AutoplayComposition` |
| Game tools | `tools/autoplay` | `unityctl.sh` wrapper (FMOD guarded files), analyzers, sweep, campaign, `scenarios/*.json` |
| Prod seams (dev use only) | `SelectionFlowService.Autopilot` (`ISelectionAutopilot`), read-only `CardPickerManager` accessors | |

## Commands (`tools/autoplay/unityctl.sh`)

| Command | Does |
|---|---|
| `compile` | refresh + compile in your headless editor, prints errors (refuses while a PlayMode run is in flight) |
| `editmode [filter]` / `playmode [filter]` | run tests in the headless editor |
| `build` | windowed Development build into `Builds/Autoplay/` (FMOD settings guarded) |
| `play-build <seed> [port]` | one player: host + 7 simulated bots, in the background |
| `play-net <clients> <seed>` | host + N real UDP clients (separate processes), bots fill to 8 |
| `last-run` | summary of the newest run (outcome, trace, counters, deduplicated errors) |

Environment: `AUTOPLAY_ARGS` (every process), `AUTOPLAY_CLIENT_ARGS` (every client), `AUTOPLAY_CLIENT1_ARGS` (client 1
only), `AUTOPLAY_TIMESCALE`, `AUTOPLAY_TIMEOUT`, `AUTOPLAY_SCENARIO`, `AUTOPLAY_VISUAL_PICKER=1`, `AUTOPLAY_PROJECT`,
`AUTOPLAY_GUARDED_FILES`.

## Player arguments (`-autoplay-<key> [value]`)

`-autoplay` turns the mode on. A bare flag reads `true`. Unknown keys still reach the adapter (`config.Option(key)`).

### Package (any game)

| Arg | Meaning |
|---|---|
| `game <name>` | adapter to run (default: the only registered one, `cdp`) |
| `scenario <name>` | label of the run folder (default `build`) |
| `seed N` | seeds bots, composition and `UnityEngine.Random` |
| `timescale X` | game speed |
| `timeout S` | real-seconds budget before the run fails |
| `port N` / `port-strict` | UDP port (next free one is taken unless strict); use 7850–7899 |
| `out <dir>` | runs root (default `persistentDataPath/AutoplayRuns`, the wrapper points it at `AutoplayRuns/`) |
| `no-png` | captures write the state JSON only |
| `png` | screenshots on even with `no-png` (the net launcher gives every client `no-png`) |
| `sound` | do not mute |
| `restore-hwnd <hwnd>` | window to hand focus back to (set by the launcher) |
| `real-input` | the window guard lets the game lock the cursor while the player is unfocused (an unfocused lock never captures the OS cursor, measured); the game adapter drives the UI by virtual devices (below) |
| `fast-phases <regex>` + `fast-timescale X` | phases matching the regex run at X (paused while recording) |
| `record "kindRegex:seconds[,…]"` | record N game seconds frame by frame after each matching journal event |
| `record-fps N` / `record-width W` | recording rate (default 20) and frame width |

### Corruption du Portail adapter

| Arg | Meaning |
|---|---|
| `role host\|client` · `connect <ip>` | process role in `play-net` (set by the launcher) |
| `players N` · `bots N` · `clients N` | seats; the host waits for N clients, then fills with bots |
| `force-roles A,B` | role-name fragments guaranteed in the composition |
| `role-holder host\|client\|bot` | who must hold the forced roles; mismatch fails fast, `run_scenario` retries the next seed |
| `vote-focus <role text>` | every bot votes the holder of that role |
| `max-days N` | stop after day N (fact `stopped-after-day`) |
| `netsim delay,jitter,loss` | Multiplayer Tools Network Simulator on that process (put it in client args) |
| `quit-at <phase text>` | this process leaves when that phase starts (client 1 args) |
| `visual-picker` | bots hover and click the real picker cards (captures every opening) |
| `fast-fakes` | fake roles go back to sleep after ~1 s |
| `target-focus client\|host\|bot\|fake\|<role text>` | power targets: pick among the valid candidates matching it when any does |
| `target-focus-power <power text>` | limit `target-focus` to picks made while that power is used |
| `chat` | every player writes one tokenized line per phase in each private channel it belongs to |
| `build-version <v>` | this client sends another build version (client 1 args; builds only) |
| `stall-load <seconds>` | this client's scene loads are held that long after synchronization starts (client 1 args) |
| `expect-clients N` | host: real clients that must join (when some are meant to be refused) |
| `spawn-during-load <seconds>` | host: that long after a joiner starts synchronizing (and while it still is), seat a simulated player (a Character spawned, then its owner / parent / roster entry written) — the late-joiner desync trigger |
| `connect-delay <seconds>` | this client waits that long before its first connect attempt (stagger joins; put it in client args) |
| `relay` | every process: the session goes through Unity Gaming Services as players do: real login screen (anonymous UGS sign-in, one profile per process), the host clicks Host (Relay allocation + UGS lobby, journals `relay.lobby <code>`), the launcher passes the code to the clients (`join-code`), which type it and click Join; the rejoin button reconnects through Relay. Needs internet; creates anonymous UGS players and a short-lived public lobby. Text fields get key events through `TMP_InputField.ProcessEvent` (`input.type mode=events`; TMP reads IMGUI events, not Input System devices) |
| `join-code <code>` | client, set by the launcher in relay runs |
| `video` | film this process: the screen at `video-fps` (10) real-time frames, turned into `video.mp4` in its run folder by `Tools~/make_videos.py` (run by `play-build` / `play-net`; needs ffmpeg). `run_scenario.py --video [all\|client1]` or scenario key `"video"` adds it. Windowed players only |
| `net-log` | NGO's own developer log (approvals, disconnect events and which side closed) in this process's player log |
| `rejoin-after <seconds>` | with `quit-at` (client): the client drops instead of leaving (no Leave button, like a crash or a lost connection), goes back to the main menu by the real client path, waits that long, reconnects with its session token and must get its seat back, then plays on. Captures `rejoin-before` / `rejoin-menu` / `rejoin-after` (burst 1/3/6 s) for `analyze_rejoin.py` |
| `rejoin-via menu` | with `rejoin-after`: the rejoin goes through the main menu's "Rejoindre la partie en cours" button, clicked with the virtual pointer (the disconnect notification dismissed first; the UGS login screen lifted, the autoplay never signs in to the cloud); a failed attempt is clicked again, up to 3 times |
| `crash-at <phase text>` | client, player build only: the game process is killed at that phase (journal `crash` first: no Leave, no disconnect message, nothing run on the way out). Captures `rejoin-before`. With scenario `client1Relaunch` the launcher starts the game again |
| `relaunched` | added by the launcher to the relaunched game (scenario `client1Relaunch` args): it keeps the session saved on the PC, rejoins through the menu button, journals `rejoin.relaunch` / `rejoin.seat`, plays its original seat |
| `rejoin-grace <seconds>` | host: a mid-game leaver's seat stays reserved that long instead of `GameValues.REJOIN_GRACE_SECONDS` (120 s), so a run sees the expiry |
| `real-input` | every seat with a screen acts through the real UI: virtual Input System mouse + keyboard (real ones disabled, settings cloned with `IgnoreFocus`); the pointer glides to the target or, cursor locked (seated vote), the camera turns until the target is under the reticle; each click must produce its effect, else `input.miss` + direct fallback. Implies `lobby-ui` and `visual-picker` |
| `lobby-ui` | lobby through the tablet by mouse (UI Toolkit in a RenderTexture): "★ Preset classique", wheel + Imposé "+" per `force-roles`, each seat's "Prêt", start by `LobbyState.TryAutoStart` (no `ForceStart`) |
| `real-input-tour` | once per process: tooltip, pause menu (button, audio slider on the host only: PlayerPrefs are shared by the processes and always put back, close), tablet (Tab, arrow, chat tab + field, Tab) at night while idle; emote wheel (hold T, mouse, release) at the vote recap. With `quit-at`, a real-input client leaves through the pause menu's Leave button |
| `menu-ui` | main menu tour (first screen captured, Host / Join panels, audio slider); with `real-input`, a refused join checks the disconnect notification is on top and closes on Dismiss |
| `real-input-mask <GameObject name>` | breakage test (needs `real-input`, else the run fails): a transparent click-eating overlay covers that object; its clicks must end in `input.miss … hit=…AutoplayMask` |
| `real-input-control` | diagnostic: virtual devices without disabling the real ones (counts the user's own input events, `realEvents=`) |
| `power-use-probability <0..1>` | chance a bot uses each usable power (0 = every seat sleeps through the sleep button) |
| `vote-probability <0..1>` | chance a bot votes (else it skips; with `real-input` it clicks the vote's Skip button) |

## Journal events (`events.ndjson`, counters in `report.json`)

| Area | Kinds |
|---|---|
| Run | `run.begin`, `session.ready` (Host step done: the net launcher starts clients after the host's), `run.fail`, `port`, `autoplay.begin`, `teardown.error` |
| Network | `connected` (with `load=` after a held load), `connect.retry`, `clients.joined`, `net.rtt`, `netsim`, `leave`, host `seat.grace`, `seat.reserved` (`<id> phase=`), `seat.released` (`<id> chained= left=`), host `relay.lobby`, `chat.grant` / `chat.revoke` (`<chatId> <seat>`), client `relay.join`, `login.ok`, `input.type` (`mode=keyboard|events|set`), `rejoin.drop`, `rejoin.menu`, `rejoin.reconnect` (`token present|missing`), `rejoin.click` / `rejoin.retry` (menu button), `rejoin.refused` (`reason= button=shown|hidden`: the host refused the rejoin, the run ends completed with `rejected=`), `crash` (`seat … token present`), `rejoin.relaunch`, `login.skip`, `rejoin.seat` (`seat <id> connection <id>`), `state.hash` (FNV of roles + flags + public-state component hashes, once per settled phase) |
| Join | `join.version`, `join.stall`, `join.synchronizing`, `join.stall.released`, `join.delay`, `connect.rejected` (`reason= \| after-sync=`), host `lobby.wait-loaders`, `lobby.loaders-done`, `join.spawn-during-load` |
| Chat | `chat.sent` (`chat= from= token= phase=`), `chat.recv` (every process, always: `chat= from= token= text=`), host `chat.members` |
| Composition | `composition`, `composition.force`, `roles.assigned`, `possess` |
| Phases | `state.enter` (every phase change), `awake`, `sleep`, `awake.layer`, `awake.layer.end`, `fake.sleep` |
| Powers | `power.start`, `power.end`, `power.skip`, `power.timeout`, `power.error`, `power.verdict` |
| Selection | `select.character`, `select.role`, `select.error`, `picker.open`, `picker.hover`, `picker.click`, `picker.closed` |
| Vote / portal | `vote`, `vote.skip`, `vote.skip.click`, `portal.click` (`via=click\|direct` in real-input mode) |
| Real input | `input.install`, `input.uninstall` (devices disabled, `realEvents=` meaningful with `real-input-control` only), `input.click` (`<action> target= pos= hit= mode=pointer\|reticle\|key`), `input.miss` (same + `reason=`: target `not-found`, `inactive`, `zero-size`, `disabled`, `no-canvas`, `no-camera`, `behind-camera`, `off-screen`, `occluded` (with `uitk=` when a UI Toolkit panel took the click), `no-effect`, `lock-changed`; reticle `screen-fixed`, `look-clamped`, `out-of-reach`, `no-raycast-target`; UI Toolkit `hidden`, `not-laid-out`, `app-closed`, `picked-other`, `no-render-texture`, `no-convergence`, `degenerate`; lobby `not-open`, `scroll-stuck`), `input.skip` (effect already there: no click), `input.reaim` (reticle target moved off before the click: aimed again once), `input.error` (exception in an input flow, then direct path), `input.scroll`, `input.mask`, `power.direct`, `vote.late`, `sleep … via=`, `picker.click … via=click\|direct\|none`, `vote … via=click\|click-late` |
| Lobby / tour / menu | `lobby.preset`, `lobby.force`, `lobby.ready` (`via=click\|already\|direct`), `lobby.role-card` (`open\|close ok\|miss`), `lobby.autostart`, `lobby.status`, `composition … via=`, `tour.<step>` (`ok\|miss`, `tour.aborted` when a seat wakes), `menu.<step>`, `menu.notification-top`, `menu.notification-dismiss`, `leave … via=` |
| Knowledge | `knowledge` (`viewer>target role= corrupt= force= hacked=` levels, on every change) |
| Captures | `capture`, `capture.state`, `capture.error`, `capture.state.error`, `record`, `record.start`, `record.skip`, `record.tracks.error` |

## Captured moments and exported state

- Automatic: every phase change, every power verdict (burst 0.15 / 0.5 / 1.2 s), every picker opening in
  `visual-picker` mode (burst 0 / 0.1 / 0.25 / 0.5 / 1 s + hover).
- Each capture writes `NNN-label.json` (time, phase, probes, game state under `"game"`: characters, flags, roles,
  powers, picker state, `frostAlpha`, pickable cards, knowledge) and `NNN-label.png` (not in batchmode / `no-png`).
- Recordings: `rec-NNN-label/` with `f###.png`, `tracks.csv`, `manifest.json`. Tracks: `stateIndex`, `frost`,
  `lifted`, `card0..9` transforms (`.x .y .z` …).

## Scenario checks (`run_scenario.py`)

`outcome` · `event` (kind / detail regex / min / max; `process` any|host|clients = total, all|every-client = each) ·
`noErrors` (ignore regexes) · `fact` · `roster` · `desync` (net) · `state` (path in capture JSON, op, quantifier) ·
`leaverChained` (net) · `analyzer` (external command, exit 0 = pass) · `animation` (start / end / reach / change /
monotonic, `onlyIf`, `window`). Any check takes `"severity": "warn"`. Full syntax: docstring of
`Packages/com.unpseudo.autoplay/Tools~/run_scenario.py`.

Scenario keys: `name`, `goal`, `mode` (build|net), `clients`, `seed`, `seedRetries`, `timescale`, `timeout`,
`visualPicker`, `args`, `clientArgs`, `client1Args`, `client1Relaunch` (`{"after": s, "args": [...]}`: the launcher
relaunches client1 once if its game crashes or dies first), `video` (`true` / `"client1"`), `expect`. `outcome` takes `"alsoAccept": ["Crashed"]` (a process killed by
`crash-at` writes no report; its journal ends with `crash`).

## Scenarios (regression set, `tools/autoplay/scenarios/`)

| Scenario | Proves |
|---|---|
| `mage-portal-client` | a Mage on a real client takes down the portal |
| `picker-visual` | every picker opening: blur veil + lifted valid cards (analyzer) |
| `picker-animation` | picker opening animation, frame by frame |
| `client-leaves-at-vote` | a client leaving at the vote gets a reserved seat, chained when the (10 s) grace expires; the game goes on |
| `relay-game` | a game over Unity Relay set up through the real menus (login, Host, Join by code), played to the day limit; no desync |
| `relay-crash-relaunch` | the crash + relaunch rejoin over Relay: the relaunched game signs in again, clicks the rejoin button, reconnects through Relay |
| `client-crash-relaunch` | a client's game is killed at the first awakening recap, the launcher relaunches it 3 s later; it finds its session, clicks the menu's rejoin button and takes its seat back (reserved, or taken over from the dead connection when the host has not noticed yet); `analyze_rejoin.py` pairs the crashed and the relaunched process |
| `client-rejoin` | a client drops at the first awakening recap, clicks the menu's rejoin button 8 s later: same seat, role, powers, chat channels, icons and knowledge (`analyze_rejoin.py`), no longer reserved nor left, votes again; no desync |
| `client-disconnect-reserved` | a client leaving at night: seat reserved (skipped, no vote), chained only when the 30 s grace expires; no hang, no desync |
| `lag-150ms` | a full game under 150 ms simulated latency |
| `heavy-loss` | a full game with 3 chatting clients under 250 ms latency, 80 ms jitter and 5 % loss; no desync |
| `net-sync-7clients-chat` | a full table of real players (host + 7 clients, no bot) chatting in private channels: zero desync on 8 processes, private lines delivered, no leak |
| `rejoin-at-vote` | a client drops in the middle of the day-1 vote and rejoins: the vote resolves, seat intact, votes again |
| `rejoin-at-night` | a client drops during a night (powers in use) and rejoins: no hang, seat intact (a use spent just before the drop is accounted for), every power list complete on the rejoined peer (`[DESYNC] component=Powers` before the `CharacterManager` re-scan fix) |
| `rejoin-under-lag` | the menu rejoin with 150 ms latency, 40 ms jitter, 2 % loss on every client |
| `mass-rejoin` | every real client drops at once and rejoins: three seats reserved and claimed concurrently, all intact |
| `rejoin-after-expiry` | a rejoin after the grace delay is refused with the game-in-progress wording, the saved session is dropped (button hidden), the game goes on |
| `net-sync-3clients` | zero desync on every public-state component |
| `client-owner-local-powers` | client-held Repenti / Orpheline reveals reach the owning client |
| `orpheline-contact-chosen` | Lack of Affection on a chosen real client: contact line + the Orpheline's role revealed to the target |
| `orpheline-contact-anomaly` | Lack of Affection on an anomaly real client: contact line, no reveal |
| `chat-private` | private channels (ink, anomaly): every line reaches every real member, never a real non-member |
| `join-version-mismatch` | a client with another build version is refused with the version wording (join error report) |
| `join-stuck-load-kick` | a joiner whose load never ends is kicked by the host at the 90 s cap; the lobby then starts without it |
| `join-slow-load-honest` | a load held 30 s is not kicked: the client joins and plays |
| `join-spawn-during-load` | a player seated while a joiner's load outlasts NGO's SpawnTimeout: every client still sees every owner and the full roster (late-joiner Characters desync, 2026-10-05) |
| `real-input-actions` | every power, card pick, vote (reticle), sleep and the Mage's portal card is a real click with its effect, host + 3 real clients, real devices disabled; only two exempted, warned design findings: role-picker cards off screen, Skip vote button out of the seated reticle's reach |
| `real-input-mask` | breakage test: a covered sleep button fails as `input.miss hit=…AutoplayMask` |
| `real-input-tour` | tooltip, pause menu + audio slider, tablet + chat app, emote wheel by real input on every screen; a client leaves through the pause menu and is chained |
| `real-input-lobby` | lobby by mouse on the tablet: preset, role card overlay (screen-space UI Toolkit) opened and closed, wheel + Imposé "+", every "Prêt", start by `TryAutoStart` |
| `real-input-menu` | first screen captured; a refused join's notification is above the login screen and closes on Dismiss |

Other tools: `campaign.sh` (all scenarios + random seeds → `summary.md`), `sweep_powers.py` (one forced-role run per
role → `coverage.md`), `analyze_picker.py`, `analyze_chat.py` (private chat delivery / leaks), `analyze_rejoin.py` (what a rejoined player sees, before the drop vs after the rejoin), `analyze_contact.py`
(Lack of Affection per target faction), `compare_runs.py`, `contact_sheet.py`. Shared run loading for analyzers:
`autoplay_runs.py`.

## Not covered yet → playtest, or extend

| Gap | Why autoplay misses it | Extension |
|---|---|---|
| Typing / sending in the chat | `TMP_InputField` reads keystrokes from IMGUI events and `ChatPanel.cs:56` sends on legacy `Input.GetKeyDown(Return)`: virtual Input System keys reach neither (real input focuses the field; sending stays direct with `chat`) | an Input System send action, or IMGUI `Event` injection |
| Nested tooltip links | `TooltipWindow.cs:35` hit-tests `<link>` with legacy `Input.mousePosition` (the real OS cursor) | read the pointer from the Input System |
| Notes ribbon | `NoteRibbon` and its prefab are placed in no scene: nothing to click | place it, then add a tour step |
| Main menu panels (Host / Join, audio) | the login screen (`LoginCanvas`, order 1000) covers the menu while autoplay does not log in; UGS (Authentication, Lobby, Relay) and Steam are out of scope (`real-input-menu` warns) | a login-free dev path, or an authenticated run |
| F1-F4 dev keys, `DevIdentityController` | dev tools, not player paths | none planned |
| Pause by keyboard | Escape is never wired (`InputManager.OnEscapePressed`): only the HUD pause button exists (the tour clicks it) | design decision |

Add a row whenever a goal turns out not to be covered, with the extension that would cover it.

## Extending in this game

Generic method, rules and definition of done: `Packages/com.unpseudo.autoplay/EXTENDING.md`. Game specifics:

- Levers and process roles: `CdpAutoplayGame` (`AutoplayOptions` carries them to the driver). Bot actions and game
  events: `AutoplayDriver`. Target / card choices: `AutoplaySelectionAutopilot` (plugged into
  `SelectionFlowService.Autopilot`). Composition: `AutoplayComposition`.
- Simulated bots (id >= 100) live on the host: server-side calls on their behalf are legit; RPC targets go through
  `GetSafeRpcTarget`, ownership checks through `IsLocalOrSimulated` (CLAUDE.md critical patterns).
- Known trap: `TakeDownThePortalState.shouldActivate` is server-only; the client learns the Mage id by RPC.

### Worked examples (gaps closed on 2026-10-05)

How the four gaps reported after the network-hardening runs were covered; copy the pattern for the next one.

#### Deterministic targets (`target-focus`, `target-focus-power`)

- `AutoplaySelectionAutopilot.Focus` keeps the valid candidates matching `client`, `host`, `bot`, `fake` or a role
  text (`AutoplayFocus.Matches`), and falls back to every valid one when none matches. `target-focus-power` limits it
  to picks made while a power whose name contains the text is used (`CurrentPowerName`, set by the driver around
  `StartUse`). Works in visual-picker mode too.
- Journal: `select.character … focus=<spec>` when the focus decided the pick.
- Lack of Affection (`PLackOfAffection`): the server runs the decision with the target as viewer; a chosen target
  learns the Orpheline's role (Personal), any real target gets the line "… est venu(e) vous voir..." in the server
  channel. `analyze_contact.py` checks both per contact, by the target's faction from the host's final roster.

#### Private chat (`chat`)

- Server side (host seat + simulated bots), the driver reads the server's membership (`ChatManager.ServerChannelsOf`,
  read-only seam over `ChatMembership.ChannelsOf`) and writes through `SendChatMessageServerRpc` with the player as
  sender, which keeps the server's membership check. A real client writes like its chat panel
  (`ChangeActiveChat` + `TrySendChatMessage`).
- Every line carries a token `[ap:<sender>:<n>]`. Journal: `chat.sent` (sender process), `chat.recv` (every process,
  always on, with the text: power feedback lines arrive through the chat too), `chat.members` (host, on change).
- `analyze_chat.py`: delivery to every real client that was a member all day, no real non-member ever receives a
  line, at least one delivery proven (else NOT COVERED).

#### Join refused or kicked (`build-version`, `stall-load`, `expect-clients`)

- `build-version <v>` rewrites the connection payload after `ClientConnectionPayload.Apply`. Builds only: an Editor
  on either side is let through by `BuildVersionGate`.
- `stall-load <s>` parks an additive `LoadSceneAsync` with `allowSceneActivation = false` before connecting: Unity
  queues scene loads, so NGO's GameScene load waits behind it while the transport and the main thread keep running
  (a frozen or suspended process would be dropped by the transport timeout instead, another path). The hold is
  released `s` seconds after synchronization starts; past the 90 s cap the host's `KickExpiredLoaders` ends it.
- Client join wait: 10 s per attempt until the host synchronizes it, then no client deadline (so the kick tested is
  the host's). A refusal with a server reason (`RelayFallbackPolicy.HasServerReason`) records `connect.rejected`,
  reports the wording through `LobbyManager.ReportError` (what the join menu does, shown by the notification panel),
  captures `join-rejected` (`-autoplay-png` on that client) and ends the run completed with a `rejected=` fact.
  The notification panel sorts above the login screen (`ClientDisconnectHandler`, order 32000): with
  `-autoplay-real-input` on that client, `menu.notification-top` proves it is the first thing under the pointer and
  `menu.notification-dismiss` that a click closes it (`real-input-menu`).
- Host: `expect-clients N` waits for the N clients meant to join; before starting it waits for joiners still loading
  (`lobby.wait-loaders` / `lobby.loaders-done`), as a forced start is refused while one loads.

#### Real-input mode (2026-10-05, spec `spec-autoplay-real-input.md`)

- `AutoplayVirtualInput` (package) adds a virtual mouse + keyboard, installs a runtime clone of the input settings
  with `IgnoreFocus` (the project asset is untouched) and disables every real mouse / keyboard while installed (also
  ones added later). Trap: `InputSystem.onDeviceChange` fires inside `AddDevice`, before the returned device is
  stored: guard your own devices or the hook disables them.
- `AutoplayUiLocator` aims at a point where the real `EventSystem.RaycastAll` reaches the target (a button's
  RectTransform centre can lie on no graphic): uGUI graphics or collider bounds sampled on a 3x3 grid; any UI Toolkit
  element, on a screen-space panel or drawn into a RenderTexture (`TryLocateUitkElement`), by inverting the panel's own
  screen-to-panel function numerically (no assumption on scale or axes), validated by `panel.Pick`. A covered target
  is waited for 1.5 s (animations) before it counts as a miss.
- Driver building blocks: `ClickTarget` / `HoverTarget` (GameObject), `ClickUitk` / `HoverUitk` (UI Toolkit element
  found by a lambda, re-run before and after the glide: panels rebuild their trees), `PressKeyFor`, `ClickAt`. Each
  checks the effect, clicks once (never twice: a late effect must not be toggled back) and skips the click when the
  effect already holds (`input.skip`).
- Cursor locked (seated vote): `AimAt` turns the camera with mouse deltas, steered in angles (the camera's measured
  rotation per delta unit; a hovered card moves by itself and would fool a pixel ratio), one step then a wait for the
  damped camera; a screen-overlay element cannot be aimed (`reason=screen-fixed`).
- Host: the acting seat is possessed before every UI action, one action at a time (`inputBusy`; flows not started by
  the driver's Update wait for it). `End()` cancels every in-flight click and releases held keys / buttons.
- Without `real-input` / `lobby-ui` / `menu-ui` nothing is installed and no `input.*` event is journaled.
