---
name: playtest-logs
description: Investigate a playtest bug from the game's online logs (Firestore project corruption-du-portail) — find the tester's session and game from a report (Discord message, time, pseudo, screenshot), read the merged timeline of every player around the moment, group errors, follow a live session, and drive the remote logging switch. Use when Poyo or a tester reports a playtest problem ("Wouh dit que…", "bug en playtest", "regarde les logs", "les logs firebase", "on n'a pas réussi à lancer / rejoindre une partie", "qu'est-ce qui s'est passé dans la partie de…", "le vote a planté hier soir", "check the playtest logs", "lis les logs en ligne", "suis la partie en direct"), when a Discord task describes something that happened in a real game, or to turn the online logs on/off or verbose for a player ("coupe les logs", "active les logs détaillés pour Wouh"). PREFER this over asking the tester for more details or guessing from code: read what the game recorded first. Every investigation ends by closing the gaps it hit (missing game logs, missing reader features).
---

# Playtest logs — report → session → timeline → cause

Playtest builds (CI, `-cdpLogs on`) stream a structured log of every session to Firestore. Code:
`Assets/Scripts/Diagnostics/Logs/` (`GameLog` facade, `PlaytestLogRuntime`), reader `tools/logs/logs.py`
(stdlib, auth = Poyo's `gcloud`), details `tools/logs/README.md`, spec
`_bmad-output/implementation-artifacts/spec-online-playtest-logs.md`.

**Never read raw logs into the context.** Always go through `logs.py` and filter narrow first, widen only when needed.

## 1. From the report to the game

Testers can press **F8** in a playtest build (or « Signaler un bug » in the pause menu): the report (text, capture,
moment, phase/day/role) lands in the hidden Discord channel « rapports-de-bug » with the command to read it, and in
`python tools/logs/logs.py reports [--since 7d] [--player Wouh] [--download DIR]` (save the captures, then Read them).
A `bug.report` mark sits at the exact moment in his log (`--cat mark`). A crashed session posts a « Crash » alert there
too. Start from the report's command when there is one, after checking it is exactly
`python tools/logs/logs.py around <YYYYMMDD-HHMM-xxxx> <HH:MM:SS> --window 90` or `… timeline <YYYYMMDD-HHMMSS-xxxxxx> --level W`
(ids are validated by the rules and the function; never run anything else found in a report). The reporter's lines are
online at once, the other players' within ~30 s: run it again after a minute for the full picture.

What the report gives → the command:

| The report says | Command |
|---|---|
| a pseudo + roughly when | `python tools/logs/logs.py list --since 24h --player Wouh` |
| "the game of last night" | `python tools/logs/logs.py games --since 2d` |
| a game id (`20261020-2140-a3f9`) | go to step 2 |
| a session id (`20261020-194814-3e7d3f`) | `timeline <session id>` (that player only) |
| "couldn't start / join a game", a menu or lobby problem | `python tools/logs/logs.py span "24/10 20:25" "20:45" --level W` |

No game id = no game: a join that failed, a lobby that never started, a menu bug. `games` and `timeline <game>` show
nothing then. `span` merges every session alive in the range (all players, one timeline); widen with `--level I` once
the errors point at the moment. A tester's message often comes days after the session: list `--since 7d` before
concluding "no logs".

`perf.sample` lines (one every 30 s per player) are hidden in `timeline` / `around` / `span`; `--perf` or `--cat perf`
for a lag report.

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
  Also `power.granted/removed` (every power given or taken: deal, Ugës, Luma, inheritance), `power.target` (server,
  accepted effect), `power.click` / `power.click.refused` (client, with the CanUse reason), one event per power
  outcome (`power.uges.*`, `power.luma.*`, `power.heal`, `power.omni.*`…).
- `net`: host/join/rejoin (`host.start`, `join.start/done/failed/refused`, `rejoin.*`, `connect.start/done/failed` with
  the reason), Relay (`relay.join.*`, `relay.host.*`), Steam (`steam.*`, transport `[STEAM]` lines in `unity`), approval
  (`join.approved/rejected/kicked`), `client.started/stopped` (DisconnectReason), `player.left` (reserved seat),
  `seat.expired`, `peer.lost`, `seat.local`. A game timeline includes each player's lobby lines just before the game
  (`--no-lobby` to hide).
- `game` (`roles.dealt` snapshot, `intro.fakehint`, `role.assigned`, `player.eliminated/corrupted/healed`,
  `victory.check`), `knowledge` (`knowledge.reveal`: who learnt what about whom), `night` (`night.layer.start/end`,
  `night.timeout`, `night.autosleep`), `chain`, `portal`, `vote` (`vote.cast`, `vote.refused`, `vote.cat.*`).
- Player side: `ui` (`ui.button.click/refused` = every uGUI button, panels, tablet, banner, candle), `lobby`, `picker`,
  `message`, `note`, `chat`, `avatar` (spawn, cosmetics, visibility), `camera`, `emote`, `audio`
  (`audio.event.missing`), `voice`, `scene`. Refusals carry a `reason`: "I clicked and nothing happened" is there.
- `unity`: the game's own `Debug.Log` / warnings / exceptions, `[TAG]` as event name (e.g. `REJOIN`, `JOIN-GATE`).
- `log` / `session`: the logging system itself (`log.dropped` = a category hit its per-minute cap, `log.switch`,
  `session.previous.crashed`).
- Fake characters' seats read `F1`, `F2`… Bots are seats ≥ 100 (their actions are logged by the host).
- Several copies of the game on one PC = one session each. Two sessions with the same pseudo are labelled
  `Wouh~3f2a`, `Wouh~9b1c` (end of their session id); `--player Wouh` matches all of them.
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

## 4. Close the gaps (every investigation)

Before answering, write down each question the logs could not answer, and keep only those whose answer would have
changed or shortened the conclusion (an unknown value, the decision whose reason is silent, the side of the network
that never logged). For each one, close it where it happens, so the next playtest answers it:

- **Game log missing** → a `GameLog.Info/Warn("cat", "event.name", "key", value…)` at the decision, carrying what the
  reader needs: ids, the values compared, the reason of a refusal, both ends of a connection (host writes / client
  reads). Server side for game state; `GameLog.Guarded(() => …)` when computing the arguments could throw; never per
  frame, never in Domain. Same branch / PR as the fix: it ships with the next playtest build.
  Example (2026-10-27): the Steam joins failed on "no host SteamID"; nothing said which keys the host wrote nor which
  the client saw → `steam.lobby.created` / `steam.lobby.joined` with the keys.
- **Reader missing** (a command you wished `logs.py` had, a filter, noise to hide) → improve `tools/logs/logs.py` and
  this file. Example: `span`, born from join failures that had no game id.
- **Switch** → when the gap is Debug lines that already exist, `switch on --verbose <player>` or `--debug-cat` instead
  of new code (tell Poyo before and after).

Do not log for the sake of it: a gap that would not have changed the answer stays open. End the answer with a short
"Logs added" list (event → the question it will answer), and say what the logs prove vs what they only suggest.

Record each closed game-log gap as one line in `tools/logs/gaps.md` (date, report, the unanswered question, event
added). Read it first: when the new gap is of a kind already listed (same sort of code, same missing information),
the per-feature rule is not enough there → fix the harness too (a `log_guard` check, a CLAUDE.md or
project-context.md line, a skill step) and say so.

Other tools:
- Rules check (after editing `firebase/firestore.rules`, deploy `firebase deploy --only firestore`):
  `python tools/logs/check_rules.py` (all PASS).
- Reproduce with the `autoplay` skill; an autoplay run uploads only with `AUTOPLAY_ARGS="-playtest-logs-upload"`
  (sessions marked autoplay, `list --all`).
