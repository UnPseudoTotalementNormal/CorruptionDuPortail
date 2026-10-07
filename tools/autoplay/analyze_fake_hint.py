"""Anomalies' fake-role hint over a run (GD wording: "les Anomalies connaissent autant de rôles factices que leur
nombre dans la partie"): every anomaly played by a real process knows the same fake roles, at most as many as there
are anomalies, and no other player knows any.

    python -X utf8 tools/autoplay/analyze_fake_hint.py <run>

Reads the "knowledge" lines "V>T fake=10 role=<name>" each process journals for the seats it controls (V = viewer).
Exit 0 = pass, 1 = failure, 2 = not covered (no anomaly on a process, or no fake to learn).
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import load_run, roster  # noqa: E402


def main():
    run = sys.argv[1]
    procs = load_run(run)
    factions = roster(procs)
    anomalies = sorted(pid for pid, (_, faction) in factions.items() if faction == "anomaly")

    known = {}  # viewer -> {fake target: role}
    for p in procs:
        for e in p.of("knowledge"):
            m = re.match(r"(\d+)>(\d+) fake=(\d+) role=(.*)", e["detail"])
            if m and int(m.group(3)) > 0:
                known.setdefault(int(m.group(1)), {})[int(m.group(2))] = m.group(4)

    failures = []
    for viewer, fakes in sorted(known.items()):
        if viewer not in anomalies:
            failures.append(f"LEAK  {viewer} ({factions.get(viewer, ('?', '?'))[1]}) knows fake roles {sorted(fakes.values())}")
    seen = {v: known.get(v, {}) for v in anomalies}
    checked = [v for v in anomalies if v in known]
    sets = {frozenset(f) for f in seen.values()}
    for viewer, fakes in seen.items():
        print(f"anomaly {viewer} ({factions[viewer][0]}): {len(fakes)} fake role(s) {sorted(fakes.values())}")
        if len(fakes) > len(anomalies):
            failures.append(f"TOO MANY  {viewer} knows {len(fakes)} fakes for {len(anomalies)} anomalies")
    if len(sets) > 1:
        failures.append(f"DIFFER  the anomalies do not know the same fakes: {[sorted(s) for s in sets]}")

    for line in failures:
        print(line)
    if failures:
        print("FAIL")
        sys.exit(1)
    if not anomalies or not checked:
        print("NOT COVERED: no anomaly learned a fake role on a journaled seat")
        sys.exit(2)
    print(f"PASS ({len(anomalies)} anomalies, {len(next(iter(seen.values())))} fake role(s) each)")


if __name__ == "__main__":
    main()
