"""Table-size sweep: one whole game per seat count (designer's classic preset for that size), host + bots.

    python -X utf8 tools/autoplay/sweep_table_sizes.py [--sizes 5,6,7,9,10,11,12,13,14] [--seed 500] [--extra "..."]

Every other campaign plays 8 seats; the presets exist for 5..14. Each run plays to its end (no day limit) and lingers
on the ending screen. Writes AutoplayRuns/table-sizes-<stamp>.md: outcome, days, winners, errors per size.
Needs an up-to-date dev build (tools/autoplay/unityctl.sh build). Long: run it in the background.
"""
import argparse
import datetime
import glob
import json
import os
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASH = r"C:\Program Files\Git\bin\bash.exe"


def newest_run(before):
    runs = [d for d in glob.glob(os.path.join(ROOT, "AutoplayRuns", "*")) if os.path.isdir(d) and d not in before]
    return max(runs, key=os.path.getmtime) if runs else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sizes", default="5,6,7,9,10,11,12,13,14")
    ap.add_argument("--seed", type=int, default=500)
    ap.add_argument("--extra", default="", help="more player args")
    ap.add_argument("--port", default="7851")
    a = ap.parse_args()
    rows = []
    for i, size in enumerate(int(s) for s in a.sizes.split(",")):
        seed = a.seed + i
        before = set(glob.glob(os.path.join(ROOT, "AutoplayRuns", "*")))
        env = dict(os.environ)
        env["AUTOPLAY_ARGS"] = f"-autoplay-players {size} -autoplay-fast-fakes -autoplay-linger-end 6 -autoplay-timeout 1100 {a.extra}".strip()
        env.setdefault("AUTOPLAY_TIMEOUT", "1300")
        subprocess.run([BASH, os.path.join(ROOT, "tools", "autoplay", "unityctl.sh"), "play-build", str(seed), a.port],
                       env=env, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        run = newest_run(before)
        rep = {}
        if run and os.path.exists(os.path.join(run, "report.json")):
            rep = json.load(open(os.path.join(run, "report.json"), encoding="utf-8-sig"))
        errs = sorted({e.splitlines()[0][:160] for e in rep.get("errors", [])})
        rows.append((size, seed, rep.get("outcome", "NO REPORT"), rep.get("failureReason") or "",
                     ", ".join(rep.get("facts", [])),
                     len(rep.get("errors", [])), errs, os.path.basename(run or "")))
        print(f"{size} seats seed {seed}: {rows[-1][2]} errors={rows[-1][5]} {rows[-1][3]}", flush=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = os.path.join(ROOT, "AutoplayRuns", f"table-sizes-{stamp}.md")
    with open(out, "w", encoding="utf-8") as f:
        f.write("| Seats | Seed | Outcome | Facts | Errors | Run |\n|---|---|---|---|---|---|\n")
        for size, seed, outcome, reason, facts, n, errs, run in rows:
            f.write(f"| {size} | {seed} | {outcome} {reason} | {facts} | {n} {'; '.join(errs)[:400]} | {run} |\n")
    print(out)
    return 0 if all(r[2] == "Completed" and r[5] == 0 for r in rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
