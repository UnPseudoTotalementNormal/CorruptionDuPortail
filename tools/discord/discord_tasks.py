#!/usr/bin/env python3
"""Read the team task board (Discord forum channel `liste-de-taches`) completely.

Read-only. Stdlib only. Token comes from the DISCORD_BOT_TOKEN env var or the
repo-root `.env` (found by walking up from cwd and from this script, so it works
from a worktree too).

    python tools/discord/discord_tasks.py check
    python tools/discord/discord_tasks.py list [--open|--done|--all] [--json]
    python tools/discord/discord_tasks.py read <thread id | title substring> [--download DIR] [--json]
    python tools/discord/discord_tasks.py dump [--open|--done|--all] [--download DIR] [--json]

What "complete" means here: active + archived (public and private) threads with
pagination, every message of a thread (not just the first 50), the starter post,
attachments, embeds, reactions, polls, stickers, replies, mentions resolved to
names, tag ids resolved to names. If the bot lacks the privileged Message Content
intent, Discord blanks content/attachments/embeds: every command says so loudly
instead of printing empty bodies.
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path

API = "https://discord.com/api/v10"
FLAG_MESSAGE_CONTENT = 1 << 18
FLAG_MESSAGE_CONTENT_LIMITED = 1 << 19

for stream in (sys.stdout, sys.stderr):
    try:
        stream.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass


# ---------------------------------------------------------------- config / http

def load_env():
    env = {}
    seen = set()
    for start in (Path.cwd(), Path(__file__).resolve().parent):
        for d in [start, *start.parents]:
            f = d / ".env"
            if f in seen or not f.is_file():
                continue
            seen.add(f)
            for line in f.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                k, v = line.split("=", 1)
                env.setdefault(k.strip(), v.strip().strip('"').strip("'"))
            if "DISCORD_BOT_TOKEN" in env:
                break
    for k in ("DISCORD_BOT_TOKEN", "DISCORD_GUILD_ID", "DISCORD_TASK_CHANNEL_ID", "DISCORD_DONE_TAG_ID"):
        if os.environ.get(k):
            env[k] = os.environ[k]
    missing = [k for k in ("DISCORD_BOT_TOKEN", "DISCORD_GUILD_ID", "DISCORD_TASK_CHANNEL_ID") if not env.get(k)]
    if missing:
        sys.exit(f"missing config: {', '.join(missing)} (env var or repo-root .env)")
    return env


class Client:
    def __init__(self, token):
        self.headers = {"Authorization": f"Bot {token}", "User-Agent": "DiscordBot (cdp-task-reader, 2)"}

    def get(self, path, params=None, allow_403=False):
        url = API + path
        if params:
            url += "?" + urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
        for _ in range(6):
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers=self.headers), timeout=30) as r:
                    return json.load(r)
            except urllib.error.HTTPError as e:
                if e.code == 429:
                    retry = json.load(e).get("retry_after", 1)
                    time.sleep(float(retry) + 0.1)
                    continue
                if e.code == 403 and allow_403:
                    return None
                body = e.read().decode("utf-8", "replace")[:300]
                sys.exit(f"HTTP {e.code} on GET {path}: {body}")
        sys.exit(f"rate-limited too many times on GET {path}")

    def download(self, url, dest):
        req = urllib.request.Request(url, headers={"User-Agent": self.headers["User-Agent"]})
        with urllib.request.urlopen(req, timeout=60) as r, open(dest, "wb") as f:
            f.write(r.read())


# ---------------------------------------------------------------- board model

class Board:
    def __init__(self, env):
        self.env = env
        self.api = Client(env["DISCORD_BOT_TOKEN"])
        self.guild = env["DISCORD_GUILD_ID"]
        self.channel_id = env["DISCORD_TASK_CHANNEL_ID"]
        self.done_tag = env.get("DISCORD_DONE_TAG_ID")
        self._channel = None
        self._members = {}
        self._roles = None
        self._channels = None

    @property
    def channel(self):
        if self._channel is None:
            self._channel = self.api.get(f"/channels/{self.channel_id}")
        return self._channel

    @property
    def tag_names(self):
        return {t["id"]: t["name"] for t in self.channel.get("available_tags", [])}

    def intent_status(self):
        app = self.api.get("/applications/@me")
        flags = app.get("flags", 0)
        on = bool(flags & (FLAG_MESSAGE_CONTENT | FLAG_MESSAGE_CONTENT_LIMITED))
        return app, on

    # threads ---------------------------------------------------------------
    def threads(self):
        out = {}
        for t in self.api.get(f"/guilds/{self.guild}/threads/active").get("threads", []):
            if t.get("parent_id") == self.channel_id:
                out[t["id"]] = t
        for kind in ("public", "private"):
            before = None
            while True:
                page = self.api.get(f"/channels/{self.channel_id}/threads/archived/{kind}",
                                    {"limit": 100, "before": before}, allow_403=True)
                if not page:
                    break
                for t in page.get("threads", []):
                    out.setdefault(t["id"], t)
                if not page.get("has_more") or not page.get("threads"):
                    break
                before = page["threads"][-1]["thread_metadata"]["archive_timestamp"]
        return sorted(out.values(), key=lambda t: int(t["id"]), reverse=True)

    def is_done(self, t):
        return bool(self.done_tag) and self.done_tag in t.get("applied_tags", [])

    def find_thread(self, key):
        threads = self.threads()
        for t in threads:
            if t["id"] == key:
                return t
        hits = [t for t in threads if key.casefold() in t["name"].casefold()]
        if len(hits) == 1:
            return hits[0]
        if not hits:
            sys.exit(f"no task matches {key!r}")
        sys.exit("ambiguous, candidates:\n" + "\n".join(f"  {t['id']}  {t['name']}" for t in hits))

    # messages --------------------------------------------------------------
    def messages(self, thread_id):
        msgs, before = [], None
        while True:
            page = self.api.get(f"/channels/{thread_id}/messages", {"limit": 100, "before": before})
            msgs.extend(page)
            if len(page) < 100:
                break
            before = page[-1]["id"]
        # Forum starter post has id == thread id; fetch it if it fell outside history.
        if not any(m["id"] == thread_id for m in msgs):
            starter = self.api.get(f"/channels/{thread_id}/messages/{thread_id}", allow_403=True)
            if isinstance(starter, dict) and starter.get("id"):
                msgs.append(starter)
        return sorted(msgs, key=lambda m: int(m["id"]))

    # mention resolution ----------------------------------------------------
    def member_name(self, uid, hint=None):
        if uid not in self._members:
            if hint:
                self._members[uid] = hint
            else:
                m = self.api.get(f"/guilds/{self.guild}/members/{uid}", allow_403=True) or {}
                u = m.get("user", {})
                self._members[uid] = m.get("nick") or u.get("global_name") or u.get("username") or uid
        return self._members[uid]

    def role_name(self, rid):
        if self._roles is None:
            self._roles = {r["id"]: r["name"] for r in (self.api.get(f"/guilds/{self.guild}/roles", allow_403=True) or [])}
        return self._roles.get(rid, rid)

    def channel_name(self, cid):
        if self._channels is None:
            self._channels = {c["id"]: c["name"] for c in (self.api.get(f"/guilds/{self.guild}/channels", allow_403=True) or [])}
        return self._channels.get(cid, cid)

    def resolve(self, text, msg):
        for u in msg.get("mentions", []):
            self.member_name(u["id"], (u.get("member") or {}).get("nick") or u.get("global_name") or u.get("username"))
        text = re.sub(r"<@!?(\d+)>", lambda m: "@" + self.member_name(m.group(1)), text)
        text = re.sub(r"<@&(\d+)>", lambda m: "@" + self.role_name(m.group(1)), text)
        text = re.sub(r"<#(\d+)>", lambda m: "#" + self.channel_name(m.group(1)), text)
        text = re.sub(r"<a?(:\w+:)\d+>", r"\1", text)
        text = re.sub(r"<t:(\d+)(?::\w)?>", lambda m: datetime.fromtimestamp(int(m.group(1))).strftime("%Y-%m-%d %H:%M"), text)
        return text


# ---------------------------------------------------------------- rendering

def author_name(m):
    a = m.get("author", {})
    return (m.get("member") or {}).get("nick") or a.get("global_name") or a.get("username") or "?"


def ts(iso):
    return iso[:16].replace("T", " ") if iso else ""


def thread_row(board, t):
    names = board.tag_names
    tags = [names.get(x, x) for x in t.get("applied_tags", [])]
    state = "DONE" if board.is_done(t) else "open"
    meta = t.get("thread_metadata", {})
    return {
        "id": t["id"],
        "name": t["name"],
        "state": state,
        "tags": tags,
        "archived": meta.get("archived", False),
        "locked": meta.get("locked", False),
        "messages": t.get("message_count"),
        "created": ts(meta.get("create_timestamp")) or snowflake_time(t["id"]),
        "url": f"https://discord.com/channels/{board.guild}/{t['id']}",
    }


def snowflake_time(sid):
    return datetime.fromtimestamp(((int(sid) >> 22) + 1420070400000) / 1000).strftime("%Y-%m-%d %H:%M")


def message_view(board, m, download_dir=None):
    v = {
        "id": m["id"],
        "author": author_name(m),
        "time": ts(m.get("timestamp")),
        "edited": ts(m.get("edited_timestamp")),
        "content": board.resolve(m.get("content", ""), m),
    }
    if m.get("type") not in (0, 19, 21):
        v["system_type"] = m.get("type")
    ref = m.get("referenced_message")
    if ref:
        v["reply_to"] = f"{author_name(ref)}: {ref.get('content', '')[:80]}"
    atts = []
    for a in m.get("attachments", []):
        item = {"name": a["filename"], "type": a.get("content_type"), "size": a.get("size"), "url": a["url"]}
        if a.get("description"):
            item["alt"] = a["description"]
        if download_dir:
            dest = Path(download_dir) / f"{m['id']}_{a['filename']}"
            dest.parent.mkdir(parents=True, exist_ok=True)
            board.api.download(a["url"], dest)
            item["local"] = str(dest)
        atts.append(item)
    if atts:
        v["attachments"] = atts
    embeds = []
    for e in m.get("embeds", []):
        parts = [e.get("title"), e.get("description"), e.get("url")]
        parts += [f"{f['name']}: {f['value']}" for f in e.get("fields", [])]
        if e.get("image"):
            parts.append("image " + e["image"].get("url", ""))
        txt = " | ".join(p for p in parts if p)
        if txt:
            embeds.append(txt)
    if embeds:
        v["embeds"] = embeds
    if m.get("sticker_items"):
        v["stickers"] = [s["name"] for s in m["sticker_items"]]
    if m.get("reactions"):
        v["reactions"] = [f"{(r['emoji'].get('name') or '?')}x{r['count']}" for r in m["reactions"]]
    poll = m.get("poll")
    if poll:
        counts = {c["id"]: c["count"] for c in (poll.get("results") or {}).get("answer_counts", [])}
        v["poll"] = {
            "question": poll["question"].get("text"),
            "answers": [f"{a['poll_media'].get('text')} ({counts.get(a['answer_id'], 0)})" for a in poll.get("answers", [])],
        }
    return v


def print_thread(row, views):
    print(f"## {row['name']}")
    print(f"id {row['id']} · {row['state']} · tags: {', '.join(row['tags']) or '-'} · créé {row['created']}"
          f"{' · archivé' if row['archived'] else ''}{' · verrouillé' if row['locked'] else ''}")
    print(row["url"])
    for i, v in enumerate(views):
        head = "[post]" if i == 0 and v["id"] == row["id"] else "-"
        edited = f" (édité {v['edited']})" if v.get("edited") else ""
        print(f"\n{head} {v['author']} · {v['time']}{edited}")
        if v.get("system_type") is not None:
            print(f"  (message système type {v['system_type']})")
        if v.get("reply_to"):
            print(f"  ↪ en réponse à {v['reply_to']}")
        if v["content"]:
            for line in v["content"].splitlines():
                print("  " + line)
        for a in v.get("attachments", []):
            print(f"  📎 {a['name']} ({a.get('type') or '?'}, {a.get('size') or '?'} o) {a.get('local') or a['url']}")
            if a.get("alt"):
                print(f"     alt: {a['alt']}")
        for e in v.get("embeds", []):
            print(f"  ▣ {e}")
        if v.get("stickers"):
            print(f"  stickers: {', '.join(v['stickers'])}")
        if v.get("poll"):
            print(f"  sondage: {v['poll']['question']}")
            for ans in v["poll"]["answers"]:
                print(f"    - {ans}")
        if v.get("reactions"):
            print(f"  réactions: {' '.join(v['reactions'])}")
        if not (v["content"] or v.get("attachments") or v.get("embeds") or v.get("stickers") or v.get("poll")):
            print("  (vide)")
    print()


INTENT_WARNING = (
    "!! Message Content intent DÉSACTIVÉ : Discord renvoie le texte, les pièces jointes et les embeds VIDES.\n"
    "!! Fix (30 s) : https://discord.com/developers/applications -> app du bot -> Bot -> Privileged Gateway Intents\n"
    "!!            -> activer « Message Content Intent » -> Save. Aucune vérification requise (< 100 serveurs).\n"
)


def warn_if_blind(board):
    app, on = board.intent_status()
    if not on:
        print(INTENT_WARNING, file=sys.stderr)
    return on


# ---------------------------------------------------------------- commands

def filter_threads(board, threads, which):
    if which == "open":
        return [t for t in threads if not board.is_done(t)]
    if which == "done":
        return [t for t in threads if board.is_done(t)]
    return threads


def cmd_check(board, args):
    app, on = board.intent_status()
    threads = board.threads()
    print(f"bot: {app['name']} (app {app['id']}) · flags {app.get('flags', 0)}")
    print(f"Message Content intent: {'ON' if on else 'OFF'}")
    print(f"salon: #{board.channel.get('name')} · {len(threads)} tâches "
          f"({sum(not board.is_done(t) for t in threads)} ouvertes)")
    print("tags: " + ", ".join(f"{n} ({i})" for i, n in board.tag_names.items()))
    if not on:
        print("\n" + INTENT_WARNING)


def cmd_list(board, args):
    rows = [thread_row(board, t) for t in filter_threads(board, board.threads(), args.which)]
    if args.json:
        print(json.dumps(rows, ensure_ascii=False, indent=1))
        return
    for r in rows:
        print(f"{r['id']}  {r['state']:4}  {r['created'][:10]}  {r['name']}"
              f"{'  [' + ', '.join(r['tags']) + ']' if r['tags'] else ''}")
    print(f"\n{len(rows)} tâche(s)")


def read_one(board, t, args):
    row = thread_row(board, t)
    views = [message_view(board, m, args.download and Path(args.download) / t["id"]) for m in board.messages(t["id"])]
    return row, views


def cmd_read(board, args):
    warn_if_blind(board)
    row, views = read_one(board, board.find_thread(args.thread), args)
    if args.json:
        print(json.dumps({**row, "messages": views}, ensure_ascii=False, indent=1))
    else:
        print_thread(row, views)


def cmd_dump(board, args):
    warn_if_blind(board)
    out = []
    for t in filter_threads(board, board.threads(), args.which):
        row, views = read_one(board, t, args)
        if args.json:
            out.append({**row, "messages": views})
        else:
            print_thread(row, views)
    if args.json:
        print(json.dumps(out, ensure_ascii=False, indent=1))


def main():
    p = argparse.ArgumentParser(description="Read the Discord task board completely (read-only).")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("check", help="bot, intent status, tag list, task count")

    def which(sp):
        g = sp.add_mutually_exclusive_group()
        g.add_argument("--open", dest="which", action="store_const", const="open")
        g.add_argument("--done", dest="which", action="store_const", const="done")
        g.add_argument("--all", dest="which", action="store_const", const="all")
        sp.set_defaults(which="open")

    sp = sub.add_parser("list", help="tasks (default: open)")
    which(sp)
    sp.add_argument("--json", action="store_true")

    sp = sub.add_parser("read", help="one task with every message")
    sp.add_argument("thread", help="thread id or title substring")
    sp.add_argument("--download", metavar="DIR", help="save attachments under DIR/<thread id>/")
    sp.add_argument("--json", action="store_true")

    sp = sub.add_parser("dump", help="every task with every message (default: open)")
    which(sp)
    sp.add_argument("--download", metavar="DIR")
    sp.add_argument("--json", action="store_true")

    args = p.parse_args()
    board = Board(load_env())
    {"check": cmd_check, "list": cmd_list, "read": cmd_read, "dump": cmd_dump}[args.cmd](board, args)


if __name__ == "__main__":
    main()
