"""Board T2 check over a run: a one-shot copied power (isStolenCopy) shows the slime mark on its owner's table, a
normal power never does. Reads the `game.powerBar` lines every capture exports (AutoplayDriver.DescribePowerBar):
"<power> stolen=<True|False> left=<uses> coat=<True|False> drips=<live drops>".

    python -X utf8 tools/autoplay/analyze_slime.py <run> [--min-marked N]

Rules (FAIL on any breach):
  unmarked   a stolen copy without the slime coat
  no-drips   a stolen copy with uses left whose drip emitter has no live drop (prewarmed and looping: never empty;
             a spent copy shrinking out has released its drops, so it is not checked)
  leaked     a normal power wearing the coat or drips
Expectation (FAIL when not met, i.e. the situation did not happen: "not covered"):
  --min-marked N   at least N stolen-copy observations over the run (default 1)
Exit code 0 = pass.
"""
import argparse
import glob
import json
import os
import re
import sys

LINE_RE = re.compile(r"^(?P<name>.+) stolen=(?P<stolen>True|False) left=(?P<left>-?\d+) coat=(?P<coat>True|False) drips=(?P<drips>\d+)$")


def captures(run):
    for path in sorted(glob.glob(os.path.join(run, "*", "*.json"))):
        if os.path.basename(path) in ("verdict.json", "report.json"):
            continue
        try:
            with open(path, encoding="utf-8-sig") as f:
                data = json.load(f)
        except (OSError, ValueError):
            continue
        game = data.get("game") if isinstance(data, dict) else None
        if isinstance(game, dict) and "powerBar" in game:
            yield os.path.relpath(path, run), game.get("powerBar") or []


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("run")
    parser.add_argument("--min-marked", type=int, default=1)
    args = parser.parse_args()

    breaches = {"unmarked": [], "no-drips": [], "leaked": []}
    marked = 0
    plain = 0
    seen_any = False
    for where, lines in captures(args.run):
        seen_any = True
        for line in lines:
            m = LINE_RE.match(line)
            if not m:
                continue
            stolen = m["stolen"] == "True"
            coat = m["coat"] == "True"
            drips = int(m["drips"])
            if stolen:
                marked += 1
                if not coat:
                    breaches["unmarked"].append(f"{where}: {line}")
                if drips <= 0 and int(m["left"]) > 0:
                    breaches["no-drips"].append(f"{where}: {line}")
            else:
                plain += 1
                if coat or drips > 0:
                    breaches["leaked"].append(f"{where}: {line}")

    ok = True
    for rule, hits in breaches.items():
        print(f"  [{'FAIL' if hits else 'PASS'}] {rule} ({len(hits)})")
        for hit in hits[:5]:
            print(f"      {hit}")
        ok &= not hits
    covered = seen_any and marked >= args.min_marked
    print(f"  [{'PASS' if covered else 'FAIL'}] covered: {marked} stolen-copy and {plain} normal power observations"
          f" (min {args.min_marked} stolen){'' if seen_any else ' - no capture exports powerBar'}")
    ok &= covered
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
