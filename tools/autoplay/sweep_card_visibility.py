"""Card visibility sweep: one host + bots game per table size, the card visibility probe at the first vote.

    python -X utf8 tools/autoplay/sweep_card_visibility.py [--sizes 5,6,...,14] [--layouts "cur"]
        [--layouts-by-size "5=...|6=..."] [--seed 920] [--lanes 2] [--extra "..."]

Each size runs `-autoplay-card-visibility <layouts>` (AutoplayDriverVisibility: every card's face, vote button and
vote count text measured in the top view and the seated first-person view, hovered or not), then
analyze_card_visibility.py. Default layouts = "cur" (the game's own layout: the regression check). Writes
AutoplayRuns/card-visibility-<stamp>.md. Needs an up-to-date dev build. Long: run it in the background.
"""
import argparse
import concurrent.futures
import datetime
import glob
import os
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASH = r"C:\Program Files\Git\bin\bash.exe"


def run_size(size, seed, port, layouts, extra):
    out_root = os.path.join(ROOT, "AutoplayRuns")
    started = time.time()
    env = dict(os.environ)
    env["AUTOPLAY_ARGS"] = (f"-autoplay-players {size} -autoplay-fast-fakes -autoplay-max-days 1 "
                            f"-autoplay-card-visibility {layouts} {extra}").strip()
    env.setdefault("AUTOPLAY_TIMEOUT", "1500")
    subprocess.run([BASH, os.path.join(ROOT, "tools", "autoplay", "unityctl.sh"), "play-build", str(seed), str(port)],
                   env=env, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    runs = [d for d in glob.glob(os.path.join(out_root, f"*-seed{seed}")) if os.path.isdir(d) and os.path.getmtime(d) >= started - 5]
    run = max(runs, key=os.path.getmtime) if runs else None
    if run is None:
        return size, None, "NO RUN", ""
    res = subprocess.run([sys.executable, "-X", "utf8", os.path.join(ROOT, "tools", "autoplay", "analyze_card_visibility.py"), run],
                         capture_output=True, text=True, encoding="utf-8")
    return size, run, "PASS" if res.returncode == 0 else "FAIL", res.stdout


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sizes", default="5,6,7,8,9,10,11,12,13,14")
    ap.add_argument("--layouts", default="cur")
    ap.add_argument("--layouts-by-size", default="", help='"5=spec|6=spec": overrides --layouts for those sizes')
    ap.add_argument("--seed", type=int, default=920)
    ap.add_argument("--lanes", type=int, default=2)
    ap.add_argument("--extra", default="")
    a = ap.parse_args()
    # The launchers let one run at a time on the machine by default: allow one per lane so the lanes really play together.
    os.environ.setdefault("AUTOPLAY_MAX_PARALLEL", str(a.lanes))
    by_size = dict(item.split("=", 1) for item in a.layouts_by_size.split("|") if "=" in item)
    sizes = [int(s) for s in a.sizes.split(",")]
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=a.lanes) as pool:
        futures = [pool.submit(run_size, size, a.seed + i, 7861 + 10 * (i % a.lanes), by_size.get(str(size), a.layouts), a.extra)
                   for i, size in enumerate(sizes)]
        for f in concurrent.futures.as_completed(futures):
            size, run, verdict, text = f.result()
            print(f"{size} seats: {verdict} {os.path.basename(run or '')}", flush=True)
            results.append((size, run, verdict, text))
    results.sort()
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = os.path.join(ROOT, "AutoplayRuns", f"card-visibility-{stamp}.md")
    with open(out, "w", encoding="utf-8") as fh:
        for size, run, verdict, text in results:
            fh.write(f"## {size} seats: {verdict} ({os.path.basename(run or '')})\n\n```\n{text}\n```\n\n")
    print(out)
    return 0 if all(r[2] == "PASS" for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
