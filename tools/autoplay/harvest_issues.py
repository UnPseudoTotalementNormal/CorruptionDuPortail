"""Bug harvest over many autoplay runs: every distinct problem once, with how often and where it showed up.

    python -X utf8 tools/autoplay/harvest_issues.py [runs root (AutoplayRuns)] [--since YYYYMMDD-HHMMSS] [--md out.md]

Sources, per process folder (single build run or one folder per process of a net run):
  - report.json: outcome (not Completed = a failed / stuck run, with its failureReason) and errors (LogError, exceptions)
  - events.ndjson: power.error, power.timeout, select.error, input.miss, input.error, run.fail, capture.error, *.error
  - player logs next to a net run (host.log, clientN.log): [DESYNC], [CHARLIST], DOTween safe-mode captures,
    exceptions and assertion failures (whatever the report missed, e.g. after the run ended)
Messages are normalised (numbers, ids, paths) so the same problem in different runs counts as one row.
"""
import argparse
import collections
import glob
import json
import os
import re

EVENT_KINDS = re.compile(r"^(power\.error|power\.timeout|select\.error|input\.miss|input\.error|run\.fail|capture\.error|"
                         r"capture\.state\.error|teardown\.error|record\.tracks\.error)$")
LOG_PATTERNS = [
    ("desync", re.compile(r"^\[DESYNC\] (component=\w+).*")),
    ("charlist", re.compile(r"^\[CHARLIST\].*")),
    ("dotween", re.compile(r"DOTWEEN .*?(Target or field is missing/null.*|.*)$")),
    ("exception", re.compile(r"^(\w+(?:\.\w+)*Exception): (.*)$")),
    ("assert", re.compile(r"^(Assertion failed.*)$")),
]


def norm(text):
    text = re.sub(r"[A-Za-z]:[\\/][^\s'\"]+", "<path>", text)
    text = re.sub(r"\b1844674407370955\d{4}\b", "<fake>", text)
    text = re.sub(r"\b\d+(\.\d+)?\b", "N", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:220]


def run_name(folder, root):
    rel = os.path.relpath(folder, root).replace("\\", "/")
    return rel.split("/")[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?", default="AutoplayRuns")
    ap.add_argument("--since", default="")
    ap.add_argument("--md", default="")
    args = ap.parse_args()

    issues = collections.OrderedDict()  # key -> {count, runs:set, procs:set, example, source}

    def add(source, key_text, run, proc, example):
        key = (source, norm(key_text))
        it = issues.setdefault(key, {"count": 0, "runs": set(), "procs": set(), "example": example, "source": source})
        it["count"] += 1
        it["runs"].add(run)
        it["procs"].add(proc)

    runs = set()
    for events in glob.glob(os.path.join(args.root, "**", "events.ndjson"), recursive=True):
        folder = os.path.dirname(events)
        run = run_name(folder, args.root)
        if run.startswith("_"):
            continue  # _aborted/ and the like: runs killed from outside (not the game's doing)
        stamp = re.search(r"(\d{8}-\d{6})", run)
        if args.since and stamp and stamp.group(1) < args.since:
            continue
        runs.add(run)
        proc = os.path.basename(folder)
        role = "host" if "-host-" in proc or not re.search(r"-client\d+-", proc) else re.search(r"(client\d+)", proc).group(1)
        rep_path = os.path.join(folder, "report.json")
        if os.path.exists(rep_path):
            rep = json.load(open(rep_path, encoding="utf-8-sig"))
            if rep.get("outcome") not in ("Completed", None):
                add("run", f"{rep.get('outcome')}: {rep.get('failureReason')}", run, role, rep.get("failureReason") or "")
            for err in rep.get("errors", []):
                add("error", err.split(" @ ")[0], run, role, err[:300])
        else:
            crashed = False
            with open(events, encoding="utf-8-sig") as f:
                crashed = any('"kind":"crash"' in line for line in f)
            if not crashed:
                add("run", "no report (process killed or crashed)", run, role, folder)
        with open(events, encoding="utf-8-sig") as f:
            for line in f:
                try:
                    e = json.loads(line)
                except ValueError:
                    continue
                if EVENT_KINDS.match(e.get("kind", "")):
                    detail = e.get("detail", "")
                    if e["kind"] == "input.miss":
                        detail = re.sub(r"pos=\S+", "", detail)
                    add("event", f"{e['kind']} {detail}", run, role, detail[:300])

    # Player logs of net runs (one per process at the run root).
    for log in glob.glob(os.path.join(args.root, "*", "*.log")):
        run = run_name(log, args.root)
        if run not in runs or os.path.basename(log) == "launcher.log":
            continue
        proc = os.path.splitext(os.path.basename(log))[0]
        with open(log, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.rstrip()
                for source, pat in LOG_PATTERNS:
                    m = pat.search(line)
                    if m:
                        add("log:" + source, m.group(0), run, proc, line[:300])
                        break

    rows = sorted(issues.items(), key=lambda kv: (-len(kv[1]["runs"]), -kv[1]["count"]))
    out = [f"# Autoplay issue harvest — {len(runs)} runs ({args.root}{' since ' + args.since if args.since else ''})", "",
           "| # | Source | Problem (normalised) | Runs | Hits | Processes |", "|---|---|---|---|---|---|"]
    for i, ((source, text), it) in enumerate(rows, 1):
        out.append(f"| {i} | {source} | `{text.replace('|', '/')}` | {len(it['runs'])} | {it['count']} | "
                   f"{','.join(sorted(it['procs']))[:60]} |")
    out.append("")
    out.append("## Examples")
    for i, ((source, text), it) in enumerate(rows, 1):
        out.append(f"{i}. {source} — {sorted(it['runs'])[0]}: {it['example'].replace(chr(10), ' ')[:300]}")
    text = "\n".join(out)
    print(text)
    if args.md:
        with open(args.md, "w", encoding="utf-8") as f:
            f.write(text + "\n")


if __name__ == "__main__":
    main()
