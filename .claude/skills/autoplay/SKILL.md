---
name: autoplay
description: Play Corruption du Portail automatically with bots (host + simulated players or up to 7 real network clients, real GameScene, real power / vote / picker / network code paths) to check a precise goal, with screenshots, recordings and exported state at the right moments, then answer with evidence; and extend the autoplay framework (new lever, event, check, scenario) when a goal is not covered yet. Use when the user asks to test / check / verify / playtest / reproduce something in-game automatically ("check que tous les pouvoirs…", "vérifie que le flou…", "lance une partie auto", "fais jouer des bots", "autoplay", "teste en jeu", "teste en réseau", "est-ce que ce feedback s'affiche quand…", "simule une partie"), or to add / improve an autoplay feature ("ajoute à l'autoplay", "l'autoplay ne couvre pas…", "nouveau scénario", "extend autoplay"). PREFER this over manual play mode, MCP play-mode driving or ad-hoc scripts for any in-game behaviour or visual check.
---

# Autoplay — goal → bots play → evidence → answer

Framework: package `Packages/com.unpseudo.autoplay` (generic: runner, journal, captures + state export, dev-build
bootstrap, window guard, `Tools~/`) + the game adapter `Assets/Scripts/Autoplay/` (`CdpAutoplayGame`,
`AutoplayDriver` bot brain, `AutoplaySelectionAutopilot`). R&D + findings:
`_bmad-output/implementation-artifacts/investigations/autoplay-automated-games-rnd.md`.

- **Every lever, event, capture, check and scenario that exists:** `tools/autoplay/REFERENCE.md`. Look there first.
- **Adding to the framework:** `Packages/com.unpseudo.autoplay/EXTENDING.md` (generic method, rules, definition of
  done) + `tools/autoplay/REFERENCE.md` § Extending in this game (where it goes here, backlog recipes).
- Package README (layout, outputs, hard-won lessons): read once.

## 1. Turn the request into a goal you can check

Write down, before running anything: **what must be observed, when, on whose screen, and what counts as pass/fail**.
Map it to what the run produces:
- game logic / flow / errors → `report.json` (outcome, phase trace, counters, errors) + `events.ndjson`
- exact values at a moment → the capture's state file (`NNN-label.json`, game state under `"game"`, probes)
- visuals → the capture PNGs at that moment (bursts give several timings of one moment)

Moments captured out of the box: every phase change, every power verdict (burst 0.15/0.5/1.2 s), and in
`visual-picker` mode every picker opening (burst 0/0.1/0.25/0.5/1 s + hover). Need another moment or value? Add a
`capture.Request(...)` / `RequestBurst(...)` call or an `AddProbe(...)` in the driver — small, then rebuild.

**Force the situation from the start.** A random game does not guarantee the case under test: before launching, ask
"what must happen for this run to prove something, and what guarantees it?" If nothing does, add the scenario lever
first (`-autoplay-vote-focus <role text>` = every bot votes that role, `-autoplay-clients/players/bots`, seed, or a
new forced-composition option), then run. Afterwards, check in the logs that the situation really happened (e.g.
`portal.click` logged by a client) — otherwise report "not covered", never "OK".

## 2. Environment (once per session)

- By default drive **your own headless editor on your checkout** (batchmode → modal dialogs auto-cancelled, nothing
  on screen). Use the user's open editor only when they ask for it; when unclear, ask. Recipe + traps:
  `tools/HEADLESS_UNITY.md`.
- Everything long runs **in the background** (`run_in_background` / Monitor), never in a foreground loop.
- Use `tools/autoplay/unityctl.sh` (wrapper of the package CLI): `compile`, `editmode`, `build`, `play-build`,
  `last-run`. Never recompile while a PlayMode run is in flight.

## 3. Pick the run mode

| Goal | Mode |
|---|---|
| Whole game / flow / errors / visuals | **windowed dev build**: `tools/autoplay/unityctl.sh build` (only if code changed since the last build) then `play-build <seed> <port>` |
| Picker visuals (blur veil, lifted cards, hover) | same, with `AUTOPLAY_VISUAL_PICKER=1` |
| Real network: replication / desync between host and clients | `tools/autoplay/unityctl.sh play-net <clients> <seed>` (host + N real clients, bots fill to 8) → `compare_runs.py` verdict |
| Quick logic check of the first night only | `tools/autoplay/unityctl.sh playmode AutoplaySoloHostTests` (batchmode editor cannot finish a game: end of frame never comes) |

Coverage = several seeds (roles are drawn from the designer's classic preset). Report which powers/roles a run covered;
if the goal needs a specific power, loop seeds until it appears (events `power.start`), or add a forced-composition
option before claiming coverage.

**Changing the bot brain (new role, state, action)?** Validate it with `play-net` too, not only `play-build`: host +
simulated bots runs everything on the server, which hides client-side bot bugs. On a real client, never gate an
action on a field only the server writes (GameState objects are cloned per process; e.g. `TakeDownThePortalState.
shouldActivate` is server-only, the client only gets the Mage id by RPC) — react to what the server actually sends the
client, like the UI does. (Memory: `reference_autoplay_client_vs_server_state`.)

Players are launched **without focus**, muted, never cursor-locked (`launch-background.ps1` + `AutoplayWindowGuard`):
the user keeps working. Never minimize a player (black captures). Ports 7850–7899 only.

### Scenario levers (player args, `-autoplay-<key>`)

Complete list with every package option: `tools/autoplay/REFERENCE.md`. Most used:
`force-roles A,B` (role-name fragments guaranteed in the composition) · `role-holder host|client|bot` (who must hold
them; mismatch fails fast and the scenario runner retries the next seed) · `vote-focus <role text>` · `max-days N` ·
`netsim delay,jitter,loss` (Multiplayer Tools Network Simulator; put it in `clientArgs`) · `quit-at <phase text>`
(a client leaves mid-game; put it in `client1Args`) · `visual-picker` · `clients/players/bots`.

### Declarative scenarios — prefer them for anything worth re-running

Write `tools/autoplay/scenarios/<name>.json` (goal, mode, levers, expectations — format in
`Packages/com.unpseudo.autoplay/Tools~/run_scenario.py`) and run
`python -X utf8 Packages/com.unpseudo.autoplay/Tools~/run_scenario.py tools/autoplay/scenarios/<name>.json`.
A passing scenario stays as a regression test; `tools/autoplay/campaign.sh` replays them all + random games.
Coverage of targeted powers: `python -X utf8 tools/autoplay/sweep_powers.py` (one forced-role visual run per role →
`coverage.md`, OK / FAIL / NOT COVERED per power).

### Animations

To check that an animation plays right, record it: `-autoplay-record "power.start:1.8"` (trigger on an event BEFORE the
animation, not on its own start, or the first frames are lost) → `rec-NNN-*/` frames + `tracks.csv`. Look at ONE contact
sheet (`python -X utf8 Packages/com.unpseudo.autoplay/Tools~/contact_sheet.py <rec-dir> --every 2 --track <name>`), and
assert on the numbers with an `animation` check (thresholds measured on a reference run, never invented). Reference:
`tools/autoplay/scenarios/picker-animation.json`. A late-started recording or a run stopped mid-burst is an artefact,
not a game bug — check before concluding.

### Speed

`-autoplay-fast-fakes` (fake roles sleep after ~1 s: night time 77 s → 12 s on 3 days, seed 777) and
`-autoplay-fast-phases "Recap|Intro|Chaining" -autoplay-fast-timescale 12` cut a game ~3× (118 s → 39.5 s). Use them by
default for flow / coverage runs; leave them OFF when the goal is the real timing of fake roles or of those phases.

### Not covered? Extend, do not hand it back

When the goal needs something autoplay cannot do yet (bots never chat, nothing stalls a join, targets are random…),
the default is to **add the lever / event / check**, then prove the goal with a scenario: follow
`Packages/com.unpseudo.autoplay/EXTENDING.md` (definition of done: compile, scenario PASS on `play-net` if a client is
involved, shown able to fail, `REFERENCE.md` updated). Start from the backlog recipes in `tools/autoplay/REFERENCE.md`
when the gap is listed there. Hand a point to human playtest only when the extension is out of scope for the task,
and then say which extension would cover it (and add it to the backlog table).

## 4. Read the evidence — cheaply

1. `tools/autoplay/unityctl.sh last-run` (outcome, trace, counters, deduplicated errors).
2. Goal-specific analysis over the state files (e.g. `python -X utf8 tools/autoplay/analyze_picker.py <run>`); write a
   small analyzer for a new kind of goal rather than reading hundreds of files.
3. Look at **a few targeted PNGs** only (the ones an analysis flags, plus one passing example) — never all of them.

## 5. Answer

- Verdict per goal (pass / fail / not covered), with the run folder, the key capture paths and numbers. A "not
  covered" names the extension that would cover it.
- Separate: **game bugs** (with root cause if proven — instrument with a tagged log before theorising),
  **tool problems** (fix them), and **design observations** (overlaps, layout — report only, design is owned by the
  game designer).
- Never commit `AutoplayRuns/` or editor import noise; stage explicit files only, and only when the user says so.
