"""Private chat delivery check over a net run played with -autoplay-chat.

    python -X utf8 tools/autoplay/analyze_chat.py <run>

Every tokenized line a player wrote in a private channel (chat.sent) must reach every real client that was a member
of that channel for the whole day (host chat.members snapshots), and no real client outside the channel may ever
receive a line of it (chat.recv). Exit 0 = pass, 1 = failure, 2 = not covered (no private line reached another real
player, so nothing was proven).
"""
import os
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from autoplay_runs import fields, host_of, load_run  # noqa: E402


def main():
    run = sys.argv[1]
    procs = load_run(run)
    host = host_of(procs)
    if host is None:
        sys.exit("no host process in " + run)
    by_id = {p.player_id: p for p in procs if p.player_id is not None}
    real_clients = sorted(i for i in by_id if i != 0)

    # Host snapshots: chat -> day -> list of member sets.
    snapshots = defaultdict(lambda: defaultdict(list))
    ever = defaultdict(set)
    for e in host.of("chat.members"):
        f = fields(e["detail"])
        members = {int(x) for x in f.get("members", "").split(",") if x}
        snapshots[int(f["chat"])][int(f["day"])].append(members)
        ever[int(f["chat"])] |= members

    received = defaultdict(set)  # player id -> tokens
    failures, notes = [], []
    for pid, p in by_id.items():
        for e in p.of("chat.recv"):
            f = fields(e["detail"])
            token = f.get("token", "-")
            if not token.startswith("ap:"):
                continue
            chat = int(f["chat"])
            received[pid].add(token)
            if pid != 0 and chat in ever and pid not in ever[chat]:
                failures.append(f"LEAK  client {pid} received {token} on private channel {chat} (members ever: {sorted(ever[chat])})")

    sent = []
    for p in procs:
        for e in p.of("chat.sent"):
            f = fields(e["detail"])
            sent.append((int(f["chat"]), int(f["from"]), f["token"], int(f["phase"].split("/")[1])))

    proven = 0
    for chat, sender, token, day in sent:
        days = snapshots.get(chat, {}).get(day)
        if not days:
            notes.append(f"skip  {token}: no host membership snapshot for channel {chat} on day {day}")
            continue
        stable = set.intersection(*days)
        for pid in real_clients:
            if pid == sender or pid not in stable:
                continue
            if token in received.get(pid, set()):
                proven += 1
            else:
                failures.append(f"MISS  client {pid} (member of {chat} all day {day}) never received {token} from {sender}")

    print(f"run: {run}")
    print(f"private lines sent: {len(sent)} · channels seen: {sorted(ever)} · deliveries to real clients proven: {proven}")
    for line in notes[:10]:
        print(line)
    for line in failures:
        print(line)
    if failures:
        print("FAIL")
        sys.exit(1)
    if proven == 0:
        print("NOT COVERED: no private line reached another real player")
        sys.exit(2)
    print("PASS")


if __name__ == "__main__":
    main()
