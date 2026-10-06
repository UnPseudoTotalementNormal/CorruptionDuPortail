# TimeRecorder: AI working time

The Claude Code hooks (`.claude/hooks/claude_time_*.ps1`) and the Codex hooks (`.codex/hooks/`) book AI working
time. The Unity calendar (`Tools > Time recorder > Time Calendar`) shows it, and `tr.py` queries and corrects it.

All data is machine-local and gitignored, in the **main checkout's** `.claude/timerecorder/`. Sessions running in a
git worktree book there too (`Resolve-MainCheckout`), so nothing dies with a worktree.

| File | Written by | Content |
|---|---|---|
| `claude_time.json` | hooks | seconds per day, all history (pre-journal days only have this) |
| `claude_intervals.jsonl` | hooks | one line per booked span: start/end, src (claude/codex), kind (turn/gap), trigger (prompt/background), branch, worktree, prompt, model, skills, tags, tools, commands, files |
| `claude_adjustments.jsonl` | `tr.py` | corrections: a factor on a fixed list of span ids, with a reason; revocable |
| `dev_intervals.jsonl` | TimeRecorder (Unity) | dev spans: one line per 5-min period with the editor open, with branch and checkout |
| `claude_invoices.jsonl` | Unity calendar | billing marks: "Mark invoiced" instants, with the billable / AI / dev amounts of the period they close (frozen); revocable |
| `ai_paused.flag` | Unity calendar | while present, nothing is booked |

## Billable time and the billing counter

**Billable = AI time + dev time merged, an instant counted once** (Claude working while the editor is open counts
once). It is the main figure of the calendar: cards, cells (gold value), week column, day detail.

- AI spans (`claude_intervals.jsonl`) and dev spans (`dev_intervals.jsonl`, one line per 5-min period written by
  TimeRecorder while the editor is open and the dev recorder runs) merge exactly (`BillableTime.cs`).
- Time without timestamps (days booked before these journals; a dev day edited by hand below its spans) can't be
  merged: the larger of its AI and dev parts counts, shown with `≈` (estimated).
- The **Since last invoice** card shows billable time since the last **Mark invoiced** click (or since the
  beginning), in h/min and decimal hours, with AI and dev below. Click it right after giving your hours to the
  client; **History** lists past marks (amounts frozen at the mark) and can undo the last one. Spans are clipped
  exactly at the mark; untimed time counts for the days strictly after the mark's day.
- `tr.py billing` prints the merged AI + dev spans since the last mark and the mark history (untimed dev day totals
  live in Unity PlayerPrefs: the calendar is the reference).
- The dev recorder can be paused (toolbar "Dev paused"): nothing is booked, neither day totals nor spans.

## How a day is counted

`day total = (ledger day - raw seconds journaled that day) + union of the day's spans`

- The union counts parallel sessions once: two sessions working 17:30-17:35 = 5 min.
- With corrections, each instant counts at the highest factor among the spans covering it.
- `tr.py` and `ClaudeTimeline.cs` implement the same math; change both together.

Spans: `turn` = from the prompt (or the previous hook) to the end of the AI response, capped at 30 min; `gap` =
reading/typing between a response and the next prompt, dropped above 15 min. `trigger=background` = a turn nobody
prompted (task notification wake-up), e.g. waiting on an autoplay run.

Tags come from the turn's transcript: `autoplay` (autoplay skill or game launch), `unity-tests`, `unity-build`,
`unity-editor`, `git`, `discord`, `code` (.cs edited), `ui` (.uxml/.uss), `docs` (.md), `tooling`, `assets`,
`subagents`, `web`, `research` (read/search only), `chat` (no tool).

## tr.py

```bash
python tools/timerecorder/tr.py days --since 7d
python tools/timerecorder/tr.py list --since today -v                 # spans with prompt, commands, files
python tools/timerecorder/tr.py summary --since 30d --by branch       # or --by tag|day|worktree|src|trigger|model
python tools/timerecorder/tr.py adjust --since 3d --tag autoplay --factor 0.5 --reason "autoplay halved"   # dry run
python tools/timerecorder/tr.py adjust ... --yes                      # write it
python tools/timerecorder/tr.py adjustments                           # list corrections
python tools/timerecorder/tr.py undo <id> --reason "..." --yes        # revoke one
```

Filters: `--since/--until` (`today`, `yesterday`, `3d` = today and the 2 days before, `12h`, `2026-10-06`,
`"2026-10-06 17:30"`), `--tag` (any), `--all-tags`, `--no-tag`, `--branch`, `--worktree`, `--src`, `--kind`,
`--trigger`, `--skill`, `--text` (prompt/commands/files), `--session`.

Always show the dry run to the user and get a yes before `--yes`: this data decides pay.
