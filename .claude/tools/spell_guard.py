#!/usr/bin/env python3
"""Spell guard - no spelling or grammar mistake reaches a player.

Every text a player reads has its entry in Assets/Resources/Localization/texts.json (French source + English), the
coverage test makes sure of it. So the guard proofreads that table: at a `git commit`, every entry the commit adds
or changes (French and English) is read by Claude (`claude -p`, no tools, structured answer). A mistake blocks the
commit (exit 2) with the wrong text, the fix and why. Fix it where the French is written (the .cs, prefab, asset or
scene) AND in texts.json, in the same commit. Origin: "X à été soigné(e)" in the chat (2026-11-26), and ~30 more
mistakes found in the whole table that day (Choissisez, tout les, Heberger, Menu de connection...).

Hook (stdin = the hook payload):
  PreToolUse  Bash | PowerShell   on a `git commit` that stages texts.json: its new / changed entries are proofread.

What is checked: spelling, grammar, agreement, conjugation, accents, French typography (space before : ; ! ?).
Not checked: style, wording choices, proper names of the game, deliberate jargon (skip, lobby, hacker).

False positive: add the exact text, one per line, to .claude/tools/spell_guard_ok.txt (reviewed with the commit).
A text once read clean is remembered in .git/spell_guard_cache.json: a retried commit costs nothing.

Other uses:
  spell_guard.py --scan [root]   proofread the whole table (audit, ~10 calls)
  spell_guard.py --test          self-test of the offline parts (commit detection, diff of the table)

Any failure of the hook itself (no `claude`, timeout, bad answer) lets the commit through with a warning: a broken
guard must never block work.
"""
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time

TABLE = "Assets/Resources/Localization/texts.json"
OK_FILE = ".claude/tools/spell_guard_ok.txt"
MODEL = "sonnet"
BATCH = 45

COMMIT = re.compile(r"""\bgit\b(\s+-C\s+("[^"]*"|'[^']*'|\S+))?(\s+-c\s+\S+)*\s+commit(?![\w-])""")

SYSTEM = ("Tu es correcteur professionnel (français et anglais) pour un jeu vidéo. Tu ne relèves que des fautes "
          "certaines ; tu réponds uniquement par l'objet JSON demandé.")

PROMPT = """Relis ces textes de jeu vidéo (jeu de déduction sociale « Corruption du Portail »). Chaque ligne : un numéro, la langue (fr ou en), le texte entre guillemets.

Relève toute faute CERTAINE :
- orthographe, accents (« A son éveil » → « À son éveil », « connaitre » → « connaître »), mots mal écrits ;
- grammaire, accord (genre, nombre), conjugaison (« a / à », « tout les », « il réussi »), participes ;
- typographie française : espace avant « : ; ! ? » (« Votes: » → « Votes : ») ;
- en anglais : orthographe, grammaire, espace en trop avant « ? ! : ».

N'en relève PAS :
- le style, les tournures, le choix des mots, une ponctuation finale absente, les majuscules d'un titre ;
- les noms propres et termes du jeu : Ugës, Gloubi, Va'ahl, Mo'orkn, Hurluberluges, Abyss, Luma, Nara, ORIGINE, Élu(s), Anomalie(s), Marginal/Marginaux, robot, portail, lobby, skip, host, hacker, WIP ;
- les balises et variables : {{0}}, {{var:x}}, <color=…>, <i>, <b>, <u>, ⏎ (= saut de ligne), les caractères ⢵ ⢶ (chiffres dessinés) ;
- un texte laissé exprès en anglais dans la colonne fr.

Textes :
{lines}"""

SCHEMA = {
    "type": "object",
    "properties": {
        "fautes": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "numero": {"type": "integer"},
                    "faute": {"type": "string"},
                    "correction": {"type": "string"},
                    "pourquoi": {"type": "string"},
                },
                "required": ["numero", "faute", "correction", "pourquoi"],
            },
        }
    },
    "required": ["fautes"],
}


# ---------------------------------------------------------------------------------------------- the table

def entries_of(text):
    try:
        return [(e.get("fr") or "", e.get("en") or "") for e in json.loads(text).get("entries", [])]
    except (ValueError, AttributeError):
        return []


def changed_texts(old_text, new_text):
    """(lang, text) of every value the new table has and the old one did not."""
    old = set()
    for fr, en in entries_of(old_text):
        old.add(("fr", fr))
        old.add(("en", en))
    out = []
    for fr, en in entries_of(new_text):
        for item in (("fr", fr), ("en", en)):
            if item[1].strip() and item not in old and item not in out:
                out.append(item)
    return out


def git(repo, *args):
    r = subprocess.run(["git", "-C", repo] + list(args), capture_output=True, text=True, encoding="utf-8",
                       errors="replace")
    return r.stdout if r.returncode == 0 else None


def read_ok(repo):
    try:
        return set(l.rstrip("\n") for l in io.open(os.path.join(repo, OK_FILE), encoding="utf-8")
                   if l.strip() and not l.startswith("#"))
    except OSError:
        return set()


def cache_path(repo):
    common = git(repo, "rev-parse", "--git-common-dir")
    if not common:
        return None
    common = common.strip()
    return os.path.join(common if os.path.isabs(common) else os.path.join(repo, common), "spell_guard_cache.json")


def key(lang, text):
    return hashlib.sha1(("%s\n%s" % (lang, text)).encode("utf-8")).hexdigest()


# ---------------------------------------------------------------------------------------------- proofreading

def proofread(items, label):
    """[(lang, text)] -> list of (lang, text, faute, correction, pourquoi), or None when Claude could not answer."""
    exe = shutil.which("claude")
    if not exe:
        return None
    lines = "\n".join('%d %s "%s"' % (i, lang, text.replace("\r", "").replace("\n", "⏎"))
                      for i, (lang, text) in enumerate(items, 1))
    env = dict(os.environ, MAX_THINKING_TOKENS="0", CLAUDE_CODE_DISABLE_CLAUDE_MDS="1")
    try:
        out = subprocess.run(
            [exe, "-p", "--model", MODEL, "--no-session-persistence", "--setting-sources", "", "--strict-mcp-config",
             "--disable-slash-commands", "--output-format", "json", "--tools", "", "--system-prompt", SYSTEM,
             "--json-schema", json.dumps(SCHEMA)],
            # The prompt goes through stdin: on Windows, claude.CMD cuts an argument at its first line break.
            input=PROMPT.format(lines=lines), capture_output=True, text=True, encoding="utf-8", timeout=170, env=env,
            creationflags=0x08000000 if os.name == "nt" else 0,
        ).stdout
        r = json.loads(out)
    except Exception:
        return None
    try:  # usage shown under "Hooks" in Clawd's Quota screen
        with io.open(os.path.join(os.path.expanduser("~"), ".claude", "hook-usage.jsonl"), "a", encoding="utf-8") as f:
            f.write(json.dumps({"t": time.time(), "hook": "Orthographe (%s)" % label, "sid": r.get("session_id"),
                                "models": r.get("modelUsage")}) + "\n")
    except Exception:
        pass
    answer = r.get("structured_output")
    if answer is None:
        try:
            raw = r.get("result") or ""
            answer = json.loads(raw[raw.index("{"):raw.rindex("}") + 1])
        except Exception:
            return None
    found = []
    for f in answer.get("fautes") or []:
        n = f.get("numero")
        if isinstance(n, int) and 1 <= n <= len(items):
            lang, text = items[n - 1]
            found.append((lang, text, f.get("faute", ""), f.get("correction", ""), f.get("pourquoi", "")))
    return found


def check(repo, items, label):
    """Proofread what is neither accepted nor cached. Returns (mistakes, could_check)."""
    ok = read_ok(repo)
    path = cache_path(repo)
    try:
        cache = json.load(io.open(path, encoding="utf-8")) if path else {}
    except (OSError, ValueError):
        cache = {}
    todo = [(lang, text) for lang, text in items if text not in ok and key(lang, text) not in cache]
    mistakes, complete = [], True
    for start in range(0, len(todo), BATCH):
        chunk = todo[start:start + BATCH]
        found = proofread(chunk, label)
        if found is None:
            complete = False
            continue
        wrong = set((lang, text) for lang, text, _, _, _ in found)
        for lang, text in chunk:
            if (lang, text) not in wrong:
                cache[key(lang, text)] = int(time.time())
        mistakes.extend(found)
    if path:
        try:
            io.open(path, "w", encoding="utf-8").write(json.dumps(cache))
        except OSError:
            pass
    return mistakes, complete


def report(mistakes, header):
    lines = [header]
    for lang, text, faute, correction, why in mistakes[:40]:
        short = text.replace("\n", "⏎")
        lines.append('  [%s] "%s"\n      %s -> %s  (%s)' % (lang, short[:140] + ("…" if len(short) > 140 else ""),
                                                          faute, correction, why))
    lines.append("Fix the text where it is written (.cs, prefab, asset, scene: the French is the key) and in "
                 "%s, in the same commit. False positive: add the exact text to %s." % (TABLE, OK_FILE))
    sys.stderr.write("\n".join(lines) + "\n")


# ---------------------------------------------------------------------------------------------- hook / modes

def repo_of(command, cwd):
    match = re.search(r"\bgit\s+-C\s+(\"([^\"]+)\"|'([^']+)'|(\S+))", command)
    if not match:
        match = re.search(r"(?:^|[;&|]\s*)(?:cd|Set-Location)\s+(\"([^\"]+)\"|'([^']+)'|([^\s;&|]+))", command)
    repo = next((g for g in (match.groups()[1:] if match else ()) if g), cwd)
    drive = re.match(r"^/([a-zA-Z])/(.*)$", repo or "")
    return "%s:/%s" % (drive.group(1), drive.group(2)) if drive else repo


def staged_texts(repo, everything):
    old = git(repo, "show", "HEAD:" + TABLE)
    if everything:
        try:
            new = io.open(os.path.join(repo, TABLE), encoding="utf-8-sig").read()
        except OSError:
            return []
    else:
        new = git(repo, "show", ":" + TABLE)
    if new is None:
        return []
    return changed_texts(old or '{"entries": []}', new)


def before_commit(payload):
    command = str((payload.get("tool_input") or {}).get("command") or "")
    if not COMMIT.search(command) or re.search(r"--dry-run", command):
        return 0
    repo = repo_of(command, str(payload.get("cwd") or ""))
    if not repo or not os.path.isdir(repo):
        return 0
    everything = bool(re.search(r"\scommit\b.*\s(-a\b|--all\b|-[a-zA-Z]*a[a-zA-Z]*\s)", command))
    items = staged_texts(repo, everything)
    if not items:
        return 0
    mistakes, complete = check(repo, items, "commit")
    if mistakes:
        report(mistakes, "Spell guard: %d mistake(s) in the player texts this commit adds." % len(mistakes))
        return 2
    if not complete:
        sys.stderr.write("Spell guard: could not proofread the new texts (claude unavailable); commit let through.\n")
    return 0


def scan(root):
    try:
        text = io.open(os.path.join(root, TABLE), encoding="utf-8-sig").read()
    except OSError as e:
        print("cannot read the table: %s" % e)
        return 1
    items = changed_texts('{"entries": []}', text)
    mistakes, complete = check(root, items, "audit")
    if mistakes:
        report(mistakes, "Spell guard: %d mistake(s) in %s." % (len(mistakes), TABLE))
    print("spell_guard scan: %d text(s), %d mistake(s)%s" % (len(items), len(mistakes),
                                                              "" if complete else ", some batches unchecked"))
    return 1 if mistakes or not complete else 0


def self_test():
    failures = []
    for command, expected in [("git commit -m x", True), ('git -C "C:/a b" commit -F msg', True),
                              ("git commit-tree abc", False), ("git log --grep commit", False),
                              ("cd /c/x && git add . && git commit -m y", True)]:
        if bool(COMMIT.search(command)) != expected:
            failures.append("commit detection: %r" % command)
    old = json.dumps({"entries": [{"fr": "Bonjour", "en": "Hello"}, {"fr": "Il à été", "en": "He was"}]})
    new = json.dumps({"entries": [{"fr": "Bonjour", "en": "Hi"}, {"fr": "Il a été", "en": "He was"},
                                  {"fr": "Nouveau", "en": "New"}]})
    got = changed_texts(old, new)
    want = [("en", "Hi"), ("fr", "Il a été"), ("fr", "Nouveau"), ("en", "New")]
    if got != want:
        failures.append("changed texts: %r" % got)
    if repo_of("cd /c/Users/x && git commit -m y", "C:/z") != "c:/Users/x":
        failures.append("repo_of: %r" % repo_of("cd /c/Users/x && git commit -m y", "C:/z"))
    for failure in failures:
        print("FAIL " + failure)
    print("spell_guard self-test: %s" % ("%d failure(s)" % len(failures) if failures else "ok"))
    return 1 if failures else 0


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        return self_test()
    if len(sys.argv) > 1 and sys.argv[1] == "--scan":
        return scan(sys.argv[2] if len(sys.argv) > 2 else os.getcwd())
    try:
        raw = sys.stdin.buffer.read().decode("utf-8-sig", errors="replace")
        if not raw.strip():
            return 0
        payload = json.loads(raw)
        if payload.get("tool_name") in ("Bash", "PowerShell") and payload.get("hook_event_name") != "PostToolUse":
            return before_commit(payload)
        return 0
    except Exception:  # a broken guard never blocks work
        return 0


if __name__ == "__main__":
    sys.exit(main())
