# Autoplay (bot playtesting) — `com.unpseudo.autoplay`

Runs whole games **without humans**: a game adapter plays every seat through the game's real code paths, while the
package handles everything generic — phase tracking, stall / timeout detection, a live event trace, state files and
screenshots at capture points, a compact report, and a dev-build entry point that stays out of the user's way.

Editor and Development builds only: every file is guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD`, release players
contain none of it.

## Install

- Embedded: copy this folder into `Packages/`.
- From git: `"com.unpseudo.autoplay": "https://github.com/<owner>/<repo>.git?path=/Packages/com.unpseudo.autoplay"`.

## Plug a game in

Implement `IAutoplayGame` (boot, host, set up, start, current phase, over / alive, roster, outcome facts, exported
state JSON, audio mute, teardown) and register it:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Register() => AutoplayRegistry.Register("mygame", () => new MyAutoplayGame());
```

Bots are the game's business: usually a MonoBehaviour started in `SetUp`, playing through the same entry points as a
human (UI seams, server calls), choosing among options the game declares legal through an `IAutoplayPolicy`
(`RandomValidPolicy` is seeded, so a run is reproducible). Use `context.Capture.Request(label, delay)` /
`RequestBurst(label, delays)` at the moments worth seeing, `context.Capture.AddProbe(name, read)` for extra values.

Also implement `IAutoplayWatchdogSource` (optional, strongly advised): `BudgetFor(step)` gives each step (`boot`, `host`,
`setup`, `start`) and phase (`phase <label>`) a real-seconds budget ("never longer when all is well": derive phase
budgets from the game's own timers and `Time.timeScale`), `DescribeWait()` says in one line what the game waits on.
`AutoplayWatchdog` then turns any hang into alerts (`watchdog.slow` / `watchdog.stall`, relayed live by the launchers to
`alerts.log`) and, at 3× budget, a failed run WITH a report; an operator can `extend` / `abort` it through
`watchdog-control.txt`. Without the interface the package defaults apply and alerts say nothing about the wait.

## Run

| Where | How |
|---|---|
| PlayMode test (editor) | `yield return AutoplayRunner.Run(game, config, result);` then destroy `result.host` |
| Development player | `Game.exe -autoplay [-autoplay-seed N] [-autoplay-timescale 4] [-autoplay-port 7850] [-autoplay-out dir] [-autoplay-<game option> …]` → exit code 0 = completed |

Every run writes `<out>/<stamp>-<scenario>-seed<N>/`:

- `report.json` — read first: outcome, failure reason, phase trace, game facts, event counters, roster, errors.
- `events.ndjson` — every event, appended live (a hung or crashed run still leaves it).
- `NNN-<label>.json` — at each capture point: time, phase, probes, and the game's state under `"game"`.
- `NNN-<label>.jpg` — the same moment as rendered (windowed runs only; JPEG q85, older runs have `.png`).

## Verify an animation

`-autoplay-record "kindRegex:seconds[,…]"` (+ `-autoplay-record-fps 20`, `-autoplay-record-width 480`): when a journal
event matches, the next N game seconds are recorded frame by frame into `rec-NNN-<label>/` — one downscaled PNG per
frame, `tracks.csv` (time + every measured track per frame) and `manifest.json`. During the window game time advances by
a fixed step per rendered frame (`Time.captureFramerate`, time scale 1), so the sequence does not depend on the machine.
Tracks come from the game (`IAutoplayAnimationSource.TracksFor(kind, detail)`; helpers `AutoplayTrack.Value`,
`AutoplayTrack.TransformOf`). Trigger on an event that happens **before** the animation (e.g. the action that starts it)
to capture it from its first frame.

- `Tools~/contact_sheet.py <rec-dir|run-dir> [--every N] [--track name …] [--gif]` → one labelled grid image
  (`contact.png`, + `anim.gif`): the whole animation in one look.
- Scenario check `"type": "animation"`: start / end / reach-within / change / monotonic on a track, per recording,
  filtered with `onlyIf`; add `"severity": "warn"` for observations that should be reported without failing.

## Speed (tests only, all opt-in)

- `-autoplay-fast-phases <regex>` + `-autoplay-fast-timescale X`: phases matching the regex (pure animation / recap
  phases where no bot acts) run faster; the others keep the run's time scale. Paused while a recording runs.
- Game-specific levers live in the adapter (Corruption du Portail: `-autoplay-fast-fakes`).

## Tools (`Tools~/`, ignored by the Unity importer)

- `unityctl.sh` — around the official Unity CLI (`unity`): `compile`, `playmode <filter>`, `editmode`, `build`
  (refuses if guarded settings drifted from git), `play-build [seed] [port]`, `last-run`.
- `unityctl.sh play-net <clients> [seed] [port]` — host + N **real network clients** (separate processes of the same
  build, loopback UDP, agreed port), then `compare_runs.py` on their traces.
- `unityctl.sh prune [days] [--dry-run]` / `prune_runs.py` — retention, run automatically before `play-build` /
  `play-net`: images of PASSED runs older than N days (2, `AUTOPLAY_KEEP_IMAGES_DAYS`) go, in every checkout of the
  repository; reports, journals, logs and failed / unjudged runs stay.
- `unityctl.sh park` / `unpark` — free a finished worktree's `Library` + `Temp` (~4 GB), and re-seed it from the main
  checkout (robocopy, no full reimport) to resume.
- `compare_runs.py` — desync detector: every process journals `state.hash` (a hash of a canonical view of the
  replicated state, once each phase has settled); the host's hashes are compared phase by phase with each client's.
- `run_scenario.py <scenario.json>` — **declarative scenarios**: goal, mode (build / net), levers (player args, per
  client args), seed retries, and expectations (outcome, events, facts, roster, state values at capture points,
  desync, no unexpected errors, external analyzers) → `verdict.json` + PASS/FAIL. Format in the file's docstring.
- `campaign.py` — every scenario of a folder + N random build / network games → `summary.md` / `summary.json`
  (verdicts, failing checks, most frequent error signatures).
- `launch-net.ps1` — the multi-process launcher behind `play-net` (host window with screenshots, small client windows
  with `-autoplay-no-png`, all without focus, everything killed at the end).
- `launch-background.ps1` — starts a player **without focus** (`SW_SHOWNOACTIVATE`), passes the focused window to the
  player so it can hand focus back, waits (bounded), always kills it at the end.

## What we learned the hard way

- **Run the editor headless for automation**: `Unity.exe -batchmode -automated -projectPath … -logFile …`. In batchmode
  `EditorUtility.DisplayDialog` answers "cancel" immediately, so no modal dialog (package repair prompts, "save
  changes?", missing component prompts…) can ever block an unattended session; drive it with `unity command`.
- **But a batchmode editor never reaches the end of frame** (`WaitForEndOfFrame`, `Awaitable.EndOfFrameAsync`): any game
  flow awaiting it stalls, and no screenshot can be taken. Complete games and screenshots → **windowed dev build**.
- **Never minimize** an autoplay player: a minimized player stops rendering (black captures). The window guard keeps it
  un-focused and at the bottom of the z-order instead, never locks the cursor, and mutes the game.
- A run killed mid-game can leave its UDP socket bound in the process: the runner probes for a free port.
- Never recompile while a PlayMode run is in flight (domain reload = run killed without a report).
- **The splash screen comes first**: the window shows (and Unity activates it) before any scene script runs. An
  autoplay player stops the splash at `BeforeSplashScreen` and a native thread hands the focus back from there on;
  the guard component takes over once scripts run.
- **Start clients only once the host is ready**: a client that joins while the host is still loading its game scene
  gets a broken scene synchronization and hangs. The runner records `session.ready` after the Host step and
  `launch-net.ps1` waits for the host's before starting any client.

## Extend it

A goal autoplay does not cover yet? Add a lever, an event or a check rather than handing it back to a human playtest:
`EXTENDING.md` (where code goes, building blocks, rules, definition of done).
