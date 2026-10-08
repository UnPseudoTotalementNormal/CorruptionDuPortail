"""Rejoin check (feat/player-rejoin): a client that dropped and rejoined must see the game as before the drop.

    python -X utf8 tools/autoplay/analyze_rejoin.py <net run folder>

Finds the client process that journaled `rejoin.seat`, loads its `rejoin-before` capture (just before the drop; for a
game relaunched after a crash, the crashed process's one) and its
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

    def seat_name(proc):  # "<stamp>-client1-seed33" -> "client1-seed33": the same player across a relaunch
        return proc.name.split("-", 2)[-1]

    failures = []
    for proc in rejoiners:
        before = sorted(glob.glob(os.path.join(proc.folder, "*rejoin-before*.json")))
        if not before:
            # A relaunched game: what he saw before is in the process that crashed (same player, lever crash-at).
            crashed = [p for p in procs if p is not proc and p.of("crash") and seat_name(p) == seat_name(proc)]
            for p in crashed:
                before = sorted(glob.glob(os.path.join(p.folder, "*rejoin-before*.json")))
                if before:
                    print(f"{proc.name}: relaunched after the crash of {p.name}")
                    break
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
        # Power uses: identical (he could not use them while away), but his own awakening regenerates them
        # (Role.AwakenRole): compare on the last "rejoin-after" capture taken before he was awakened again.
        events = proc.events
        seat_at = next((i for i, e in enumerate(events) if e["kind"] == "rejoin.seat"), 0)
        awake_t = next((e["realTime"] for e in events[seat_at:] if e["kind"] == "awake" and e["detail"].split()[0] == str(a.get("localSeat"))), None)
        # "capture" when a PNG is written, "capture.state" for the state file alone (processes run with no-png).
        shots = [(e["realTime"], e["detail"]) for e in events[seat_at:]
                 if e["kind"] in ("capture", "capture.state") and "rejoin-after" in e["detail"]]
        usable = [d for t, d in shots if awake_t is None or t < awake_t]
        powers_after = a
        if usable and awake_t is not None:
            powers_after = load_capture(os.path.join(proc.folder, os.path.splitext(usable[-1])[0] + ".json"))
            print(f"  (powers compared on {usable[-1]}: he was awakened again at {awake_t:.1f}s)")
        # The "before" capture can precede a last power use of that night (he still acts until the drop): uses may
        # be lower by the number of power.end he journaled between that capture and the drop / crash.
        before_proc = next((p for p in procs if os.path.dirname(before[-1]).rstrip("/\\") == p.folder.rstrip("/\\")), proc)
        before_file = os.path.basename(before[-1])
        b_events = before_proc.events
        b_at = next((i for i, e in enumerate(b_events) if e["kind"] in ("capture", "capture.state")
                     and os.path.splitext(e["detail"])[0] == os.path.splitext(before_file)[0]), None)
        used_late = {}
        awoke_late = False  # the player's own awakening refills his uses (Role.AwakenRole)
        if b_at is not None:
            for e in b_events[b_at:]:
                if e["kind"] in ("rejoin.drop", "crash"):
                    break
                if e["kind"] == "awake" and e["detail"].split()[0] == str(b.get("localSeat")):
                    awoke_late = True
                    used_late.clear()  # uses spent before the refill no longer count
                if e["kind"] == "power.end" and e["detail"].split()[0] == str(b.get("localSeat")):
                    power = e["detail"].split(" ", 1)[1]
                    used_late[power] = used_late.get(power, 0) + 1

        def uses(entries):
            return dict(x.rsplit(":", 1) for x in entries or [])
        pb, pa = uses(b.get("localPowers")), uses(powers_after.get("localPowers"))
        bad = [k for k in set(pb) | set(pa) if k not in pb or k not in pa
               or not (int(pb[k]) - used_late.get(k, 0) <= int(pa[k]) <= int(pb[k]) + (99 if awoke_late else 0))]
        if bad:
            failures.append(f"{name}: localPowers changed: {b.get('localPowers')!r} -> {powers_after.get('localPowers')!r}"
                            + (f" (used after the before capture: {used_late})" if used_late else ""))
        elif pb != pa:
            print(f"  OK localPowers {b.get('localPowers')!r} -> {powers_after.get('localPowers')!r}: "
                  f"{'awakened (uses refilled) and ' if awoke_late else ''}used before the drop, after the before capture ({used_late})")
        else:
            print(f"  OK localPowers = {powers_after.get('localPowers')!r}")
        if a.get("connectionId") == b.get("connectionId"):
            failures.append(f"{name}: the rejoin did not get a new connection id ({a.get('connectionId')})")
        # A private channel may be gone only if the host revoked it for this seat after his drop (host journal).
        missing = sorted(set(b.get("chatChannels") or []) - set(a.get("chatChannels") or []))
        if missing:
            host = next((p for p in procs if p.is_host), None)
            seat = a.get("localSeat")
            revoked = set()
            if host is not None:
                events = host.events
                start = next((i for i, e in enumerate(events) if e["kind"] == "seat.reserved" and e["detail"].split()[0] == str(seat)), 0)
                for e in events[start:]:
                    if e["kind"] == "chat.revoke" and e["detail"].split()[1] == str(seat):
                        revoked.add(int(e["detail"].split()[0]))
            unexplained = [c for c in missing if c not in revoked]
            if unexplained:
                failures.append(f"{name}: chatChannels lost after the rejoin: {unexplained}")
            else:
                print(f"  OK chatChannels: {missing} revoked by the host while he was away (chat.revoke), the rest kept")
        else:
            print(f"  OK chatChannels: all {len(b.get('chatChannels') or [])} kept ({len(a.get('chatChannels') or [])} now)")
        kept("icons")
        # Knowledge only grows ("viewer>target role=… corrupt=… force=… hacked=…", levels may rise while he was away).
        # A factice role's entry carries its name ("…>id fake=10 role=La Dryade"): a token without "=" continues the
        # previous value. Only numeric levels are compared.
        def levels(entries):
            out = {}
            for e in entries or []:
                key, _, rest = e.partition(" ")
                fields, last = {}, None
                for tok in rest.split():
                    if "=" in tok:
                        last, _, val = tok.partition("=")
                        fields[last] = val
                    elif last is not None:
                        fields[last] += " " + tok
                out[key] = fields
            return out
        def num(v):
            return int(v) if str(v).lstrip("-").isdigit() else None
        kb, ka = levels(b.get("knowledge")), levels(a.get("knowledge"))
        lost = [k for k, lv in kb.items() if k not in ka or any(
            num(v) is not None and (num(ka[k].get(n, 0)) or 0) < num(v) for n, v in lv.items())]
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
