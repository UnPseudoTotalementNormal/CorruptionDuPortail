---
name: playtest-logs
description: Investigate a playtest bug from the game's online logs (Firestore project corruption-du-portail) — find the tester's session and game from a report (Discord message, time, pseudo, screenshot), read the merged timeline of every player around the moment, group errors, follow a live session, and drive the remote logging switch. Use when Poyo or a tester reports a playtest problem ("Wouh dit que…", "bug en playtest", "regarde les logs", "qu'est-ce qui s'est passé dans la partie de…", "le vote a planté hier soir", "check the playtest logs", "lis les logs en ligne", "suis la partie en direct"), when a Discord task describes something that happened in a real game, or to turn the online logs on/off or verbose for a player ("coupe les logs", "active les logs détaillés pour Wouh"). PREFER this over asking the tester for more details or guessing from code: read what the game recorded first.
---

# Playtest logs — report → session → timeline → cause

Playtest builds (CI, `-cdpLogs on`) stream a structured log of every session to Firestore. Code:
`Assets/Scripts/Diagnostics/Logs/` (`GameLog` facade, `PlaytestLogRuntime`), reader `tools/logs/logs.py`
(stdlib, auth = Poyo's `gcloud`), details `tools/logs/README.md`, spec
`_bmad-output/implementation-artifacts/spec-online-playtest-logs.md`.

**Never read raw logs into the context.** Always go through `logs.py` and filter narrow first, widen only when needed.

## 1. From the report to the game

What the report gives → the command:

| The report says | Command |
|---|---|
| a pseudo + roughly when | `python tools/logs/logs.py list --since 24h --player Wouh` |
| "the game of last night" | `python tools/logs/logs.py games --since 2d` |
| a game id (`20261020-2140-a3f9`) | go to step 2 |
| a session id (`20261020-194814-3e7d3f`) | `timeline <session id>` (that player only) |

`list` shows per session: start→last seen, player, flavor (relay/steam), version, host/client, `CRASH`/`end`/`live?`,
error and warning counts, game ids. A `CRASH` session is the first suspect for "the game froze / closed". Autoplay
sessions are hidden unless `--all`.

## 2. Read the game, narrow first

```bash
python tools/logs/logs.py timeline <game> --level W                    # what went wrong, every player, merged in time
python tools/logs/logs.py around <game> 21:40 --window 90              # everything ±90 s around the moment described
python tools/logs/logs.py timeline <game> --cat state,vote,power,phase # what happened in the game
python tools/logs/logs.py timeline <game> --player Wouh --cat net,unity
python tools/logs/logs.py errors --since 24h --stack                   # every error, grouped, with stacks
```

How to read a line: `21:40:12.345 Wouh#3(Abyss) I vote vote.cast from=3 to=5`
= local time, player, seat, local role, level (D/I/W/E), category, event, parameters; `×n` = repeated n times.
- `state` lines (host only): the picture at each phase start (seat, pseudo, role/faction, chained/awake/left/fake);
  `(unchanged)` when identical to the previous one (`--full` to see them all). `vote.result`, `game.end` = outcomes.
- `power`: `power.start` (owner client), `power.used` / `power.rejected` (server, with the reason), `power.verdict`,
  `effect` (every executed effect, its `ToString`).
- `net`: lobby/join (`join.approved/rejected/synchronized`), `client.started/stopped`, `player.left`, `player.rejoined`,
  `seat.local`. A game timeline includes each player's lobby lines just before the game (`--no-lobby` to hide).
- `unity`: the game's own `Debug.Log` / warnings / exceptions, `[TAG]` as event name (e.g. `REJOIN`, `JOIN-GATE`).
- `log` / `session`: the logging system itself (`log.dropped` = a category hit its per-minute cap, `log.switch`,
  `session.previous.crashed`).
- Fake characters' seats read `F1`, `F2`… Bots are seats ≥ 100.
- A PC whose clock is off is corrected automatically (noted at the top of the timeline).

Then go to the code with the evidence (event names are greppable: `GameLog.Info("vote", "vote.cast"`), and say what
the logs prove vs what they only suggest. Nothing in the logs for the moment = say so (switch off? session not
uploaded yet? not a playtest build?), never invent.

## 3. Live, and the switch

```bash
python tools/logs/logs.py tail Wouh                        # follow his current session (~20 s delay)
python tools/logs/logs.py switch                           # show config/logging
python tools/logs/logs.py switch off                       # stop every upload within a minute (waiting lines dropped)
python tools/logs/logs.py switch on --verbose Wouh         # + his Debug lines (pseudo, Steam id or session id)
python tools/logs/logs.py switch --debug-cat ui net        # + Debug lines of some categories for everyone
python tools/logs/logs.py switch --verbose                 # clear the verbose list
```

Changing the switch changes what testers send: say it to Poyo before (and after) doing it.

## 4. When the logs are not enough

- A useful fact is missing: add a `GameLog.Info/Warn("cat", "event.name", "key", value…)` where it happens (server side
  for game state; `GameLog.Guarded(() => …)` when computing the arguments could throw), never per frame, never in
  Domain. It ships with the next playtest build.
- Rules check (after editing `firebase/firestore.rules`, deploy `firebase deploy --only firestore`):
  `python tools/logs/check_rules.py` (all PASS).
- Reproduce with the `autoplay` skill; an autoplay run uploads only with `AUTOPLAY_ARGS="-playtest-logs-upload"`
  (sessions marked autoplay, `list --all`).
