"""Checks a run's alerts.log (watchdog relay of launch-net.ps1 / launch-background.ps1): every regex must match a line.

    python -X utf8 Tools~/check_alerts.py <run folder> <regex> [<regex> ...]

For a single-process run the alerts are in <run>/<process folder>/alerts.log; both places are read.
Exit 0 = every regex matched, 1 = one did not, 2 = no alerts.log at all.
"""
import glob
import os
import re
import sys


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    run, patterns = argv[0], argv[1:]
    files = [os.path.join(run, "alerts.log")] + glob.glob(os.path.join(run, "*", "alerts.log"))
    lines = []
    for path in files:
        if os.path.exists(path):
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                lines += [l.rstrip() for l in f]
    if not lines:
        print("NO ALERTS: no alerts.log in the run")
        return 2
    missing = [p for p in patterns if not any(re.search(p, l) for l in lines)]
    for line in lines[:20]:
        print("  " + line[:220])
    if missing:
        print(f"MISSING: {missing}")
        return 1
    print(f"OK: {len(patterns)} expected alert(s) found among {len(lines)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
