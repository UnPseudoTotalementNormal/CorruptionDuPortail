"""Phase sweep: one scenario template replayed at every phase of the day (drop / rejoin / host loss at each one).

    python -X utf8 tools/autoplay/sweep_phases.py <template.json> [--phases "A,B,…"] [--seed 800] [--port N]

The template is a normal scenario whose strings may contain {phase} (the phase text, e.g. "VoteRecapState day=1")
and {slug}; each phase gets its own scenario (seed + 1 per phase) run through run_scenario.py. Verdict per phase:
PASS / FAIL (with the failed checks). Output: AutoplayRuns/phase-sweep-<name>-<stamp>/coverage.md.

Default phases: every state of a day-1 / day-2 loop where something async runs (intro, awakening and its recap, the
vote, vote recap, chaining; the portal step lasts one frame unless a Mage is chained, too short to be seen).
"""
import argparse
import datetime
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
RUNNER = os.path.join(PROJECT, "Packages", "com.unpseudo.autoplay", "Tools~", "run_scenario.py")
DEFAULT_PHASES = ["GameIntroductionState day=1", "AwakeningState day=1", "AwakeningRecapState day=1", "VoteState day=1",
                  "VoteRecapState day=1", "ChainingState day=1", "AwakeningState day=2"]


def fill(value, phase, slug):
    if isinstance(value, str):
        return value.replace("{phase}", phase).replace("{slug}", slug)
    if isinstance(value, list):
        return [fill(v, phase, slug) for v in value]
    if isinstance(value, dict):
        return {k: fill(v, phase, slug) for k, v in value.items()}
    return value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("template")
    ap.add_argument("--phases")
    ap.add_argument("--seed", type=int)
    ap.add_argument("--port", type=int, default=0)
    a = ap.parse_args()

    template = json.load(open(a.template, encoding="utf-8"))
    phases = [p.strip() for p in a.phases.split(",")] if a.phases else DEFAULT_PHASES
    seed = a.seed if a.seed is not None else template.get("seed", 800)
    out = os.path.join(PROJECT, "AutoplayRuns", f"phase-sweep-{template['name']}-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "scenarios"), exist_ok=True)

    rows = []
    for i, phase in enumerate(phases):
        slug = re.sub(r"[^A-Za-z0-9]+", "-", phase).strip("-").lower()
        scenario = fill(template, phase, slug)
        scenario["name"] = f"{template['name']}-{slug}"
        scenario["seed"] = seed + i
        path = os.path.join(out, "scenarios", scenario["name"] + ".json")
        json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"[{i + 1}/{len(phases)}] {phase} …", flush=True)
        res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", os.path.join(HERE, "unityctl.sh"),
                              "--project", PROJECT] + (["--port", str(a.port)] if a.port else []), cwd=PROJECT,
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        run = (re.findall(r"VERDICT: \w+\s+\((.+?)\)", res.stdout) or [""])[-1]
        verdict = (re.findall(r"VERDICT: (\w+)", res.stdout) or ["NO RUN"])[-1]
        fails = [l.strip()[7:160] for l in res.stdout.splitlines() if l.strip().startswith("[FAIL]")]
        rows.append((phase, verdict, " / ".join(fails), os.path.basename(run)))
        print(f"    {verdict}: {' / '.join(fails)[:300]}", flush=True)

    lines = [f"# Phase sweep — {template['name']}", "", template.get("goal", ""), "", "| Phase | Verdict | Failed checks | Run |",
             "|---|---|---|---|"]
    lines += [f"| {p} | {v} | {f.replace('|', '/')} | {r} |" for p, v, f, r in rows]
    open(os.path.join(out, "coverage.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"coverage: {os.path.join(out, 'coverage.md')}")


if __name__ == "__main__":
    main()
