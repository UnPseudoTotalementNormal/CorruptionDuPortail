"""Random network games over every table size: host + N real clients, bots fill the table, whole games.

    python -X utf8 tools/autoplay/sweep_random_net.py [--games 10] [--seed 600] [--clients 2] [--sizes 5,...,14]
                                                     [--relay-every 2] [--extra "..."] [--client-extra "..."]

Game i plays sizes[i % len(sizes)] seats with seed seed+i; every `relay-every`-th game goes through Unity Relay (real
UGS login + lobby), the others over direct loopback. Each game ends on victory (no day limit), lingers on the ending
screen, chats, and is checked by compare_runs (desync) + the reports (errors). Writes AutoplayRuns/random-net-<stamp>.md
and runs harvest_issues.py over the new runs. Needs an up-to-date dev build. Long: run it in the background.
"""
import argparse
import datetime
import glob
import json
import os
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASH = r"C:\Program Files\Git\bin\bash.exe"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--games", type=int, default=10)
    ap.add_argument("--seed", type=int, default=600)
    ap.add_argument("--clients", type=int, default=2)
    ap.add_argument("--sizes", default="5,6,7,8,9,10,11,12,13,14")
    ap.add_argument("--relay-every", type=int, default=2, help="0 = never")
    ap.add_argument("--extra", default="", help="more args for every process")
    ap.add_argument("--client-extra", default="", help="more args for every client")
    a = ap.parse_args()
    sizes = [int(s) for s in a.sizes.split(",")]
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    rows = []
    for i in range(a.games):
        seed, size = a.seed + i, sizes[i % len(sizes)]
        relay = a.relay_every > 0 and i % a.relay_every == a.relay_every - 1
        before = set(glob.glob(os.path.join(ROOT, "AutoplayRuns", "net-*")))
        env = dict(os.environ)
        env["AUTOPLAY_ARGS"] = (f"-autoplay-players {size} -autoplay-fast-fakes -autoplay-linger-end 6 -autoplay-chat "
                                f"-autoplay-timeout 1300 {'-autoplay-relay ' if relay else ''}{a.extra}").strip()
        env["AUTOPLAY_CLIENT_ARGS"] = a.client_extra
        env.setdefault("AUTOPLAY_TIMESCALE", "3")
        env.setdefault("AUTOPLAY_TIMEOUT", "1500")
        out = subprocess.run([BASH, os.path.join(ROOT, "tools", "autoplay", "unityctl.sh"), "play-net", str(a.clients),
                              str(seed)], env=env, cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                             errors="replace").stdout
        new = sorted(set(glob.glob(os.path.join(ROOT, "AutoplayRuns", "net-*"))) - before, key=os.path.getmtime)
        run = new[-1] if new else ""
        procs = []
        for rep_path in glob.glob(os.path.join(run, "*", "report.json")):
            rep = json.load(open(rep_path, encoding="utf-8-sig"))
            errs = sorted({e.splitlines()[0][:140] for e in rep.get("errors", [])})
            procs.append((os.path.basename(os.path.dirname(rep_path)).split("-")[2], rep.get("outcome"),
                          rep.get("failureReason") or "", len(rep.get("errors", [])), errs, rep.get("facts", [])))
        result = [l for l in out.splitlines() if l.startswith("RESULT:")]
        verdict = result[-1][len("RESULT:"):].strip() if result else "no compare"
        rows.append((size, seed, relay, verdict, procs, os.path.basename(run)))
        bad = [p for p in procs if p[1] != "Completed" or p[3]]
        print(f"{size} seats seed {seed} relay={relay}: compare={verdict} "
              f"{'ALL OK' if not bad and verdict == 'OK' else bad}", flush=True)
    md = os.path.join(ROOT, "AutoplayRuns", f"random-net-{stamp}.md")
    with open(md, "w", encoding="utf-8") as f:
        f.write("| Seats | Seed | Relay | Compare | Processes (outcome, errors) | Run |\n|---|---|---|---|---|---|\n")
        for size, seed, relay, verdict, procs, run in rows:
            cell = "; ".join(f"{n}: {o} {r} {e} {' / '.join(errs)[:200]}" for n, o, r, e, errs, _ in procs)
            f.write(f"| {size} | {seed} | {relay} | {verdict} | {cell} | {run} |\n")
    print(md)
    since = stamp
    subprocess.run(["python", "-X", "utf8", os.path.join(ROOT, "tools", "autoplay", "harvest_issues.py"),
                    os.path.join(ROOT, "AutoplayRuns"), "--since", since, "--md",
                    os.path.join(ROOT, "AutoplayRuns", f"random-net-{stamp}-harvest.md")], cwd=ROOT)


if __name__ == "__main__":
    main()
