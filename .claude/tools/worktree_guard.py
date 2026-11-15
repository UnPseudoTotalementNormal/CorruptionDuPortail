"""Keeps a project's main checkout out of reach: each Claude session works in its own worktree, so sessions running at the
same time stop mixing their lines and breaking each other's build.

Hook PreToolUse of the project's .claude/settings.json:
  - Edit | Write | MultiEdit | NotebookEdit of a file in a main checkout: blocked, it says how to get a worktree;
  - Bash | PowerShell `git commit` in a main checkout: blocked, unless everything staged is free.
Free: what git ignores, and the `free` list. A worktree is never concerned.

Lives in <project>/.claude/tools/. The repositories guarded are the project's, and those right under it that it ignores
(a workspace of several repos). Optional worktree_guard.json next to it:
  {"repos": [".", "app"], "free": [".claude/", "CLAUDE.md", "docs/"]}

Starts asleep so that sessions at work are not stopped halfway: it arms itself the first time the main checkouts have no
uncommitted change (`worktree-guard-armed` in the .git of the first repo). Any failure of the hook itself lets the action
through: a broken guard must never block work.

It also notes, on every call, the session worktrees (<project>/.claude/worktrees/<name>) the call works in, so that
`session_worktree.py clean` leaves alone what was used lately; and it refuses to write into one that clean has removed.

    python worktree_guard.py --test
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
FREE = [".claude/", "CLAUDE.md"]
COMMIT = re.compile(r"\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b")


def _plain(p: str) -> str:
    return os.path.normpath(re.sub(r"^/([a-zA-Z])/", lambda m: f"{m.group(1)}:/", p))


def _norm(p) -> str:
    return os.path.normcase(_plain(str(p)))


def _git(cwd, *args) -> tuple[int, str]:
    p = subprocess.run(["git", "-c", "core.quotepath=off", "-C", str(cwd), *args], capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=15)
    return p.returncode, p.stdout


def config(root: pathlib.Path) -> tuple[list[pathlib.Path], list[str]]:
    path = HERE / "worktree_guard.json"
    conf = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    if "repos" in conf:
        repos = [(root / r).resolve() for r in conf["repos"]]
    else:
        repos = [root] + [d for d in sorted(root.iterdir()) if d.is_dir() and (d / ".git").is_dir()
                          and _git(root, "check-ignore", "-q", d.name + "/")[0] == 0]
    return repos, list(conf.get("free", FREE))


def armed(repos: list[pathlib.Path]) -> bool:
    mark = repos[0] / ".git" / "worktree-guard-armed"
    if mark.exists():
        return True
    if any(_git(r, "status", "--porcelain", "--untracked-files=no")[1].strip() for r in repos):
        return False
    mark.touch()
    return True


def _is_free(rel: str, free: list[str]) -> bool:
    return any(rel == f or (f.endswith("/") and rel.startswith(f)) for f in free)


def guarded_file(path: str, repos: list[pathlib.Path], free: list[str]) -> str:
    """The path relative to its main checkout when [path] must not be written there, else ""."""
    for repo in sorted(repos, key=lambda r: -len(str(r))):  # the nested repos first
        root = _norm(repo) + os.sep
        if _norm(path).startswith(root):
            rel = _plain(path)[len(root):].replace(os.sep, "/")
            if _is_free(rel, free) or _git(repo, "check-ignore", "-q", "--no-index", rel)[0] == 0:
                return ""
            return rel
    return ""


def _commit_dir(cmd: str, cwd: str) -> str:
    m = re.search(r"\bgit\s+-C\s+(\"([^\"]+)\"|'([^']+)'|(\S+))", cmd)
    if not m:
        m = re.search(r"(?:^|[;&|]\s*)(?:cd|Set-Location)\s+(\"([^\"]+)\"|'([^']+)'|([^\s;&|]+))", cmd)
    d = next(g for g in m.groups()[1:] if g) if m else cwd
    d = _plain(d)
    return d if os.path.isabs(d) else os.path.join(cwd, d)


def _how(root: pathlib.Path) -> str:
    tool = (HERE / "session_worktree.py").as_posix()
    return (f'Crée le worktree de cette session : python "{tool}" new <nom> ; il te donne le dossier, travailles-y en chemins '
            f'absolus. Pour finir : python "{tool}" land, lancé depuis ce dossier.\n'
            "Des changements à toi sont déjà là ? `git diff > x.patch`, `git checkout -- <tes fichiers>`, puis `git apply x.patch` "
            "dans le worktree ; jamais `git stash`, sa pile est commune à toutes les sessions.")


def _named(text: str, wdir: pathlib.Path) -> set[str]:
    """The session worktrees under [wdir] that [text] (a path, a command) names, whatever its slashes."""
    def flat(s: str) -> str:
        s = re.sub(r"[\\/]+", "/", s)
        return s.lower() if os.name == "nt" else s
    base = flat(str(wdir))
    forms = [base] + ([f"/{base[0]}{base[2:]}"] if base[1:2] == ":" else [])  # C:/x and Git Bash's /c/x
    names = {n for f in forms for n in re.findall(re.escape(f) + r"/([^/\s\"';&|`)]+)", flat(text))}
    return {n for n in names if not n.startswith(".") and not n.endswith("~")}


def seen(payload: dict, root: pathlib.Path) -> None:
    wdir = root / ".claude" / "worktrees"
    if not wdir.is_dir():
        return
    args = payload.get("tool_input") or {}
    text = " ".join(str(v) for v in (payload.get("cwd"), args.get("file_path"), args.get("notebook_path"), args.get("command")) if v)
    for name in _named(text, wdir):
        if (wdir / name / ".git").exists():
            d = root / ".git" / "session-worktrees"
            d.mkdir(exist_ok=True)
            (d / name).touch()


def check(payload: dict, root: pathlib.Path) -> str:
    repos, free = config(root)
    tool = str(payload.get("tool_name") or "")
    args = payload.get("tool_input") or {}
    if tool in ("Bash", "PowerShell"):
        cmd = str(args.get("command") or "")
        if not COMMIT.search(cmd):
            return ""
        code, top = _git(_commit_dir(cmd, str(payload.get("cwd") or os.getcwd())), "rev-parse", "--show-toplevel")
        repo = next((r for r in repos if code == 0 and _norm(r) == _norm(top.strip())), None)
        if repo is None or not armed(repos):
            return ""
        staged = [f for f in _git(repo, "diff", "--cached", "--name-only")[1].split("\n") if f]
        if staged and all(_is_free(f, free) for f in staged):
            return ""
        return f"Worktree guard : pas de commit dans le dossier principal {repo.name}/ (les sessions s'y mélangent).\n" + _how(root)
    path = str(args.get("file_path") or args.get("notebook_path") or "")
    wdir = root / ".claude" / "worktrees"
    marks = root / ".git" / "session-worktrees"
    gone = next((n for n in _named(path, wdir) if not (wdir / n).exists() and (marks / f"{n}.removed").exists()), "") if path else ""
    if gone:
        tool = (HERE / "session_worktree.py").as_posix()
        return (f"Worktree guard : le worktree {gone} n'existe plus (nettoyé : fusionné ou inactif depuis longtemps). "
                f'python "{tool}" revive {gone} le refait depuis sa branche ; new <nom> en donne un neuf.')
    rel = guarded_file(path, repos, free) if path else ""
    if not rel or not armed(repos):
        return ""
    return f"Worktree guard : {rel} est dans le dossier principal (les sessions s'y mélangent).\n" + _how(root)


def _test() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp).resolve() / "projet"
        (root / "app" / "src").mkdir(parents=True)
        def run(d, *a):
            subprocess.run(["git", *a], cwd=d, check=True, capture_output=True)
        for d in (root / "app", root):
            run(d, "init", "-q", "-b", "main")
            run(d, "config", "user.email", "t@t")
            run(d, "config", "user.name", "t")
        (root / ".gitignore").write_text("/app/\nbuild/\n")
        (root / "a.py").write_text("a\n")
        (root / "CLAUDE.md").write_text("c\n")
        (root / "app" / "src" / "b.py").write_text("b\n")
        run(root, "add", ".")
        run(root, "commit", "-qm", "i")
        run(root / "app", "add", ".")
        run(root / "app", "commit", "-qm", "i")
        repos, free = config(root)
        assert [r.name for r in repos] == ["projet", "app"], repos

        def edit(p):
            return {"tool_name": "Edit", "tool_input": {"file_path": str(p)}, "cwd": str(root)}

        def commit(where):
            return {"tool_name": "Bash", "tool_input": {"command": "git commit -m x"}, "cwd": str(where)}

        # asleep while a session has uncommitted work
        (root / "a.py").write_text("work\n")
        assert check(edit(root / "a.py"), root) == ""
        run(root, "checkout", "a.py")

        assert "a.py" in check(edit(root / "a.py"), root)  # clean: armed from now on
        assert (root / ".git" / "worktree-guard-armed").exists()
        assert "src/b.py" in check(edit(root / "app" / "src" / "b.py"), root)
        assert check(edit(root / "CLAUDE.md"), root) == ""
        assert check(edit(root / ".claude" / "rules" / "r.md"), root) == ""
        assert check(edit(root / ".claude" / "worktrees" / "w" / "app" / "src" / "b.py"), root) == ""
        assert check(edit(root / "build" / "x.bin"), root) == ""  # ignored by git
        assert check(edit(root.parent / "ailleurs.py"), root) == ""
        assert "pas de commit" in check(commit(root / "app"), root)
        (root / "CLAUDE.md").write_text("d\n")
        run(root, "add", "CLAUDE.md")
        assert check(commit(root), root) == ""  # only what is free is staged
        assert check({"tool_name": "Bash", "tool_input": {"command": "git status"}, "cwd": str(root)}, root) == ""

        # a worktree is free, and its commits too
        wt = root / ".claude" / "worktrees" / "w"
        run(root / "app", "worktree", "add", "-q", "-b", "w", str(wt / "app"))
        assert check(commit(wt / "app"), root) == ""

        # the worktrees a call works in are noted for the clean-up; one it removed refuses writes
        w0 = root / ".claude" / "worktrees" / "w0"
        run(root, "worktree", "add", "-q", "-b", "w0", str(w0))
        mark = root / ".git" / "session-worktrees" / "w0"
        seen({"tool_name": "Bash", "tool_input": {"command": f'cd "{w0}" && ls'}, "cwd": str(root)}, root)
        assert mark.exists()
        mark.unlink()
        seen(edit(str(w0 / "a.py").replace("\\", "/")), root)
        assert mark.exists()
        if os.name == "nt":
            p = str(w0 / "a.py").replace("\\", "/")
            seen(edit(f"/{p[0].lower()}{p[2:]}"), root)  # Git Bash's form
        assert check(edit(root / ".claude" / "worktrees" / "gone" / "a.py"), root) == ""  # never existed
        (root / ".git" / "session-worktrees" / "gone.removed").touch()  # left by session_worktree.py clean
        assert "n'existe plus" in check(edit(root / ".claude" / "worktrees" / "gone" / "a.py"), root)
        assert check(edit(w0 / "a.py"), root) == ""
    print("ok")
    return 0


def main() -> int:
    if "--test" in sys.argv:
        return _test()
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
        try:
            seen(payload, HERE.parent.parent)
        except Exception:  # noqa: BLE001 - the clean-up only waits longer
            pass
        why = check(payload, HERE.parent.parent) if payload else ""
    except Exception:  # noqa: BLE001 - a broken guard never blocks
        return 0
    if why:
        sys.stderr.write(why + "\n")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
