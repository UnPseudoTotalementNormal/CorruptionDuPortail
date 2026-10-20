# Playtest logs

Playtest builds stream a structured log of every session to Firestore (Firebase project `corruption-du-portail`,
europe-west1). Code: `Assets/Scripts/Diagnostics/Logs/` (`GameLog` facade, `PlaytestLogRuntime`), spec
`_bmad-output/implementation-artifacts/spec-online-playtest-logs.md`.

## Investigating a report (Claude's recipe)

```bash
python tools/logs/logs.py list --since 24h --player Wouh   # his sessions: version, crash, error counts, game ids
python tools/logs/logs.py games --since 2d                  # games and who played them
python tools/logs/logs.py timeline <game id> --level W      # warnings/errors of every player, merged in time
python tools/logs/logs.py around <game id> 21:40 --window 90  # everything ±90 s around the moment he describes
python tools/logs/logs.py timeline <game id> --cat vote,power,state  # what happened in the game
python tools/logs/logs.py errors --since 24h --stack         # every error, grouped, with stacks
python tools/logs/logs.py tail Wouh                          # follow a live session (~20 s delay)
```

Read narrow first (`--level W`, `--cat`, `around`), widen only when needed. `state` lines are the host's picture at each
phase start (seats, roles, factions, chained/awake/left); `unity` lines are the game's own `Debug.Log`, `[TAG]` as event.

## Switch (instant, no rebuild)

```bash
python tools/logs/logs.py switch            # show config/logging
python tools/logs/logs.py switch off        # stop every upload within a minute, waiting lines dropped
python tools/logs/logs.py switch on --verbose Wouh   # + Debug lines of one player
python tools/logs/logs.py switch --debug-cat ui net  # + Debug lines of some categories for everyone
```

Public build: CI passes `-cdpLogs off` to `CdpBuild` (Build.yml `customParameters`): the define `CDP_PLAYTEST_LOGS` is
not set, every `GameLog` call, the Firebase URL and key are compiled out. Logs expire after 60 days (Firestore TTL).

## Where things are

- Player PC: `%USERPROFILE%/AppData/LocalLow/<company>/<product>/logs/<session>.ndjson` (last 10 sessions) + `.meta.json`
  (upload cursor). A session that did not end cleanly uploads at the next launch, marked crashed.
- Firestore: `sessions/{sid}` (index: player, version, flavor, host, gameIds, errors, crashed…) and
  `sessions/{sid}/chunks/{seq}` (`lines` = NDJSON). Rules and indexes: `firebase/` (`firebase deploy --only firestore`).
- The Editor never logs online; an autoplay run uploads only with `-playtest-logs-upload` (sessions marked autoplay,
  hidden from `list` unless `--all`).

## Adding a log

`GameLog.Info("category", "event.name", "key", value, ...)` at the place where the thing happens (server side for game
decisions). Never per frame, never in Domain. `GameLog.Verbose` for detail that only goes online for verbose players.
