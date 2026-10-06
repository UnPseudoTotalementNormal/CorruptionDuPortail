"""Declarative autoplay scenarios: goal + run mode + levers + expectations -> pass/fail verdict.

    python Tools~/run_scenario.py <scenario.json> [--ctl <project unityctl wrapper>] [--seed N]

Scenario file (JSON):
{
  "name": "mage-portal-client",
  "goal": "A Mage played by a real client takes down the portal",
  "mode": "build" | "net",              # play-build (host + bots) or play-net (host + real clients)
  "clients": 7,                          # net only
  "seed": 89, "seedRetries": 6,          # retried with seed+1, +2… when the host reports "composition mismatch"
  "timescale": 3, "timeout": 900,
  "visualPicker": false,
  "args": ["-autoplay-force-roles", "Mage"],      # every process
  "clientArgs": [], "client1Args": [],           # net only: every client / client1 only
  "video": true | "client1",                     # film the run (or --video [all|client1]): video.mp4 per process
  "client1Relaunch": {"after": 5, "args": ["-autoplay-relaunched"]},   # net only: relaunch client1 once if its game
                                                                       # dies first (lever crash-at), after N seconds
  "expect": [ … see CHECKS below … ]
}

CHECKS (all must pass; a check with "severity": "warn" is reported but does not fail the scenario):
  {"type": "outcome", "value": "Completed", "process": "all|host|clients"}   (+ "alsoAccept": ["Crashed"]: a process
                                                                     killed on purpose by crash-at writes no report)
  {"type": "event", "kind": "portal.click", "detail": "regex", "process": "any|host|clients|all|every-client", "min": 1, "max": 5}
      any / host / clients = total over those processes; all / every-client = each of those processes on its own
  {"type": "noErrors", "ignore": ["regex", …], "process": "all|host|clients"}
  {"type": "fact", "match": "regex", "process": "host|clients|any"}
  {"type": "roster", "match": "regex", "min": 1}                      (host final roster lines)
  {"type": "desync", "max": 0}                                        (net only, via compare_runs)
  {"type": "state", "label": "regex", "path": "game.frostAlpha", "op": ">=", "value": 0.9, "quantifier": "all|any"}
  {"type": "leaverChained"}                                           (net: whoever left is chained on the host)
  {"type": "analyzer", "cmd": "python -X utf8 tools/autoplay/analyze_picker.py {run}"}   (exit code 0 = pass)

Writes <run>/verdict.json and prints one line per check. Exit code 0 = scenario passed.
"""
import argparse
import glob
import json
import operator
import os
import re
import shlex
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import compare_runs  # noqa: E402

def find_bash():
    """Git Bash — never WSL's System32 bash.exe, which `bash` on PATH often is on Windows."""
    candidates = [os.environ.get("AUTOPLAY_BASH"),
                  r"C:\Program Files\Git\bin\bash.exe", r"C:\Program Files\Git\usr\bin\bash.exe",
                  shutil.which("bash")]
    for c in candidates:
        if c and os.path.exists(c) and "system32" not in c.lower():
            return c
    return "bash"


BASH = find_bash()

OPS = {">=": operator.ge, ">": operator.gt, "<=": operator.le, "<": operator.lt, "==": operator.eq, "!=": operator.ne}


def quote_args(items):
    # Values travel as one player command line: quote the ones with spaces (Windows command-line rules).
    return " ".join(f'"{a}"' if " " in a else a for a in items)


def run_once(scenario, seed, ctl, project):
    env = dict(os.environ)
    # "video": true / "all" films every process, "client1" only that one (video.mp4 in each process folder).
    video = scenario.get("video")
    film_all = video is True or video == "all"
    film_c1 = video == "client1"
    env["AUTOPLAY_ARGS"] = quote_args(scenario.get("args", []) + (["-autoplay-video"] if film_all else []))
    env["AUTOPLAY_CLIENT_ARGS"] = quote_args(scenario.get("clientArgs", []))
    env["AUTOPLAY_CLIENT1_ARGS"] = quote_args(scenario.get("client1Args", []) + (["-autoplay-video"] if film_c1 else []))
    relaunch = scenario.get("client1Relaunch")
    relaunch_args = (relaunch.get("args", ["-autoplay-relaunched"]) + (["-autoplay-video"] if film_c1 else [])) if relaunch else []
    env["AUTOPLAY_CLIENT1_RELAUNCH_ARGS"] = quote_args(relaunch_args)
    env["AUTOPLAY_CLIENT1_RELAUNCH_DELAY"] = str(relaunch.get("after", 5)) if relaunch else "5"
    env["AUTOPLAY_TIMESCALE"] = str(scenario.get("timescale", 4))
    env["AUTOPLAY_TIMEOUT"] = str(scenario.get("timeout", 900))
    env["AUTOPLAY_SCENARIO"] = re.sub(r"[^A-Za-z0-9-]", "-", scenario["name"])
    if scenario.get("visualPicker"):
        env["AUTOPLAY_VISUAL_PICKER"] = "1"
    port = str(scenario.get("port", 7851 if scenario.get("mode", "build") == "build" else 7870))
    if scenario.get("mode", "build") == "net":
        cmd = [BASH, ctl, "play-net", str(scenario.get("clients", 3)), str(seed), port]
    else:
        cmd = [BASH, ctl, "play-build", str(seed), port]
    out = subprocess.run(cmd, env=env, cwd=project, capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    match = re.search(r"^run: (.+?)/?\s*$", out, re.M)
    return (match.group(1).rstrip("/\\") if match else None), out


class Run:
    def __init__(self, path, mode):
        self.path = path
        self.mode = mode
        self.procs = compare_runs.process_dirs(path) if mode == "net" else [path]
        self.host = compare_runs.host_dir(self.procs) if mode == "net" else path

    def select(self, which):
        if which == "host":
            return [self.host]
        if which in ("clients", "every-client"):
            return [p for p in self.procs if p != self.host]
        return self.procs

    @staticmethod
    def report(proc):
        rep = compare_runs.load_report(proc)
        if rep is None and any(e["kind"] == "crash" for e in Run.events(proc)):
            # Killed on purpose (lever crash-at): a real crash writes no report.
            return {"outcome": "Crashed", "errors": [], "facts": []}
        return rep or {}

    @staticmethod
    def events(proc):
        path = os.path.join(proc, "events.ndjson")
        if not os.path.exists(path):
            return []
        with open(path, encoding="utf-8-sig") as f:
            return [json.loads(l) for l in f if l.strip()]


def check_animation(run, c):
    """Assertions on recorded animation tracks (AutoplayRecorder tracks.csv), per matching recording.

    {"type": "animation", "recording": "regex on rec-folder name", "track": "frost",
     "start": {"op": "<", "value": 0.2}, "end": {"op": ">=", "value": 0.9},
     "reach": {"op": ">=", "value": 0.9, "within": 0.6},      # first time the condition holds, in seconds
     "change": {"op": ">", "value": 0.05},                     # end - start
     "monotonic": "up|down", "quantifier": "all|any", "minRecordings": 1,
     "onlyIf": {"track": "lifted", "at": "end", "op": ">=", "value": 1},  # skip recordings where it does not hold
     "window": {"from": 0, "to": 1.4}}                                   # only judge samples inside [from, to] seconds
    Blank samples (track not available on that frame) are skipped.
    """
    import csv
    rx = re.compile(c.get("recording", "."))
    recs = [d for p in run.select(c.get("process", "host")) for d in sorted(glob.glob(os.path.join(p, "rec-*")))
            if rx.search(os.path.basename(d))]
    if len(recs) < c.get("minRecordings", 1):
        return False, f"animation {c['track']}: {len(recs)} recording(s) match /{c.get('recording', '.')}/"

    verdicts, notes, skipped = [], [], 0
    for rec in recs:
        path = os.path.join(rec, "tracks.csv")
        rows = list(csv.DictReader(open(path, encoding="utf-8-sig", newline=""))) if os.path.exists(path) else []
        win = c.get("window", {})
        rows = [r for r in rows if win.get("from", float("-inf")) <= float(r["time"]) <= win.get("to", float("inf"))]
        cond = c.get("onlyIf")
        if cond:
            vals = [float(r[cond["track"]]) for r in rows if r.get(cond["track"]) not in (None, "")]
            probe = (vals[-1] if cond.get("at", "end") == "end" else vals[0]) if vals else None
            if probe is None or not OPS[cond["op"]](probe, cond["value"]):
                skipped += 1
                continue
        series = [(float(r["time"]), float(r[c["track"]])) for r in rows if r.get(c["track"]) not in (None, "")]
        problems = []
        if not series:
            problems.append("no samples")
        else:
            first, last = series[0][1], series[-1][1]
            for key, value in (("start", first), ("end", last), ("change", last - first)):
                if key in c and not OPS[c[key]["op"]](value, c[key]["value"]):
                    problems.append(f"{key}={value:.3f} not {c[key]['op']} {c[key]['value']}")
            if "reach" in c:
                r = c["reach"]
                hit = next((t - series[0][0] for t, v in series if OPS[r["op"]](v, r["value"])), None)
                if hit is None or hit > r["within"]:
                    problems.append(f"never {r['op']} {r['value']} within {r['within']}s" if hit is None else f"reached at {hit:.2f}s > {r['within']}s")
            if c.get("monotonic") in ("up", "down"):
                eps = c.get("tolerance", 1e-4)
                steps = [b - a for (_, a), (_, b) in zip(series, series[1:])]
                bad = [s for s in steps if (s < -eps if c["monotonic"] == "up" else s > eps)]
                if bad:
                    problems.append(f"not monotonic {c['monotonic']} ({len(bad)} step(s) against)")
        verdicts.append(not problems)
        if problems:
            notes.append(f"{os.path.basename(rec)}: {'; '.join(problems)}")

    if len(verdicts) < c.get("minRecordings", 1):
        return False, f"animation {c['track']}: only {len(verdicts)} recording(s) left after onlyIf (skipped {skipped})"
    quant = all if c.get("quantifier", "all") == "all" else any
    ok = quant(verdicts)
    return ok, (f"animation {c['track']} on {len(verdicts)} recording(s){f' (+{skipped} skipped by onlyIf)' if skipped else ''}: "
                f"{sum(verdicts)} ok") + (f" — {notes[:2]}" if notes else "")


def check(run, c, project):
    t = c["type"]
    if t == "outcome":
        accepted = [c.get("value", "Completed")] + list(c.get("alsoAccept", []))
        bad = [os.path.basename(p) for p in run.select(c.get("process", "all"))
               if run.report(p).get("outcome") not in accepted]
        return not bad, f"outcome {c.get('value', 'Completed')}" + (f" — not for {bad}" if bad else "")

    if t == "event":
        rx = re.compile(c.get("detail", ""))
        which = c.get("process", "any")
        counts = {os.path.basename(p): sum(1 for e in run.events(p) if e["kind"] == c["kind"] and rx.search(e["detail"] or ""))
                  for p in run.select("all" if which == "any" else which)}
        lo, hi = c.get("min", 1), c.get("max")
        ok_one = lambda n: n >= lo and (hi is None or n <= hi)  # noqa: E731
        ok = all(ok_one(n) for n in counts.values()) if which in ("all", "every-client") else ok_one(sum(counts.values()))
        return ok, f"event {c['kind']}{' ~/' + c['detail'] + '/' if c.get('detail') else ''} counts={counts} (min {lo}{'' if hi is None else ', max ' + str(hi)})"

    if t == "noErrors":
        ignore = [re.compile(r) for r in c.get("ignore", [])]
        found = {}
        for p in run.select(c.get("process", "all")):
            for e in run.report(p).get("errors", []):
                if not any(r.search(e) for r in ignore):
                    found[e[:140]] = found.get(e[:140], 0) + 1
        top = sorted(found.items(), key=lambda x: -x[1])[:3]
        return not found, "no unexpected errors" + (f" — {sum(found.values())} found, e.g. {top}" if found else "")

    if t == "fact":
        rx = re.compile(c["match"])
        procs = run.select(c.get("process", "host") if c.get("process") != "any" else "all")
        hits = [os.path.basename(p) for p in procs if any(rx.search(f) for f in run.report(p).get("facts", []))]
        return bool(hits), f"fact /{c['match']}/ in {hits or 'none'}"

    if t == "roster":
        rx = re.compile(c["match"])
        n = sum(1 for line in run.report(run.host).get("finalRoster", []) if rx.search(line))
        return n >= c.get("min", 1), f"roster /{c['match']}/ x{n}"

    if t == "desync":
        if run.mode != "net":
            return False, "desync check needs mode net"
        _, _, desyncs = compare_runs.compare(run.path)
        return desyncs <= c.get("max", 0), f"desync={desyncs} (max {c.get('max', 0)})"

    if t == "state":
        rx = re.compile(c["label"])
        op = OPS[c.get("op", ">=")]
        values = []
        for p in run.select(c.get("process", "host")):
            for f in sorted(glob.glob(os.path.join(p, "*.json"))):
                name = os.path.basename(f)
                if name in ("report.json", "verdict.json", "roles.json") or not rx.search(name):
                    continue
                data = json.load(open(f, encoding="utf-8-sig"))
                for key in c["path"].split("."):
                    data = data.get(key) if isinstance(data, dict) else None
                values.append((name, data))
        results = [v is not None and op(v, c["value"]) for _, v in values]
        quant = all if c.get("quantifier", "all") == "all" else any
        ok = bool(values) and quant(results)
        failing = [n for (n, _), r in zip(values, results) if not r][:3]
        return ok, f"state {c['path']} {c.get('op', '>=')} {c['value']} on {len(values)} capture(s)" + (f" — failing {failing}" if failing else "")

    if t == "leaverChained":
        ids = [m.group(1) for p in run.procs for e in run.events(p) if e["kind"] == "leave"
               for m in [re.search(r"client (\d+) leaves", e["detail"])] if m]
        roster = run.report(run.host).get("finalRoster", [])
        chained = [i for i in ids if any(line.startswith(i + " ") and "chained=True" in line for line in roster)]
        return bool(ids) and len(chained) == len(ids), f"leavers {ids or 'none'} chained on host: {chained}"

    if t == "animation":
        return check_animation(run, c)

    if t == "analyzer":
        cmd = c["cmd"].replace("{run}", run.path)
        res = subprocess.run(shlex.split(cmd), cwd=project, capture_output=True, text=True, encoding="utf-8", errors="replace")
        tail = (res.stdout.strip().splitlines() or ["(no output)"])[-1]
        return res.returncode == 0, f"analyzer `{c['cmd'].split()[-2] if len(c['cmd'].split()) > 1 else c['cmd']}`: {tail}"

    return False, f"unknown check type {t}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scenario")
    ap.add_argument("--ctl", default=os.environ.get("AUTOPLAY_CTL", "tools/autoplay/unityctl.sh"))
    ap.add_argument("--seed", type=int)
    ap.add_argument("--project", default=os.getcwd())
    ap.add_argument("--evaluate", help="re-check an existing run folder against the scenario, without playing")
    ap.add_argument("--video", nargs="?", const="all", choices=["all", "client1"],
                    help="film the run (all processes, or client1 only): video.mp4 in each process folder")
    a = ap.parse_args()

    scenario = json.load(open(a.scenario, encoding="utf-8-sig"))
    if a.video:
        scenario["video"] = a.video
    seed = a.seed if a.seed is not None else scenario.get("seed", 1)
    attempts = max(1, scenario.get("seedRetries", 1))
    print(f"scenario: {scenario['name']} — {scenario.get('goal', '')}")

    run_path = a.evaluate.rstrip("/\\") if a.evaluate else None
    attempt = 0
    for attempt in range(0 if a.evaluate else attempts):
        run_path, out = run_once(scenario, seed + attempt, a.ctl, a.project)
        if run_path is None:
            print("could not find the run folder in the launcher output:\n" + out[-2000:])
            sys.exit(2)
        with open(os.path.join(run_path, "launcher.log"), "w", encoding="utf-8") as f:
            f.write(out)
        run = Run(run_path, scenario.get("mode", "build"))
        reason = run.report(run.host).get("failureReason") or ""
        if reason.startswith("composition mismatch") and attempt + 1 < attempts:
            print(f"  seed {seed + attempt}: {reason} -> retry")
            continue
        break

    run = Run(run_path, scenario.get("mode", "build"))
    results = []
    for c in scenario.get("expect", []):
        try:
            ok, detail = check(run, c, a.project)
        except Exception as exc:  # a broken check is a failed check, with the reason
            ok, detail = False, f"{c.get('type')}: {type(exc).__name__}: {exc}"
        warn = c.get("severity") == "warn"
        results.append({"check": c, "pass": ok, "warn": warn, "detail": detail})
        print(f"  [{'PASS' if ok else ('WARN' if warn else 'FAIL')}] {detail}")

    passed = all(r["pass"] or r["warn"] for r in results)
    verdict = {"scenario": scenario["name"], "goal": scenario.get("goal"), "seed": seed + attempt, "run": run_path,
               "pass": passed, "checks": results}
    with open(os.path.join(run_path, "verdict.json"), "w", encoding="utf-8") as f:
        json.dump(verdict, f, ensure_ascii=False, indent=2)
    for video in sorted(glob.glob(os.path.join(run_path, "**", "video.mp4"), recursive=True)):
        print(f"  video: {video}")
    print(f"VERDICT: {'PASS' if passed else 'FAIL'}  ({run_path})")
    sys.exit(0 if passed else 1)


if __name__ == "__main__":
    main()
