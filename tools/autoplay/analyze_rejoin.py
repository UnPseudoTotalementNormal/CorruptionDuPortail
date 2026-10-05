"""Rejoin check (feat/player-rejoin): a client that dropped and rejoined must see the game as before the drop.

    python -X utf8 tools/autoplay/analyze_rejoin.py <net run folder>

Finds the client process that journaled `rejoin.seat`, loads its `rejoin-before` capture (just before the drop) and its
last `rejoin-after` capture (a few seconds after the seat came back), and compares what that player sees as himself:
the seat he plays, his role, his powers and their uses, his chat channels, the icons he sees, what he knows about the
others, the day; and checks that he is no longer listed as left. Things that can legitimately grow while he was away
(knowledge, channels, icons) must at least contain everything he had. Exit 0 = pass, 1 = fail, 2 = not covered.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import Process  # noqa: E402


def load_capture(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f).get("game") or {}


def main(run):
    procs = [Process(p) for p in sorted(glob.glob(os.path.join(run, "*/"))) if os.path.exists(os.path.join(p, "events.ndjson"))]
    rejoiners = [p for p in procs if p.of("rejoin.seat")]
    if not rejoiners:
        print("NOT COVERED: no client journaled rejoin.seat")
        return 2

    failures = []
    for proc in rejoiners:
        before = sorted(glob.glob(os.path.join(proc.folder, "*rejoin-before*.json")))
        after = sorted(glob.glob(os.path.join(proc.folder, "*rejoin-after*.json")))
        if not before or not after:
            print(f"NOT COVERED: {proc.name} has no rejoin-before / rejoin-after capture")
            return 2
        b, a = load_capture(before[-1]), load_capture(after[-1])
        name = proc.name
        print(f"{name}: before={os.path.basename(before[-1])} after={os.path.basename(after[-1])}")
        print(f"  seat {b.get('localSeat')} (connection {b.get('connectionId')}) -> seat {a.get('localSeat')} (connection {a.get('connectionId')})")

        def same(field):
            if b.get(field) != a.get(field):
                failures.append(f"{name}: {field} changed: {b.get(field)!r} -> {a.get(field)!r}")
            else:
                print(f"  OK {field} = {a.get(field)!r}")

        def kept(field):
            missing = sorted(set(b.get(field) or []) - set(a.get(field) or []))
            if missing:
                failures.append(f"{name}: {field} lost after the rejoin: {missing}")
            else:
                print(f"  OK {field}: all {len(b.get(field) or [])} kept ({len(a.get(field) or [])} now)")

        same("localSeat")
        same("localRole")
        # Power uses may only go down while he was away if he used them; he could not: identical.
        same("localPowers")
        if a.get("connectionId") == b.get("connectionId"):
            failures.append(f"{name}: the rejoin did not get a new connection id ({a.get('connectionId')})")
        kept("chatChannels")
        kept("icons")
        # Knowledge only grows ("viewer>target role=… corrupt=… force=… hacked=…", levels may rise while he was away).
        def levels(entries):
            out = {}
            for e in entries or []:
                key, _, rest = e.partition(" ")
                out[key] = dict(kv.split("=") for kv in rest.split())
            return out
        kb, ka = levels(b.get("knowledge")), levels(a.get("knowledge"))
        lost = [k for k, lv in kb.items() if k not in ka or any(int(ka[k].get(n, 0)) < int(v) for n, v in lv.items())]
        if lost:
            failures.append(f"{name}: knowledge lost after the rejoin: {sorted(lost)}")
        else:
            print(f"  OK knowledge: all {len(kb)} kept or grown ({len(ka)} now)")
        if (a.get("day") or 0) < (b.get("day") or 0):
            failures.append(f"{name}: day went back {b.get('day')} -> {a.get('day')}")
        else:
            print(f"  OK day {b.get('day')} -> {a.get('day')}")
        if a.get("localSeat") in (a.get("leftPlayers") or []):
            failures.append(f"{name}: still listed as left after the rejoin")
        else:
            print(f"  OK not listed as left (left now: {a.get('leftPlayers')})")
        own = [c for c in a.get("characters") or [] if c.get("id") == a.get("localSeat")]
        if not own:
            failures.append(f"{name}: his own character (seat {a.get('localSeat')}) is missing after the rejoin")
        elif own[0].get("chained"):
            # A reserved seat can still be voted out while he is away; only the grace expiry must never chain it.
            host_log = os.path.join(run, "host.log")
            expired = os.path.exists(host_log) and f"Reserved seat of {a.get('localSeat')} expired" in open(host_log, encoding="utf-8", errors="replace").read()
            if expired:
                failures.append(f"{name}: his seat was chained by the grace expiry although he rejoined")
            else:
                print(f"  OK own character present, chained by the game while away (not by the grace expiry)")
        else:
            print(f"  OK own character present, not chained ({own[0].get('role')})")

    for f in failures:
        print("FAIL", f)
    print("RESULT:", "FAIL" if failures else "PASS")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
