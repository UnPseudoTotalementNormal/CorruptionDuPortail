"""Serves the mockups on 127.0.0.1:PORT (opened on the phone through Clawd Code's "Aperçu de l'appli", or in a
browser on the PC). One folder per subject, one page per round (<subject>/v1.html, v2.html...).
GET /retours?s=<subject>&t=<text> appends the feedback to <subject>/retours.txt (the preview relay does not
carry POST bodies); a page sends its whole state, so a new send of the same page replaces the last one. Every
send is logged in retours.log. The subjects of the repository's worktrees (<repo>/.claude/worktrees/<name>/,
same folder) are served too, their feedback written there. From the "maquettes" template of the skill marketplace."""

import datetime
import html
import http.server
import pathlib
import re
import threading
import urllib.parse

HERE = pathlib.Path(__file__).resolve().parent
PORT = 5198  # one port per project: see the list in the template's variantes.md
APPLI = "Corruption du Portail"  # title of "/", the name Clawd Code's preview gives this server in its list
CROQUIS = False  # True (an option of the project): a round's thumbnail is Claude's pen sketch <subject>/v<N>.svg when there is one
LOCK = threading.Lock()  # two taps on "Envoyer" arrive together: read and write retours.txt one at a time


def main_folder() -> pathlib.Path:
    """This folder in the main checkout, even when serve.py was started from a worktree's copy."""
    for p in [HERE, *HERE.parents]:
        if (p / ".git").exists():
            if p.parent.name == "worktrees" and p.parent.parent.name == ".claude":
                return p.parent.parent.parent / HERE.relative_to(p)
            return HERE
    return HERE


ROOT = main_folder()


def roots() -> list:
    """Where subjects live: this folder, then the same folder in each worktree of the repository, so that a
    session working in a worktree writes its pages there, no copies."""
    found = [ROOT]
    repo = next((p for p in [ROOT, *ROOT.parents] if (p / ".git").is_dir()), None)
    if repo:
        rel = ROOT.relative_to(repo).as_posix()
        found += sorted(d for d in (repo / ".claude" / "worktrees").glob("*" if rel == "." else f"*/{rel}") if d.is_dir())
    return found


def home(subject: str) -> pathlib.Path:
    """The folder of a subject: the one holding its newest page (a worktree's), else here."""
    pages = [p for r in roots() for p in (r / subject).glob("v*.html")]
    return max(pages, key=lambda p: p.stat().st_mtime).parent if pages else ROOT / subject


def page_of(text: str):
    m = re.search(r"^Page : (\S+)", text, re.M)
    return m.group(1) if m else None


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=str(ROOT), **k)

    def translate_path(self, path):  # a file missing here may be a worktree's
        here = pathlib.Path(super().translate_path(path))
        if not here.exists():
            try:
                rel = here.relative_to(ROOT)
            except ValueError:
                return str(here)
            for r in roots()[1:]:
                if (r / rel).exists():
                    return str(r / rel)
        return str(here)

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        if url.path in ("/", "/index.html"):  # every round of every subject, newest first (the preview opens "/")
            return self.send_body(contents().encode("utf-8"), "text/html; charset=utf-8")
        if url.path == "/retours":
            query = urllib.parse.parse_qs(url.query)
            text = query.get("t", [""])[0]
            subject = re.sub(r"[^\w-]", "", query.get("s", ["divers"])[0]) or "divers"
            stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
            with LOCK:
                folder = home(subject)
                folder.mkdir(parents=True, exist_ok=True)
                path = folder / "retours.txt"
                old = path.read_text(encoding="utf-8") if path.exists() else ""
                parts = re.split(r"^(?=--- )", old, flags=re.M)
                head, _, last = parts[-1].partition("\n") if old else ("", "", "")
                if old and text.strip() == last.strip():  # the same feedback again (several taps, a retried request)
                    what = "identique, ignoré"
                elif old and page_of(text) and page_of(text) == page_of(last):  # the page's whole state again: it replaces
                    was = re.search(r"\d\d:\d\d", head)
                    path.write_text("".join(parts[:-1]) + f"--- {stamp} (remplace {was.group(0) if was else 'le précédent'})\n"
                                    f"{text}\n", encoding="utf-8")
                    what = "remplace le précédent"
                else:
                    with open(path, "a", encoding="utf-8") as f:
                        f.write(f"--- {stamp}\n{text}\n")
                    what = "ajouté"
                bits = folder.parent.parts
                where = "" if folder.parent == ROOT else f" (worktree {bits[bits.index('worktrees') + 1]})"
                with open(ROOT / "retours.log", "a", encoding="utf-8") as f:  # was a vote said lost received?
                    f.write(f"{stamp} {subject}{where} {page_of(text) or '?'} : {what}, {len(text)} car.\n")
            return self.send_body(b'{"ok":true}', "application/json")
        if self.headers.get("Range") and self.send_range(pathlib.Path(self.translate_path(url.path))):
            return
        super().do_GET()

    def send_range(self, path: pathlib.Path) -> bool:
        """A byte range: the phone's WebView asks for one and plays no mp4 served whole."""
        m = re.fullmatch(r"bytes=(\d*)-(\d*)", self.headers.get("Range", "").strip())
        if not m or not (m.group(1) or m.group(2)) or not path.is_file():
            return False
        size = path.stat().st_size
        if m.group(1):
            start, end = int(m.group(1)), min(int(m.group(2)) if m.group(2) else size - 1, size - 1)
        else:
            start, end = max(0, size - int(m.group(2))), size - 1
        if start > end:
            self.send_response(416)
            self.send_header("Content-Range", f"bytes */{size}")
            self.end_headers()
            return True
        with open(path, "rb") as f:
            f.seek(start)
            body = f.read(end - start + 1)
        self.send_response(206)
        self.send_header("Content-Type", self.guess_type(str(path)))
        self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        return True

    def send_body(self, body, kind):
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):  # pythonw has no stderr
        pass

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


MONTHS = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre",
          "novembre", "décembre"]
DAYS = ["lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche"]
SHOT = (".png", ".jpg", ".jpeg", ".webp")


def day_name(when: datetime.datetime) -> str:
    """Aujourd'hui, Hier, or « Mardi 13 octobre »."""
    days = (datetime.date.today() - when.date()).days
    if days == 0:
        return "Aujourd'hui"
    if days == 1:
        return "Hier"
    return f"{DAYS[when.weekday()]} {when.day} {MONTHS[when.month - 1]}".capitalize()


def feedback(subject: pathlib.Path) -> list:
    """The entries of <subject>/retours.txt: (time, page number or None, text)."""
    path = subject / "retours.txt"
    if not path.exists():
        return []
    entries = []
    for stamp, text in re.findall(r"^--- (\S+ \S+)[^\n]*\n(.*?)(?=^--- |\Z)", path.read_text(encoding="utf-8"), re.M | re.S):
        try:
            when = datetime.datetime.strptime(stamp, "%Y-%m-%d %H:%M").timestamp()
        except ValueError:
            continue
        page = re.search(r"^Page : /[^/]+/v(\d+)\.html", text, re.M)
        entries.append((when, int(page.group(1)) if page else None, text))
    return entries


def rounds() -> list:
    """Every round, newest first: subject, number, title, time, captures, the feedback it got, its sketch."""
    found, seen = [], set()
    for f in (f for r in roots() for f in r.glob("*/v*.html")):
        m = re.fullmatch(r"v(\d+)", f.stem)  # v3.brouillon.html, not ready yet, stays out
        if not m or (f.parent.name, m.group(1)) in seen:  # here and in a worktree: here wins
            continue
        seen.add((f.parent.name, m.group(1)))
        t = re.search(r"<title>(.*?)</title>", f.read_text(encoding="utf-8"), re.S)
        shots = f.parent / "captures" / f.stem
        found.append({
            "s": f.parent, "n": int(m.group(1)), "t": f.stat().st_mtime,
            "title": re.sub(r"^v\d+\s*·\s*", "", (t.group(1) if t else f.stem).strip()),
            "shots": sorted(p for p in shots.glob("*") if p.suffix.lower() in SHOT) if shots.is_dir() else [],
            "sketch": f.with_suffix(".svg"), "said": [],
        })
    for subject in {r["s"] for r in found}:
        mine = sorted((r for r in found if r["s"] == subject), key=lambda r: r["t"])
        for when, page, text in feedback(subject):
            # an entry names its page; older ones without the line go to the round then on screen
            hit = [r for r in mine if r["n"] == page] if page else [r for r in mine if r["t"] <= when + 60][-1:]
            for r in hit:
                r["said"].append(text)
        mine[-1]["todo"] = not mine[-1]["said"]  # only a subject's last round can wait for a vote
    return sorted(found, key=lambda r: -r["t"])


def link(r: dict) -> str:
    return f"{r['s'].name}/v{r['n']}.html"


def url(p: pathlib.Path) -> str:
    root = next(r for r in roots() if p.is_relative_to(r))
    return html.escape(p.relative_to(root).as_posix(), quote=True)


def thumb(r: dict) -> str:
    """The capture of the variant liked first, else the first capture; with CROQUIS, Claude's pen sketch of
    what the round changes first (<subject>/v<N>.svg, inlined to take the page's colours)."""
    if CROQUIS and r["sketch"].exists():
        svg = re.sub(r"<\?xml.*?\?>", "", r["sketch"].read_text(encoding="utf-8")).strip()
        return f'<div class="im sk">{svg}</div>'
    liked = [m for text in r["said"] for m in re.findall(r"^([\w-]+) \(.*?\) : 👍", text, re.M)]
    by_id = {p.stem: p for p in r["shots"]}
    pick = next((by_id[i] for i in liked if i in by_id), r["shots"][0] if r["shots"] else None)
    return f'<div class="im" style="background-image:url(\'{url(pick)}\')"></div>' if pick else '<div class="im"></div>'


STYLE = """
.sm { --sm-card: var(--card, var(--surface, #2f2f2d)); --sm-soft: var(--soft, var(--muted, #a6a39a)); padding-bottom:24px; }
.sm h2 { color:var(--sm-soft); font-size:13px; font-weight:600; line-height:1.3; margin:0; padding:20px 18px 8px; }
.sm .big { display:block; margin:0 12px 12px; background:var(--sm-card); border-radius:18px; overflow:hidden; color:var(--fg); text-decoration:none; max-width:560px; }
.sm .shots { display:flex; gap:6px; padding:12px 12px 0; }
.sm .shots div { flex:1; aspect-ratio:9/13; border-radius:10px; background:var(--bg) center top / cover no-repeat; border:1px solid var(--line); }
.sm .big b { display:block; font-size:16px; font-weight:600; padding:12px 14px 2px; line-height:1.3; }
.sm .big span { display:block; font-size:13px; color:var(--sm-soft); padding:0 14px 14px; }
.sm .grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(104px, 1fr)); gap:12px 8px; padding:0 12px; }
.sm .cell { color:var(--fg); text-decoration:none; min-width:0; }
.sm .im { aspect-ratio:3/4; border-radius:12px; background:var(--sm-card) center top / cover no-repeat; border:1px solid var(--line); overflow:hidden; display:flex; align-items:center; justify-content:center; }
.sm .cell span { display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden; font-size:12.5px; line-height:1.3; margin-top:6px; }
/* the pen sketch: b a body, a what changes (orange), t a line of text */
.sm .sk svg { width:92%; height:auto; overflow:visible; }
.sm .sk svg > * { filter:url(#sm-pen); }
.sm .sk .b, .sm .sk .a { fill:none; stroke-width:1.6; stroke-linejoin:round; stroke-linecap:round; }
.sm .sk .b { stroke:var(--fg); }
.sm .sk .a { stroke:var(--accent); }
.sm .sk .t { stroke:var(--sm-soft); stroke-width:1.4; stroke-linecap:round; }
"""


def contents() -> str:
    """The contents page (maquettes sommaire v1-v4 : S3, G2, D2): the rounds waiting for a vote, big, with
    their captures; then every other round by day, three by row, each with its capture (or Claude's sketch)."""
    all_rounds = rounds()
    out = []
    for r in (r for r in all_rounds if r.get("todo")):
        shots = "".join(f'<div style="background-image:url(\'{url(p)}\')"></div>' for p in r["shots"][:2])
        nth = "1er" if r["n"] == 1 else f"{r['n']}e"
        since = day_name(datetime.datetime.fromtimestamp(r["t"])).lower()
        out.append(f'<h2>À voter</h2><a class="big" href="{link(r)}">{f"<div class=shots>{shots}</div>" if shots else ""}'
                   f'<b>{html.escape(r["title"])}</b><span>{nth} tour, depuis {since}</span></a>')
    day = None
    for r in (r for r in all_rounds if not r.get("todo")):
        d = day_name(datetime.datetime.fromtimestamp(r["t"]))
        if d != day:
            out.append(("</div>" if day else "") + f'<h2>{d}</h2><div class="grid">')
            day = d
        out.append(f'<a class="cell" href="{link(r)}">{thumb(r)}<span>{html.escape(r["title"])}</span></a>')
    if day:
        out.append("</div>")
    name = html.escape(APPLI)
    return f"""<!doctype html><html lang="fr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>Maquettes {name}</title>
<link rel="stylesheet" href="common/mock.css"><style>{STYLE}</style></head><body>
<svg width="0" height="0" style="position:absolute"><filter id="sm-pen"><feTurbulence type="fractalNoise" baseFrequency="0.04"
numOctaves="2" seed="3"/><feDisplacementMap in="SourceGraphic" scale="2.2"/></filter></svg>
<header class="page"><h1>Maquettes {name}</h1></header><main class="sm">{''.join(out)}</main></body></html>"""


if __name__ == "__main__":
    http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
