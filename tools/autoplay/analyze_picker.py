"""Picker visual check over one autoplay run (built with -autoplay-visual-picker).

For every picker opening it reads the capture burst's state files (NNN-pickerXXX-open-T.json) and checks:
  * the blur veil comes up   : frostAlpha >= FROST_MIN once T >= SETTLE_S
  * valid cards rise         : for a character picker, liftedCount == pickableCount once settled
  * the picker stays open    : pickerActive during the whole burst
and names the power that opened it (last `power.start` before `picker.open` in events.ndjson).

Usage: python tools/autoplay/analyze_picker.py [run_dir]   (default: newest AutoplayRuns/*)
Exit code 1 when at least one opening fails, so it can gate a scenario.
"""
import glob
import json
import os
import re
import sys

FROST_MIN = 0.9
SETTLE_S = 0.5


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def main():
    root = os.path.join(os.path.dirname(__file__), "..", "..", "AutoplayRuns")
    run = sys.argv[1] if len(sys.argv) > 1 else max(glob.glob(os.path.join(root, "*")), key=os.path.getmtime)

    # opening number -> power that opened it, from the event trace
    opener, last_power = {}, None
    events = os.path.join(run, "events.ndjson")  # written at the end of the run; absent while it plays
    with open(events, encoding="utf-8-sig") if os.path.exists(events) else open(os.devnull) as f:
        for line in f:
            e = json.loads(line)
            if e["kind"] == "power.start":
                last_power = e["detail"]
            elif e["kind"] == "portal.click":
                last_power = "portal " + e["detail"]
            elif e["kind"] == "picker.open":
                n = int(re.match(r"#(\d+)", e["detail"]).group(1))
                opener[n] = last_power

    bursts = {}
    for path in glob.glob(os.path.join(run, "*-picker*-open-*.json")):
        m = re.search(r"picker(\d+)-open-([\d.]+)s\.json$", path)
        if m:
            bursts.setdefault(int(m.group(1)), []).append((float(m.group(2)), path))

    failures = 0
    print(f"run: {run}")
    print(f"{'#':>4} {'kind':5} {'pickable':>8} {'lifted@settle':>13} {'frost@settle':>12}  verdict  power")
    for n in sorted(bursts):
        frames = sorted(bursts[n])
        states = [(t, load(p), p) for t, p in frames]
        declared = {s.get("pickerKind") for _, s, _ in states} - {None, "none"}
        if declared:  # the picker states its own kind (role cards also carry a characterInfo)
            kind = "role" if declared == {"role"} else "char" if declared == {"character"} else "mixed"
        else:  # older runs: guess from the card labels
            kinds = {c.split(":")[0] for _, s, _ in states for c in s.get("pickableCards", [])}
            kind = "role" if kinds == {"role"} else "char" if kinds == {"char"} else "mixed" if kinds else "?"
        settled = [(t, s, p) for t, s, p in states if t >= SETTLE_S] or states[-1:]
        t_set, s_set, p_set = settled[0]

        problems = []
        if not all(s["pickerActive"] for _, s, _ in states):
            problems.append("closed during burst")
        if s_set["frostAlpha"] < FROST_MIN:
            problems.append(f"blur {s_set['frostAlpha']:.2f} < {FROST_MIN}")
        if kind == "char" and s_set["liftedCount"] != s_set["pickableCount"]:
            problems.append(f"lifted {s_set['liftedCount']} != pickable {s_set['pickableCount']}")

        verdict = "OK" if not problems else "FAIL"
        failures += bool(problems)
        print(f"{n:>4} {kind:5} {s_set['pickableCount']:>8} {s_set['liftedCount']:>13} {s_set['frostAlpha']:>12.2f}  "
              f"{verdict:7}  {opener.get(n, '?')}")
        for problem in problems:
            png = os.path.splitext(p_set)[0] + ".png"
            print(f"       - {problem}  (look: {os.path.basename(png) if os.path.exists(png) else 'no png'})")

    print(f"{len(bursts)} openings, {failures} failing")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
