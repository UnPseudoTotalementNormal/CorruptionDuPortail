#!/usr/bin/env python3
"""Read the playtest logs (Firestore project corruption-du-portail) — the way Claude investigates a playtest bug.

Stdlib only. Auth = `gcloud auth print-access-token` (the developer's own Google account, admin access).
Chunks are immutable: they are cached in tools/logs/.cache/ (gitignored).

    python tools/logs/logs.py list   [--since 24h] [--player NAME] [--all]      sessions, newest first
    python tools/logs/logs.py games  [--since 7d]                                games and who played them
    python tools/logs/logs.py timeline <game id | session id> [filters]          merged timeline, every player
    python tools/logs/logs.py around <game | session> <HH:MM[:SS]> [--window 60] [filters]
    python tools/logs/logs.py errors [--since 24h] [--player NAME]               errors and warnings, grouped
    python tools/logs/logs.py grep <regex> [--since 24h] [--player NAME]         raw search in every line
    python tools/logs/logs.py tail <player | session id>                         follow a live session
    python tools/logs/logs.py switch [on|off] [--verbose NAME ...] [--debug-cat CAT ...] [--min-level D|I|W|E]

Filters (timeline / around): --level W (minimum level), --cat vote,power (categories), --player Wouh,
--grep REGEX, --stack (print stack traces), --full (no truncation), --json (raw NDJSON lines).

Line format: `HH:MM:SS.mmm Player#seat L cat event k=v …  ×n` in local time. `state` lines are the host's picture of
the game at each phase; `unity` lines are the game's own Debug.Log output ([TAG] as event).
"""
import argparse
import json
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path

PROJECT = "corruption-du-portail"
BASE = f"https://firestore.googleapis.com/v1/projects/{PROJECT}/databases/(default)/documents"
CACHE = Path(__file__).resolve().parent / ".cache"
LEVELS = "DIWE"

for stream in (sys.stdout, sys.stderr):
    try:
        stream.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass

_token = None


def token():
    global _token
    if _token is None:
        cmd = "gcloud auth print-access-token"
        _token = subprocess.run(cmd, shell=True, capture_output=True, text=True).stdout.strip()
        if not _token:
            sys.exit("No gcloud token: run `gcloud auth login` with the account that owns the Firebase project.")
    return _token


def http(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {token()}",
        "x-goog-user-project": PROJECT,
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        sys.exit(f"HTTP {e.code} {method} {url}\n{e.read().decode(errors='replace')[:800]}")


# ---------- Firestore values ----------

def value(v):
    if "stringValue" in v: return v["stringValue"]
    if "integerValue" in v: return int(v["integerValue"])
    if "doubleValue" in v: return v["doubleValue"]
    if "booleanValue" in v: return v["booleanValue"]
    if "timestampValue" in v: return v["timestampValue"]
    if "arrayValue" in v: return [value(x) for x in v["arrayValue"].get("values", [])]
    if "mapValue" in v: return fields(v["mapValue"])
    return None


def fields(doc):
    return {k: value(v) for k, v in (doc.get("fields") or {}).items()}


def ts(s):
    """RFC3339 → aware datetime."""
    if not s: return None
    s = s.replace("Z", "+00:00")
    if "." in s:
        head, rest = s.split(".", 1)
        frac = re.match(r"\d+", rest).group(0)
        s = head + "." + frac[:6].ljust(6, "0") + rest[len(frac):]
    return datetime.fromisoformat(s)


def local(dt):
    return dt.astimezone() if dt else None


def since_arg(text):
    m = re.fullmatch(r"(\d+)([mhd])", text or "")
    if not m: sys.exit(f"--since: expected like 30m, 24h, 7d, got {text!r}")
    n, unit = int(m.group(1)), m.group(2)
    return datetime.now(timezone.utc) - timedelta(**{"m": {"minutes": n}, "h": {"hours": n}, "d": {"days": n}}[unit])


# ---------- queries ----------

def run_query(structured, parent=BASE):
    rows = http("POST", f"{parent}:runQuery", {"structuredQuery": structured})
    return [r["document"] for r in rows if "document" in r]


def sessions_since(since, limit=300):
    docs = run_query({
        "from": [{"collectionId": "sessions"}],
        "where": {"fieldFilter": {"field": {"fieldPath": "startedAt"}, "op": "GREATER_THAN_OR_EQUAL",
                                  "value": {"timestampValue": since.strftime("%Y-%m-%dT%H:%M:%SZ")}}},
        "orderBy": [{"field": {"fieldPath": "startedAt"}, "direction": "DESCENDING"}],
        "limit": limit,
    })
    return [fields(d) for d in docs]


def session(sid):
    doc = http("GET", f"{BASE}/sessions/{urllib.parse.quote(sid)}")
    return fields(doc)


def chunks_of_session(sid, after_seq=-1):
    out, page = [], None
    while True:
        url = f"{BASE}/sessions/{urllib.parse.quote(sid)}/chunks?pageSize=300&orderBy=seq"
        if page: url += "&pageToken=" + urllib.parse.quote(page)
        r = http("GET", url)
        for d in r.get("documents", []):
            seq = int(d["name"].rsplit("/", 1)[1])
            if seq > after_seq:
                out.append(cached_chunk(sid, seq, d))
        page = r.get("nextPageToken")
        if not page: return out


def chunks_of_game(game):
    docs = run_query({
        "from": [{"collectionId": "chunks", "allDescendants": True}],
        "where": {"fieldFilter": {"field": {"fieldPath": "gameIds"}, "op": "ARRAY_CONTAINS",
                                  "value": {"stringValue": game}}},
    })
    return [cached_chunk(d["name"].split("/sessions/")[1].split("/")[0], int(d["name"].rsplit("/", 1)[1]), d) for d in docs]


def cached_chunk(sid, seq, doc):
    path = CACHE / sid / f"{seq:06d}.json"
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    chunk = fields(doc)
    chunk["sid"] = sid
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(chunk, ensure_ascii=False), encoding="utf-8")
    return chunk


# ---------- lines ----------

def lines_of(chunks, game=None):
    """Parsed lines of every chunk, each tagged with its player, merged by time."""
    out = []
    for c in chunks:
        who = c.get("player") or c["sid"][-6:]
        for raw in (c.get("lines") or "").splitlines():
            try:
                line = json.loads(raw)
            except ValueError:
                continue
            if game and line.get("g") != game:
                continue
            line["_who"] = who
            line["_sid"] = c["sid"]
            out.append(line)
    out.sort(key=lambda l: (l.get("t", 0), l["_who"]))
    return out


FAKE_MIN = 2**64 - 200  # GameValues.FAKE_CLIENT_ID = ulong.MaxValue, fake characters count down from it
FAKE_RX = re.compile(r"\b1844674407370955\d{4}\b")


def seat_name(n):
    """Fake characters' seats (ulong.MaxValue - i) read as F1, F2…"""
    return f"F{2**64 - int(n)}" if int(n) >= FAKE_MIN else str(n)


def fakes(text):
    return FAKE_RX.sub(lambda m: seat_name(m.group(0)), text)


def fmt_value(v):
    if isinstance(v, int) and v >= FAKE_MIN:
        return seat_name(v)
    if isinstance(v, str):
        return v if re.fullmatch(r"[\w.\-/:#@]+", v) else json.dumps(v, ensure_ascii=False)
    if isinstance(v, list):
        return "[" + "; ".join(str(x) for x in v) + "]"
    return json.dumps(v, ensure_ascii=False)


def fmt_line(l, full=False, stack=False):
    t = datetime.fromtimestamp(l.get("t", 0) / 1000).strftime("%H:%M:%S.%f")[:-3]
    seat = f"#{l['s']}" if "s" in l else ""
    parts = [t, f"{l['_who']}{seat}", l.get("lv", "?"), l.get("c", ""), l.get("e", "") or ""]
    if l.get("c") == "phase":
        parts.append(f"→ {l.get('ph', '?')}" + (f" day {l['d']}" if l.get("d") else ""))
    params = l.get("p") or {}
    parts += [f"{k}={fmt_value(v)}" for k, v in params.items()]
    msg = l.get("m")
    if msg:
        if l.get("e") and msg.startswith(f"[{l['e']}]"):
            msg = msg[len(l["e"]) + 2:].lstrip()
        if not full and len(msg) > 300:
            msg = msg[:300] + "…"
        parts.append(msg.replace("\n", " ⏎ "))
    if l.get("r", 1) > 1:
        parts.append(f"×{l['r']}")
    text = fakes(" ".join(p for p in parts if p != ""))
    if stack and l.get("st"):
        text += "\n      " + l["st"].strip().replace("\n", "\n      ")
    return text


def apply_filters(lines, a):
    lv = LEVELS.index(a.level.upper()) if getattr(a, "level", None) else 0
    cats = set(a.cat.split(",")) if getattr(a, "cat", None) else None
    players = {p.lower() for p in a.player.split(",")} if getattr(a, "player", None) else None
    rx = re.compile(a.grep, re.I) if getattr(a, "grep", None) else None
    out = []
    for l in lines:
        if LEVELS.find(l.get("lv", "I")) < lv: continue
        if cats and l.get("c") not in cats and not (l.get("c") == "phase"): continue
        if players and l["_who"].lower() not in players: continue
        if rx and not rx.search(json.dumps(l, ensure_ascii=False)): continue
        out.append(l)
    return out


def resolve(target):
    """A session id ("20261020-…-abc123", 3 dash parts) or a game id ("20261020-2140-a3f9")."""
    if re.fullmatch(r"\d{8}-\d{6}-[0-9a-f]{6}", target):
        return lines_of(chunks_of_session(target)), f"session {target}"
    return lines_of(chunks_of_game(target), game=target), f"game {target}"


def print_lines(lines, a):
    if getattr(a, "json", False):
        for l in lines:
            print(json.dumps({k: v for k, v in l.items() if not k.startswith("_")} | {"who": l["_who"]}, ensure_ascii=False))
        return
    last_players = None
    for l in lines:
        if l.get("e") == "state.phase" and not a.full:
            players = (l.get("p") or {}).get("players")
            if players == last_players:
                l = dict(l, p={k: v for k, v in l["p"].items() if k != "players"} | {"players": "(unchanged)"})
            else:
                last_players = players
        print(fmt_line(l, a.full, a.stack))


# ---------- commands ----------

def cmd_list(a):
    rows = sessions_since(since_arg(a.since))
    if a.player:
        rows = [r for r in rows if a.player.lower() in (r.get("player") or "").lower()]
    if not a.all:
        rows = [r for r in rows if not r.get("autoplay")]
    if not rows:
        print("No session.")
        return
    for r in rows:
        start = local(ts(r.get("startedAt")))
        seen = local(ts(r.get("lastSeen")))
        state = "CRASH" if r.get("crashed") else ("end" if r.get("ended") else "live?")
        games = ",".join(r.get("gameIds") or []) or "-"
        print(f"{r['sid']}  {start:%d/%m %H:%M}→{seen:%H:%M}  {r.get('player') or '?':<14} {r.get('flavor')} "
              f"v{r.get('version')} {'host' if r.get('host') else 'client'} {state:<5} "
              f"E{r.get('errors', 0)} W{r.get('warnings', 0)} {r.get('lines', 0)}l  games={games}"
              + ("  [autoplay]" if r.get("autoplay") else ""))


def cmd_games(a):
    games = {}
    for r in sessions_since(since_arg(a.since)):
        for g in r.get("gameIds") or []:
            games.setdefault(g, []).append(r)
    if not games:
        print("No game.")
        return
    for g in sorted(games, reverse=True):
        who = ", ".join(f"{r.get('player') or '?'}{'(host)' if r.get('host') else ''}"
                        + ("(CRASH)" if r.get("crashed") else "") for r in games[g])
        print(f"{g}  {who}")


def cmd_timeline(a):
    lines, label = resolve(a.target)
    if getattr(a, "at", None):
        lines = window(lines, a.at, a.window)
    lines = apply_filters(lines, a)
    print(f"— {label}: {len(lines)} line(s), players: {', '.join(sorted({l['_who'] for l in lines}))}")
    print_lines(lines, a)


def window(lines, at, seconds):
    if not lines: return lines
    day = datetime.fromtimestamp(lines[0]["t"] / 1000)
    parts = [int(x) for x in at.split(":")] + [0, 0]
    center = day.replace(hour=parts[0], minute=parts[1], second=parts[2], microsecond=0)
    lo, hi = (center - timedelta(seconds=seconds)).timestamp() * 1000, (center + timedelta(seconds=seconds)).timestamp() * 1000
    return [l for l in lines if lo <= l["t"] <= hi]


def chunks_since(a):
    rows = sessions_since(since_arg(a.since))
    if a.player:
        rows = [r for r in rows if a.player.lower() in (r.get("player") or "").lower()]
    return rows, [c for r in rows for c in chunks_of_session(r["sid"])]


def cmd_errors(a):
    rows, chunks = chunks_since(a)
    groups = {}
    for l in lines_of(chunks):
        if l.get("lv") not in ("E", "W"): continue
        key = (l.get("lv"), l.get("c"), l.get("e"), re.sub(r"\d+", "N", (l.get("m") or json.dumps(l.get("p")))[:160]))
        g = groups.setdefault(key, {"n": 0, "who": set(), "first": l})
        g["n"] += l.get("r", 1)
        g["who"].add(l["_who"])
    for key, g in sorted(groups.items(), key=lambda kv: (kv[0][0] != "E", -kv[1]["n"])):
        print(f"{g['n']:>4}× {', '.join(sorted(g['who']))}: {fmt_line(g['first'], stack=a.stack)}")
    print(f"— {len(groups)} group(s) over {len(rows)} session(s)")


def cmd_grep(a):
    rx = re.compile(a.regex, re.I)
    _, chunks = chunks_since(a)
    for l in lines_of(chunks):
        if rx.search(json.dumps(l, ensure_ascii=False)):
            print(f"{l['_sid']} {fmt_line(l, a.full, a.stack)}")


def cmd_tail(a):
    sid = a.target
    if not re.fullmatch(r"\d{8}-\d{6}-[0-9a-f]{6}", sid):
        rows = [r for r in sessions_since(since_arg("2d")) if a.target.lower() in (r.get("player") or "").lower()]
        if not rows: sys.exit(f"No recent session for {a.target!r}.")
        sid = rows[0]["sid"]
    print(f"— following {sid} (Ctrl+C to stop)")
    last = -1
    while True:
        new = chunks_of_session(sid, last)
        for l in lines_of(new):
            print(fmt_line(l, a.full, a.stack))
        if new: last = max(c["seq"] for c in new)
        time.sleep(10)


def cmd_switch(a):
    url = f"{BASE}/config/logging"
    current = json.loads(fields(http("GET", url)).get("json") or "{}")
    if a.state is None and not (a.verbose or a.debug_cat or a.min_level):
        print(json.dumps(current, indent=2, ensure_ascii=False))
        return
    if a.state: current["enabled"] = a.state == "on"
    if a.verbose is not None: current["verbosePlayers"] = a.verbose
    if a.debug_cat is not None: current["debugCategories"] = a.debug_cat
    if a.min_level: current["minLevel"] = a.min_level.upper()
    http("PATCH", url + "?updateMask.fieldPaths=json", {"fields": {"json": {"stringValue": json.dumps(current)}}})
    print("config/logging =", json.dumps(current, ensure_ascii=False), "(clients apply it within a minute)")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def filters(p):
        p.add_argument("--level"); p.add_argument("--cat"); p.add_argument("--player"); p.add_argument("--grep")
        p.add_argument("--stack", action="store_true"); p.add_argument("--full", action="store_true")
        p.add_argument("--json", action="store_true")

    p = sub.add_parser("list"); p.add_argument("--since", default="24h"); p.add_argument("--player")
    p.add_argument("--all", action="store_true", help="include autoplay sessions"); p.set_defaults(f=cmd_list)
    p = sub.add_parser("games"); p.add_argument("--since", default="7d"); p.set_defaults(f=cmd_games)
    p = sub.add_parser("timeline"); p.add_argument("target"); filters(p); p.set_defaults(f=cmd_timeline)
    p = sub.add_parser("around"); p.add_argument("target"); p.add_argument("at"); p.add_argument("--window", type=int, default=60)
    filters(p); p.set_defaults(f=cmd_timeline)
    p = sub.add_parser("errors"); p.add_argument("--since", default="24h"); p.add_argument("--player")
    p.add_argument("--stack", action="store_true"); p.set_defaults(f=cmd_errors)
    p = sub.add_parser("grep"); p.add_argument("regex"); p.add_argument("--since", default="24h"); p.add_argument("--player")
    p.add_argument("--stack", action="store_true"); p.add_argument("--full", action="store_true"); p.set_defaults(f=cmd_grep)
    p = sub.add_parser("tail"); p.add_argument("target"); p.add_argument("--stack", action="store_true")
    p.add_argument("--full", action="store_true"); p.set_defaults(f=cmd_tail)
    p = sub.add_parser("switch"); p.add_argument("state", nargs="?", choices=["on", "off"])
    p.add_argument("--verbose", nargs="*"); p.add_argument("--debug-cat", nargs="*"); p.add_argument("--min-level")
    p.set_defaults(f=cmd_switch)

    a = ap.parse_args()
    a.f(a)


if __name__ == "__main__":
    try:
        main()
    except (BrokenPipeError, OSError) as e:
        if isinstance(e, BrokenPipeError) or getattr(e, "errno", None) == 22:  # output piped into head
            sys.exit(0)
        raise
