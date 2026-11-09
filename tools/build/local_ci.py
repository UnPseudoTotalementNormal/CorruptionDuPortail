"""Local CI: every GitHub Actions workflow of the project, run on this PC (Actions was blocked, 2026-11-07).

Watches origin/Main; for each new commit: Discord "builds started" (Dev webhook), Relay + Steam Windows builds through
CdpBuild.BuildFromCommandLine in a checkout of its own, zips uploaded to Google Drive (rclone), changelog written by
Claude (then Codex, then Gemini, prompt .github/changelog-prompt.md), Discord "builds ready" (Builder webhook) or
"builds failed" (Dev webhook). Also DevPush, DevPROpened, DevMergeNotif (Discord) and fmod-sync (commit status).
Same embeds as the workflows, field for field.

    python tools/build/local_ci.py watch            every 2 min: Dev push / PR notifications, FMOD checks, and a
                                                    build per new Main head (started at logon, see install)
    python tools/build/local_ci.py build [sha]      build one commit now (default origin/Main)
    python tools/build/local_ci.py status           state, last builds
    python tools/build/local_ci.py install          start the watcher now and at every logon (Startup folder)

Needs: Unity (version of ProjectSettings/ProjectVersion.txt, licensed on this PC), git, rclone remote `gdrive`
(Drive scope drive.file), the `claude` CLI (codex / gemini as fallbacks), DISCORD_DEV_WEBHOOK and
DISCORD_BUILD_WEBHOOK in the repo's .env (gitignored). State and logs: %LOCALAPPDATA%\\CdpLocalCI.
Details: tools/build/README.md.
"""
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
MAIN_REPO = os.path.abspath(os.path.join(subprocess.run(
    ["git", "-C", REPO, "rev-parse", "--git-common-dir"], capture_output=True, text=True).stdout.strip() or ".git", ".."))
if not os.path.isabs(MAIN_REPO):
    MAIN_REPO = os.path.abspath(os.path.join(REPO, MAIN_REPO))
CHECKOUT = os.path.join(os.path.dirname(MAIN_REPO), os.path.basename(MAIN_REPO) + "-ci")
STATE_DIR = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "CdpLocalCI")
STATE_FILE = os.path.join(STATE_DIR, "state.json")
LOCK_FILE = os.path.join(STATE_DIR, "build.lock")
DRIVE_ROOT = "gdrive:Corruption du Portail - Builds"
KEEP_BUILDS = 3
POLL_SECONDS = 120
TITLE_SUFFIX = " — Corruption du Portail"
CHANGELOG_ASK = ("Lis changelog_prompt.md : ce sont tes consignes de rédaction. Les commits sont dans commits.txt. "
                 "Rédige le changelog en suivant ces consignes et écris-le, et rien d'autre, dans le fichier changelog.txt.")

# Same two flavors as the workflow's matrix.
FLAVORS = [
    {"flavor": "relay", "name": "CorruptionDuPortail_b{n}", "field": "🖥️ Build Relay (tests)",
     "note": "Connexion Unity Relay. Plusieurs copies sur le même PC pour tester."},
    {"flavor": "steam", "name": "CorruptionDuPortail_Steam_b{n}", "field": "🎮 Build Steam",
     "note": "Connexion par Steam (Steam doit être lancé). Une copie par compte Steam."},
]


def log(msg):
    os.makedirs(STATE_DIR, exist_ok=True)
    line = f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} {msg}"
    print(line, flush=True)
    with open(os.path.join(STATE_DIR, "local_ci.log"), "a", encoding="utf-8") as f:
        f.write(line + "\n")


def run(cmd, cwd=None, check=True, **kw):
    done = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", **kw)
    if check and done.returncode != 0:
        raise RuntimeError(f"{' '.join(map(str, cmd))[:200]} -> {done.returncode}: {(done.stderr or done.stdout)[-800:]}")
    return done


def git(*args, cwd=None, check=True):
    return run(["git", *args], cwd=cwd or CHECKOUT, check=check).stdout.strip()


def load_state():
    if os.path.exists(STATE_FILE):
        with open(STATE_FILE, encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_state(state):
    os.makedirs(STATE_DIR, exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2)


def env_value(key):
    if os.environ.get(key):
        return os.environ[key]
    path = os.path.join(MAIN_REPO, ".env")
    if os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            if line.startswith(key + "="):
                return line.split("=", 1)[1].strip().strip('"')
    raise RuntimeError(f"{key} missing from .env")


def discord(webhook_key, embed):
    data = json.dumps({"embeds": [embed]}).encode("utf-8")
    req = urllib.request.Request(env_value(webhook_key), data=data, method="POST",
                                 headers={"Content-Type": "application/json", "User-Agent": "CdpLocalCI"})
    try:
        urllib.request.urlopen(req, timeout=30).read()
    except Exception as e:  # a lost announcement must never stop a build
        log(f"discord {webhook_key} failed: {e}")


def unity_exe():
    version = re.search(r"m_EditorVersion:\s*(\S+)",
                        open(os.path.join(CHECKOUT, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8").read()).group(1)
    exe = rf"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe"
    if not os.path.exists(exe):
        raise RuntimeError(f"Unity {version} not installed ({exe})")
    return exe


def ensure_checkout():
    """The build checkout: a worktree of its own (detached), never the one somebody works in. Its Library is kept
    between builds (first one seeded from the main checkout, without compiled assemblies)."""
    if not os.path.exists(os.path.join(CHECKOUT, ".git")):
        log(f"creating build checkout {CHECKOUT}")
        run(["git", "-C", MAIN_REPO, "fetch", "-q", "origin", "Main"])
        run(["git", "-C", MAIN_REPO, "worktree", "add", "--detach", CHECKOUT, "origin/Main"])
        src = os.path.join(MAIN_REPO, "Library")
        if os.path.isdir(src):
            run(["robocopy", src, os.path.join(CHECKOUT, "Library"), "/E", "/MT:16", "/XF", "*.lock", "UnityLockfile",
                 "/XD", "ScriptAssemblies", "Bee", "Pipeline", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"], check=False)


def drive_link(path):
    return run(["rclone", "link", path]).stdout.strip()


def build_flavor(sha, number, flavor, out_root, version):
    name = flavor["name"].format(n=number)
    out = os.path.join(out_root, flavor["flavor"])
    shutil.rmtree(out, ignore_errors=True)
    exe_path = os.path.join(out, "StandaloneWindows64", name + ".exe")
    unity_log = os.path.join(STATE_DIR, "logs", f"b{number}-{flavor['flavor']}.log")
    os.makedirs(os.path.dirname(unity_log), exist_ok=True)
    cmd = [unity_exe(), "-batchmode", "-quit", "-projectPath", CHECKOUT, "-buildTarget", "Win64",
           "-executeMethod", "CdpBuild.BuildFromCommandLine", "-customBuildPath", exe_path, "-customBuildName", name,
           "-cdpFlavor", flavor["flavor"], "-cdpLogs", "on", "-buildVersion", version, "-logFile", unity_log]
    log(f"b{number} {flavor['flavor']}: building {sha[:8]} v{version}")
    # Below normal priority: the PC stays usable while it builds.
    done = subprocess.run(cmd, creationflags=0x00004000 if os.name == "nt" else 0)
    if done.returncode != 0 or not os.path.exists(exe_path):
        raise RuntimeError(f"Unity exit {done.returncode}, log {unity_log}")
    if flavor["flavor"] == "steam":
        folder = os.path.dirname(exe_path)
        if not os.path.exists(os.path.join(folder, "steam_appid.txt")):
            raise RuntimeError("steam_appid.txt manquant")
        if not any(f.lower() == "steam_api64.dll" for _, _, fs in os.walk(folder) for f in fs):
            raise RuntimeError("steam_api64.dll manquant : la build Steam ne pourra pas démarrer Steam")
    # Same layout as the workflow's artifact (the `build` folder): StandaloneWindows64/<files>.
    zip_path = os.path.join(out_root, name + ".zip")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(os.path.join(out, "StandaloneWindows64")):
            for f in files:
                full = os.path.join(root, f)
                if "_BurstDebugInformation_DoNotShip" in full or "_BackUpThisFolder_ButDontShipItWithYourGame" in full:
                    continue
                z.write(full, os.path.relpath(full, out))
    remote = f"{DRIVE_ROOT}/b{number}/{name}.zip"
    log(f"b{number} {flavor['flavor']}: uploading {os.path.getsize(zip_path) // (1 << 20)} MB")
    run(["rclone", "copyto", zip_path, remote])
    return drive_link(remote)


def changelog(prev_sha, sha):
    if not prev_sha:
        return "Première build — pas de changelog."
    commits = git("log", f"{prev_sha}..{sha}", "--pretty=format:### %s%n%b%n", "--no-merges", check=False)
    if not commits.strip():
        return "Améliorations internes."
    work = tempfile.mkdtemp(prefix="cdp-changelog-")
    with open(os.path.join(work, "commits.txt"), "w", encoding="utf-8") as f:
        f.write(commits)
    shutil.copy(os.path.join(CHECKOUT, ".github", "changelog-prompt.md"), os.path.join(work, "changelog_prompt.md"))
    out = os.path.join(work, "changelog.txt")

    def produced():
        return os.path.exists(out) and open(out, encoding="utf-8").read().strip()

    attempts = [
        ["claude", "-p", CHANGELOG_ASK, "--model", "claude-opus-5-5", "--allowedTools", "Read,Write", "--permission-mode", "acceptEdits"],
        ["codex", "exec", "--full-auto", "--skip-git-repo-check", "--cd", work, CHANGELOG_ASK],
        ["gemini", "-p", CHANGELOG_ASK, "--yolo"],
    ]
    for cmd in attempts:
        if produced():
            break
        exe = shutil.which(cmd[0])
        if not exe:
            continue
        try:
            subprocess.run([exe, *cmd[1:]], cwd=work, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=900)
        except Exception as e:
            log(f"changelog {cmd[0]} failed: {e}")
        if produced():
            log(f"changelog written by {cmd[0]}")
    text = produced() or "Changelog indisponible."
    shutil.rmtree(work, ignore_errors=True)
    return text


def prune_drive():
    names = run(["rclone", "lsf", "--dirs-only", DRIVE_ROOT], check=False).stdout.split()
    builds = sorted((int(m.group(1)), n) for n in names if (m := re.match(r"b(\d+)/$", n)))
    for _, name in builds[:-KEEP_BUILDS]:
        # Straight delete, not the Drive trash: the trash still counts against the quota.
        run(["rclone", "purge", "--drive-use-trash=false", f"{DRIVE_ROOT}/{name.rstrip('/')}"], check=False)
        log(f"pruned {name}")


def build(sha=None):
    os.makedirs(STATE_DIR, exist_ok=True)
    if os.path.exists(LOCK_FILE) and time.time() - os.path.getmtime(LOCK_FILE) < 4 * 3600:
        log("a build is already running")
        return
    open(LOCK_FILE, "w").write(str(os.getpid()))
    try:
        ensure_checkout()
        git("fetch", "-q", "origin", "Main")
        sha = git("rev-parse", sha or "origin/Main")
        state = load_state()
        number = state.get("last_number", 0) + 1
        state.update({"last_number": number, "last_seen_sha": sha})
        save_state(state)
        start = time.time()
        subject = git("log", "-1", "--format=%B", sha)
        actor = git("log", "-1", "--format=%an", sha)
        # The build's Drive folder plays the part of the workflow run page (the embeds' title link).
        folder_url = ""
        try:
            run(["rclone", "mkdir", f"{DRIVE_ROOT}/b{number}"])
            folder_url = drive_link(f"{DRIVE_ROOT}/b{number}")
        except Exception as e:
            log(f"folder link failed: {e}")
        started = {"title": "🔨 Builds démarrées (Relay + Steam)" + TITLE_SUFFIX, "color": 15105570,
                   "fields": [{"name": "Auteur", "value": actor, "inline": True},
                              {"name": "Commit", "value": subject[:1024], "inline": False}]}
        if folder_url:
            started["url"] = folder_url
        discord("DISCORD_DEV_WEBHOOK", started)
        git("checkout", "-q", "--force", "--detach", sha)
        git("clean", "-q", "-fdx", "-e", "Library", "-e", "Logs", "-e", "UserSettings")
        version = "0.0." + git("rev-list", "--count", sha)
        out_root = os.path.join(STATE_DIR, "out")
        shutil.rmtree(out_root, ignore_errors=True)
        links, errors, success_sha = {}, {}, ""
        for flavor in FLAVORS:
            try:
                links[flavor["flavor"]] = build_flavor(sha, number, flavor, out_root, version)
            except Exception as e:
                errors[flavor["flavor"]] = str(e)
                log(f"b{number} {flavor['flavor']} FAILED: {e}")
        # Logs next to the builds, so a failure links somewhere readable.
        for flavor in FLAVORS:
            p = os.path.join(STATE_DIR, "logs", f"b{number}-{flavor['flavor']}.log")
            if os.path.exists(p):
                run(["rclone", "copyto", p, f"{DRIVE_ROOT}/b{number}/logs/{os.path.basename(p)}"], check=False)
        diff = int(time.time() - start)
        duration = f"{diff // 60}m{diff % 60:02d}s"

        if links:
            text = changelog(state.get("last_success_sha", ""), sha)
            with open(os.path.join(STATE_DIR, "logs", f"b{number}-changelog.txt"), "w", encoding="utf-8") as f:
                f.write(text)
            both = len(links) == len(FLAVORS)
            fields = [{"name": "Build", "value": f"b{number}", "inline": True},
                      {"name": "Durée", "value": duration, "inline": True}]
            for flavor in FLAVORS:
                url = links.get(flavor["flavor"])
                value = f"[Télécharger]({url})\n{flavor['note']}" if url else f"❌ Build échouée, [voir les logs]({folder_url})"
                fields.append({"name": flavor["field"], "value": value, "inline": False})
            embed = {"title": ("✅ Builds réussies" if both else "⚠️ Une build sur deux réussie") + TITLE_SUFFIX,
                     "color": 5763719 if both else 16776960,
                     "description": text if len(text) <= 4000 else text[:3990] + "\n…", "fields": fields}
            if folder_url:
                embed["url"] = folder_url
            discord("DISCORD_BUILD_WEBHOOK", embed)
            success_sha = sha
            prune_drive()
        else:
            embed = {"title": "❌ Builds échouées" + TITLE_SUFFIX, "color": 15548997,
                     "fields": [{"name": "Durée", "value": duration, "inline": True},
                                {"name": "Logs", "value": f"[Voir logs]({folder_url})", "inline": False}]}
            if folder_url:
                embed["url"] = folder_url
            discord("DISCORD_DEV_WEBHOOK", embed)
        # Fresh read: the watcher wrote its own keys (Dev, PRs) meanwhile.
        state = load_state()
        if success_sha:
            state["last_success_sha"] = success_sha
        state.setdefault("history", []).append({"number": number, "sha": sha, "ok": sorted(links), "failed": errors,
                                                "duration": duration, "at": datetime.datetime.now().isoformat(timespec="seconds")})
        state["history"] = state["history"][-30:]
        save_state(state)
        log(f"b{number} done in {duration}: ok={sorted(links)} failed={sorted(errors)}")
    finally:
        try:
            os.remove(LOCK_FILE)
        except OSError:
            pass


REPO_SLUG = "UnPseudoTotalementNormal/CorruptionDuPortail"
FMOD_PATHS = ("FMODProject/", "Assets/FMODBanks/", "tools/fmod/banks.py")
FMOD_CONTEXT = "FMOD project in sync / Banks built from the committed FMOD project"


def gh_json(*args):
    done = run(["gh", "api", *args], cwd=MAIN_REPO, check=False)
    return json.loads(done.stdout) if done.returncode == 0 and done.stdout.strip() else None


def login_of(sha):
    """GitHub login of a commit's author (the workflows show logins), else the git author name."""
    data = gh_json(f"repos/{REPO_SLUG}/commits/{sha}")
    return ((data or {}).get("author") or {}).get("login") or git("log", "-1", "--format=%an", sha)


def dev_push(state):
    """DevPush.yml: every push to Dev, "⚠️ Push direct sur Dev" with the pushed commits."""
    head = git("rev-parse", "origin/Dev")
    before = state.get("dev_seen_sha")
    if head == before:
        return
    state["dev_seen_sha"] = head
    save_state(state)
    if not before:
        return  # first run: nothing to replay
    lines = [l.split("\t", 1) for l in git("log", f"{before}..{head}", "--reverse", "--format=%H%x09%s", check=False).splitlines()]
    msg = "\n".join(f"• {subject} ({full[:7]})" for full, subject in (l for l in lines if len(l) == 2))
    msg = msg or git("log", "-1", "--format=%B", head)
    discord("DISCORD_DEV_WEBHOOK", {"title": "⚠️ Push direct sur Dev" + TITLE_SUFFIX, "color": 16776960,
                                    "fields": [{"name": "Auteur", "value": login_of(head), "inline": True},
                                               {"name": "Commits", "value": msg[:1000], "inline": False}],
                                    "url": f"https://github.com/{REPO_SLUG}/compare/{before[:12]}...{head[:12]}"})
    log(f"dev push {before[:8]}..{head[:8]} announced")
    fmod_check(head, before)


def pull_requests(state):
    """DevPROpened.yml + DevMergeNotif.yml: PRs into Dev opened ("📥 Nouvelle PR") and merged ("✅ PR mergée")."""
    prs = gh_json(f"repos/{REPO_SLUG}/pulls?base=Dev&state=all&sort=updated&direction=desc&per_page=30")
    if prs is None:
        return
    first = "pr_opened_max" not in state
    opened_max = state.get("pr_opened_max", max([p["number"] for p in prs] or [0]))
    merged_seen = set(state.get("pr_merged_seen", []))
    if first:
        merged_seen |= {p["number"] for p in prs if p.get("merged_at")}
    fmod_heads = state.get("pr_fmod_heads", {})
    for pr in sorted(prs, key=lambda p: p["number"]):
        n = pr["number"]
        if n > opened_max:
            opened_max = n
            run(["git", "fetch", "-q", "origin", f"pull/{n}/head"], cwd=CHECKOUT, check=False)
            base, head = pr["base"]["sha"], pr["head"]["sha"]
            lst = git("log", f"{base}..{head}", "--no-merges", "--format=- %s (%h)", check=False)
            count = len([l for l in lst.splitlines() if l.startswith("-")])
            if not lst:
                lst, count = "Aucun commit", 0
            discord("DISCORD_DEV_WEBHOOK", {"title": "📥 Nouvelle PR — " + pr["title"], "color": 3447003,
                                            "fields": [{"name": "Auteur", "value": pr["user"]["login"], "inline": True},
                                                       {"name": f"Commits ({count})", "value": lst[:1000], "inline": False}],
                                            "url": pr["html_url"]})
            log(f"PR #{n} opened announced")
        if pr.get("merged_at") and n not in merged_seen:
            merged_seen.add(n)
            full = gh_json(f"repos/{REPO_SLUG}/pulls/{n}") or pr
            merger = (full.get("merged_by") or {}).get("login") or "?"
            discord("DISCORD_DEV_WEBHOOK", {"title": "✅ PR mergée — " + pr["title"], "color": 5763719,
                                            "fields": [{"name": "Créée par", "value": pr["user"]["login"], "inline": True},
                                                       {"name": "Acceptée par", "value": merger, "inline": True}],
                                            "url": pr["html_url"]})
            log(f"PR #{n} merged announced")
        if pr["state"] == "open" and fmod_heads.get(str(n)) != pr["head"]["sha"]:
            fmod_heads[str(n)] = pr["head"]["sha"]
            run(["git", "fetch", "-q", "origin", f"pull/{n}/head"], cwd=CHECKOUT, check=False)
            fmod_check(pr["head"]["sha"], pr["base"]["sha"])
    state.update({"pr_opened_max": opened_max, "pr_merged_seen": sorted(merged_seen)[-100:],
                  "pr_fmod_heads": {k: v for k, v in fmod_heads.items() if int(k) > opened_max - 50}})
    save_state(state)


def fmod_check(sha, base):
    """fmod-sync.yml: when a push / PR touches the FMOD project or the banks, tools/fmod/banks.py diff on that commit,
    reported as a commit status (the check the workflow put on the commit / PR)."""
    changed = git("diff", "--name-only", f"{base}...{sha}", check=False).splitlines()
    if not any(f.startswith(FMOD_PATHS) for f in changed):
        return
    work = tempfile.mkdtemp(prefix="cdp-fmod-")
    try:
        archive = os.path.join(work, "src.tar")
        run(["git", "archive", "-o", archive, sha, "FMODProject", "Assets/FMODBanks",
             "Assets/Plugins/FMOD/platforms/win/lib/x86_64", "tools/fmod"], cwd=CHECKOUT)
        run(["tar", "-xf", archive, "-C", work])
        python = sys.executable.replace("pythonw.exe", "python.exe")
        done = run([python, "tools/fmod/banks.py", "diff"], cwd=work, check=False)
        ok = done.returncode == 0
        tail = (done.stdout or done.stderr).strip().splitlines()[-1:] or [""]
        run(["gh", "api", f"repos/{REPO_SLUG}/statuses/{sha}", "-f", f"state={'success' if ok else 'failure'}",
             "-f", f"context={FMOD_CONTEXT}", "-f", f"description={('ok' if ok else tail[0])[:140]}"],
            cwd=MAIN_REPO, check=False)
        log(f"fmod check {sha[:8]}: {'ok' if ok else 'FAILED'}")
    finally:
        shutil.rmtree(work, ignore_errors=True)


def watch():
    """Every 2 min: Dev pushes, PRs into Dev, FMOD checks, and a build (its own process) for each new Main head."""
    log("watching origin/Main, origin/Dev and the PRs into Dev")
    while True:
        try:
            ensure_checkout()
            run(["git", "fetch", "-q", "origin", "Main", "Dev"], cwd=CHECKOUT)
            state = load_state()
            for job in (dev_push, pull_requests):
                try:
                    job(state)
                except Exception as e:
                    log(f"{job.__name__} error: {e}")
            head = git("rev-parse", "origin/Main")
            if head != load_state().get("last_seen_sha") and not os.path.exists(LOCK_FILE):
                # Its own process: a 30 min build must not hold back the Dev notifications.
                subprocess.Popen([sys.executable, os.path.abspath(__file__), "build", head],
                                 creationflags=0x08000000 if os.name == "nt" else 0)
                time.sleep(10)
        except Exception as e:
            log(f"watch error: {e}")
        time.sleep(POLL_SECONDS)


def status():
    state = load_state()
    print(json.dumps({k: v for k, v in state.items() if k != "history"}, indent=2))
    for h in state.get("history", [])[-8:]:
        print(h)
    print("building" if os.path.exists(LOCK_FILE) else "idle", "| watcher pids", running() or "none", "| checkout", CHECKOUT)


def system_pythonw():
    """The PC's own Python (never a project's venv: the CLI may run from one)."""
    root = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs", "Python")
    for d in sorted(os.listdir(root), reverse=True) if os.path.isdir(root) else []:
        exe = os.path.join(root, d, "pythonw.exe")
        if os.path.exists(exe):
            return exe
    return os.path.join(os.path.dirname(sys.executable), "pythonw.exe")


def install():
    """Starts the watcher at every logon (Startup folder: no admin rights, unlike an ONLOGON scheduled task) and now."""
    script = os.path.join(MAIN_REPO, "tools", "build", "local_ci.py")
    startup = os.path.join(os.environ["APPDATA"], "Microsoft", "Windows", "Start Menu", "Programs", "Startup")
    launcher = os.path.join(startup, "CdP Local CI.cmd")
    with open(launcher, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(f'@start "" "{system_pythonw()}" "{script}" watch\n')
    subprocess.Popen([system_pythonw(), script, "watch"], creationflags=0x00000008 | 0x00000200 if os.name == "nt" else 0)
    print(f"watcher started; {launcher} starts it at every logon")


def running():
    """The watcher's process, if any (by command line)."""
    done = run(["powershell", "-NoProfile", "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name like 'python%'\" | Where-Object { $_.CommandLine -like '*local_ci.py*watch*' } | Select-Object -ExpandProperty ProcessId"], check=False)
    return [int(x) for x in done.stdout.split() if x.isdigit()]


if __name__ == "__main__":
    if sys.stdout is not None:  # None under pythonw (no console): the watcher runs there
        sys.stdout.reconfigure(encoding="utf-8")
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "watch":
        if any(pid != os.getpid() for pid in running()):
            sys.exit(0)  # one watcher at a time
        watch()
    elif cmd == "build":
        build(sys.argv[2] if len(sys.argv) > 2 else None)
    elif cmd == "status":
        status()
    elif cmd == "install":
        install()
    else:
        print(__doc__)
