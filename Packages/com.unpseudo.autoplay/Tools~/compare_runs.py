"""Desync detector for a multi-process autoplay run (Tools~/launch-net.ps1).

Every process journals `state.hash` events: "<phase> | <hash> | <canonical state>", recorded once the phase has
settled. This compares, phase occurrence by phase occurrence, each client's hashes against the host's, and prints
the first differences with the canonical states side by side. It also summarizes each process's report.
A client that left on purpose (report fact "left=…") is not blamed for the phases it was no longer there for.

Usage: python Tools~/compare_runs.py <parent run folder>   (default: newest AutoplayRuns/net-*)
Exit code 1 on any desync or failed process. Importable: compare(parent) -> (problems, lines, desyncs).
"""
import glob
import json
import os
import sys


def load_hashes(run):
    hashes = []
    path = os.path.join(run, "events.ndjson")
    if not os.path.exists(path):
        return hashes
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            e = json.loads(line)
            if e.get("kind") == "state.hash":
                phase, digest, canonical = (part.strip() for part in e["detail"].split("|", 2))
                hashes.append((phase, digest, canonical))
    return hashes


def load_report(run):
    path = os.path.join(run, "report.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def process_dirs(parent):
    return sorted(d.rstrip("/\\") for d in glob.glob(os.path.join(parent, "*")) if os.path.isdir(d))


def host_dir(runs):
    return next((r for r in runs if "-host-" in os.path.basename(r)), runs[0] if runs else None)


def _keyed(items):
    # align on the n-th occurrence of each phase label (processes may join or record slightly differently)
    seen, out = {}, {}
    for phase, digest, canonical in items:
        seen[phase] = seen.get(phase, 0) + 1
        out[(phase, seen[phase])] = (digest, canonical)
    return out


def compare(parent):
    lines, problems, desyncs = [], 0, 0
    runs = process_dirs(parent)
    host = host_dir(runs)
    if host is None:
        return 1, [f"no process folders under {parent}"], 0

    lines.append(f"run: {parent}")
    for run in runs:
        rep = load_report(run)
        name = os.path.basename(run)
        if rep is None:
            lines.append(f"  {name}: NO REPORT (crashed or killed)")
            problems += 1
            continue
        lines.append(f"  {name}: {rep.get('outcome')} {rep.get('failureReason') or ''} errors={rep.get('errorCount')} "
                     f"facts={','.join(rep.get('facts', []))}")
        problems += rep.get("outcome") != "Completed"

    host_hashes = _keyed(load_hashes(host))
    lines.append(f"host state hashes: {len(host_hashes)}")
    for run in runs:
        if run == host:
            continue
        name = os.path.basename(run)
        rep = load_report(run) or {}
        # A process that left, or was refused at join, on purpose has no phases to compare after that point.
        left = any(f.startswith(("left=", "rejected=")) for f in rep.get("facts", []))
        theirs = _keyed(load_hashes(run))
        common = [k for k in host_hashes if k in theirs]
        diffs = [k for k in common if host_hashes[k][0] != theirs[k][0]]
        missing = 0 if left else len(set(host_hashes) - set(theirs))
        lines.append(f"  {name}: {len(common)} phases compared, {len(diffs)} desync, {missing} phases missing"
                     f"{' (left / refused on purpose)' if left else ''}")
        for k in diffs[:3]:
            lines.append(f"    DESYNC at {k[0]} (#{k[1]})")
            for a, b in zip(host_hashes[k][1].split(";"), theirs[k][1].split(";")):
                if a != b:
                    lines.append(f"      host  : {a}")
                    lines.append(f"      client: {b}")
        desyncs += len(diffs)
        problems += len(diffs)

    lines.append("RESULT: " + ("OK" if problems == 0 else f"{problems} problem(s)"))
    return problems, lines, desyncs


def main():
    root = os.path.join(os.getcwd(), "AutoplayRuns")
    parent = sys.argv[1] if len(sys.argv) > 1 else max(glob.glob(os.path.join(root, "net-*")), key=os.path.getmtime)
    problems, lines, _ = compare(parent.rstrip("/\\"))
    print("\n".join(lines))
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
