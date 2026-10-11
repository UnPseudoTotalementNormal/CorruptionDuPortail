"""Seated view comparison (board task T16): one host + bots game per variant x table size, the card visibility probe at
the first vote with the variant applied through -autoplay-seated-view.

    python -X utf8 tools/autoplay/sweep_seated_view.py --variants "cur=;high=h=2,pitch=10;round=round=1" \
        [--sizes 8,14] [--views fps-rest,fps-hover,top] [--seed 940] [--lanes 2] [--clients 3]

A variant is name=spec, spec = the -autoplay-seated-view value (h, pitch, r, scale, round, stand; empty = the scene's
own values). --clients N plays host + N real clients (play-net): only real players have a seated cat, a host + bots
game shows an empty table. Each run gives the clean view the player sees (vis-cur-<view>-clean.jpg) and the probe's measurements;
everything is copied to AutoplayRuns/seated-view-<stamp>/<variant>-<size>-<view>.jpg next to a summary .md (least
visible face / vote button / vote count text per view, PASS = analyze_card_visibility.py verdict). Needs an up-to-date
dev build. Long: run it in the background.
"""
import argparse
import concurrent.futures
import datetime
import glob
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASH = r"C:\Program Files\Git\bin\bash.exe"


def run_one(name, spec, size, seed, port, views, clients):
    out_root = os.path.join(ROOT, "AutoplayRuns")
    started = time.time()
    env = dict(os.environ)
    args = (f"-autoplay-players {size} -autoplay-fast-fakes -autoplay-max-days 1 -autoplay-card-visibility cur "
            f"-autoplay-card-visibility-views {views}")
    if spec:
        args += f" -autoplay-seated-view {spec}"
    env["AUTOPLAY_ARGS"] = args
    env.setdefault("AUTOPLAY_TIMEOUT", "1500")
    command = ["play-net", str(clients), str(seed), str(port)] if clients else ["play-build", str(seed), str(port)]
    subprocess.run([BASH, os.path.join(ROOT, "tools", "autoplay", "unityctl.sh")] + command,
                   env=env, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    pattern = f"net-*-c{clients}-seed{seed}" if clients else f"*-seed{seed}"
    runs = [d for d in glob.glob(os.path.join(out_root, pattern)) if os.path.isdir(d) and os.path.getmtime(d) >= started - 5]
    run = max(runs, key=os.path.getmtime) if runs else None
    if run is None:
        return name, size, None, "NO RUN", ""
    res = subprocess.run([sys.executable, "-X", "utf8", os.path.join(ROOT, "tools", "autoplay", "analyze_card_visibility.py"),
                          run, "--views", views], capture_output=True, text=True, encoding="utf-8")
    return name, size, run, "PASS" if res.returncode == 0 else "FAIL", res.stdout


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variants", required=True, help='"name=spec;name=spec" (spec may be empty)')
    ap.add_argument("--sizes", default="8")
    ap.add_argument("--views", default="fps-rest,fps-hover,top")
    ap.add_argument("--seed", type=int, default=940)
    ap.add_argument("--lanes", type=int, default=2)
    ap.add_argument("--clients", type=int, default=0, help="real network clients (play-net): their cats sit at the table")
    a = ap.parse_args()
    # The launchers let one run at a time on the machine by default: allow one per lane so the lanes really play together.
    os.environ.setdefault("AUTOPLAY_MAX_PARALLEL", str(a.lanes))
    variants = [tuple(item.split("=", 1)) for item in a.variants.split(";") if item.strip()]
    jobs = [(name, spec, int(size)) for name, spec in variants for size in a.sizes.split(",")]
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out_dir = os.path.join(ROOT, "AutoplayRuns", f"seated-view-{stamp}")
    os.makedirs(out_dir, exist_ok=True)
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=a.lanes) as pool:
        futures = [pool.submit(run_one, name, spec, size, a.seed + i, 7861 + 10 * (i % a.lanes), a.views, a.clients)
                   for i, (name, spec, size) in enumerate(jobs)]
        for f in concurrent.futures.as_completed(futures):
            name, size, run, verdict, text = f.result()
            print(f"{name} {size} seats: {verdict} {os.path.basename(run or '')}", flush=True)
            results.append((name, size, run, verdict, text))
            for shot in glob.glob(os.path.join(run or "", "**", "vis-cur-*.jpg"), recursive=True):
                view = os.path.basename(shot)[len("vis-cur-"):-len(".jpg")]
                process = os.path.basename(os.path.dirname(shot)).split("-")
                who = f"-{process[2]}" if a.clients and len(process) > 2 else ""  # host / clientN in a play-net run
                shutil.copy(shot, os.path.join(out_dir, f"{name}-{size}{who}-{view}.jpg"))
    order = {name: i for i, (name, _) in enumerate(variants)}
    results.sort(key=lambda r: (order[r[0]], r[1]))
    specs = dict(variants)
    with open(os.path.join(out_dir, "summary.md"), "w", encoding="utf-8") as fh:
        for name, size, run, verdict, text in results:
            fh.write(f"## {name} ({specs[name] or 'scene values'}), {size} seats: {verdict} ({os.path.basename(run or '')})\n\n"
                     f"```\n{text}\n```\n\n")
    print(out_dir)
    return 0 if all(r[3] == "PASS" for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
