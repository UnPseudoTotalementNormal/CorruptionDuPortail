"""Autoplay campaign: every scenario of a folder + N random games (build and/or network), one aggregated summary.

    python Tools~/campaign.py --ctl tools/autoplay/unityctl.sh --scenarios tools/autoplay/scenarios \
        [--random-builds 3] [--random-nets 1] [--clients 3] [--base-seed 1000] [--max-days 0] \
        [--ignore "regex" ...]

Random games use RandomValidPolicy with a different seed each; they must complete, (net) without desync, and with no
error outside the --ignore patterns (known, accepted issues). Output: AutoplayRuns/campaign-<stamp>/summary.md +
summary.json (verdict per run, failing checks, most frequent error signatures across all runs).
Exit code 1 when any run failed. Long: launch it in the background.
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
sys.path.insert(0, HERE)
import compare_runs  # noqa: E402

RUNNER = os.path.join(HERE, "run_scenario.py")


def run_scenario(path, ctl, project):
    res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", ctl, "--project", project],
                         cwd=project, capture_output=True, text=True, encoding="utf-8", errors="replace")
    m = re.search(r"VERDICT: (PASS|FAIL)\s+\((.+)\)", res.stdout)
    failing = [line.strip()[7:] for line in res.stdout.splitlines() if line.strip().startswith("[FAIL]")]
    return (m.group(1) if m else "ERROR"), (m.group(2) if m else None), failing, res.stdout


def error_signatures(run):
    if not run:
        return []
    procs = compare_runs.process_dirs(run) if os.path.basename(run).startswith("net-") else [run]
    sigs = []
    for p in procs:
        for e in (compare_runs.load_report(p) or {}).get("errors", []):
            sigs.append(re.sub(r"\d+", "#", e[:160]))
    return sigs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ctl", required=True)
    ap.add_argument("--scenarios", nargs="*", default=[])
    ap.add_argument("--random-builds", type=int, default=0)
    ap.add_argument("--random-nets", type=int, default=0)
    ap.add_argument("--clients", type=int, default=3)
    ap.add_argument("--base-seed", type=int, default=1000)
    ap.add_argument("--max-days", type=int, default=0)
    ap.add_argument("--ignore", nargs="*", default=[])
    ap.add_argument("--project", default=os.getcwd())
    a = ap.parse_args()

    out = os.path.join(a.project, "AutoplayRuns", f"campaign-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "generated"), exist_ok=True)

    files = []
    for s in a.scenarios:
        files += sorted(glob.glob(os.path.join(s, "*.json"))) if os.path.isdir(s) else [s]

    extra = ["-autoplay-max-days", str(a.max_days)] if a.max_days > 0 else []
    for i in range(a.random_builds):
        seed = a.base_seed + i
        files.append(_generated(out, {"name": f"random-build-{seed}", "goal": "random legal game completes cleanly",
                                      "mode": "build", "seed": seed, "timescale": 4, "timeout": 900, "args": extra,
                                      "expect": [{"type": "outcome", "value": "Completed"},
                                                 {"type": "noErrors", "ignore": a.ignore}]}))
    for i in range(a.random_nets):
        seed = a.base_seed + 500 + i
        files.append(_generated(out, {"name": f"random-net-{seed}", "goal": "random networked game completes without desync",
                                      "mode": "net", "clients": a.clients, "seed": seed, "timescale": 3, "timeout": 1500,
                                      "args": extra,
                                      "expect": [{"type": "outcome", "value": "Completed", "process": "all"},
                                                 {"type": "desync", "max": 0},
                                                 {"type": "noErrors", "process": "all", "ignore": a.ignore}]}))

    results, signatures = [], {}
    print(f"campaign: {len(files)} run(s) -> {out}", flush=True)
    for n, f in enumerate(files, 1):
        name = os.path.splitext(os.path.basename(f))[0]
        verdict, run, failing, _ = run_scenario(f, a.ctl, a.project)
        print(f"[{n}/{len(files)}] {verdict:5} {name}  {'; '.join(failing)[:200]}", flush=True)
        for sig in error_signatures(run):
            signatures[sig] = signatures.get(sig, 0) + 1
        results.append({"scenario": name, "verdict": verdict, "failing": failing, "run": run})

    passed = sum(r["verdict"] == "PASS" for r in results)
    json.dump({"runs": results, "errorSignatures": signatures}, open(os.path.join(out, "summary.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=2)
    with open(os.path.join(out, "summary.md"), "w", encoding="utf-8") as md:
        md.write(f"# Autoplay campaign {os.path.basename(out)}\n\n**{passed}/{len(results)} passed**\n\n")
        md.write("| Scenario | Verdict | Failing checks | Run |\n|---|---|---|---|\n")
        for r in results:
            md.write(f"| {r['scenario']} | {r['verdict']} | {'<br>'.join(r['failing'])} | `{r['run']}` |\n")
        md.write("\n## Most frequent error signatures (all runs)\n\n")
        for sig, count in sorted(signatures.items(), key=lambda x: -x[1])[:15]:
            md.write(f"- x{count} `{sig}`\n")
    print(f"campaign: {passed}/{len(results)} passed -> {os.path.join(out, 'summary.md')}")
    sys.exit(0 if passed == len(results) else 1)


def _generated(out, scenario):
    path = os.path.join(out, "generated", scenario["name"] + ".json")
    json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    return path


if __name__ == "__main__":
    main()
