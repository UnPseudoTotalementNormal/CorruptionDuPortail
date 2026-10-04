"""Desync detector for a multi-process autoplay run (Tools~/launch-net.ps1).

Every process journals `state.hash` events: "<phase> | <hash> | <canonical state>", recorded once the phase has
settled. This compares, phase occurrence by phase occurrence, each client's hashes against the host's, and prints
the first differences with the canonical states side by side. It also summarizes each process's report.

Usage: python Tools~/compare_runs.py <parent run folder>   (default: newest AutoplayRuns/net-*)
Exit code 1 on any desync or failed process.
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


def main():
    root = os.path.join(os.getcwd(), "AutoplayRuns")
    parent = sys.argv[1] if len(sys.argv) > 1 else max(glob.glob(os.path.join(root, "net-*")), key=os.path.getmtime)
    runs = sorted(d for d in glob.glob(os.path.join(parent, "*")) if os.path.isdir(d))
    host = next((r for r in runs if "-host-" in os.path.basename(r)), runs[0] if runs else None)
    if host is None:
        print(f"no process folders under {parent}")
        sys.exit(1)

    print(f"run: {parent}")
    bad = 0
    for run in runs:
        rep = load_report(run)
        name = os.path.basename(run)
        if rep is None:
            print(f"  {name}: NO REPORT (crashed or killed)")
            bad += 1
            continue
        print(f"  {name}: {rep.get('outcome')} {rep.get('failureReason') or ''} errors={rep.get('errorCount')} "
              f"facts={','.join(rep.get('facts', []))}")
        bad += rep.get("outcome") != "Completed"

    host_hashes = load_hashes(host)
    print(f"host state hashes: {len(host_hashes)}")
    for run in runs:
        if run == host:
            continue
        name = os.path.basename(run)
        theirs = load_hashes(run)
        # align on the n-th occurrence of each phase label (processes may join or record slightly differently)
        def keyed(items):
            seen, out = {}, {}
            for phase, digest, canonical in items:
                seen[phase] = seen.get(phase, 0) + 1
                out[(phase, seen[phase])] = (digest, canonical)
            return out
        h, c = keyed(host_hashes), keyed(theirs)
        common = [k for k in h if k in c]
        diffs = [k for k in common if h[k][0] != c[k][0]]
        print(f"  {name}: {len(common)} phases compared, {len(diffs)} desync, {len(set(h) - set(c))} phases missing")
        for k in diffs[:3]:
            print(f"    DESYNC at {k[0]} (#{k[1]})")
            hs, cs = h[k][1].split(";"), c[k][1].split(";")
            for a, b in zip(hs, cs):
                if a != b:
                    print(f"      host  : {a}\n      client: {b}")
        bad += len(diffs)

    print("RESULT:", "OK" if bad == 0 else f"{bad} problem(s)")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
