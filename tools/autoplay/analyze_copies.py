"""Copied powers check over a run (Ugës's Marque d'Hurluberluges, Luma's Mélange des cartes, l'Incomplet's
Réincarnation), from the `power.copies` / `power.copyuse` events every process journals (AutoplayDriverCopies).

    python -X utf8 tools/autoplay/analyze_copies.py <run> [--expect-use <power regex>] [--expect-seat client|host|bot]
                                                         [--expect-copy <power regex>] [--min-day N]

Rules (FAIL on any breach):
  locked-use     a copy tied to a Marque was started while locked (flags ML at power.copyuse)
  one-per-night  more than one Marque copy started by the same seat the same day
  theft-night    a Marque copy usable (MU) in a snapshot of the day it first appeared
  unlock         a Marque copy still locked (ML) at the next night's first snapshot although nothing was used that day
  husk           a one-shot copy with no use left still listed at a settled phase (spent copies despawn)
  peers          host and a client list different copies at the same settled phase
  unresolved     a copy use that timed out or errored
Expectations (FAIL when not met, i.e. the situation did not happen: "not covered"):
  --expect-use P   a copy whose name matches P was started at least once (by a seat of --expect-seat, from --min-day)
  --expect-copy P  a copy whose name matches P was held by someone at some snapshot
  --expect-marque-seats N  at least N seats held Marque copies (Ugës and an Incomplet who reincarnated into him)
Exit code 0 = pass. Prints one line per rule and a summary of what was covered.
"""
import argparse
import glob
import json
import os
import re
import sys
from collections import defaultdict

COPY_RE = re.compile(r"([^,\[\]]+?)\{(S|P)(M[LU])? u=(-?\d+)\}")
SEAT_RE = re.compile(r"(\d+):\[(.*?)\]")
PHASE_RE = re.compile(r"^(\d+):(\w+) day=(\d+)")


def load(folder):
    path = os.path.join(folder, "events.ndjson")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8-sig") as f:
        return [json.loads(line) for line in f if line.strip()]


def processes(run):
    out = {}
    for folder in sorted(glob.glob(os.path.join(run, "*", ""))):
        name = os.path.basename(os.path.dirname(folder))
        m = re.search(r"-(host|client\d+)-", name + "-")
        if m and os.path.exists(os.path.join(folder, "events.ndjson")):
            out[m.group(1)] = load(folder)
    if not out and os.path.exists(os.path.join(run, "events.ndjson")):
        out["host"] = load(run)
    return out


def parse_snapshot(detail):
    phase, _, rest = detail.partition(" | ")
    seats = {}
    for seat, body in SEAT_RE.findall(rest):
        seats[int(seat)] = [(n.strip(), kind, marque or "", int(u)) for n, kind, marque, u in COPY_RE.findall(body)]
    return phase.strip(), seats


def seat_kind(seat):
    return "host" if seat == 0 else "bot" if seat >= 100 else "client"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    ap.add_argument("--expect-use")
    ap.add_argument("--expect-seat")
    ap.add_argument("--expect-copy")
    ap.add_argument("--min-day", type=int, default=0)
    ap.add_argument("--expect-marque-seats", type=int, default=0,
                    help="at least N distinct seats held Marque copies (e.g. Ugës AND an Incomplet who copied him)")
    a = ap.parse_args()

    procs = processes(a.run)
    if "host" not in procs:
        print(f"FAIL no host events under {a.run}")
        return 1
    failures = []

    # --- uses (journaled by the process that drives the seat) -------------------------------------------------
    uses = []  # (proc, seat, power, day, flags, index in its events)
    for proc, events in procs.items():
        for i, e in enumerate(events):
            if e["kind"] == "power.copyuse":
                m = re.match(r"(\d+) (.+) day=(-?\d+) (\S+)$", e["detail"])
                if m:
                    uses.append((proc, int(m.group(1)), m.group(2), int(m.group(3)), m.group(4), i))
    per_night = defaultdict(list)
    for proc, seat, power, day, flags, i in uses:
        if "ML" in flags:
            failures.append(f"locked-use: {proc} seat {seat} started '{power}' on day {day} while its Marque locked it")
        if "M" in flags:
            per_night[(seat, day)].append(power)
        # resolved: before the seat's next power or the next phase, no timeout / error for that seat (the server may
        # put the seat to sleep right after its use, then the bot journals no power.end: that is a normal end)
        events = procs[proc]
        for x in events[i + 1:]:
            if x["kind"] == "state.enter" or (x["kind"] == "power.start" and x["detail"].startswith(f"{seat} ")):
                break
            if x["kind"] in ("power.timeout", "power.error") and x["detail"].startswith(f"{seat} "):
                failures.append(f"unresolved: {proc} seat {seat} '{power}' (day {day}) {x['kind']} {x['detail']}")
                break
    for (seat, day), powers in sorted(per_night.items()):
        if len(powers) > 1:
            failures.append(f"one-per-night: seat {seat} used {len(powers)} Marque copies on day {day}: {powers}")

    # --- snapshots ---------------------------------------------------------------------------------------------
    host_snaps = []  # (phase, state, day, seats)
    for e in procs["host"]:
        if e["kind"] == "power.copies":
            phase, seats = parse_snapshot(e["detail"])
            m = PHASE_RE.match(phase)
            if m:
                host_snaps.append((phase, m.group(2), int(m.group(3)), seats))

    first_day = {}  # (seat, name) -> first day a Marque copy of that name was seen
    used_days = {(seat, day) for (_, seat, _, day, flags, _) in uses if "M" in flags}
    seen_night = set()
    for phase, state, day, seats in host_snaps:
        settled = state != "AwakeningState"
        first_of_night = state == "AwakeningState" and day not in seen_night
        if state == "AwakeningState":
            seen_night.add(day)
        for seat, copies in seats.items():
            for name, kind, marque, u in copies:
                if kind == "S" and u <= 0 and settled:
                    failures.append(f"husk: {phase} seat {seat} still lists spent one-shot '{name}'")
                if not marque:
                    continue
                key = (seat, name)
                if key not in first_day:
                    first_day[key] = day
                if first_day[key] == day and marque == "MU":
                    failures.append(f"theft-night: {phase} seat {seat} '{name}' usable the day it was stolen")
                if first_of_night and first_day[key] < day and marque == "ML" and (seat, day - 1) not in used_days \
                        and (seat, day) not in used_days:
                    failures.append(f"unlock: {phase} seat {seat} '{name}' still locked at the start of night {day}")

    # --- peers: same copies at each settled phase -------------------------------------------------------------
    host_by_phase = {p: seats for p, st, _, seats in host_snaps if st != "AwakeningState"}
    for proc, events in procs.items():
        if proc == "host":
            continue
        for e in events:
            if e["kind"] != "power.copies":
                continue
            phase, seats = parse_snapshot(e["detail"])
            if phase in host_by_phase and phase.split(":")[1].split(" ")[0] != "AwakeningState":
                if seats != host_by_phase[phase]:
                    failures.append(f"peers: {phase} {proc} {seats} != host {host_by_phase[phase]}")

    # --- coverage ----------------------------------------------------------------------------------------------
    # A copy obtained and spent within one night (Luma's) is never in a settled snapshot: its use counts as held.
    held = sorted({name for _, _, _, seats in host_snaps for copies in seats.values() for name, *_ in copies}
                  | {u[2] for u in uses})
    print(f"copies held: {held or 'none'}")
    print("copy uses: " + (", ".join(f"{seat_kind(s)} {s} '{p}' day {d} {f}" for _, s, p, d, f, _ in uses) or "none"))
    if a.expect_use:
        ok = [u for u in uses if re.search(a.expect_use, u[2], re.I) and u[3] >= a.min_day
              and (not a.expect_seat or seat_kind(u[1]) == a.expect_seat)]
        if not ok:
            failures.append(f"not covered: no copy matching '{a.expect_use}' used"
                            f"{' by a ' + a.expect_seat if a.expect_seat else ''}{f' from day {a.min_day}' if a.min_day else ''}")
    marque_seats = sorted({seat for _, _, _, seats in host_snaps for seat, copies in seats.items()
                           if any(m for _, _, m, _ in copies)})
    print(f"seats holding Marque copies: {marque_seats or 'none'}")
    if a.expect_marque_seats and len(marque_seats) < a.expect_marque_seats:
        failures.append(f"not covered: {len(marque_seats)} seat(s) held Marque copies, wanted {a.expect_marque_seats}")
    if a.expect_copy and not any(re.search(a.expect_copy, n, re.I) for n in held):
        failures.append(f"not covered: no copy matching '{a.expect_copy}' ever held")

    for f in failures:
        print("FAIL " + f)
    print("PASS" if not failures else f"{len(failures)} failure(s)")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
