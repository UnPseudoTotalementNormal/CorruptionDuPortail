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

## Journal events (`events.ndjson`, counters in `report.json`)

| Area | Kinds |
|---|---|
| Run | `run.begin`, `run.fail`, `port`, `autoplay.begin`, `teardown.error` |
| Network | `connected`, `connect.retry`, `clients.joined`, `net.rtt`, `netsim`, `leave`, `state.hash` (FNV of roles + flags + public-state component hashes, once per settled phase) |
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

Other tools: `campaign.sh` (all scenarios + random seeds → `summary.md`), `sweep_powers.py` (one forced-role run per
role → `coverage.md`), `analyze_picker.py`, `compare_runs.py`, `contact_sheet.py`.

## Not covered yet → playtest, or extend (recipes below)

| Gap | Why autoplay misses it | Extension |
|---|---|---|
| Private chat | bots never write | `-autoplay-chat` lever + `chat.sent` / `chat.recv` events |
| Build-version mismatch message | every process runs the same build | `-autoplay-build-version` override on client 1 + `connect.rejected` event |
| Kick of a joiner stuck loading > 90 s | nothing stalls a join | dev-only hold of the client's synchronization (not a frozen process) |
| Lack of Affection reveal, per target faction | the Orpheline's contact is a random pick (fake characters included), so which reveal fires, if any, is luck | `-autoplay-target-focus` lever (deterministic targets) |

## Extending in this game

Generic method, rules and definition of done: `Packages/com.unpseudo.autoplay/EXTENDING.md`. Game specifics:

- Levers and process roles: `CdpAutoplayGame` (`AutoplayOptions` carries them to the driver). Bot actions and game
  events: `AutoplayDriver`. Target / card choices: `AutoplaySelectionAutopilot` (plugged into
  `SelectionFlowService.Autopilot`). Composition: `AutoplayComposition`.
- Simulated bots (id >= 100) live on the host: server-side calls on their behalf are legit; RPC targets go through
  `GetSafeRpcTarget`, ownership checks through `IsLocalOrSimulated` (CLAUDE.md critical patterns).
- Known trap: `TakeDownThePortalState.shouldActivate` is server-only; the client learns the Mage id by RPC.

### Backlog recipes

Sketches, not specs: read the code they name before writing anything.

#### Private chat (lever `chat`)

- Driver: on each day phase, each controlled player writes one line in every channel it has (`ChatManager.discoveredChatIds`).
  Real client: `ChatManager.instance.ChangeActiveChat(id)` + `TrySendChatMessage(text)` (the UI path). Simulated bot:
  the host calls `SendChatMessageServerRpc(new ChatMessage(botId, text, id))`; a server-sent message keeps its sender,
  the server still checks channel membership.
- Events: `chat.sent chat=<id> from=<id>`; `chat.recv chat=<id> from=<id>` on every process, from
  `onChatMessageReceived`.
- Situation: force the role that opens the private channel (`force-roles` + `role-holder client`).
- Checks: `chat.recv` with that channel on the member clients (`min: 1`, `every-client` where relevant) and `max: 0` on
  non-members; `noErrors` (catches `[CHAT] … not a member` warnings only if promoted to errors, so also assert on
  `chat.sent` count).

#### Build-version mismatch message (lever `build-version`)

- Adapter: in the client connect path, right after `ClientConnectionPayload.Apply(networkManager)`, rewrite the payload
  with the forced `BuildVersion` (`ConnectionPayload.ToBytes()` into `NetworkConfig.ConnectionData`). Put it in
  `client1Args` only.
- Must run in **builds**: an Editor on either side is allowed through (`BuildVersionGate`, `AllowedEditorMismatch`).
- Connect loop: stop retrying when the server gave a reason (`DisconnectReason` with a server reason, see the NGO
  placeholder trap) and record `connect.rejected reason=<text>`; capture the menu at that moment for the visual.
- Expected outcome for that client is a rejection, not "Completed": add an outcome value or assert with `event` +
  `process` and keep `outcome Completed` on the host.
- Checks: `connect.rejected` detail matching `BuildVersionGate.MismatchReason` on client 1, host completes with the
  other clients, no desync.

#### Joiner stuck loading > 90 s (lever `stall-load`)

- The kick lives server-side in the lobby (`ConnectionApprovalGate.KickExpiredLoaders`, cap
  `JoinHandshake.SyncTotalTimeoutSeconds`). Its timing logic is pure and belongs in EditMode tests; autoplay adds the
  end-to-end proof only.
- Do **not** freeze or suspend the client process: the transport drops it on its own disconnect timeout first, which
  tests another path. Hold the client's scene synchronization instead (dev-only seam around NGO scene loading on the
  client), so the transport stays alive while the load never completes.
- A dev-only override of the 90 s cap keeps the run short; one run at the real value before calling it done.
- Checks: host journal / log `[JOIN-GATE] Disconnecting <id>: still loading`, client `connect.rejected` with the
  stuck-load message, lobby goes on without it.

#### Lack of Affection reveal, per target faction (lever `target-focus`)

- `PLackOfAffection` (the Orpheline's contact): the server runs `LackOfAffectionDecision` with the target as viewer;
  what is revealed depends on the target's faction (chosen / marginal / anomaly), and the reveal + chat line are only
  delivered to a real player (`_targetClientId < 100`).
- Today the target is a random valid pick, fake characters included (net seed 51: first contact on a fake character,
  second on Luma). Nothing guarantees each faction case.
- Selection autopilot: like `vote-focus`, prefer a valid target matching a role text (or `client` / `host`).
- Situation: `force-roles Orpheline,<target role>` + `role-holder client` + `target-focus <target role>`; one scenario
  per faction case.
- Checks: `power.end` Orpheline (`min: 1`), the expected `knowledge` line on the target's process (viewer = target),
  `max: 0` of it on the others, no errors.
