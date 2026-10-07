"""Duplicate vote check over a run: one click must cast one vote. The server logs "Player X tried to vote for player Y
but cannot vote." when a voter's vote arrives a second time (seen when a first-person click was delivered twice, by the
reticle and by the UI input module). Any such line fails.

    python -X utf8 tools/autoplay/analyze_duplicate_votes.py <run>

Reads every *.log of the run folder (host.log holds the server's lines). Exit 0 = pass, 1 = failure.
"""
import glob
import os
import re
import sys


def main():
    run = sys.argv[1]
    logs = glob.glob(os.path.join(run, "*.log")) + glob.glob(os.path.join(run, "*", "*.log"))
    hits = []
    for path in logs:
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if re.search(r"tried to vote for player \d+ but cannot vote", line):
                    hits.append(f"{os.path.basename(path)}: {line.strip()}")
    for h in hits:
        print(f"DUPLICATE  {h}")
    if hits:
        print(f"FAIL ({len(hits)} refused duplicate vote(s))")
        sys.exit(1)
    print(f"PASS (no duplicate vote in {len(logs)} log(s))")


if __name__ == "__main__":
    main()
