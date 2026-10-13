"""Serves the game's UI mockups on 127.0.0.1:5198 (Poyo opens them on his phone through the session's "Aperçu de
l'appli", or in a browser on the PC). One folder per subject (personnalisation/, ...), one page per round (v1.html,
v2.html...). GET /retours?t=<text>&s=<subject> appends the feedback to <subject>/retours.txt: the preview relay does
not carry POST bodies. Adapted from the Clawd mockups (C:/Users/azert/Projects/clawd/maquettes)."""

import datetime
import html
import http.server
import pathlib
import re
import urllib.parse

HERE = pathlib.Path(__file__).resolve().parent
PORT = 5198


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=str(HERE), **k)

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        if url.path in ("/", "/index.html"):  # every round of every subject, newest first (the preview opens "/")
            return self.send_html(contents())
        if url.path == "/retours":
            query = urllib.parse.parse_qs(url.query)
            text = query.get("t", [""])[0]
            subject = re.sub(r"[^\w-]", "", query.get("s", ["divers"])[0]) or "divers"
            stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
            folder = HERE / subject
            folder.mkdir(exist_ok=True)
            with open(folder / "retours.txt", "a", encoding="utf-8") as f:
                f.write(f"--- {stamp}\n{text}\n")
            body = b'{"ok":true}'
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()

    def send_html(self, text):
        body = text.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):  # pythonw has no stderr
        pass

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


def contents() -> str:
    """Every round (<subject>/v<N>.html), newest first, with its title."""
    pages = sorted(HERE.glob("*/v*.html"), key=lambda f: f.stat().st_mtime, reverse=True)
    rows = []
    for i, f in enumerate(pages):
        m = re.search(r"<title>(.*?)</title>", f.read_text(encoding="utf-8"))
        title = html.escape(m.group(1) if m else f.stem)
        new = " <em>nouveau</em>" if i == 0 else ""
        href = f"{f.parent.name}/{f.name}"
        rows.append(f'<a class="card" href="{href}"><b>{f.parent.name}/{f.stem}</b> {title}{new}</a>')
    return f"""<!doctype html><html lang="fr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>Maquettes</title>
<link rel="stylesheet" href="common/mock.css"></head><body><header class="page"><h1>Maquettes</h1>
<p>La plus récente en haut.</p></header>{''.join(rows)}</body></html>"""


if __name__ == "__main__":
    http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
