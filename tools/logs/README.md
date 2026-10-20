# Playtest logs

Playtest builds stream a structured log of every session to Firestore (Firebase project `corruption-du-portail`,
europe-west1). Code: `Assets/Scripts/Diagnostics/Logs/` (`GameLog` facade, `PlaytestLogRuntime`), spec
`_bmad-output/implementation-artifacts/spec-online-playtest-logs.md`.

## Investigating a report (Claude's recipe)

Claude: use the `playtest-logs` skill (`.claude/skills/playtest-logs/SKILL.md`), it carries the full recipe.

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

The switch is checked every ~50 s and before each chunk. Missing, unreadable content or unreachable for 3 minutes =
off (local file only). Lines logged while it is off never go online, not even later.

Builds: `CdpBuild` requires `-cdpLogs on` (playtest: define `CDP_PLAYTEST_LOGS`) or `-cdpLogs off` (public: every
`GameLog` call, the Firebase URL and key are compiled out); anything else fails the build. CI (Build.yml) passes
`-cdpLogs on` today: a public build job must pass `-cdpLogs off`. Logs expire after 60 days (Firestore TTL).

Security rules (`firebase/firestore.rules`): a player writes only its own session (known fields, bounded sizes, expiry
≤ 61 days), chunks are write-once (a resent chunk answers ALREADY_EXISTS), nothing is readable but `config/logging`.
Check them against the live project with `python tools/logs/check_rules.py`.

## Where things are

- Player PC: `%USERPROFILE%/AppData/LocalLow/<company>/<product>/logs/<session>.ndjson` (last 10 sessions, up to 30
  while some still wait to go online) + `.meta.json` (upload cursor, chunk in flight, anonymous uid) + `auth.json` (one
  anonymous account per PC, shared by every copy of the game). A session that did not end cleanly uploads at the next
  launch, marked crashed; a local-only run (autoplay) never does.
- Firestore: `sessions/{sid}` (index: player, version, flavor, host, gameIds, errors, crashed…) and
  `sessions/{sid}/chunks/{seq}` (`lines` = NDJSON). Rules and indexes: `firebase/` (`firebase deploy --only firestore`).
- The Editor never logs online; an autoplay run uploads only with `-playtest-logs-upload` (sessions marked autoplay,
  hidden from `list` unless `--all`).

## Adding a log

`GameLog.Info("category", "event.name", "key", value, ...)` at the place where the thing happens (server side for game
decisions). Never per frame, never in Domain. `GameLog.Verbose` for detail that only goes online for verbose players.
