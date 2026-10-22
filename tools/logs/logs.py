#!/usr/bin/env python3
"""Read the playtest logs (Firestore project corruption-du-portail) — the way Claude investigates a playtest bug.

Stdlib only. Auth = `gcloud auth print-access-token` (the developer's own Google account, admin access), refreshed
automatically when it expires. Chunks are immutable: they are cached in tools/logs/.cache/ (gitignored) and only the
missing ones are downloaded.

    python tools/logs/logs.py list   [--since 24h] [--player NAME] [--game ID] [--all]   sessions, newest first
    python tools/logs/logs.py games  [--since 7d]                                         games and who played them
    python tools/logs/logs.py timeline <game id | session id> [filters]                   merged timeline, every player
    python tools/logs/logs.py around <game | session> <HH:MM[:SS]> [--window 60] [filters]
    python tools/logs/logs.py errors [--since 24h] [--player NAME] [--stack]             errors and warnings, grouped
    python tools/logs/logs.py grep <regex> [--since 24h] [--player NAME]                  raw search in every line
    python tools/logs/logs.py tail <player | session id>                                  follow a live session
    python tools/logs/logs.py switch [on|off] [--verbose NAME ...] [--debug-cat CAT ...] [--min-level D|I|W|E]
    python tools/logs/logs.py reports [--since 7d] [--player NAME] [--download DIR]  F8 reports and crash alerts

Filters (timeline / around): --level W (minimum level), --cat vote,power (categories), --player Wouh,
--grep REGEX, --stack (stack traces), --full (no truncation, every state picture), --json (raw lines),
--no-lobby (game lines only), --utc (times in UTC).

Line format: `HH:MM:SS.mmm Player#seat(role) L cat event k=v …  ×n`, local time unless --utc. `state` lines are the
host's picture of the game at each phase; `unity` lines are the game's own Debug.Log output ([TAG] as event).
A game timeline also shows each player's lines from the lobby just before the game (connections), and corrects the
clock of a PC whose time is off (measured on the phase changes every peer logs; noted at the top).
"""
import argparse
import json
import re
import statistics
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
DB = f"projects/{PROJECT}/databases/(default)/documents"
CACHE = Path(__file__).resolve().parent / ".cache"
LEVELS = "DIWE"
LOBBY_LEAD_MS = 180_000
SKEW_MIN_MS = 1500
SID_RX = re.compile(r"\d{8}-\d{6}-[0-9a-f]{6}")

for stream in (sys.stdout, sys.stderr):
    try:
        stream.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass

_token = None
UTC = False


def token(refresh=False):
    global _token
    if _token is None or refresh:
        _token = subprocess.run("gcloud auth print-access-token", shell=True, capture_output=True, text=True).stdout.strip()
        if not _token:
            sys.exit("No gcloud token: run `gcloud auth login` with the account that owns the Firebase project.")
    return _token


class NotFound(Exception):
    pass


def http(method, url, body=None, _retry=True):
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
        if e.code == 401 and _retry:  # gcloud tokens last an hour (tail runs longer)
            token(refresh=True)
            return http(method, url, body, _retry=False)
        if e.code == 404:
            raise NotFound(url)
        sys.exit(f"HTTP {e.code} {method} {url}\n{e.read().decode(errors='replace')[:800]}")
    except urllib.error.URLError as e:
        sys.exit(f"Network error on {url}: {e.reason}")


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
    """RFC3339 → aware datetime (None stays None)."""
    if not s: return None
    s = s.replace("Z", "+00:00")
    if "." in s:
        head, rest = s.split(".", 1)
        frac = re.match(r"\d+", rest).group(0)
        s = head + "." + frac[:6].ljust(6, "0") + rest[len(frac):]
    return datetime.fromisoformat(s)


def shown(dt):
    """Aware datetime in the display zone."""
    return dt.astimezone(timezone.utc) if UTC else dt.astimezone()


def hm(dt, fmt="%H:%M"):
    return shown(dt).strftime(fmt) if dt else "?"


def since_arg(text):
    m = re.fullmatch(r"(\d+)([mhd])", text or "")
    if not m: sys.exit(f"--since: expected like 30m, 24h, 7d, got {text!r}")
    n, unit = int(m.group(1)), m.group(2)
    return datetime.now(timezone.utc) - timedelta(**{"m": {"minutes": n}, "h": {"hours": n}, "d": {"days": n}}[unit])


# ---------- queries ----------

def run_query(structured, parent=BASE):
    rows = http("POST", f"{parent}:runQuery", {"structuredQuery": structured})
    return [r["document"] for r in rows if "document" in r]


def sessions_since(since, page=300):
    """Every session started after `since`, newest first (paginated)."""
    out, cursor = [], None
    while True:
        q = {
            "from": [{"collectionId": "sessions"}],
            "where": {"fieldFilter": {"field": {"fieldPath": "startedAt"}, "op": "GREATER_THAN_OR_EQUAL",
                                      "value": {"timestampValue": since.strftime("%Y-%m-%dT%H:%M:%SZ")}}},
            "orderBy": [{"field": {"fieldPath": "startedAt"}, "direction": "DESCENDING"},
                        {"field": {"fieldPath": "__name__"}, "direction": "DESCENDING"}],
            "limit": page,
        }
        if cursor:
            q["startAt"] = {"values": cursor, "before": False}
        docs = run_query(q)
        out += [fields(d) for d in docs]
        if len(docs) < page:
            return out
        last = docs[-1]
        cursor = [last["fields"]["startedAt"], {"referenceValue": last["name"]}]


def chunk_names(structured, parent=BASE):
    """(sid, seq) of the chunks a query matches, without downloading their lines."""
    structured = dict(structured, select={"fields": [{"fieldPath": "seq"}]})
    out = []
    for d in run_query(structured, parent):
        sid = d["name"].split("/sessions/")[1].split("/")[0]
        out.append((sid, int(d["name"].rsplit("/", 1)[1])))
    return out


def load_chunks(names):
    """Chunks by (sid, seq): from the cache, the missing ones in batches."""
    out, missing = [], []
    for sid, seq in names:
        path = CACHE / sid / f"{seq:06d}.json"
        if path.exists():
            out.append(json.loads(path.read_text(encoding="utf-8")))
        else:
            missing.append((sid, seq))
    for i in range(0, len(missing), 100):
        batch = missing[i:i + 100]
        rows = http("POST", f"{BASE}:batchGet", {"documents": [f"{DB}/sessions/{s}/chunks/{q:06d}" for s, q in batch]})
        for r in rows:
            if "found" not in r: continue
            d = r["found"]
            sid = d["name"].split("/sessions/")[1].split("/")[0]
            seq = int(d["name"].rsplit("/", 1)[1])
            chunk = fields(d)
            chunk["sid"], chunk["seq"] = sid, seq
            path = CACHE / sid / f"{seq:06d}.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(chunk, ensure_ascii=False), encoding="utf-8")
            out.append(chunk)
    return out


def chunks_of_session(sid, after_seq=-1):
    q = {"from": [{"collectionId": "chunks"}],
         "orderBy": [{"field": {"fieldPath": "seq"}, "direction": "ASCENDING"}]}
    if after_seq >= 0:
        q["where"] = {"fieldFilter": {"field": {"fieldPath": "seq"}, "op": "GREATER_THAN",
                                      "value": {"integerValue": str(after_seq)}}}
    return load_chunks(chunk_names(q, parent=f"{BASE}/sessions/{urllib.parse.quote(sid)}"))


def chunks_of_game(game):
    return load_chunks(chunk_names({
        "from": [{"collectionId": "chunks", "allDescendants": True}],
        "where": {"fieldFilter": {"field": {"fieldPath": "gameIds"}, "op": "ARRAY_CONTAINS",
                                  "value": {"stringValue": game}}},
    }))


# ---------- lines ----------

def session_players(sids):
    """Final pseudo of each session (a chunk carries the pseudo of its own time: "Player1234" before the login)."""
    names = {}
    sids = sorted(set(sids))
    for i in range(0, len(sids), 100):
        rows = http("POST", f"{BASE}:batchGet", {"documents": [f"{DB}/sessions/{s}" for s in sids[i:i + 100]],
                                                 "mask": {"fieldPaths": ["player"]}})
        for r in rows:
            if "found" in r:
                names[r["found"]["name"].rsplit("/", 1)[1]] = fields(r["found"]).get("player")
    return names


def parse(chunks):
    out = []
    names = session_players(c["sid"] for c in chunks) if chunks else {}
    labels = {}
    for c in chunks:
        labels.setdefault(c["sid"], names.get(c["sid"]) or c.get("player") or c["sid"][-6:])
    # Two sessions with the same pseudo (the same tester running two copies of the game): tell them apart by the end
    # of their session id ("Wouh~3f2a", "Wouh~9b1c").
    taken = {}
    for sid, label in labels.items():
        taken.setdefault(label, []).append(sid)
    for label, sids in taken.items():
        if len(sids) > 1:
            for sid in sids:
                labels[sid] = f"{label}~{sid[-4:]}"
    for c in chunks:
        who = labels[c["sid"]]
        for raw in (c.get("lines") or "").splitlines():
            try:
                line = json.loads(raw)
            except ValueError:
                continue
            line["_who"] = who
            line["_sid"] = c["sid"]
            out.append(line)
    return out


def sort_lines(lines):
    lines.sort(key=lambda l: (l.get("t", 0), l["_who"]))
    return lines


def game_lines(game, lobby=True):
    """The game's lines of every player, plus (lobby) each player's game-less lines just before it."""
    chunks = chunks_of_game(game)
    sids = sorted({c["sid"] for c in chunks})
    if lobby and sids:
        # the chunks around the game's start may hold the lobby lines (connections) of each session
        for sid in sids:
            seqs = [c["seq"] for c in chunks if c["sid"] == sid]
            first = min(seqs)
            if first > 0:
                chunks += chunks_of_session_range(sid, max(0, first - 3), first - 1)
    lines = parse(chunks)
    in_game = [l for l in lines if l.get("g") == game]
    if not in_game:
        return [], {}
    start = min(l["t"] for l in in_game)
    end = max(l["t"] for l in in_game)
    keep = in_game + ([l for l in lines if "g" not in l and start - LOBBY_LEAD_MS <= l.get("t", 0) <= end] if lobby else [])
    seen = set()
    unique = []
    for l in keep:  # a chunk may be loaded twice (range + game query)
        key = (l["_sid"], l.get("t"), l.get("e"), l.get("m"), json.dumps(l.get("p"), sort_keys=True))
        if key in seen: continue
        seen.add(key)
        unique.append(l)
    skews = align_clocks(unique, game)
    return sort_lines(unique), skews


def chunks_of_session_range(sid, lo, hi):
    return load_chunks([(sid, s) for s in range(lo, hi + 1)])


def align_clocks(lines, game):
    """Shift each player's times by its clock error against the host, measured on the game's phase changes every peer
    logs (lobby excluded: players join it at different times; a client enters a phase a little after the host, so only
    skews above SKEW_MIN_MS are corrected)."""
    by_sid = {}
    for l in lines:
        if l.get("e") == "phase.enter" and l.get("g") == game and l.get("ph") != "LobbyState":
            by_sid.setdefault(l["_sid"], []).append(l)
    host = next((sid for sid, ls in by_sid.items() for l in lines if l["_sid"] == sid and l.get("e") == "state.phase"), None)
    if not host: return {}
    host_by_ph = {}
    for l in by_sid[host]:
        host_by_ph.setdefault(l.get("ph"), []).append(l["t"])
    skews = {}
    for sid, ls in by_sid.items():
        if sid == host: continue
        # every pairing of a client phase change with a host change of the same phase (within 1 h) votes for an
        # offset; the true one gathers the most votes (phases repeat, so nearest-in-time or in-sequence matching lies)
        votes = [l["t"] - t for l in ls for t in host_by_ph.get(l.get("ph"), []) if abs(l["t"] - t) <= 3_600_000]
        if len(ls) < 3 or not votes:
            continue
        buckets = {}
        for d in votes:
            buckets[d // 500] = buckets.get(d // 500, 0) + 1
        best = max(buckets, key=lambda k: buckets[k] + buckets.get(k - 1, 0) + buckets.get(k + 1, 0))
        skew = statistics.median([d for d in votes if abs(d // 500 - best) <= 1])
        if abs(skew) >= SKEW_MIN_MS:
            skews[sid] = int(skew)
    for l in lines:
        if l["_sid"] in skews:
            l["t"] -= skews[l["_sid"]]
    return skews


FAKE_MIN = 2**64 - 200  # GameValues.FAKE_CLIENT_ID = ulong.MaxValue, fake characters count down from it
FAKE_RX = re.compile(r"\b1844674407370955\d{4}\b")


def seat_name(n):
    """Fake characters' seats (ulong.MaxValue - i) read as F1, F2…"""
    return f"F{2**64 - int(n)}" if int(n) >= FAKE_MIN else str(n)


def fakes(text):
    return FAKE_RX.sub(lambda m: seat_name(m.group(0)), text)


def fmt_value(v):
    if isinstance(v, int) and not isinstance(v, bool) and v >= FAKE_MIN:
        return seat_name(v)
    if isinstance(v, str):
        return v if re.fullmatch(r"[\w.\-/:#@]+", v) else json.dumps(v, ensure_ascii=False)
    if isinstance(v, list):
        return "[" + "; ".join(str(x) for x in v) + "]"
    return json.dumps(v, ensure_ascii=False)


def fmt_line(l, full=False, stack=False):
    t = shown(datetime.fromtimestamp(l.get("t", 0) / 1000, timezone.utc)).strftime("%H:%M:%S.%f")[:-3]
    who = l["_who"] + (f"#{l['s']}" if "s" in l else "") + (f"({l['ro']})" if l.get("ro") else "")
    parts = [t, who, l.get("lv", "?"), l.get("c", ""), l.get("e", "") or ""]
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
    text = fakes(" ".join(str(p) for p in parts if p != ""))
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
        if players and l["_who"].lower() not in players and l["_who"].split("~")[0].lower() not in players: continue
        if rx and not rx.search(json.dumps(l, ensure_ascii=False)): continue
        out.append(l)
    return out


def resolve(target, lobby=True):
    """A session id ("20261020-174814-3e7d3f") or a game id ("20261020-1749-9bd8")."""
    if SID_RX.fullmatch(target):
        return sort_lines(parse(chunks_of_session(target))), f"session {target}", {}
    lines, skews = game_lines(target, lobby)
    return lines, f"game {target}", skews


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
    if a.game:
        rows = [r for r in rows if a.game in (r.get("gameIds") or [])]
    if not a.all:
        rows = [r for r in rows if not r.get("autoplay")]
    if not rows:
        print("No session.")
        return
    for r in rows:
        start, seen = ts(r.get("startedAt")), ts(r.get("lastSeen"))
        state = "CRASH" if r.get("crashed") else ("end" if r.get("ended") else "live?")
        games = ",".join(r.get("gameIds") or []) or "-"
        print(f"{r['sid']}  {hm(start, '%d/%m %H:%M')}→{hm(seen)}  {r.get('player') or '?':<14} {r.get('flavor')} "
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
                        + ("(CRASH)" if r.get("crashed") else "") + ("[autoplay]" if r.get("autoplay") else "")
                        for r in games[g])
        print(f"{g}  {who}")


def cmd_timeline(a):
    lines, label, skews = resolve(a.target, not a.no_lobby)
    if getattr(a, "at", None):
        lines = window(lines, a.at, a.window)
    lines = apply_filters(lines, a)
    print(f"— {label}: {len(lines)} line(s), players: {', '.join(sorted({l['_who'] for l in lines})) or '-'}")
    for sid, skew in skews.items():
        print(f"— clock of session {sid} corrected by {-skew / 1000:+.1f} s")
    print_lines(lines, a)


def window(lines, at, seconds):
    if not lines: return lines
    m = re.fullmatch(r"(\d{1,2}):(\d{2})(?::(\d{2}))?", at)
    if not m: sys.exit(f"time: expected HH:MM or HH:MM:SS, got {at!r}")
    h, mi, se = int(m.group(1)), int(m.group(2)), int(m.group(3) or 0)
    if h > 23 or mi > 59 or se > 59: sys.exit(f"time: {at!r} is not a valid time of day")
    first = shown(datetime.fromtimestamp(lines[0]["t"] / 1000, timezone.utc))
    last = shown(datetime.fromtimestamp(lines[-1]["t"] / 1000, timezone.utc))
    # the day that puts the time inside the logs (a game may cross midnight)
    candidates = [first.replace(hour=h, minute=mi, second=se, microsecond=0) + timedelta(days=d) for d in (0, 1, -1)]
    center = min(candidates, key=lambda c: 0 if first <= c <= last else min(abs(c - first), abs(c - last)).total_seconds())
    lo = (center - timedelta(seconds=seconds)).timestamp() * 1000
    hi = (center + timedelta(seconds=seconds)).timestamp() * 1000
    return [l for l in lines if lo <= l["t"] <= hi]


def chunks_since(a):
    rows = sessions_since(since_arg(a.since))
    if a.player:
        rows = [r for r in rows if a.player.lower() in (r.get("player") or "").lower()]
    return rows, [c for r in rows for c in chunks_of_session(r["sid"])]


def cmd_errors(a):
    rows, chunks = chunks_since(a)
    groups = {}
    for l in sort_lines(parse(chunks)):
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
    for l in sort_lines(parse(chunks)):
        if rx.search(json.dumps(l, ensure_ascii=False)):
            print(f"{l['_sid']} {fmt_line(l, a.full, a.stack)}")


def cmd_tail(a):
    sid = a.target
    if not SID_RX.fullmatch(sid):
        rows = [r for r in sessions_since(since_arg("2d")) if a.target.lower() in (r.get("player") or "").lower()]
        if not rows: sys.exit(f"No recent session for {a.target!r}.")
        sid = rows[0]["sid"]
    print(f"— following {sid} (Ctrl+C to stop)")
    last = -1
    while True:
        new = chunks_of_session(sid, last)
        for l in sort_lines(parse(new)):
            print(fmt_line(l, a.full, a.stack), flush=True)
        if new: last = max(c["seq"] for c in new)
        time.sleep(10)


GAME_RX = re.compile(r"\d{8}-\d{4}-[0-9a-f]{4}")
REPORT_RX = re.compile(r"\d{8}-\d{6}-[0-9a-f]{6}")


def report_command(r):
    """The command that prints the reported moment (same as in the Discord alert), built only from ids in their exact
    formats: a planted report can never turn it into another command."""
    when = ts(r.get("t"))
    if GAME_RX.fullmatch(r.get("gameId") or "") and when:
        return f"python tools/logs/logs.py around {r['gameId']} {shown(when).strftime('%H:%M:%S')} --window 90"
    if SID_RX.fullmatch(r.get("sid") or ""):
        return f"python tools/logs/logs.py timeline {r['sid']} --level W"
    return "(invalid ids: no command)"


def cmd_reports(a):
    since = since_arg(a.since).strftime("%Y-%m-%dT%H:%M:%SZ")
    docs = run_query({
        "from": [{"collectionId": "reports"}],
        "where": {"fieldFilter": {"field": {"fieldPath": "t"}, "op": "GREATER_THAN_OR_EQUAL",
                                  "value": {"timestampValue": since}}},
        "orderBy": [{"field": {"fieldPath": "t"}, "direction": "DESCENDING"}],
        "limit": 200,
    })
    rows = [fields(d) for d in docs]
    rows = [r for r in rows if r.get("player") != "RulesCheck"]  # tools/logs/check_rules.py test documents
    if a.player:
        rows = [r for r in rows if a.player.lower() in (r.get("player") or "").lower()]
    if not rows:
        print("No report.")
        return
    for r in rows:
        when = ts(r.get("t"))
        where = ", ".join(x for x in (r.get("phase"), f"day {r['day']}" if r.get("day") else None, r.get("role")) if x)
        kind = "CRASH " if r.get("kind") == "crash" else ""
        print(f"{hm(when, '%d/%m %H:%M:%S')}  {kind}{r.get('player') or '?'}  v{r.get('version')} {r.get('flavor') or ''}"
              f"  {where or 'out of game'}")
        if r.get("text"):
            print(f"    « {r['text']} »")
        if r.get("postError"):
            print(f"    Discord: {r['postError']}")
        print(f"    {report_command(r)}")
        if r.get("capture"):
            if a.download and REPORT_RX.fullmatch(r.get("id") or "") and r["capture"] == f"reports/{r['id']}.jpg":
                out = Path(a.download) / f"{r['id']}.jpg"
                out.parent.mkdir(parents=True, exist_ok=True)
                url = (f"https://storage.googleapis.com/storage/v1/b/{PROJECT}.firebasestorage.app/o/"
                       f"{urllib.parse.quote(r['capture'], safe='')}?alt=media")
                req = urllib.request.Request(url, headers={"Authorization": f"Bearer {token()}"})
                try:
                    with urllib.request.urlopen(req, timeout=60) as resp:
                        out.write_bytes(resp.read())
                    print(f"    capture: {out}")
                except urllib.error.URLError as e:
                    print(f"    capture missing ({getattr(e, 'code', e)})")
            else:
                print(f"    capture: {r['capture']} (--download DIR to save it)")


def cmd_switch(a):
    url = f"{BASE}/config/logging"
    try:
        current = json.loads(fields(http("GET", url)).get("json") or "{}")
    except NotFound:
        current = {}
    changes = a.state is not None or a.verbose is not None or a.debug_cat is not None or a.min_level
    if not changes:
        print(json.dumps(current, indent=2, ensure_ascii=False) if current else "config/logging missing: uploads are OFF")
        return
    if a.state: current["enabled"] = a.state == "on"
    if a.verbose is not None: current["verbosePlayers"] = a.verbose
    if a.debug_cat is not None: current["debugCategories"] = a.debug_cat
    if a.min_level: current["minLevel"] = a.min_level.upper()
    http("PATCH", url + "?updateMask.fieldPaths=json", {"fields": {"json": {"stringValue": json.dumps(current)}}})
    print("config/logging =", json.dumps(current, ensure_ascii=False), "(clients apply it within a minute)")


def main():
    global UTC
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--utc", action="store_true", help="times in UTC instead of this PC's zone")
    sub = ap.add_subparsers(dest="cmd", required=True)
    add = sub.add_parser
    sub.add_parser = lambda name, **kw: add(name, parents=[common], **kw)

    def filters(p):
        p.add_argument("--level"); p.add_argument("--cat"); p.add_argument("--player"); p.add_argument("--grep")
        p.add_argument("--stack", action="store_true"); p.add_argument("--full", action="store_true")
        p.add_argument("--json", action="store_true")
        p.add_argument("--no-lobby", action="store_true", help="game lines only (no lobby lead-in)")

    p = sub.add_parser("list"); p.add_argument("--since", default="24h"); p.add_argument("--player"); p.add_argument("--game")
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
    p = sub.add_parser("reports"); p.add_argument("--since", default="7d"); p.add_argument("--player")
    p.add_argument("--download", help="folder where the captures are saved"); p.set_defaults(f=cmd_reports)
    p = sub.add_parser("switch"); p.add_argument("state", nargs="?", choices=["on", "off"])
    p.add_argument("--verbose", nargs="*", help="players whose Debug lines go online (none = clear)")
    p.add_argument("--debug-cat", nargs="*", help="categories whose Debug lines go online (none = clear)")
    p.add_argument("--min-level", choices=list("DIWEdiwe"))
    p.set_defaults(f=cmd_switch)

    a = ap.parse_args()
    UTC = a.utc
    a.f(a)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(130)
    except (BrokenPipeError, OSError) as e:
        if isinstance(e, BrokenPipeError) or getattr(e, "errno", None) == 22:  # output piped into head
            sys.exit(0)
        raise
