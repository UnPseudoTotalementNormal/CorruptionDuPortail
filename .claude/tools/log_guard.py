#!/usr/bin/env python3
"""Garde du journal : le code d'un projet part avec ses lignes de journal.

Deux moments, un seul script (entrées PreToolUse et PostToolUse de settings.json) :

- PostToolUse (Edit | MultiEdit | Write) : un fichier de code surveillé vient d'être modifié. Si ce qu'il a de nouveau
  depuis le dernier commit fait au moins `edit_lines` lignes de code sans aucun appel au journal, Claude reçoit le
  rappel (sortie 2, texte sur stderr), une fois par fichier et par session. Rien n'est bloqué.
- PreToolUse (Bash | PowerShell) sur un `git commit` : si le commit ajoute au moins `commit_lines` lignes de code
  surveillé sans aucun appel au journal, il est refusé. Rien à journaliser pour de bon (mise en page, renommage, pur
  calcul) : une ligne `Logs: none (<pourquoi>)` (ou `Logs: aucun (…)`) dans le message du commit.

Réglages : `log_guard.json` à côté de ce script (ou `--config <fichier>`) :

    {
      "repo": ".",                       # le dépôt surveillé, relatif au dossier du réglage ; défaut : celui qui le contient
      "rule": ".claude/rules/logs.md",   # où Claude lit quoi journaliser (relatif au dépôt)
      "commit_lines": 8,                 # 0 = pas de refus au commit
      "edit_lines": 8,                   # 0 = pas de rappel à l'écriture
      "parts": [                         # une partie par journal (un projet peut en avoir deux : serveur, appli)
        {"name": "appli",
         "code": ["src/**/*.py"],        # fichiers surveillés, motifs relatifs au dépôt (** = n'importe quel sous-dossier)
         "skip": ["**/tests/**"],        # jamais surveillés (tests, le journal lui-même, code généré)
         "call": "\\\\bjournal\\\\.(log|warn|error)\\\\(",   # une expression régulière : un appel au journal
         "hint": "journal.log / warn / error"}               # comment Claude l'appelle, dit dans le refus
      ]
    }

Un commit fait dans un worktree du dépôt est surveillé aussi ; un commit dans un autre dépôt est ignoré. Une panne de
ce script laisse tout passer : une garde cassée ne doit jamais bloquer le travail.

    python log_guard.py --test               # ses essais, un vrai dépôt jetable compris ; finit par « ok »
    python log_guard.py --check [fichier]    # ce que dirait un commit maintenant (index du dépôt surveillé)
"""
from __future__ import annotations

import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

ESCAPE = re.compile(r"Logs\s*:\s*(none|aucun)\b", re.I)
COMMIT = re.compile(r"\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b")
# Lignes qui ne sont pas du code à journaliser : commentaires, imports, accolades seules, annotations.
NOT_CODE = re.compile(r'^(#|//|/\*|\*|"""|\'\'\'|<!--|@|[{}\[\]();,]+$|import\s|from\s\S+\s+import\s|using\s|package\s)')


# ---------------------------------------------------------------- réglages


def _path(p: str) -> str:
    """/c/Users/x (Git Bash) -> c:/Users/x."""
    m = re.match(r"^/([a-zA-Z])/(.*)$", p)
    return f"{m.group(1)}:/{m.group(2)}" if m else p


def glob_re(pattern: str) -> re.Pattern:
    """Un motif de chemin (`**`, `*`, `?`) en expression régulière, `/` comme séparateur."""
    out, i = "", 0
    pattern = pattern.replace("\\", "/")
    while i < len(pattern):
        c = pattern[i]
        if pattern.startswith("**/", i):
            out += "(?:.*/)?"
            i += 3
        elif pattern.startswith("**", i):
            out += ".*"
            i += 2
        elif c == "*":
            out += "[^/]*"
            i += 1
        elif c == "?":
            out += "[^/]"
            i += 1
        else:
            out += re.escape(c)
            i += 1
    return re.compile(f"^{out}$", re.I)


class Part:
    def __init__(self, raw: dict):
        self.name = str(raw.get("name") or "code")
        self.code = [glob_re(p) for p in raw.get("code") or []]
        self.skip = [glob_re(p) for p in raw.get("skip") or []]
        self.call = re.compile(raw.get("call") or r"\blog\.")
        self.hint = str(raw.get("hint") or raw.get("call") or "")

    def covers(self, rel: str) -> bool:
        rel = rel.replace("\\", "/")
        return any(p.match(rel) for p in self.code) and not any(p.match(rel) for p in self.skip)


class Config:
    def __init__(self, file: pathlib.Path):
        raw = json.loads(file.read_text(encoding="utf-8"))
        self.file = file
        base = file.parent
        self.repo = str((base / raw["repo"]).resolve()) if raw.get("repo") else str(base)
        self.rule = str(raw.get("rule") or ".claude/rules/logs.md")
        self.commit_lines = int(raw.get("commit_lines", 8))
        self.edit_lines = int(raw.get("edit_lines", 8))
        self.parts = [Part(p) for p in raw.get("parts") or []]
        self.common = common_dir(self.repo)

    def part(self, rel: str) -> Part | None:
        return next((p for p in self.parts if p.covers(rel)), None)


def config_file(argv: list[str]) -> pathlib.Path:
    if "--config" in argv:
        return pathlib.Path(_path(argv[argv.index("--config") + 1]))
    return pathlib.Path(__file__).resolve().with_name("log_guard.json")


def load(argv: list[str]) -> Config | None:
    """None without a log_guard.json; one that does not read (bad JSON, bad expression) raises BrokenConfig."""
    f = config_file(argv)
    if not f.is_file():
        return None
    try:
        return Config(f)
    except Exception as e:  # noqa: BLE001
        raise BrokenConfig(f"{f} : {type(e).__name__}: {e}") from e


class BrokenConfig(Exception):
    pass


def broken_notice(payload: dict, why: str) -> str:
    """A guard that cannot read its settings checks nothing: said once per session, never blocking. Before 1.0.1 it was
    silent, and a log_guard.json whose `\\\\` a shell had eaten let every commit through unseen."""
    event = payload.get("hook_event_name")
    cmd = str((payload.get("tool_input") or {}).get("command") or "")
    if event == "PreToolUse" and not COMMIT.search(cmd):
        return ""
    key = hashlib.sha1(f"broken|{payload.get('session_id', '')}|{why}".encode()).hexdigest()[:12]
    seen = pathlib.Path(tempfile.gettempdir()) / f"log-guard-{key}.txt"
    if seen.exists():
        return ""
    seen.write_text("1", encoding="utf-8")
    text = (f"Garde du journal en panne, rien n'est vérifié : réglage illisible ({why}). Le corriger (avec l'outil "
            "d'écriture plutôt qu'un shell, qui avale les \\\\), puis `python log_guard.py --check`.")
    out = {"systemMessage": text}
    if event == "PostToolUse":
        out["hookSpecificOutput"] = {"hookEventName": "PostToolUse", "additionalContext": text}
    return json.dumps(out, ensure_ascii=False)


# ---------------------------------------------------------------- git


def git(repo: str, *args: str) -> str:
    done = subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=20)
    return done.stdout if done.returncode == 0 else ""


def common_dir(d: str) -> str:
    """Le .git partagé par un dépôt et ses worktrees ("" hors d'un dépôt)."""
    out = git(d, "rev-parse", "--path-format=absolute", "--git-common-dir").strip()
    return os.path.normcase(os.path.normpath(out)) if out else ""


def top(d: str) -> str:
    return git(d, "rev-parse", "--show-toplevel").strip()


# ---------------------------------------------------------------- comptage


def count(diff: str, cfg: Config) -> dict[str, list]:
    """{partie: [lignes de code ajoutées, appels au journal ajoutés, fichiers]} d'un diff unifié."""
    out: dict[str, list] = {}
    part = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            f = line[6:] if line.startswith("+++ b/") else ""
            part = cfg.part(f) if f else None
            if part:
                entry = out.setdefault(part.name, [0, 0, []])
                if f not in entry[2]:
                    entry[2].append(f)
            continue
        if part is None or not line.startswith("+"):
            continue
        text = line[1:].strip()
        if not text or NOT_CODE.match(text):
            continue
        out[part.name][0] += 1
        if part.call.search(text):
            out[part.name][1] += 1
    return out


def missing(counts: dict[str, list], least: int) -> list[tuple[str, int, list]]:
    return [(name, n, files) for name, (n, calls, files) in counts.items() if least and n >= least and not calls]


def hint(cfg: Config, name: str) -> str:
    p = next((p for p in cfg.parts if p.name == name), None)
    return p.hint if p else ""


# ---------------------------------------------------------------- avant un commit


def _arg(cmd: str, pattern: str) -> str:
    m = re.search(pattern + r"""("([^"]+)"|'([^']+)'|([^\s;&|]+))""", cmd)
    return _path(next(g for g in m.groups()[1:] if g)) if m else ""


def commit_repo(cmd: str, cwd: str) -> str:
    """Le dossier où le commit tourne : `git -C <dir>`, sinon `cd <dir>`, sinon le dossier de la session."""
    d = _arg(cmd, r"\bgit\s+-C\s+") or _arg(cmd, r"(?:^|[;&|]\s*)(?:cd|Set-Location|pushd)\s+") or cwd
    return d if os.path.isabs(d) else os.path.join(cwd, d)


def before_commit(payload: dict, cfg: Config) -> str:
    """Le texte qui refuse le commit ("" = il passe)."""
    cmd = str((payload.get("tool_input") or {}).get("command") or "")
    if not COMMIT.search(cmd) or re.search(r"--amend\b.*--no-edit|--dry-run", cmd) or ESCAPE.search(cmd):
        return ""
    if not cfg.commit_lines:
        return ""
    repo = commit_repo(cmd, str(payload.get("cwd") or os.getcwd()))
    if not os.path.isdir(repo) or not cfg.common or common_dir(repo) != cfg.common:
        return ""
    message = _arg(cmd, r"(?:-F|--file)[\s=]+")
    if message and message != "-":
        f = message if os.path.isabs(message) else os.path.join(repo, message)
        try:
            if ESCAPE.search(pathlib.Path(f).read_text(encoding="utf-8", errors="replace")):
                return ""
        except OSError:
            pass
    every = re.search(r"\scommit\b.*\s(-a\b|--all\b|-[a-zA-Z]*a[a-zA-Z]*\s)", cmd)
    diff = git(repo, "diff", "--unified=0", *(["HEAD"] if every else ["--cached"]))
    bad = missing(count(diff, cfg), cfg.commit_lines)
    if not bad:
        return ""
    lines = []
    for name, n, files in bad:
        lines.append(f"- {name} : {n} lignes de code sans {hint(cfg, name)}")
        lines += [f"    {f}" for f in files[:8]]
    return ("Garde du journal : ce commit ajoute du code sans rien écrire au journal.\n" + "\n".join(lines) + "\n\n"
            f"Chaque changement de comportement part avec ses lignes de journal ({cfg.rule}) : chaque décision avec "
            "sa raison, chaque refus silencieux (un `return` qui ne fait rien) avec sa raison, chaque erreur rattrapée. "
            "Ajoute-les puis recommence le commit. Rien à journaliser pour de bon (mise en page, renommage, pur "
            "calcul) : une ligne `Logs: none (<pourquoi>)` dans le message du commit.")


# ---------------------------------------------------------------- après une écriture


def after_edit(payload: dict, cfg: Config) -> str:
    """Le rappel après l'écriture d'un fichier surveillé ("" = rien à dire)."""
    if not cfg.edit_lines:
        return ""
    file = _path(str((payload.get("tool_input") or {}).get("file_path") or ""))
    if not file or not os.path.isfile(file):
        return ""
    where = os.path.dirname(file)
    root = top(where)
    if not root or not cfg.common or common_dir(where) != cfg.common:
        return ""
    rel = os.path.relpath(file, root).replace("\\", "/")
    part = cfg.part(rel)
    if not part:
        return ""
    diff = git(root, "diff", "--unified=0", "HEAD", "--", rel)
    if not diff and not git(root, "ls-files", "--", rel):  # fichier neuf : tout son contenu est nouveau
        body = pathlib.Path(file).read_text(encoding="utf-8", errors="replace")
        diff = f"+++ b/{rel}\n" + "\n".join("+" + line for line in body.splitlines())
    bad = missing(count(diff, cfg), cfg.edit_lines)
    if not bad:
        return ""
    key = hashlib.sha1(f"{cfg.common}|{payload.get('session_id', '')}".encode()).hexdigest()[:12]
    seen = pathlib.Path(tempfile.gettempdir()) / f"log-guard-{key}.txt"
    already = seen.read_text(encoding="utf-8").splitlines() if seen.exists() else []
    if rel in already:
        return ""
    seen.write_text("\n".join(already + [rel]), encoding="utf-8")
    n = bad[0][1]
    return (f"Garde du journal : {rel} a {n} lignes de code nouvelles et aucun appel au journal "
            f"({part.hint}).\nQuoi journaliser : {cfg.rule}. Ajoute les lignes utiles maintenant ; s'il n'y a vraiment "
            "rien à journaliser ici, continue : le commit demandera `Logs: none (<pourquoi>)`.")


# ---------------------------------------------------------------- essais


def _test() -> int:
    tmp = pathlib.Path(tempfile.mkdtemp(prefix="log-guard-test-"))
    repo = tmp / "projet"
    (repo / ".claude" / "tools").mkdir(parents=True)
    (repo / "src" / "app").mkdir(parents=True)
    (repo / "tests").mkdir()
    cfg_file = repo / ".claude" / "tools" / "log_guard.json"
    cfg_file.write_text(json.dumps({"repo": "../..", "commit_lines": 8, "edit_lines": 8, "parts": [
        {"name": "appli", "code": ["src/**/*.py"], "skip": ["src/app/journal.py", "**/tests/**"],
         "call": r"\bjournal\.(log|warn|error)\(", "hint": "journal.log"},
        {"name": "jeu", "code": ["Assets/Scripts/**/*.cs"], "skip": ["**/Editor/**"], "call": r"\bGameLog\.",
         "hint": "GameLog.Info"}]}), encoding="utf-8")
    run = lambda *a: subprocess.run(["git", "-C", str(repo), *a], capture_output=True, text=True, check=True)  # noqa: E731
    run("init", "-q")
    run("config", "user.email", "t@t")
    run("config", "user.name", "t")
    (repo / "README.md").write_text("x\n", encoding="utf-8")
    run("add", ".")
    run("commit", "-qm", "init")
    cfg = Config(cfg_file)
    assert cfg.common, "dépôt jetable introuvable"

    # motifs
    assert cfg.part("src/app/quota.py") and not cfg.part("src/app/journal.py")
    assert cfg.part("src/deep/er/x.py") and not cfg.part("tests/test_a.py")
    assert cfg.part("Assets/Scripts/Game/Vote.cs").name == "jeu" and not cfg.part("Assets/Scripts/Editor/Tool.cs")

    # comptage
    py = "+++ b/src/app/quota.py\n" + "".join(f"+    x{i} = f({i})\n" for i in range(9))
    assert missing(count(py, cfg), 8)
    assert not missing(count(py + '+    journal.log("quota", "route", sid=s)\n', cfg), 8)
    assert not missing(count("+++ b/src/app/a.py\n" + "".join(f"+# commentaire {i}\n" for i in range(20)), cfg), 8)
    assert not missing(count(py.replace("src/app/quota.py", "tests/test_q.py"), cfg), 8)

    # commit réel
    body = "".join(f"value{i} = compute({i})\n" for i in range(10))
    (repo / "src" / "app" / "quota.py").write_text(body, encoding="utf-8")
    run("add", ".")
    pay = lambda c, cwd=str(repo): {"hook_event_name": "PreToolUse", "cwd": cwd, "tool_input": {"command": c}}  # noqa: E731
    assert before_commit(pay('git commit -m "add quota"'), cfg).startswith("Garde du journal")
    assert before_commit(pay(f'git -C "{repo.as_posix()}" commit -m "x"', cwd=str(tmp)), cfg)
    assert before_commit(pay(f'cd "{repo.as_posix()}" && git commit -m "x"', cwd=str(tmp)), cfg)
    assert before_commit(pay('git commit -m "x\n\nLogs: none (pur calcul)"'), cfg) == ""
    assert before_commit(pay("git commit -m @'\nx\n\nLogs: aucun (refactor)\n'@"), cfg) == ""
    (tmp / "msg.txt").write_text("x\n\nLogs: none (renommage)\n", encoding="utf-8")
    assert before_commit(pay(f'git commit -F "{(tmp / "msg.txt").as_posix()}"'), cfg) == ""
    assert before_commit(pay("git status"), cfg) == ""
    assert before_commit(pay('git commit --amend --no-edit'), cfg) == ""
    other = tmp / "autre"
    other.mkdir()
    subprocess.run(["git", "-C", str(other), "init", "-q"], check=True)
    assert before_commit(pay('git commit -m "x"', cwd=str(other)), cfg) == ""  # un autre dépôt
    (repo / "src" / "app" / "quota.py").write_text(body + 'journal.log("quota", "start", n=1)\n', encoding="utf-8")
    run("add", ".")
    assert before_commit(pay('git commit -m "x"'), cfg) == ""

    # worktree du même dépôt
    run("commit", "-qm", "quota", "--no-verify")
    wt = tmp / "wt"
    run("worktree", "add", "-q", str(wt), "-b", "wt")
    (wt / "src" / "app" / "other.py").write_text(body, encoding="utf-8")
    subprocess.run(["git", "-C", str(wt), "add", "."], check=True)
    assert before_commit(pay('git commit -m "x"', cwd=str(wt)), cfg), "worktree non surveillé"

    # après une écriture : rappel une fois par fichier et par session
    new = repo / "src" / "app" / "neuf.py"
    new.write_text(body, encoding="utf-8")
    edit = {"hook_event_name": "PostToolUse", "session_id": f"essai-{os.getpid()}",
            "tool_input": {"file_path": str(new)}}
    assert after_edit(edit, cfg).startswith("Garde du journal")
    assert after_edit(edit, cfg) == ""
    readme = {**edit, "tool_input": {"file_path": str(repo / "README.md")}}
    assert after_edit(readme, cfg) == ""

    # réglages : seuil à 0 = éteint
    off = json.loads(cfg_file.read_text(encoding="utf-8"))
    off["commit_lines"] = 0
    cfg_file.write_text(json.dumps(off), encoding="utf-8")
    run("reset", "-q")
    (repo / "src" / "app" / "quota.py").write_text(body * 2, encoding="utf-8")
    run("add", ".")
    assert before_commit(pay('git commit -m "x"'), Config(cfg_file)) == ""

    # réglage illisible (un `\\` avalé par un shell) : dit une fois, jamais bloquant
    cfg_file.write_text('{"parts": [{"code": ["src/**/*.py"], "call": "\\bjournal\\.("}]}', encoding="utf-8")
    try:
        load(["--config", str(cfg_file)])
        raise AssertionError("réglage illisible accepté")
    except BrokenConfig:
        pass
    sid = f"essai-panne-{os.getpid()}"
    commit = {"hook_event_name": "PreToolUse", "session_id": sid, "tool_input": {"command": "git commit -m x"}}
    assert "en panne" in json.loads(broken_notice(commit, "x"))["systemMessage"]
    assert broken_notice(commit, "x") == ""
    assert broken_notice({**commit, "tool_input": {"command": "git status"}, "session_id": sid + "b"}, "x") == ""

    subprocess.run(["git", "-C", str(repo), "worktree", "remove", "--force", str(wt)], capture_output=True)
    print("log_guard ok")
    return 0


def _check(cfg: Config) -> int:
    """The settings read, then what a commit of the staged files would give."""
    print(f"réglage lu : {cfg.file} ; dépôt {top(cfg.repo) or '(aucun : rien ne sera vérifié)'} ; "
          + ", ".join(f"{p.name} ({len(p.code)} motifs)" for p in cfg.parts))
    diff = git(cfg.repo, "diff", "--unified=0", "--cached")
    counts = count(diff, cfg)
    for name, (n, calls, files) in counts.items():
        print(f"{name} : {n} lignes de code, {calls} appels au journal, {len(files)} fichiers")
    if not counts:
        print("rien d'indexé dans le code surveillé")
    return 0 if cfg.common else 1


def main() -> int:
    argv = sys.argv[1:]
    if "--test" in argv:
        return _test()
    if "--check" in argv:
        try:
            cfg = load(argv)
        except BrokenConfig as e:
            print(f"réglage illisible : {e}")
            return 1
        if not cfg:
            print(f"pas de réglage : {config_file(argv)}")
            return 1
        return _check(cfg)
    try:
        raw = sys.stdin.read()
        if not raw.strip():
            return 0
        payload = json.loads(raw)
        try:
            cfg = load(argv)
        except BrokenConfig as e:
            notice = broken_notice(payload, str(e))
            if notice:
                print(notice)
            return 0
        if not cfg:
            return 0
        event = payload.get("hook_event_name")
        why = after_edit(payload, cfg) if event == "PostToolUse" else before_commit(payload, cfg)
    except Exception:  # noqa: BLE001 - une garde cassée ne bloque jamais
        return 0
    if why:
        sys.stderr.write(why + "\n")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
