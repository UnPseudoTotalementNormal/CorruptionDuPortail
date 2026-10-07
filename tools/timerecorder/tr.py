#!/usr/bin/env python3
"""TimeRecorder query & correction tool for the AI working-time journal.

Data (machine-local, gitignored) lives in the MAIN checkout's .claude/timerecorder/:
  claude_time.json         day totals booked by the hooks (all history)
  claude_intervals.jsonl   one line per booked span: start/end, branch, worktree,
                           prompt, skills, tools, commands, files, tags (append-only)
  claude_adjustments.jsonl corrections: a factor applied to a fixed list of span ids,
                           with a reason; revocable. Raw spans are never edited.
  claude_invoices.jsonl    billing marks ("Mark as invoiced" in the Unity calendar)

Day total = (ledger day - raw seconds journaled that day)   <- time booked before the journal
          + union of the day's spans, each instant weighted by the highest factor
            among the spans covering it (parallel sessions count once).
The Unity calendar (ClaudeTimeReader) computes the same thing.

Examples:
  tr.py days --since 7d
  tr.py list --since today --tag autoplay
  tr.py summary --since 30d --by branch
  tr.py adjust --since 3d --tag autoplay --factor 0.5 --reason "autoplay halved"        (dry run)
  tr.py adjust --since 3d --tag autoplay --factor 0.5 --reason "autoplay halved" --yes  (write)
  tr.py adjustments
  tr.py undo <adjustment-id> --reason "..." --yes
  tr.py billing                     AI time since the last invoice mark + mark history
"""
import argparse
import datetime as dt
import json
import os
import re
import subprocess
import sys
import time
import uuid
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

INTERVALS = "claude_intervals.jsonl"
ADJUSTMENTS = "claude_adjustments.jsonl"
INVOICES = "claude_invoices.jsonl"
DEV_SPANS = "dev_intervals.jsonl"
LEDGER = "claude_time.json"


# ---------------------------------------------------------------- data access

def main_checkout_dir():
    """Main checkout of the repo this script lives in (worktrees included)."""
    here = os.path.dirname(os.path.abspath(__file__))
    try:
        common = subprocess.check_output(
            ["git", "-C", here, "rev-parse", "--path-format=absolute", "--git-common-dir"],
            text=True, stderr=subprocess.DEVNULL).strip()
        return os.path.dirname(os.path.normpath(common))
    except Exception:
        return os.path.normpath(os.path.join(here, "..", ".."))


def read_jsonl(path):
    out = []
    if not os.path.exists(path):
        return out
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except json.JSONDecodeError:
                print(f"warning: {os.path.basename(path)} line {n} unreadable, skipped", file=sys.stderr)
    return out


def load(data_dir):
    ledger = {}
    lp = os.path.join(data_dir, LEDGER)
    if os.path.exists(lp):
        with open(lp, encoding="utf-8") as f:
            for e in json.load(f).get("days", []):
                k = dt.date(int(e["year"]), int(e["month"]), int(e["day"]))
                ledger[k] = ledger.get(k, 0) + int(e["seconds"])
    spans = [s for s in read_jsonl(os.path.join(data_dir, INTERVALS)) if s.get("sec", 0) > 0]
    adjs = read_jsonl(os.path.join(data_dir, ADJUSTMENTS))
    return ledger, spans, adjs


def effective_factors(adjs):
    """span id -> factor. Adjustments replay in file order (last wins); revoked ones are skipped."""
    revoked = {a["revoke"] for a in adjs if a.get("revoke")}
    factors = {}
    for a in adjs:
        if a.get("revoke") or a.get("id") in revoked:
            continue
        for sid in a.get("ids", []):
            factors[sid] = float(a["factor"])
    return factors


# ---------------------------------------------------------------- time math

def local(ms):
    return dt.datetime.fromtimestamp(ms / 1000.0)


def day_of_end(span):
    # The hooks book a span into the local day it ENDS on (Get-Date at accrual).
    return local(span["endMs"] - 1).date()


def day_bounds_ms(day):
    start = dt.datetime.combine(day, dt.time())
    return int(start.timestamp() * 1000), int((start + dt.timedelta(days=1)).timestamp() * 1000)


def weighted_union_ms(spans, factors, lo=None, hi=None):
    """Union of spans (clipped to [lo, hi)), each instant weighted by the max factor covering it."""
    events = []
    for s in spans:
        a, b = s["startMs"], s["endMs"]
        if lo is not None:
            a = max(a, lo)
        if hi is not None:
            b = min(b, hi)
        if b <= a:
            continue
        f = factors.get(s["id"], 1.0)
        events.append((a, 1, f))
        events.append((b, -1, f))
    events.sort(key=lambda e: (e[0], e[1]))
    active = defaultdict(int)  # factor -> count
    total, prev = 0.0, None
    for t, kind, f in events:
        if prev is not None and active:
            total += (t - prev) * max(active)
        if kind == 1:
            active[f] += 1
        else:
            active[f] -= 1
            if active[f] == 0:
                del active[f]
        prev = t
    return total


def day_totals(ledger, spans, factors, days):
    """day -> dict(legacy, raw_union, adjusted_union, total) in seconds."""
    journaled_by_end_day = defaultdict(int)
    for s in spans:
        journaled_by_end_day[day_of_end(s)] += int(s["sec"])
    res = {}
    for d in days:
        lo, hi = day_bounds_ms(d)
        in_day = [s for s in spans if s["endMs"] > lo and s["startMs"] < hi]
        legacy = max(0, ledger.get(d, 0) - journaled_by_end_day.get(d, 0))
        raw = weighted_union_ms(in_day, {}, lo, hi) / 1000.0
        adj = weighted_union_ms(in_day, factors, lo, hi) / 1000.0
        res[d] = dict(legacy=legacy, raw=raw, adjusted=adj, total=legacy + adj)
    return res


def fmt(sec):
    sec = int(round(sec))
    h, m = divmod(sec // 60, 60)
    if h:
        return f"{h}h{m:02d}"
    if m:
        return f"{m}min"
    return f"{sec}s"


# ---------------------------------------------------------------- filters

def parse_when(text, end=False):
    t = text.strip().lower()
    now = dt.datetime.now()
    if t in ("today", "aujourdhui", "aujourd'hui"):
        d = now.date()
        return dt.datetime.combine(d + dt.timedelta(days=1) if end else d, dt.time())
    if t in ("yesterday", "hier"):
        d = now.date() - dt.timedelta(days=1)
        return dt.datetime.combine(d + dt.timedelta(days=1) if end else d, dt.time())
    m = re.fullmatch(r"(\d+)\s*([dhm])", t)
    if m:
        n, u = int(m.group(1)), m.group(2)
        if u == "d":  # "3d" = today and the 2 days before, from midnight
            return dt.datetime.combine(now.date() - dt.timedelta(days=n - 1), dt.time())
        return now - (dt.timedelta(hours=n) if u == "h" else dt.timedelta(minutes=n))
    for f in ("%Y-%m-%d %H:%M", "%Y-%m-%d"):
        try:
            v = dt.datetime.strptime(text.strip(), f)
            if end and f == "%Y-%m-%d":
                v += dt.timedelta(days=1)
            return v
        except ValueError:
            pass
    raise SystemExit(f"bad date '{text}' (use today, yesterday, 3d, 12h, 2026-10-06, '2026-10-06 17:30')")


def add_filters(p):
    p.add_argument("--since", help="today | yesterday | 3d (today + 2 days before) | 12h | YYYY-MM-DD[ HH:MM]")
    p.add_argument("--until", help="same formats, exclusive")
    p.add_argument("--tag", action="append", default=[], help="span has ANY of these tags (repeatable)")
    p.add_argument("--all-tags", action="append", default=[], help="span has ALL of these tags")
    p.add_argument("--no-tag", action="append", default=[], help="exclude spans with this tag")
    p.add_argument("--branch", help="substring of the branch name")
    p.add_argument("--worktree", help="substring of the worktree name ('main' = main checkout)")
    p.add_argument("--src", choices=["claude", "codex"])
    p.add_argument("--kind", choices=["turn", "gap"])
    p.add_argument("--trigger", choices=["prompt", "background"])
    p.add_argument("--skill", help="skill used in the turn (substring)")
    p.add_argument("--text", help="substring searched in prompt, commands and files (case-insensitive)")
    p.add_argument("--session", help="session id prefix")


def match(s, a):
    if a.since and s["endMs"] <= parse_when(a.since).timestamp() * 1000:
        return False
    if a.until and s["startMs"] >= parse_when(a.until, end=True).timestamp() * 1000:
        return False
    tags = set(s.get("tags") or [])
    if a.tag and not tags.intersection(a.tag):
        return False
    if a.all_tags and not set(a.all_tags).issubset(tags):
        return False
    if a.no_tag and tags.intersection(a.no_tag):
        return False
    if a.branch and a.branch.lower() not in (s.get("branch") or "").lower():
        return False
    if a.worktree and a.worktree.lower() not in (s.get("worktree") or "").lower():
        return False
    for key in ("src", "kind", "trigger"):
        if getattr(a, key) and s.get(key) != getattr(a, key):
            return False
    if a.skill and not any(a.skill.lower() in k.lower() for k in s.get("skills") or []):
        return False
    if a.session and not (s.get("session") or "").startswith(a.session):
        return False
    if a.text:
        hay = " ".join([s.get("prompt") or ""] + (s.get("commands") or []) + (s.get("files") or [])).lower()
        if a.text.lower() not in hay:
            return False
    return True


def describe_filters(a):
    parts = []
    for key in ("since", "until", "branch", "worktree", "src", "kind", "trigger", "skill", "text", "session"):
        v = getattr(a, key, None)
        if v:
            parts.append(f"{key}={v}")
    for key in ("tag", "all_tags", "no_tag"):
        for v in getattr(a, key, []) or []:
            parts.append(f"{key}={v}")
    return " ".join(parts) or "(all spans)"


# ---------------------------------------------------------------- commands

def cmd_days(a, ledger, spans, factors):
    if a.since:
        first = parse_when(a.since).date()
    else:
        first = min(list(ledger) + [day_of_end(s) for s in spans] or [dt.date.today()])
    last = (parse_when(a.until, end=True) - dt.timedelta(seconds=1)).date() if a.until else dt.date.today()
    days, d = [], first
    while d <= last:
        days.append(d)
        d += dt.timedelta(days=1)
    res = day_totals(ledger, spans, factors, days)
    print(f"{'day':<12}{'before-journal':>15}{'spans raw':>11}{'spans adj':>11}{'TOTAL':>9}")
    grand = 0
    for d in days:
        r = res[d]
        if r["total"] <= 0 and r["raw"] <= 0:
            continue
        grand += r["total"]
        print(f"{d.isoformat():<12}{fmt(r['legacy']):>15}{fmt(r['raw']):>11}{fmt(r['adjusted']):>11}{fmt(r['total']):>9}")
    print(f"{'':<12}{'':>15}{'':>11}{'total':>11}{fmt(grand):>9}")


def cmd_list(a, ledger, spans, factors):
    sel = sorted((s for s in spans if match(s, a)), key=lambda s: s["startMs"])
    for s in sel:
        f = factors.get(s["id"], 1.0)
        adj = "" if f == 1.0 else f" x{f:g}"
        flags = (" CLAMPED" if s.get("clamped") else "") + (" bg" if s.get("trigger") == "background" else "")
        print(f"{local(s['startMs']):%Y-%m-%d %H:%M}-{local(s['endMs']):%H:%M} {fmt(s['sec']):>6}{adj} "
              f"{s.get('src', '?')}/{s.get('kind', '?')}{flags}  [{s.get('branch', '')}] "
              f"{','.join(s.get('tags') or [])}  id={s['id'][:8]}")
        if a.verbose:
            if s.get("prompt"):
                print(f"      prompt: {s['prompt']}")
            if s.get("skills"):
                print(f"      skills: {', '.join(s['skills'])}")
            for c in s.get("commands") or []:
                print(f"      $ {c}")
            if s.get("files"):
                print(f"      files: {', '.join(s['files'])}")
    print(f"{len(sel)} spans, raw sum {fmt(sum(s['sec'] for s in sel))}, "
          f"union raw {fmt(weighted_union_ms(sel, {}) / 1000)}, union adjusted {fmt(weighted_union_ms(sel, factors) / 1000)}")


def cmd_summary(a, ledger, spans, factors):
    sel = [s for s in spans if match(s, a)]
    groups = defaultdict(list)
    for s in sel:
        if a.by == "tag":
            for t in (s.get("tags") or ["(none)"]):
                groups[t].append(s)
        elif a.by == "day":
            groups[day_of_end(s).isoformat()].append(s)
        else:
            groups[s.get(a.by) or "(none)"].append(s)
    rows = [(k, weighted_union_ms(v, {}) / 1000, weighted_union_ms(v, factors) / 1000, len(v)) for k, v in groups.items()]
    rows.sort(key=lambda r: -r[2])
    print(f"{a.by:<44}{'raw':>9}{'adjusted':>10}{'spans':>7}")
    for k, raw, adj, n in rows:
        print(f"{str(k)[:43]:<44}{fmt(raw):>9}{fmt(adj):>10}{n:>7}")
    if a.by == "tag":
        print("(a span with several tags counts in each; groups overlap)")


def cmd_adjust(a, ledger, spans, factors, data_dir):
    if a.factor < 0:
        raise SystemExit("factor must be >= 0 (0 = drop, 0.5 = halve, 1 = restore)")
    sel = [s for s in spans if match(s, a)]
    if not sel:
        print("no span matches; nothing to do")
        return
    new_factors = dict(factors)
    for s in sel:
        new_factors[s["id"]] = a.factor
    days = sorted({day_of_end(s) for s in sel} | {local(s["startMs"]).date() for s in sel})
    before = day_totals(ledger, spans, factors, days)
    after = day_totals(ledger, spans, new_factors, days)
    print(f"filter: {describe_filters(a)}")
    print(f"{len(sel)} spans, raw sum {fmt(sum(s['sec'] for s in sel))} -> factor {a.factor:g}")
    print(f"{'day':<12}{'before':>9}{'after':>9}{'delta':>9}")
    delta_all = 0
    for d in days:
        b, af = before[d]["total"], after[d]["total"]
        delta_all += af - b
        print(f"{d.isoformat():<12}{fmt(b):>9}{fmt(af):>9}{('-' if af < b else '+') + fmt(abs(af - b)):>9}")
    print(f"total delta: {('-' if delta_all < 0 else '+')}{fmt(abs(delta_all))}")
    if not a.yes:
        print("dry run: add --yes to write this adjustment")
        return
    rec = {"v": 1, "id": uuid.uuid4().hex, "atMs": int(time.time() * 1000), "factor": a.factor,
           "reason": a.reason, "filter": describe_filters(a), "ids": [s["id"] for s in sel]}
    with open(os.path.join(data_dir, ADJUSTMENTS), "a", encoding="utf-8") as f:
        f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    print(f"written: adjustment {rec['id']}")


def active_invoices(data_dir):
    rows = read_jsonl(os.path.join(data_dir, INVOICES))
    revoked = {r["revoke"] for r in rows if r.get("revoke")}
    marks = [r for r in rows if not r.get("revoke") and r.get("id") not in revoked]
    return sorted(marks, key=lambda r: r["atMs"])


def ai_seconds_between(from_ms, to_ms, ledger, spans, factors):
    """Spans clipped to [from, to) + pre-journal totals of the days strictly after from's day (ClaudeTimeline.AiSecondsBetween)."""
    total = weighted_union_ms(spans, factors, from_ms, to_ms) / 1000.0
    first, last = local(from_ms).date() + dt.timedelta(days=1), local(to_ms - 1).date()
    if first <= last:
        days, d = [], first
        while d <= last:
            days.append(d)
            d += dt.timedelta(days=1)
        res = day_totals(ledger, spans, factors, days)
        total += sum(res[d]["legacy"] for d in days)
    return total


def cmd_billing(a, ledger, spans, factors, data_dir):
    marks = active_invoices(data_dir)
    dev_spans = [s for s in read_jsonl(os.path.join(data_dir, DEV_SPANS)) if s.get("sec", 0) > 0]
    now = int(time.time() * 1000)
    if marks:
        last = marks[-1]
        ai = ai_seconds_between(last["atMs"], now, ledger, spans, factors)
        merged = weighted_union_ms(spans + dev_spans, factors, last["atMs"], now) / 1000.0
        legacy = ai_seconds_between(last["atMs"], now, ledger, [], factors)  # untimed AI of the full days after the mark
        bill = merged + legacy
        print(f"since last invoice mark ({local(last['atMs']):%Y-%m-%d %H:%M}):")
        print(f"  billable {fmt(bill)} = {bill / 3600:.2f} h   (AI {fmt(ai)}, AI + dev spans merged)")
    else:
        print("never marked: use 'Mark invoiced' in the Unity calendar")
    print("(dev day totals without timestamps live in Unity PlayerPrefs: the calendar is the reference)")
    for m in reversed(marks):
        print(f"  {local(m['atMs']):%Y-%m-%d %H:%M}  billable {fmt(m.get('billableSeconds', 0))} ({m.get('billableSeconds', 0) / 3600:.2f} h)"
              f"  AI {fmt(m.get('aiSeconds', 0))}  dev {fmt(m.get('devSeconds', 0))}")


def cmd_adjustments(a, adjs):
    revoked = {x["revoke"]: x for x in adjs if x.get("revoke")}
    for x in adjs:
        if x.get("revoke"):
            continue
        state = f"REVOKED ({revoked[x['id']].get('reason', '')})" if x["id"] in revoked else "active"
        print(f"{local(x['atMs']):%Y-%m-%d %H:%M}  {x['id']}  x{x['factor']:g}  {len(x.get('ids', []))} spans  "
              f"[{state}]  {x.get('reason', '')}  ({x.get('filter', '')})")


def cmd_undo(a, adjs, data_dir):
    ids = [x["id"] for x in adjs if not x.get("revoke")]
    hit = [i for i in ids if i.startswith(a.adjustment)]
    if len(hit) != 1:
        raise SystemExit(f"'{a.adjustment}' matches {len(hit)} adjustments")
    if not a.yes:
        print(f"would revoke {hit[0]}; add --yes to write")
        return
    rec = {"v": 1, "id": uuid.uuid4().hex, "atMs": int(time.time() * 1000), "revoke": hit[0], "reason": a.reason}
    with open(os.path.join(data_dir, ADJUSTMENTS), "a", encoding="utf-8") as f:
        f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    print(f"revoked {hit[0]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", help="data dir (default: <main checkout>/.claude/timerecorder)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("days", help="per-day totals")
    p.add_argument("--since")
    p.add_argument("--until")
    p = sub.add_parser("list", help="list spans")
    add_filters(p)
    p.add_argument("-v", "--verbose", action="store_true", help="show prompt, skills, commands, files")
    p = sub.add_parser("summary", help="time per group (union)")
    add_filters(p)
    p.add_argument("--by", default="branch", choices=["branch", "tag", "day", "worktree", "src", "kind", "trigger", "model"])
    p = sub.add_parser("adjust", help="apply a factor to the matching spans (dry run unless --yes)")
    add_filters(p)
    p.add_argument("--factor", type=float, required=True)
    p.add_argument("--reason", required=True)
    p.add_argument("--yes", action="store_true")
    sub.add_parser("adjustments", help="list corrections")
    sub.add_parser("billing", help="AI time since the last invoice mark, and the mark history")
    p = sub.add_parser("undo", help="revoke a correction")
    p.add_argument("adjustment", help="adjustment id (prefix ok)")
    p.add_argument("--reason", required=True)
    p.add_argument("--yes", action="store_true")

    a = ap.parse_args()
    data_dir = a.dir or os.path.join(main_checkout_dir(), ".claude", "timerecorder")
    ledger, spans, adjs = load(data_dir)
    factors = effective_factors(adjs)

    if a.cmd == "days":
        cmd_days(a, ledger, spans, factors)
    elif a.cmd == "list":
        cmd_list(a, ledger, spans, factors)
    elif a.cmd == "summary":
        cmd_summary(a, ledger, spans, factors)
    elif a.cmd == "adjust":
        cmd_adjust(a, ledger, spans, factors, data_dir)
    elif a.cmd == "billing":
        cmd_billing(a, ledger, spans, factors, data_dir)
    elif a.cmd == "adjustments":
        cmd_adjustments(a, adjs)
    elif a.cmd == "undo":
        cmd_undo(a, adjs, data_dir)


if __name__ == "__main__":
    main()
