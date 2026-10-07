"""Lack of Affection (the Orpheline's contact) check over a run: every contact on a REAL player must reach that
player's screen, and reveal the Orpheline's role to the target if and only if the target is a chosen.

    python -X utf8 tools/autoplay/analyze_contact.py <run> [--power affection] [--expect-faction chosen|anomaly|marginal]

Per contact (power.start of the power, then the actor's next select.character "picked T"):
- T is a real player (host or a client process): T's process journals the contact line (chat.recv on the server
  channel with "vous voir") and, if T is a chosen, a knowledge line "T>A role=10" (Personal level). A role reveal on a
  non-chosen target is reported as a warning (another power may have revealed it).
- T is a bot or a fake character: not checkable on a screen, listed only.
Exit 0 = pass, 1 = failure, 2 = not covered (no contact on a real player, or none on --expect-faction).
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import fields, load_run, roster  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    ap.add_argument("--power", default="affection")
    ap.add_argument("--expect-faction")
    a = ap.parse_args()

    procs = load_run(a.run)
    by_id = {p.player_id: p for p in procs if p.player_id is not None}
    factions = roster(procs)
    power_rx = re.compile(a.power, re.IGNORECASE)

    contacts = []
    for p in procs:
        pending = None
        for e in p.events:
            kind, detail = e.get("kind"), e.get("detail", "")
            if kind == "power.start" and power_rx.search(detail):
                pending = int(detail.split()[0])
            elif kind == "select.character" and pending is not None:
                m = re.match(r"picked (\d+)", detail)
                if m:
                    contacts.append((pending, int(m.group(1)), "focus=" in detail))
                pending = None

    failures, warnings, checked = [], [], []
    for actor, target, focused in contacts:
        role, faction = factions.get(target, ("?", "?"))
        tp = by_id.get(target)
        label = f"{actor} -> {target} ({role}, {faction}){' [focus]' if focused else ''}"
        if tp is None:
            print(f"skip  {label}: not a real player's screen")
            continue
        checked.append(faction)
        before = len(failures)
        lines = [fields(e["detail"]) for e in tp.of("chat.recv")]
        if not any(l.get("chat") == "-1" and "vous voir" in l.get("text", "") for l in lines):
            failures.append(f"MISS  {label}: no contact line on the target's screen")
        revealed = any(e["detail"].startswith(f"{target}>{actor} ") and " role=10" in e["detail"] for e in tp.of("knowledge"))
        if faction == "chosen" and not revealed:
            failures.append(f"MISS  {label}: chosen target never learned the Orpheline's role (knowledge {target}>{actor} role=10)")
        if faction != "chosen" and revealed:
            warnings.append(f"WARN  {label}: non-chosen target knows the actor's role (another power may explain it)")
        if len(failures) == before:
            print(f"ok    {label}: contact line{' + role revealed' if revealed else ', no reveal'}")

    for line in warnings + failures:
        print(line)
    if failures:
        print("FAIL")
        sys.exit(1)
    if not checked or (a.expect_faction and a.expect_faction not in checked):
        print(f"NOT COVERED: contacts on real players: {checked or 'none'}")
        sys.exit(2)
    print(f"PASS ({len(checked)} contact(s) on real players: {', '.join(checked)})")


if __name__ == "__main__":
    main()
