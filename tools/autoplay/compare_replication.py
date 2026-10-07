"""Replication diff (desync hunt): every replicated value, host vs each real client, phase by phase.

    python -X utf8 tools/autoplay/compare_replication.py <net run folder> [<net run folder> ...] [--detail N]

Each process journals `state.repl` once per settled phase (outside the awakening): "<phase> | <count> | line ;; line",
one line per NetworkVariable / NetworkList value ("<NetworkObjectId>:<Type>.<field>=<value>", AutoplayDriverReplication).
For each phase both the host and a client sampled, the lines are diffed; differences are grouped by Type.field (the
system that diverged), with how many phases / runs show it and the first example. A value the host has and the client
lacks (object not spawned there) shows as "missing". Exit 0 = no difference, 1 = differences, 2 = nothing compared.
"""
import collections
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import Process  # noqa: E402


def samples(proc):
    out = {}
    for e in proc.events:
        if e.get("kind") != "state.repl":
            continue
        phase, _, rest = e["detail"].partition(" | ")
        _, _, body = rest.partition(" | ")
        lines = {}
        for line in body.split(" ;; "):
            key, sep, value = line.partition("=")
            if sep:
                lines[key] = value
        out.setdefault(phase, lines)  # first sample of that phase (a phase can repeat only with a new day)
    return out


def view_samples(proc):
    """state.view: {(phase, viewer): {"knowledge": str, "icons": str}} (host: every real seat; client: its own)."""
    out = {}
    for e in proc.events:
        if e.get("kind") != "state.view":
            continue
        phase, _, rest = e["detail"].partition(" | ")
        viewer, _, body = rest.partition(" | ")
        parts = dict(p.split("=", 1) for p in body.split(" ;; ") if "=" in p)
        # Fake characters (ids near ulong.MaxValue): the host's ledger lists them, a client's probe may not (older
        # builds): compare real players only.
        if "knowledge" in parts:
            parts["knowledge"] = ",".join(x for x in re.findall(r"\[[^\]]*\]", parts["knowledge"])
                                          if not x[1:].startswith("184467440737095"))
        out.setdefault((phase, viewer.replace("v=", "")), parts)
    return out


def field_of(key):
    return key.split(":", 1)[1] if ":" in key else key


def main(argv):
    detail = 3
    runs = []
    i = 0
    while i < len(argv):
        if argv[i] == "--detail":
            detail = int(argv[i + 1])
            i += 2
            continue
        runs.append(argv[i])
        i += 1
    groups = collections.OrderedDict()  # field -> {phases, runs, examples}
    compared = 0
    for run in runs:
        procs = [Process(p) for p in sorted(glob.glob(os.path.join(run, "*/"))) if os.path.exists(os.path.join(p, "events.ndjson"))]
        host = next((p for p in procs if p.is_host), None)
        if host is None:
            continue
        hs = samples(host)
        for proc in procs:
            if proc is host:
                continue
            cs = samples(proc)
            common = [ph for ph in hs if ph in cs]  # journal order = game order
            for idx, phase in enumerate(common):
                compared += 1
                h, c = hs[phase], cs[phase]
                for key in sorted(set(h) | set(c)):
                    hv, cv = h.get(key, "<missing>"), c.get(key, "<missing>")
                    if hv == cv:
                        continue
                    # Each process samples 1.5 s after ITS phase start (a client later, by the latency): a value the
                    # server changed in between differs by timing only. Persistent = still different at the next
                    # phase both sampled (or no later phase to tell).
                    nxt = common[idx + 1] if idx + 1 < len(common) else None
                    persistent = nxt is None or hs[nxt].get(key, "<missing>") != cs[nxt].get(key, "<missing>")
                    name = field_of(key) + ("" if persistent else "  (transient)")
                    g = groups.setdefault(name, {"phases": 0, "runs": set(), "procs": set(), "examples": []})
                    g["phases"] += 1
                    g["runs"].add(os.path.basename(run.rstrip("/\\")))
                    g["procs"].add(re.sub(r"^\d{8}-\d{6}-", "", proc.name))
                    if len(g["examples"]) < detail:
                        g["examples"].append(f"{os.path.basename(run.rstrip('/'))} {proc.name.split('-')[2]} {phase} "
                                             f"{key}: host={hv[:160]!r} client={cv[:160]!r}")
            # Per-player RPC state: the client's own view vs the server truth for its seat, phase by phase.
            hv_all, cv_all = view_samples(host), view_samples(proc)
            mine = [k for k in cv_all if k in hv_all]
            for idx, key in enumerate(mine):
                for part in ("knowledge", "icons"):
                    hv, cv = hv_all[key].get(part, ""), cv_all[key].get(part, "")
                    if hv == cv:
                        continue
                    later = [k for k in mine[idx + 1:] if k[1] == key[1]]
                    persistent = not later or hv_all[later[0]].get(part, "") != cv_all[later[0]].get(part, "")
                    name = f"view.{part}" + ("" if persistent else "  (transient)")
                    g = groups.setdefault(name, {"phases": 0, "runs": set(), "procs": set(), "examples": []})
                    g["phases"] += 1
                    g["runs"].add(os.path.basename(run.rstrip("/\\")))
                    g["procs"].add(re.sub(r"^\d{8}-\d{6}-", "", proc.name))
                    if len(g["examples"]) < detail:
                        g["examples"].append(f"{os.path.basename(run.rstrip('/'))} {proc.name.split('-')[2]} {key[0]} seat {key[1]} "
                                             f"{part}: server={hv[:200]!r} client={cv[:200]!r}")
    if compared == 0:
        print("NOT COVERED: no phase sampled by both the host and a client (state.repl missing: old build?)")
        return 2
    print(f"{compared} (client, phase) samples compared over {len(runs)} run(s)")
    if not groups:
        print("RESULT: no replicated value differs")
        return 0
    print(f"{'Field':45} {'Diffs':>6} {'Runs':>5}")
    for field, g in sorted(groups.items(), key=lambda kv: (-len(kv[1]["runs"]), -kv[1]["phases"])):
        print(f"{field[:45]:45} {g['phases']:>6} {len(g['runs']):>5}")
        for ex in g["examples"]:
            print(f"    {ex}")
    persistent = [f for f in groups if not f.endswith("(transient)")]
    print(f"RESULT: {len(persistent)} field(s) differ persistently, {len(groups) - len(persistent)} transiently")
    return 1 if persistent else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
