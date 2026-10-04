---
name: autoplay
description: Play Corruption du Portail automatically with bots (host + 7 simulated players, real GameScene, real power / vote / picker code paths) to check a precise goal, with screenshots and exported state at the right moments, then answer with evidence. Use when the user asks to test / check / verify / playtest / reproduce something in-game automatically ("check que tous les pouvoirs…", "vérifie que le flou…", "lance une partie auto", "fais jouer des bots", "autoplay", "teste en jeu", "est-ce que ce feedback s'affiche quand…", "simule une partie"). PREFER this over manual play mode, MCP play-mode driving or ad-hoc scripts for any in-game behaviour or visual check.
---

# Autoplay — goal → bots play → evidence → answer

Framework: package `Packages/com.unpseudo.autoplay` (generic: runner, journal, captures + state export, dev-build
bootstrap, window guard, `Tools~/`) + the game adapter `Assets/Scripts/Autoplay/` (`CdpAutoplayGame`,
`AutoplayDriver` bot brain, `AutoplaySelectionAutopilot`). R&D + findings:
`_bmad-output/implementation-artifacts/investigations/autoplay-automated-games-rnd.md`. Read the package README
once if you have not.

## 1. Turn the request into a goal you can check

Write down, before running anything: **what must be observed, when, on whose screen, and what counts as pass/fail**.
Map it to what the run produces:
- game logic / flow / errors → `report.json` (outcome, phase trace, counters, errors) + `events.ndjson`
- exact values at a moment → the capture's state file (`NNN-label.json`, game state under `"game"`, probes)
- visuals → the capture PNGs at that moment (bursts give several timings of one moment)

Moments captured out of the box: every phase change, every power verdict (burst 0.15/0.5/1.2 s), and in
`visual-picker` mode every picker opening (burst 0/0.1/0.25/0.5/1 s + hover). Need another moment or value? Add a
`capture.Request(...)` / `RequestBurst(...)` call or an `AddProbe(...)` in the driver — small, then rebuild.

## 2. Environment (once per session)

- Drive **your own headless editor on your checkout**, never the user's editor. Recipe + traps:
  memory `reference_own_unity_instance_batchmode` (batchmode → modal dialogs auto-cancelled, nothing on screen).
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

Players are launched **without focus**, muted, never cursor-locked (`launch-background.ps1` + `AutoplayWindowGuard`):
the user keeps working. Never minimize a player (black captures). Ports 7850–7899 only.

## 4. Read the evidence — cheaply

1. `tools/autoplay/unityctl.sh last-run` (outcome, trace, counters, deduplicated errors).
2. Goal-specific analysis over the state files (e.g. `python -X utf8 tools/autoplay/analyze_picker.py <run>`); write a
   small analyzer for a new kind of goal rather than reading hundreds of files.
3. Look at **a few targeted PNGs** only (the ones an analysis flags, plus one passing example) — never all of them.

## 5. Answer

- Verdict per goal (pass / fail / not covered), with the run folder, the key capture paths and numbers.
- Separate: **game bugs** (with root cause if proven — instrument with a tagged log before theorising),
  **tool problems** (fix them), and **design observations** (overlaps, layout — report only, design is owned by the
  game designer).
- Never commit `AutoplayRuns/` or editor import noise; stage explicit files only, and only when the user says so.
