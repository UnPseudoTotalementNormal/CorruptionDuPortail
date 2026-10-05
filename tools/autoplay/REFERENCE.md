# Autoplay — feature reference (Corruption du Portail)

Everything the autoplay framework can do today, in one place. How to *use* it (goal → run → evidence → answer):
the `autoplay` skill (`.claude/skills/autoplay/SKILL.md`). How to *add* to it: `Packages/com.unpseudo.autoplay/EXTENDING.md`.
Headless editor + Unity CLI workflow: `tools/HEADLESS_UNITY.md`.

Keep this file exact: a lever, event or check that is added, renamed or removed is updated here in the same commit.

## Layers

| Layer | Where | Owns |
|---|---|---|
| Package (game-agnostic) | `Packages/com.unpseudo.autoplay/Runtime` | runner, journal, captures + state export, animation recorder, command line, dev-build bootstrap, window guard |
| Package tools | `Packages/com.unpseudo.autoplay/Tools~` | `unityctl.sh`, launchers, `run_scenario.py`, `campaign.py`, `compare_runs.py`, `contact_sheet.py` |
| Game adapter | `Assets/Scripts/Autoplay` | `CdpAutoplayGame` (levers, host/client roles), `AutoplayDriver` (bot brain), `AutoplaySelectionAutopilot`, `AutoplayComposition` |
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

## Journal events (`events.ndjson`, counters in `report.json`)

| Area | Kinds |
|---|---|
| Run | `run.begin`, `session.ready` (Host step done: the net launcher starts clients after the host's), `run.fail`, `port`, `autoplay.begin`, `teardown.error` |
| Network | `connected` (with `load=` after a held load), `connect.retry`, `clients.joined`, `net.rtt`, `netsim`, `leave`, `state.hash` (FNV of roles + flags + public-state component hashes, once per settled phase) |
| Join | `join.version`, `join.stall`, `join.synchronizing`, `join.stall.released`, `connect.rejected` (`reason= \| after-sync=`), host `lobby.wait-loaders`, `lobby.loaders-done` |
| Chat | `chat.sent` (`chat= from= token= phase=`), `chat.recv` (every process, always: `chat= from= token= text=`), host `chat.members` |
| Composition | `composition`, `composition.force`, `roles.assigned`, `possess` |
| Phases | `state.enter` (every phase change), `awake`, `sleep`, `awake.layer`, `awake.layer.end`, `fake.sleep` |
| Powers | `power.start`, `power.end`, `power.skip`, `power.timeout`, `power.error`, `power.verdict` |
| Selection | `select.character`, `select.role`, `select.error`, `picker.open`, `picker.hover`, `picker.click`, `picker.closed` |
| Vote / portal | `vote`, `vote.skip`, `portal.click` |
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
`visualPicker`, `args`, `clientArgs`, `client1Args`, `expect`.

## Scenarios (regression set, `tools/autoplay/scenarios/`)

| Scenario | Proves |
|---|---|
| `mage-portal-client` | a Mage on a real client takes down the portal |
| `picker-visual` | every picker opening: blur veil + lifted valid cards (analyzer) |
| `picker-animation` | picker opening animation, frame by frame |
| `client-leaves-at-vote` | a client leaving at the vote is chained, the game goes on |
| `lag-150ms` | a full game under 150 ms simulated latency |
| `net-sync-3clients` | zero desync on every public-state component |
| `client-owner-local-powers` | client-held Repenti / Orpheline reveals reach the owning client |
| `orpheline-contact-chosen` | Lack of Affection on a chosen real client: contact line + the Orpheline's role revealed to the target |
| `orpheline-contact-anomaly` | Lack of Affection on an anomaly real client: contact line, no reveal |
| `chat-private` | private channels (ink, anomaly): every line reaches every real member, never a real non-member |
| `join-version-mismatch` | a client with another build version is refused with the version wording (join error report) |
| `join-stuck-load-kick` | a joiner whose load never ends is kicked by the host at the 90 s cap; the lobby then starts without it |
| `join-slow-load-honest` | a load held 30 s is not kicked: the client joins and plays |

Other tools: `campaign.sh` (all scenarios + random seeds → `summary.md`), `sweep_powers.py` (one forced-role run per
role → `coverage.md`), `analyze_picker.py`, `analyze_chat.py` (private chat delivery / leaks), `analyze_contact.py`
(Lack of Affection per target faction), `compare_runs.py`, `contact_sheet.py`. Shared run loading for analyzers:
`autoplay_runs.py`.

## Not covered yet → playtest, or extend

| Gap | Why autoplay misses it | Extension |
|---|---|---|
| *(none listed)* | | |

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
  Limit: autoplay clients skip the menu login, so the capture shows the login screen, whose canvas (sorting order
  1000) covers the notification panel (999): the message reaching the report is proven, its visibility to a
  logged-in player is not.
- Host: `expect-clients N` waits for the N clients meant to join; before starting it waits for joiners still loading
  (`lobby.wait-loaders` / `lobby.loaders-done`), as a forced start is refused while one loads.
