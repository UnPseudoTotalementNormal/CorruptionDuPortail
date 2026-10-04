"""Power sweep (Corruption du Portail): one forced-composition visual run per role that has targeted powers, then a
coverage table saying, for every targeted power, whether its card picker was seen working (blur veil + lifted cards).

    python -X utf8 tools/autoplay/sweep_powers.py [--roles <roles.json>] [--only <name fragment>] [--seed 500]

- Role pool: the newest AutoplayRuns/**/roles.json (every host run writes it), or --roles.
- Each role is forced into the composition (-autoplay-force-roles <unique ASCII word of its name>), the real picker is
  on screen (-autoplay-visual-picker), the run stops after day 2 (-autoplay-max-days 2).
- Verdict per power: OK (every picker it opened passed), FAIL (at least one failed), NOT COVERED (never opened one:
  the role was chained before acting, the power was not usable that night, …). NOT COVERED is never reported as OK.

Output: AutoplayRuns/sweep-<stamp>/coverage.md + coverage.json (+ one generated scenario per role).
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
ANALYZER = os.path.join(HERE, "analyze_picker.py")
KNOWN = ["CardEffect(TechnoBeacon|BoolEnabler): effectData"]


def newest_roles():
    found = glob.glob(os.path.join(PROJECT, "AutoplayRuns", "**", "roles.json"), recursive=True)
    return max(found, key=os.path.getmtime) if found else None


def fragment_for(name, all_names):
    words = sorted({w for w in re.findall(r"[A-Za-z]{4,}", name)}, key=len, reverse=True)
    for w in words:
        if sum(1 for n in all_names if w.lower() in n.lower()) == 1:
            return w
    return None


def analyze(run):
    res = subprocess.run([sys.executable, "-X", "utf8", ANALYZER, run], capture_output=True, text=True, encoding="utf-8", errors="replace")
    rows = []
    for line in res.stdout.splitlines():
        m = re.match(r"\s*\d+\s+(char|role|mixed|\?)\s+\d+\s+\d+\s+[\d.-]+\s+(OK|FAIL)\s+(.*)$", line)
        if m:
            rows.append((m.group(2), m.group(3)))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roles")
    ap.add_argument("--only")
    ap.add_argument("--seed", type=int, default=500)
    a = ap.parse_args()

    roles_path = a.roles or newest_roles()
    if not roles_path:
        sys.exit("no roles.json yet: run any host scenario first (every host run writes one)")
    pool = json.load(open(roles_path, encoding="utf-8-sig"))["roles"]
    names = [r["name"] for r in pool]
    targets = [r for r in pool if any(p["targeted"] and not p["passive"] for p in r["powers"])]
    if a.only:
        targets = [r for r in targets if a.only.lower() in r["name"].lower()]

    out = os.path.join(PROJECT, "AutoplayRuns", f"sweep-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "scenarios"), exist_ok=True)
    print(f"sweep: {len(targets)} role(s) with targeted powers, pool from {roles_path}")

    coverage = []
    for i, role in enumerate(targets):
        frag = fragment_for(role["name"], names)
        powers = [p["name"] for p in role["powers"] if p["targeted"] and not p["passive"]]
        if frag is None:
            for p in powers:
                coverage.append({"role": role["name"], "power": p, "status": "NOT COVERED", "why": "no unique ASCII name fragment to force the role", "run": None})
            continue

        scenario = {
            "name": f"sweep-{frag.lower()}",
            "goal": f"{role['name']}: every targeted power opens a working picker (blur + lifted cards)",
            "mode": "build", "seed": a.seed + i, "timescale": 3, "timeout": 600, "visualPicker": True,
            "args": ["-autoplay-force-roles", frag, "-autoplay-max-days", "2"],
            "expect": [{"type": "outcome", "value": "Completed"},
                       {"type": "noErrors", "ignore": KNOWN}],
        }
        path = os.path.join(out, "scenarios", scenario["name"] + ".json")
        json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"[{i + 1}/{len(targets)}] {role['name']} (force '{frag}') …", flush=True)
        res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", os.path.join(HERE, "unityctl.sh"), "--project", PROJECT],
                             cwd=PROJECT, capture_output=True, text=True, encoding="utf-8", errors="replace")
        m = re.search(r"VERDICT: (PASS|FAIL)\s+\((.+)\)", res.stdout)
        run = m.group(2) if m else None
        rows = analyze(run) if run else []
        for p in powers:
            hits = [v for v, opener in rows if p in opener and role["name"].split(",")[0] in opener]
            status = "NOT COVERED" if not hits else ("OK" if all(v == "OK" for v in hits) else "FAIL")
            why = "" if hits else ("run failed: " + (m.group(1) if m else "no run")) if not run or (m and m.group(1) == "FAIL") else "never opened a picker in 2 days"
            coverage.append({"role": role["name"], "power": p, "status": status, "openings": len(hits), "why": why, "run": run})
            print(f"    {status:12} {p} ({len(hits)} opening(s)) {why}")

    json.dump(coverage, open(os.path.join(out, "coverage.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    with open(os.path.join(out, "coverage.md"), "w", encoding="utf-8") as f:
        f.write("| Role | Power | Status | Openings | Note |\n|---|---|---|---|---|\n")
        for c in coverage:
            f.write(f"| {c['role']} | {c['power']} | {c['status']} | {c.get('openings', 0)} | {c['why']} |\n")
    counts = {s: sum(1 for c in coverage if c["status"] == s) for s in ("OK", "FAIL", "NOT COVERED")}
    print(f"coverage: {counts}  ->  {os.path.join(out, 'coverage.md')}")
    sys.exit(1 if counts["FAIL"] else 0)


if __name__ == "__main__":
    main()
