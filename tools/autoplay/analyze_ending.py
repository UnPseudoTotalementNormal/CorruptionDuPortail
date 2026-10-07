"""Ending screen check (lever linger-end): what every process shows once the game is over.

    python -X utf8 tools/autoplay/analyze_ending.py <run>

Reads each process's last "ending" capture state (game.winners, game.boardCards) and checks:
  - every process received winners, and the same winners as the host
  - the cards left on the board are exactly the winners' seats (no loser card, no winner missing, no duplicate)
  - the winners are coherent with the host's final roster: an "anomaly" win lists anomalies only, a "chosen" win
    chosen only (other teams are listed, not judged)
Exit 0 = pass, 1 = fail, 2 = not covered (no ending capture: the run did not reach the ending or had no linger-end).
"""
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import host_of, load_run, roster  # noqa: E402


def last_ending_state(folder):
    files = sorted(glob.glob(os.path.join(folder, "*ending*.json")))
    if not files:
        return None
    with open(files[-1], encoding="utf-8-sig") as f:
        data = json.load(f)
    return data.get("game", data), os.path.basename(files[-1])


def parse_winners(lines):
    out = {}
    for line in lines or []:
        team, _, ids = line.partition(":")
        out[team] = sorted(int(i) for i in ids.split(",") if i.strip())
    return out


def main():
    run = sys.argv[1]
    procs = load_run(run)
    host = host_of(procs)
    seats = roster(procs)
    failures, rows = [], []
    reference = None
    for p in procs:
        found = last_ending_state(p.folder)
        if found is None:
            rows.append(f"  {p.name}: no ending capture")
            continue
        game, label = found
        winners = parse_winners(game.get("winners"))
        board = list(game.get("boardCards") or [])
        everyone = sorted(i for ids in winners.values() for i in ids)
        rows.append(f"  {p.name} [{label}]: winners={winners} board={sorted(board)}")
        if not winners:
            failures.append(f"{p.name}: no winners received")
        if sorted(board) != everyone:
            failures.append(f"{p.name}: board cards {sorted(board)} != winners {everyone}")
        if len(set(board)) != len(board):
            failures.append(f"{p.name}: duplicate board cards {sorted(board)}")
        if p is host:
            reference = winners
            for team, ids in winners.items():
                for i in ids:
                    faction = seats.get(i, ("?", "?"))[1]
                    if team in ("anomaly", "chosen") and faction != team:
                        failures.append(f"host: seat {i} ({seats.get(i)}) listed in team {team}")
    if reference is not None:
        for p in procs:
            found = last_ending_state(p.folder)
            if found and p is not host and parse_winners(found[0].get("winners")) != reference:
                failures.append(f"{p.name}: winners differ from the host's")
    print("\n".join(rows))
    if not any("[" in r for r in rows):
        print("NOT COVERED: no ending capture")
        sys.exit(2)
    for f in failures:
        print("FAIL", f)
    print("ending: " + ("PASS" if not failures else "FAIL"))
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
