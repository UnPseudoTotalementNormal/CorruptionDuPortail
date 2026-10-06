"""Client power sweep (Corruption du Portail): every role with an active power, held by a REAL network client.

    python -X utf8 tools/autoplay/sweep_client_powers.py [--roles <roles.json>] [--only <name fragment>] [--seed 700]
                                                         [--clients 2] [--days 3]

sweep_powers.py plays each power on the host (simulated bots run their flow server-side), which hides the client
paths: the owner's RPCs, the server's answer to that client, the client-side picker steps. Here each role is forced
into the composition and onto a real client (-autoplay-force-roles <fragment> -autoplay-role-holder client), in a
play-net game stopped after --days days. Verdict per role:
  OK           the client used the role's powers, none timed out or errored, no desync, no error
  FAIL         a power.timeout / power.error on a client, a desync, an error, or a run that did not complete
  NOT COVERED  no power.start of that role on a client (never awake with a usable power, chained first, …)
Output: AutoplayRuns/client-sweep-<stamp>/coverage.md (+ the generated scenarios). Run issues: harvest_issues.py.
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


def client_events(run):
    out = []
    for folder in glob.glob(os.path.join(run, "*-client*", "")):
        path = os.path.join(folder, "events.ndjson")
        if os.path.exists(path):
            with open(path, encoding="utf-8-sig") as f:
                out += [json.loads(line) for line in f if line.strip()]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roles")
    ap.add_argument("--only")
    ap.add_argument("--seed", type=int, default=700)
    ap.add_argument("--clients", type=int, default=2)
    ap.add_argument("--days", type=int, default=3)
    ap.add_argument("--port", type=int, default=0, help="UDP port (parallel lanes)")
    ap.add_argument("--skip", default="", help="comma list of role-name fragments already covered")
    a = ap.parse_args()

    roles_path = a.roles or newest_roles()
    if not roles_path:
        sys.exit("no roles.json yet: run any host scenario first (every host run writes one)")
    pool = json.load(open(roles_path, encoding="utf-8-sig"))["roles"]
    names = [r["name"] for r in pool]
    targets = [r for r in pool if any(not p["passive"] for p in r["powers"])]
    if a.only:
        targets = [r for r in targets if any(o.strip().lower() in r["name"].lower() for o in a.only.split(","))]
    if a.skip:
        targets = [r for r in targets if not any(k.strip().lower() in r["name"].lower() for k in a.skip.split(",") if k.strip())]

    out = os.path.join(PROJECT, "AutoplayRuns", f"client-sweep-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "scenarios"), exist_ok=True)
    print(f"client sweep: {len(targets)} role(s) with active powers, pool from {roles_path}", flush=True)

    rows = []
    for i, role in enumerate(targets):
        frag = fragment_for(role["name"], names)
        if frag is None:
            rows.append((role["name"], "NOT COVERED", "no unique ASCII name fragment to force the role", ""))
            continue
        scenario = {
            "name": f"client-sweep-{frag.lower()}",
            "goal": f"{role['name']} held by a real client: every power use completes, no desync, no error",
            "mode": "net", "clients": a.clients, "seed": a.seed + i, "seedRetries": 3, "timescale": 3, "timeout": 900,
            # The forced role is seated on a client (role-holder: RoleAttributionState.DevSeatOrder), no re-roll.
            # Intro / chaining animations at x12: nobody acts there.
            "args": ["-autoplay-force-roles", frag, "-autoplay-role-holder", "client",
                     "-autoplay-max-days", str(a.days), "-autoplay-fast-fakes",
                     "-autoplay-fast-phases", "Intro|Chaining", "-autoplay-fast-timescale", "12"],
            "expect": [{"type": "outcome", "value": "Completed", "process": "all"},
                       {"type": "event", "kind": "power.timeout", "process": "clients", "min": 0, "max": 0},
                       {"type": "event", "kind": "power.error", "process": "any", "min": 0, "max": 0},
                       {"type": "desync", "max": 0},
                       {"type": "noErrors", "process": "all"}],
        }
        path = os.path.join(out, "scenarios", scenario["name"] + ".json")
        json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"[{i + 1}/{len(targets)}] {role['name']} (force '{frag}') …", flush=True)
        res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", os.path.join(HERE, "unityctl.sh"),
                              "--project", PROJECT] + (["--port", str(a.port)] if a.port else []), cwd=PROJECT, capture_output=True, text=True, encoding="utf-8",
                             errors="replace")
        run = (re.findall(r"VERDICT: \w+\s+\((.+?)\)", res.stdout) or [""])[-1]
        fails = [l.strip() for l in res.stdout.splitlines() if l.strip().startswith("[FAIL]")]
        events = client_events(run) if run else []
        role_word = frag.lower()
        used = [e for e in events if e["kind"] == "power.start" and role_word in e["detail"].lower()]
        if "composition mismatch" in res.stdout and not run:
            status, why = "NOT COVERED", "no seed gave the role to a client"
        elif fails:
            status, why = "FAIL", " / ".join(f[7:120] for f in fails)
        elif not used:
            status, why = "NOT COVERED", "the client never used a power of that role"
        else:
            status, why = "OK", f"{len(used)} use(s) on the client"
        rows.append((role["name"], status, why, os.path.basename(run)))
        print(f"    {status}: {why}", flush=True)

    lines = ["# Client power sweep", "", "| Role | Verdict | Why | Run |", "|---|---|---|---|"]
    lines += [f"| {r} | {s} | {w.replace('|', '/')} | {run} |" for r, s, w, run in rows]
    open(os.path.join(out, "coverage.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"coverage: {os.path.join(out, 'coverage.md')}")


if __name__ == "__main__":
    main()
