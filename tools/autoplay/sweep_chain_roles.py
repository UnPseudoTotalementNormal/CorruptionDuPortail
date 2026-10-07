"""Chain sweep (Corruption du Portail): every role of the pool chained at the first vote, held by a REAL client.

    python -X utf8 tools/autoplay/sweep_chain_roles.py [--roles <roles.json>] [--only <fragments>] [--skip <fragments>]
                                                       [--seed 900] [--clients 3] [--days 2] [--port N] [--holder client|host|bot]

The other sweeps play each role's powers; none of them makes sure each role is CHAINED: what the game does when that
role leaves play (on-chain reveals, linked roles, chat channels closed, powers removed, victory check) on the server
and on the chained player's own screen is only met by chance. Here each role is forced onto a real client
(-autoplay-force-roles <fragment> -autoplay-role-holder client) and every voter votes it (-autoplay-vote-focus
<fragment>, -autoplay-vote-probability 1), then the game goes on for --days days with the chained client still
connected. Verdict per role:
  OK           the holder was chained on the host, every process completed, no desync, no error, no power timeout
  FAIL         a check failed (outcome, error, desync, power timeout / error)
  NOT COVERED  no seed gave the role to a client, or the holder was never chained (the vote went elsewhere)
Output: AutoplayRuns/chain-sweep-<stamp>/coverage.md (+ the generated scenarios). Run issues: harvest_issues.py.
"""
import argparse
import datetime
import glob
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
RUNNER = os.path.join(PROJECT, "Packages", "com.unpseudo.autoplay", "Tools~", "run_scenario.py")
sys.path.insert(0, HERE)
from sweep_powers import fragment_for, newest_roles  # noqa: E402


def host_report(run):
    for folder in glob.glob(os.path.join(run, "*-host-*", "")):
        path = os.path.join(folder, "report.json")
        if os.path.exists(path):
            return json.load(open(path, encoding="utf-8-sig"))
    return {}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roles")
    ap.add_argument("--only")
    ap.add_argument("--skip", default="")
    ap.add_argument("--seed", type=int, default=900)
    ap.add_argument("--clients", type=int, default=3)
    ap.add_argument("--days", type=int, default=2)
    ap.add_argument("--port", type=int, default=0, help="UDP port (parallel lanes)")
    ap.add_argument("--holder", default="client", choices=["client", "host", "bot"], help="who holds the chained role")
    a = ap.parse_args()

    roles_path = a.roles or newest_roles()
    if not roles_path:
        sys.exit("no roles.json yet: run any host scenario first (every host run writes one)")
    pool = json.load(open(roles_path, encoding="utf-8-sig"))["roles"]
    names = [r["name"] for r in pool]
    targets = pool
    if a.only:
        targets = [r for r in targets if any(o.strip().lower() in r["name"].lower() for o in a.only.split(","))]
    if a.skip:
        targets = [r for r in targets if not any(k.strip().lower() in r["name"].lower() for k in a.skip.split(",") if k.strip())]

    out = os.path.join(PROJECT, "AutoplayRuns", f"chain-sweep-{a.holder}-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "scenarios"), exist_ok=True)
    print(f"chain sweep: {len(targets)} role(s), pool from {roles_path}", flush=True)

    rows = []
    for i, role in enumerate(targets):
        frag = fragment_for(role["name"], names)
        if frag is None:
            rows.append((role["name"], "NOT COVERED", "no unique ASCII name fragment to force the role", ""))
            continue
        scenario = {
            "name": f"chain-sweep-{a.holder}-{frag.lower()}",
            "goal": f"{role['name']} held by the {a.holder} is chained at the first vote; the game goes on without error",
            "mode": "net", "clients": a.clients, "seed": a.seed + i * 7, "seedRetries": 3, "timescale": 3, "timeout": 900,
            "args": ["-autoplay-force-roles", frag, "-autoplay-role-holder", a.holder,
                     "-autoplay-vote-focus", frag, "-autoplay-vote-probability", "1",
                     "-autoplay-max-days", str(a.days), "-autoplay-fast-fakes", "-autoplay-no-png",
                     "-autoplay-fast-phases", "Intro", "-autoplay-fast-timescale", "12"],
            "expect": [{"type": "outcome", "value": "Completed", "process": "all"},
                       {"type": "event", "kind": "power.timeout", "process": "any", "min": 0, "max": 0},
                       {"type": "event", "kind": "power.error", "process": "any", "min": 0, "max": 0},
                       {"type": "desync", "max": 0},
                       {"type": "noErrors", "process": "all", "ignore": ["ClientLoadedSynchronization"]}],
        }
        path = os.path.join(out, "scenarios", scenario["name"] + ".json")
        json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"[{i + 1}/{len(targets)}] {role['name']} (force '{frag}') …", flush=True)
        res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", os.path.join(HERE, "unityctl.sh"),
                              "--project", PROJECT] + (["--port", str(a.port)] if a.port else []), cwd=PROJECT,
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        run = (re.findall(r"VERDICT: \w+\s+\((.+?)\)", res.stdout) or [""])[-1]
        fails = [l.strip() for l in res.stdout.splitlines() if l.strip().startswith("[FAIL]")]
        roster = host_report(run).get("finalRoster", []) if run else []
        chained = [l for l in roster if role["name"].lower() in l.lower() and "chained=True" in l]
        if "composition mismatch" in res.stdout and not run:
            status, why = "NOT COVERED", f"no seed gave the role to the {a.holder}"
        elif fails:
            status, why = "FAIL", " / ".join(f[7:140] for f in fails)
        elif not chained:
            status, why = "NOT COVERED", "the holder was never chained (roster: " + "; ".join(roster)[:160] + ")"
        else:
            status, why = "OK", chained[0][:120]
        rows.append((role["name"], status, why, os.path.basename(run)))
        print(f"    {status}: {why}", flush=True)

    lines = [f"# Chain sweep (holder: {a.holder})", "", "| Role | Verdict | Why | Run |", "|---|---|---|---|"]
    lines += [f"| {r} | {s} | {w.replace('|', '/')} | {run} |" for r, s, w, run in rows]
    open(os.path.join(out, "coverage.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"coverage: {os.path.join(out, 'coverage.md')}")


if __name__ == "__main__":
    main()
