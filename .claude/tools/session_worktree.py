"""A session's own worktree, the way back, and the clean-up of the ones left behind.

    python session_worktree.py new <name>       <project>/.claude/worktrees/<name>, with a worktree of each repo the project ignores
    python session_worktree.py land             from inside it: rebases on the main branch, then fast-forwards it, under a lock
    python session_worktree.py clean [--dry-run] [--background]
    python session_worktree.py revive <name>    a worktree removed by `clean` comes back from its kept branch
    python session_worktree.py --test

Nested repos keep their place (project/app stays worktree/app): relative paths and rules still work. On a conflict nothing
lands and the branch is left as it was.

The clean-up never waits for the end of a task (a session stopped halfway, a branch merged on GitHub): it judges each
worktree by what it is now. It runs in the background after each `new` and at each session start (hook SessionStart).
  - nothing left to bring (clean, and merging it into the main branch or its remote copy changes nothing: merged, squashed,
    rebased alike), unused for 2 h: removed with its branch;
  - unused for heavy_hours (24): the ignored folders that rebuild themselves (`heavy`: Library, node_modules, .venv...) go,
    even with uncommitted work, never a tracked file;
  - clean, unused for drop_days (14): the folder goes, its branch stays (`revive`);
  - uncommitted work: its code is never touched.
Used = the hook PreToolUse saw it (worktree_guard.py), or git wrote in it. A folder some program holds open (Unity, a
terminal) cannot be renamed on Windows: it is left alone. Decisions go to .git/session-worktree.log. Standard library only.
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import secrets
import shutil
import stat
import subprocess
import sys
import tempfile
import time

try:
    import msvcrt
except ImportError:
    msvcrt = None
    import fcntl

HERE = pathlib.Path(__file__).resolve().parent
LOCAL_FILES = (".env", "local.properties", "android/local.properties")  # ignored by git, often needed to build and test
HEAVY = ["Library", "Temp", "Logs", "obj", "node_modules", ".venv", "venv", "build", ".gradle", "target", "dist", ".next"]
HEAVY_HOURS, DROP_DAYS, MERGED_HOURS = 24, 14, 2


class Refused(Exception):
    pass


def git(cwd, *args, check=True) -> str:
    p = subprocess.run(["git", "-c", "core.quotepath=off", *args], cwd=str(cwd), capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if check and p.returncode != 0:
        raise Refused(f"git {' '.join(args)} : {(p.stderr or p.stdout).strip()}")
    return p.stdout.rstrip()


def main_of(repo) -> pathlib.Path:
    return pathlib.Path(git(repo, "worktree", "list", "--porcelain").splitlines()[0].removeprefix("worktree ").strip())


def nested(top: pathlib.Path) -> list[str]:
    """Repos right under [top] that it ignores. A linked worktree (`.git` is a file) is not one."""
    return [d.name for d in sorted(top.iterdir()) if d.is_dir() and (d / ".git").is_dir()
            and subprocess.run(["git", "check-ignore", "-q", d.name + "/"], cwd=str(top), capture_output=True).returncode == 0]


def _state(root: pathlib.Path) -> pathlib.Path:
    return pathlib.Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir"))


def _seen(root: pathlib.Path, name: str) -> pathlib.Path:
    """Touched by worktree_guard.py each time a session works in the worktree [name]."""
    return _state(root) / "session-worktrees" / name


def _add(repo: pathlib.Path, path: pathlib.Path, branch: str) -> None:
    """A worktree of [repo] at [path] on [branch], made from HEAD when the branch does not exist."""
    has = git(repo, "rev-parse", "--verify", "-q", f"refs/heads/{branch}", check=False)
    git(repo, "-c", "checkout.workers=0", "worktree", "add", "-q", *([str(path), branch] if has else ["-b", branch, str(path), "HEAD"]))


def _make(root: pathlib.Path, name: str) -> pathlib.Path:
    path, branch = root / ".claude" / "worktrees" / name, f"claude/{name}"
    _add(root, path, branch)
    for sub in nested(root):
        _add(root / sub, path / sub, branch)
        for rel in LOCAL_FILES:
            if (root / sub / rel).exists() and (path / sub / rel).parent.is_dir() and not (path / sub / rel).exists():
                shutil.copy2(root / sub / rel, path / sub / rel)
    if not git(root, "check-ignore", ".claude/worktrees/x", check=False):
        exclude = _state(root) / "info" / "exclude"
        exclude.parent.mkdir(exist_ok=True)
        with exclude.open("a", encoding="utf-8") as f:
            f.write("/.claude/worktrees/\n")
    _seed_library(root, path)
    seen = _seen(root, name)
    seen.parent.mkdir(exist_ok=True)
    seen.touch()
    seen.with_name(f"{name}.removed").unlink(missing_ok=True)
    return path


def _seed_library(root: pathlib.Path, path: pathlib.Path) -> None:
    """A Unity project: the new worktree starts from a copy of the main checkout's Library, so its first opening does not
    reimport everything (minutes). Same exclusions as `unityctl.sh unpark` (com.unpseudo.autoplay): no compiled
    assemblies, Bee cache, pipeline port or lock of the main editor, which rebuild themselves."""
    src, dst = root / "Library", path / "Library"
    if not (root / "ProjectSettings").is_dir() or not src.is_dir() or dst.exists():
        return
    print("Library : copie de celle du dossier principal…", flush=True)
    if os.name == "nt":
        rc = subprocess.run(["robocopy", str(src), str(dst), "/E", "/MT:16", "/NFL", "/NDL", "/NJH", "/NJS", "/NP",
                             "/XF", "*.lock", "UnityLockfile", "/XD", "ScriptAssemblies", "Bee", "Pipeline"],
                            capture_output=True).returncode
        if rc >= 8:
            print(f"Library : robocopy a échoué ({rc}), Unity réimportera tout.", flush=True)
            _rmtree(dst)
            return
    else:
        shutil.copytree(src, dst, ignore=shutil.ignore_patterns("*.lock", "UnityLockfile", "ScriptAssemblies", "Bee", "Pipeline"))
    (dst / "Pipeline" / ".unity-pipeline-port").unlink(missing_ok=True)


def new(root: pathlib.Path, name: str) -> pathlib.Path:
    name = re.sub(r"[^a-z0-9-]+", "-", name.lower()).strip("-") or "session"
    return _make(root, f"{name}-{secrets.token_hex(3)}")


def revive(root: pathlib.Path, name: str) -> pathlib.Path:
    name = name.strip().removeprefix("claude/")
    if (root / ".claude" / "worktrees" / name).exists():
        raise Refused(f"{root / '.claude' / 'worktrees' / name} existe déjà.")
    mains = [root] + [root / s for s in nested(root)]
    if not any(git(m, "rev-parse", "--verify", "-q", f"refs/heads/claude/{name}", check=False) for m in mains):
        kept = sorted({b for m in mains for b in git(m, "for-each-ref", "--format=%(refname:short)", "refs/heads/claude/").split()})
        raise Refused(f"Pas de branche claude/{name}. Branches gardées : {', '.join(kept) or 'aucune'}.")
    return _make(root, name)


class Lock:
    """Held by an open file: freed when its holder dies, however it dies."""

    def __init__(self, path: pathlib.Path):
        self.f = path.open("a+b")

    def _take(self) -> bool:
        try:
            self.f.seek(0)
            if msvcrt:
                msvcrt.locking(self.f.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                fcntl.flock(self.f, fcntl.LOCK_EX | fcntl.LOCK_NB)
            return True
        except OSError:
            return False

    def __enter__(self):
        while not self._take():
            time.sleep(0.5)
        return self

    def __exit__(self, *exc):
        self.f.close()


def _rebase(path: pathlib.Path, base: str) -> None:
    if git(path, "rev-list", "--count", f"HEAD..{base}") == "0":
        return
    if subprocess.run(["git", "rebase", "-q", base], cwd=str(path), capture_output=True).returncode == 0:
        return
    files = git(path, "diff", "--name-only", "--diff-filter=U", check=False)
    git(path, "rebase", "--abort", check=False)
    raise Refused(f"Conflit avec {base} : rien intégré.\n{files}\nDans {path} : git rebase {base}, résoudre, "
                  "git rebase --continue, puis relancer.")


def land(start: pathlib.Path) -> int:
    top = pathlib.Path(git(start, "rev-parse", "--show-toplevel"))
    if main_of(top) == top and not (top.parent / ".git").is_file():
        raise Refused(f"{top} est le dossier principal : à lancer depuis le worktree de la session.")
    # started in a nested repo: the session's folder is the one above, when it is a worktree too
    w = top.parent if (top.parent / ".git").is_file() else top
    repos = [d for d in sorted(w.iterdir()) if d.is_dir() and (d / ".git").is_file()] + [w]
    dirty = [f"{r.name}/{line[3:]}" for r in repos for line in git(r, "status", "--porcelain").splitlines() if not line.startswith("??")]
    if dirty:
        raise Refused("Pas commité, ça resterait derrière : rien intégré.\n  " + "\n  ".join(dirty[:30]))
    work = [(r, main_of(r)) for r in repos]
    bases = {r: git(m, "branch", "--show-current") for r, m in work}
    for r, _ in work:
        _rebase(r, bases[r])
    arrived = 0
    state = pathlib.Path(git(work[-1][1], "rev-parse", "--path-format=absolute", "--git-common-dir"))
    with Lock(state / "session-worktree.lock"):
        for r, m in work:
            _rebase(r, bases[r])  # someone landed meanwhile
            count = int(git(r, "rev-list", "--count", f"{bases[r]}..HEAD"))
            if count:
                try:
                    git(m, "merge", "-q", "--ff-only", git(r, "rev-parse", "HEAD"))
                except Refused as e:
                    raise Refused(f"{bases[r]} n'a pas pu avancer dans {m} (travail non commité sur les mêmes fichiers ?) :\n{e}") from e
            arrived += count
    return arrived


# --- clean-up ---------------------------------------------------------------------------------------------------------

def _conf() -> dict:
    path = HERE / "worktree_guard.json"
    conf = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    return {"heavy": list(conf.get("heavy", HEAVY)), "heavy_hours": float(conf.get("heavy_hours", HEAVY_HOURS)),
            "drop_days": float(conf.get("drop_days", DROP_DAYS)), "keep": list(conf.get("keep", []))}


def _repos(w: pathlib.Path) -> list[pathlib.Path]:
    return [w] + [d for d in sorted(w.iterdir()) if d.is_dir() and (d / ".git").is_file()]


def _gitdir(repo: pathlib.Path) -> pathlib.Path:
    line = (repo / ".git").read_text(encoding="utf-8").strip().removeprefix("gitdir:").strip()
    return (repo / line).resolve()


def _activity(root: pathlib.Path, w: pathlib.Path) -> list[pathlib.Path]:
    files = [_seen(root, w.name)]
    for r in _repos(w):
        try:
            d = _gitdir(r)
        except OSError:
            continue
        files += [d / "index", d / "HEAD", d / "logs" / "HEAD"]
    return files


def _last_used(root: pathlib.Path, w: pathlib.Path) -> float:
    times = []
    for f in _activity(root, w):
        try:
            times.append(f.stat().st_mtime)
        except OSError:
            pass
    return max(times, default=0.0)


def _bases(repo) -> list[str]:
    m = main_of(repo)
    b = git(m, "branch", "--show-current", check=False)
    if not b:
        return []
    up = git(m, "rev-parse", "--abbrev-ref", "--verify", "-q", f"{b}@{{upstream}}", check=False)
    return [b] + ([up] if up else [])


def _merged(repo, head: str = "HEAD") -> bool:
    """[head] brings nothing its main branch (or the remote copy of it) lacks: merged, squashed, rebased or cherry-picked."""
    for base in _bases(repo):
        if git(repo, "rev-list", "--count", f"{base}..{head}", check=False) == "0":
            return True
        p = subprocess.run(["git", "merge-tree", "--write-tree", base, head], cwd=str(repo), capture_output=True, text=True,
                           encoding="utf-8", errors="replace")
        if p.returncode == 0 and p.stdout.split("\n", 1)[0].strip() == git(repo, "rev-parse", f"{base}^{{tree}}", check=False):
            return True
    return False


def _fetch(m: pathlib.Path, now: float) -> None:
    """Brings the remote copy of the main branch, once an hour at most: a branch merged on GitHub is seen as merged."""
    b = git(m, "branch", "--show-current", check=False)
    remote = b and git(m, "config", f"branch.{b}.remote", check=False)
    if not remote or remote == ".":
        return
    stamp = _state(m) / "session-worktree-fetch"
    if stamp.exists() and now - stamp.stat().st_mtime < 3600:
        return
    stamp.touch()
    ref = git(m, "config", f"branch.{b}.merge", check=False).removeprefix("refs/heads/") or b
    try:
        subprocess.run(["git", "fetch", "-q", "--no-tags", remote, ref], cwd=str(m), capture_output=True, timeout=15,
                       env={**os.environ, "GIT_TERMINAL_PROMPT": "0", "GCM_INTERACTIVE": "never"})
    except subprocess.TimeoutExpired:
        pass


def _force(func, path, _exc):
    os.chmod(path, stat.S_IWRITE)  # git's objects are read-only
    func(path)


def _rmtree(p: pathlib.Path) -> None:
    shutil.rmtree(p, onerror=_force)


def _size(p: pathlib.Path) -> int:
    n = 0
    for d, _, files in os.walk(p):
        for f in files:
            try:
                n += os.lstat(os.path.join(d, f)).st_size
            except OSError:
                pass
    return n


def _amount(n: int) -> str:
    if n < 1e6:
        return "moins de 1 Mo"
    return f"{n / 1e9:.1f} Go".replace(".", ",") if n >= 1e8 else f"{round(n / 1e6)} Mo"


def _for(hours: float) -> str:
    return f"{hours:.0f} h" if hours < 48 else f"{hours / 24:.0f} j"


def _busy(p: pathlib.Path) -> bool:
    """On Windows a folder cannot be renamed while a program has a file or its current folder in it."""
    if os.name != "nt":
        return False
    aside = p.with_name(p.name + "~")
    try:
        os.rename(p, aside)
    except OSError:
        return True
    for _ in range(20):
        try:
            os.rename(aside, p)
            return False
        except OSError:
            time.sleep(0.5)
    raise Refused(f"{p} renommé en {aside} et pas remis : à renommer à la main.")


def _heavy(repo: pathlib.Path, names: list[str]) -> list[pathlib.Path]:
    """Folders of [repo] named in [names] that git ignores and that hold no tracked file, three levels down at most."""
    found = []

    def walk(d: pathlib.Path, depth: int) -> None:
        try:
            entries = list(os.scandir(d))
        except OSError:
            return
        for e in entries:
            p = pathlib.Path(e.path)
            if not e.is_dir(follow_symlinks=False) or e.name == ".git" or (p / ".git").exists():
                continue
            rel = p.relative_to(repo).as_posix()
            if e.name in names:
                ignored = subprocess.run(["git", "check-ignore", "-q", rel + "/"], cwd=str(repo), capture_output=True).returncode == 0
                if ignored and not git(repo, "ls-files", "--", rel, check=False):
                    found.append(p)
            elif depth < 3:
                walk(p, depth + 1)

    walk(repo, 0)
    return found


def _trim(w: pathlib.Path, names: list[str], dry: bool) -> tuple[int, int]:
    """Bytes freed (or that would be), and how many heavy folders some program holds."""
    freed = held = 0
    trash = w.parent / ".trash"
    for r in _repos(w):
        for p in _heavy(r, names):
            if dry:
                freed += _size(p)
                continue
            trash.mkdir(exist_ok=True)
            aside = trash / f"{w.name}-{p.name}-{secrets.token_hex(3)}"
            try:
                os.rename(p, aside)
            except OSError:
                held += 1
                continue
            freed += _size(aside)
            _rmtree(aside)
    return freed, held


def _keep(root: pathlib.Path, w: pathlib.Path, patterns: list[str]) -> None:
    """Files git ignores but someone still needs (a mockup's votes...) come back to the main folder before the worktree
    goes: missing there, copied; there with other content, copied beside it under <name>.<worktree><ext>."""
    for pattern in patterns:
        for f in w.glob(pattern):
            if not f.is_file():
                continue
            dest = root / f.relative_to(w)
            if dest.exists() and dest.read_bytes() == f.read_bytes():
                continue
            if dest.exists():
                dest = dest.with_name(f"{dest.stem}.{w.name}{dest.suffix}")
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f, dest)


def _drop(root: pathlib.Path, w: pathlib.Path, merged: bool, dry: bool, why: str, keep: list[str] = ()) -> str:
    verb = "retiré avec sa branche" if merged else "retiré, branche gardée"
    if dry:
        return f"{w.name} : serait {verb} ({why})"
    if _busy(w):
        return f"{w.name} : gardé, un programme le tient ({why})"
    repos = _repos(w)
    info = []
    for r in repos[1:] + repos[:1]:  # the nested ones first
        m, b = main_of(r), git(r, "branch", "--show-current", check=False)
        if not b and not merged:  # a detached HEAD keeps its commits on a branch
            b = f"claude/{w.name}"
            git(r, "branch", b, "HEAD", check=False)
        info.append((r, m, b))
    _keep(root, w, list(keep))
    freed = _size(w)
    for r, m, _ in info:
        git(m, "worktree", "remove", "--force", str(r), check=False)
    if w.exists():
        _rmtree(w)
    for _, m, b in info:
        git(m, "worktree", "prune", check=False)
        if merged and b and b != git(m, "branch", "--show-current", check=False):
            git(m, "branch", "-D", b, check=False)
    seen = _seen(root, w.name)
    seen.with_name(f"{w.name}.removed").touch()  # the hook then refuses writes there, and says how to get it back
    seen.unlink(missing_ok=True)
    return f"{w.name} : {verb}, {_amount(freed)} rendus ({why})"


def _sweep_one(root: pathlib.Path, w: pathlib.Path, now: float, conf: dict, dry: bool) -> str:
    idle = (now - _last_used(root, w)) / 3600  # before git looks inside
    since = f"inactif depuis {_for(idle)}"
    repos = _repos(w)
    dirty = any(git(r, "--no-optional-locks", "status", "--porcelain", check=False) for r in repos)
    if not dirty and idle >= MERGED_HOURS and all(_merged(r) for r in repos):
        return _drop(root, w, True, dry, f"fusionné, {since}", conf["keep"])
    if not dirty and idle >= conf["drop_days"] * 24:
        return _drop(root, w, False, dry, f"pas fusionné, {since} ; revive {w.name} le refait", conf["keep"])
    if idle >= conf["heavy_hours"]:
        freed, held = _trim(w, conf["heavy"], dry)
        if freed:
            return f"{w.name} : {'serait allégé' if dry else 'allégé'} de {_amount(freed)} ({since})"
        if held:
            return f"{w.name} : gardé, un programme tient ses dossiers lourds ({since})"
    return f"{w.name} : gardé ({'du travail pas commité, ' if dirty else ''}{since})"


def _orphans(root: pathlib.Path, dry: bool) -> list[str]:
    """Branches claude/* without a worktree that bring nothing any more, in every repo that has them."""
    mains = [root] + [root / s for s in nested(root)]
    used, found = set(), {}
    for m in mains:
        used.update(line.removeprefix("branch refs/heads/") for line in git(m, "worktree", "list", "--porcelain").splitlines()
                    if line.startswith("branch "))
        for b in git(m, "for-each-ref", "--format=%(refname:short)", "refs/heads/claude/").split():
            found.setdefault(b, []).append(m)
    out = []
    for b, ms in sorted(found.items()):
        if b in used or not all(_merged(m, b) for m in ms):
            continue
        if not dry:
            for m in ms:
                git(m, "branch", "-D", b, check=False)
        out.append(f"branche {b} : {'serait supprimée' if dry else 'supprimée'} (fusionnée, sans worktree)")
    return out


def _log(root: pathlib.Path, lines: list[str]) -> None:
    path = _state(root) / "session-worktree.log"
    at = time.strftime("%Y-%m-%d %H:%M")
    with path.open("a", encoding="utf-8") as f:
        f.writelines(f"{at} {line}\n" for line in lines)
    if path.stat().st_size > 256_000:
        text = path.read_text(encoding="utf-8")
        path.write_text(text[len(text) // 2:].split("\n", 1)[-1], encoding="utf-8")


def sweep(root: pathlib.Path, now: float | None = None, dry: bool = False, fetch: bool = True) -> list[str]:
    now = now or time.time()
    conf, wdir = _conf(), root / ".claude" / "worktrees"
    if not dry and (wdir / ".trash").is_dir():
        for left in (wdir / ".trash").iterdir():  # what a held file stopped last time
            _rmtree(left)
    marks = _state(root) / "session-worktrees"
    for mark in marks.glob("*.removed") if not dry and marks.is_dir() else []:
        if now - mark.stat().st_mtime > 60 * 86400:
            mark.unlink()
    mains = [root] + [root / s for s in nested(root)]
    if fetch:
        for m in mains:
            _fetch(m, now)
    lines = []
    for w in sorted(wdir.iterdir()) if wdir.is_dir() else []:
        if not w.is_dir() or w.name.startswith(".") or w.name.endswith("~"):
            continue
        if not (w / ".git").is_file():
            lines.append(f"{w.name} : laissé (pas un worktree)")
            continue
        try:
            lines.append(_sweep_one(root, w, now, conf, dry))
        except (Refused, OSError) as e:
            lines.append(f"{w.name} : erreur, laissé ({e})")
    lines += _orphans(root, dry)
    if not dry:
        for m in mains:
            git(m, "worktree", "prune", check=False)
        _log(root, lines)
    return lines


def _background(root: pathlib.Path) -> None:
    """`clean` in its own process, which outlives the hook or the command that started it."""
    cmd = [sys.executable, str(pathlib.Path(__file__).resolve()), "clean"]
    kw = dict(stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, close_fds=True, cwd=str(root))
    if os.name != "nt":
        subprocess.Popen(cmd, start_new_session=True, **kw)
        return
    flags = subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP  # a hidden console its git calls share
    try:
        subprocess.Popen(cmd, creationflags=flags | 0x01000000, **kw)  # CREATE_BREAKAWAY_FROM_JOB
    except OSError:
        subprocess.Popen(cmd, creationflags=flags, **kw)


def clean(root: pathlib.Path, dry: bool) -> list[str]:
    if dry:  # touches nothing
        return sweep(root, dry=True)
    lock =Lock(_state(root) / "session-worktree-sweep.lock")
    try:
        if not lock._take():
            return ["Un nettoyage tourne déjà."]
        return sweep(root, dry=dry)
    finally:
        lock.f.close()


def _test() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp).resolve() / "projet"
        (root / "app").mkdir(parents=True)
        for d in (root / "app", root):
            git(d, "init", "-q", "-b", "main")
            git(d, "config", "user.email", "t@t")
            git(d, "config", "user.name", "t")
        (root / ".gitignore").write_text("/app/\n.env\nLibrary/\n")
        (root / "a.txt").write_text("a\n")
        (root / "app" / ".gitignore").write_text(".env\nbuild/\n")
        (root / "app" / "b.txt").write_text("b\n")
        (root / "app" / ".env").write_text("X=1\n")
        git(root, "add", ".")
        git(root, "commit", "-qm", "i")
        git(root / "app", "add", ".")
        git(root / "app", "commit", "-qm", "i")

        a, b = new(root, "Session A"), new(root, "b")
        assert (a / "app" / "b.txt").exists() and (a / "app" / ".env").exists()
        assert git(root, "status", "--porcelain") == ""
        assert _seen(root, a.name).exists()

        (a / "app" / "b.txt").write_text("from a\n")
        try:
            land(a)
            raise AssertionError("landed uncommitted work")
        except Refused as e:
            assert "app/b.txt" in str(e), e
        git(a / "app", "commit", "-qam", "a")
        (b / "app" / "c.txt").write_text("c\n")
        git(b / "app", "add", ".")
        git(b / "app", "commit", "-qm", "b")
        (b / "d.txt").write_text("d\n")
        git(b, "add", ".")
        git(b, "commit", "-qm", "b2")
        assert land(a / "app") == 1  # from the nested repo as well
        assert land(b) == 2  # rebased over A's
        assert (root / "app" / "c.txt").exists() and (root / "d.txt").read_text() == "d\n"
        assert (root / "app" / "b.txt").read_text() == "from a\n"
        assert land(b) == 0

        (a / "app" / "b.txt").write_text("again a\n")
        git(a / "app", "commit", "-qam", "a2")
        (b / "app" / "b.txt").write_text("again b\n")
        git(b / "app", "commit", "-qam", "b3")
        assert land(a) == 1
        try:
            land(b)
            raise AssertionError("landed over a conflict")
        except Refused as e:
            assert "Conflit" in str(e) and "b.txt" in str(e), e
        assert git(b / "app", "status", "--porcelain") == ""
        try:
            land(root)
            raise AssertionError("landed the main folder")
        except Refused as e:
            assert "dossier principal" in str(e), e

        # clean-up: a landed, b holds an unlanded commit, c and d left with heavy folders, e in use, f squashed on main
        c, d, e, f = (new(root, n) for n in "cdef")
        (c / "app" / "x.txt").write_text("x\n")
        git(c / "app", "add", ".")
        git(c / "app", "commit", "-qm", "c")
        for w in (c, d, e):
            (w / "Library").mkdir()
            (w / "Library" / "big.bin").write_bytes(b"0" * 1000)
        (c / "app" / "build").mkdir()
        (c / "app" / "build" / "out.bin").write_bytes(b"0" * 1000)
        (d / "a.txt").write_text("not committed\n")
        for n in ("sq1", "sq2"):
            (f / "app" / f"{n}.txt").write_text(n)
            git(f / "app", "add", ".")
            git(f / "app", "commit", "-qm", n)
        for n in ("sq1", "sq2"):
            (root / "app" / f"{n}.txt").write_text(n)
        (root / "app" / "later.txt").write_text("later\n")
        git(root / "app", "add", ".")
        git(root / "app", "commit", "-qm", "squash of f, then more")
        git(root, "branch", "claude/old", "main")  # merged, no worktree

        def age(w, hours):
            t = time.time() - hours * 3600
            for p in _activity(root, w):
                if p.exists():
                    os.utime(p, (t, t))

        for w, hours in ((a, 3), (b, 15 * 24), (c, 30), (d, 30 * 24), (e, 30), (f, 3)):
            age(w, hours)
        _seen(root, e.name).touch()  # the hook just saw a session in e
        for w in (a, b):  # ignored files listed in `keep` come back to the main folder
            (w / "Library").mkdir(exist_ok=True)
            (w / "Library" / "votes.txt").write_text(f"votes {w.name}\n")
        global _conf
        real_conf, _conf = _conf, lambda: {**real_conf(), "keep": ["Library/*.txt"]}

        lines = sweep(root, dry=True, fetch=False)
        assert all(w.exists() for w in (a, b, c, d, e, f)) and (c / "Library").exists(), lines
        assert any(a.name in x and "serait retiré avec sa branche" in x for x in lines), lines

        now, lines = time.time(), []
        if os.name == "nt":  # a program holding a file inside: left alone
            with (a / "a.txt").open():
                lines = sweep(root, now, fetch=False)
            assert a.exists() and any(a.name in x and "un programme le tient" in x for x in lines), lines
        lines += sweep(root, now, fetch=False)
        text = "\n".join(lines)
        assert not a.exists() and not git(root, "branch", "--list", f"claude/{a.name}"), text
        assert not f.exists() and not git(root / "app", "branch", "--list", f"claude/{f.name}"), text  # squashed counts
        assert not b.exists() and git(root / "app", "branch", "--list", f"claude/{b.name}"), text  # its commit kept
        assert c.exists() and not (c / "Library").exists() and not (c / "app" / "build").exists(), text
        assert (c / "app" / "x.txt").exists()
        assert d.exists() and not (d / "Library").exists() and (d / "a.txt").read_text() == "not committed\n", text
        assert e.exists() and (e / "Library").exists(), text
        assert not git(root, "branch", "--list", "claude/old"), text
        assert "allégé" in text and (root / ".git" / "session-worktree.log").exists(), text
        _conf = real_conf
        kept = sorted(x.read_text() for x in (root / "Library").glob("votes*.txt"))  # the second one lands beside the first
        assert kept == sorted(f"votes {w.name}\n" for w in (a, b)), (kept, text)

        assert (root / ".git" / "session-worktrees" / f"{a.name}.removed").exists()
        b2 = revive(root, b.name)
        assert not (root / ".git" / "session-worktrees" / f"{b.name}.removed").exists()
        assert b2 == b and (b / "app" / "b.txt").read_text() == "again b\n" and (b / "app" / ".env").exists()
        try:
            revive(root, "nothing")
            raise AssertionError("revived a branch that does not exist")
        except Refused as e:
            assert "Pas de branche" in str(e), e
    print("ok")
    return 0


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    args = sys.argv[1:]
    if args[:1] == ["--test"]:
        return _test()
    root = HERE.parent.parent
    try:
        if args[:1] == ["new"]:
            path = new(root, " ".join(args[1:]))
            _background(root)  # makes room for this one
            print(f"Worktree : {path}")
        elif args[:1] == ["land"]:
            print(f"Intégré : {land(pathlib.Path.cwd())} commit(s).")
        elif args[:1] == ["clean"]:
            if "--background" in args:
                _background(root)
            else:
                print("\n".join(clean(root, "--dry-run" in args)) or "Aucun worktree.")
        elif args[:1] == ["revive"] and args[1:]:
            path = revive(root, args[1])
            print(f"Worktree : {path}")
        else:
            print(__doc__)
            return 1
    except Refused as e:
        print(str(e), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
