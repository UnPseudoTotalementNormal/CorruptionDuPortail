---
paths:
  - "Assets/Scripts/**/*.cs"
  - "tools/logs/**"
---

# Journal: every behaviour change ships with its log lines

`GameLog.Info|Warn|Error("cat", "evt.name", key, value…)` (`Assets/Scripts/Diagnostics/Logs/GameLog.cs`), server side for game
state; `GameLog.Guarded(() => GameLog.Info(…))` when the arguments read game state that could throw.

Where it goes and how to read it:

```bash
python tools/logs/logs.py list --since 24h                 # sessions, newest first
python tools/logs/logs.py timeline <game id> --level W     # merged timeline of every player
python tools/logs/logs.py errors --since 24h               # errors and warnings, grouped
python tools/logs/logs.py --local timeline <session>       # autoplay / this PC's own log files
```

<!-- local:ou -->Playtest builds only: `CDP_PLAYTEST_LOGS` is added **only** by `CdpBuild` (CI playtest builds), never in ProjectSettings (the
Editor would compile the calls in); every entry point is `[Conditional]`, so the calls vanish elsewhere. Code that needs the
runtime (Firebase, file) stays inside `#if CDP_PLAYTEST_LOGS`. Lines go to Firestore (`corruption-du-portail`) by chunks; the
local seat, role, phase, day and game id are added to every line. A captured `Debug.Log("[TAG] …")` reaches the logs too
(category `unity`) but is not searchable by key. `GameManager.logGameId` is declared in every build (same NetworkVariable
layout Editor ↔ playtest build). Full rules: `_bmad-output/project-context.md` § Playtest logs, spec
`spec-online-playtest-logs.md`.<!-- /local -->

**A reported bug: read the log first, before guessing in the code.** A question the log could not settle = one more line,
added with the fix.

## What to log

- What the player asked for, with its target (a click, a key, a choice), where it is decided.
- Every decision of the program, with its reason and the values compared: branch taken or dropped, retry, fallback.
- Every silent refusal (an early `return` on a player input) with its reason: that is the "I clicked, nothing happened".
- Every error caught (`Warn`, the exception as a field), every failure (`Error`).
- Both ends of a long operation or an exchange between machines: start, then arrival with its duration.

One category (`cat`) per module, a short and stable event name (`vote.cast`, `power.refused`): that is what gets filtered.
Values as key/value fields, not glued into a sentence.

<!-- local:quoi -->Here: each player action with its target, each step of a sequence (start / end / cut short), each server decision. Never in
`Domain` (pure code).<!-- /local -->

## What is never logged

- Nothing per frame (`Update`), in a loop or on every clock tick: the buffer caps each category per minute anyway.
- Never what someone typed, a content, a request body, a key, a token, a password, an e-mail address.

## The guard

`.claude/tools/log_guard.py` (hook, settings in `log_guard.json` next to it) reminds this rule after writing a game script that
gains code with no `GameLog` call, and blocks a commit that adds ≥ 5 lines of game code (outside Editor, Tests, Autoplay,
Rendering, Facepunch, Domain, Diagnostics) with no `GameLog` call. Truly nothing to log (shader, pure refactor, visual only):
a `Logs: none (<why>)` line in the commit body.
