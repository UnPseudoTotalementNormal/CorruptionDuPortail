"""Copied-power sweep (Corruption du Portail): every copy path, forced, on real network clients.

    python -X utf8 tools/autoplay/sweep_copies.py [--roles <roles.json>] [--family uges,luma,incomplet,fake,rejoin]
                                                  [--only <power/role fragment>] [--seed 900] [--days 3] [--port N]

Families (each case = one play-net game, 2 clients, bots never vote so nobody is chained before the nights):
  uges       Ugës on a client steals each chosen active power first (-autoplay-steal), uses it from night 2
  luma       Luma on a client picks each chosen role made a factice (-autoplay-force-fakes) and copies it
  incomplet  l'Incomplet on a client reincarnates into Ugës (gets a Marque, steals) and into Luma
  host       Ugës on the HOST (simulated-bot / host paths) for one power
  fake       a factice Ugës: its awakening layer must last (no instant skip that gives it away)
  combos     chains of copiers in both orders (Incomplet into Ugës before / after his uses, Ugës's stolen
             Réincarnation into Luma or the Incomplet, a stolen Mélange copying factices, Luma's copied Réincarnation
             into Ugës, the Incomplet's Mélange on a factice Ugës)
  rejoin     Ugës's client drops at night 2 and rejoins: copies and their lock survive
Every case runs tools/autoplay/analyze_copies.py (rules + "this copy was really used") on top of the usual
outcome / desync / no-error checks. Verdict per case: OK / FAIL / NOT COVERED (the forced situation never happened).
Output: AutoplayRuns/copies-sweep-<stamp>/coverage.md (+ the generated scenarios).
"""
import argparse
import datetime
import glob
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
RUNNER = os.path.join(PROJECT, "Packages", "com.unpseudo.autoplay", "Tools~", "run_scenario.py")
sys.path.insert(0, HERE)
from sweep_powers import fragment_for, newest_roles  # noqa: E402

COMMON = ["-autoplay-vote-probability", "0", "-autoplay-vote-skip-cast", "-autoplay-fast-phases", "Intro|Chaining|Vote", "-autoplay-fast-timescale", "12",
          "-autoplay-net-log"]
IGNORE = ["CardEffect(TechnoBeacon|BoolEnabler): effectData", "ClientLoadedSynchronization"]


def power_fragment(name):
    """A plain-ASCII word of the power name, long enough to be unique among power names in practice."""
    words = sorted(re.findall(r"[A-Za-z]{4,}", name), key=len, reverse=True)
    return words[0] if words else name


def base_expect(extra_analyzer_args):
    return [
        {"type": "outcome", "value": "Completed", "process": "all"},
        {"type": "event", "kind": "power.error", "process": "any", "min": 0, "max": 0},
        {"type": "desync", "max": 0},
        {"type": "noErrors", "process": "all", "ignore": IGNORE},
        {"type": "analyzer", "cmd": "python -X utf8 tools/autoplay/analyze_copies.py {run} " + extra_analyzer_args},
    ]


def cases(pool, a):
    names = [r["name"] for r in pool]
    chosen = [r for r in pool if r["faction"] == "chosen"]
    actives = [(r, p) for r in chosen for p in r["powers"] if not p["passive"]]
    uges = next(r for r in pool if "Hurluberlu" in "".join(p["name"] for p in r["powers"]))
    luma = next(r for r in pool if any("lange" in p["name"] for p in r["powers"]))
    incomplet = next(r for r in pool if any("incarnation" in p["name"] for p in r["powers"]))
    f_uges, f_luma, f_inc = (fragment_for(r["name"], names) for r in (uges, luma, incomplet))
    days = str(a.days)

    if "uges" in a.family:
        for role, power in actives:
            pf, rf = power_fragment(power["name"]), fragment_for(role["name"], names)
            yield {
                "name": f"copies-uges-{pf.lower()}", "label": f"Ugës (client) steals {power['name']}",
                "args": ["-autoplay-force-roles", f"{f_uges},{rf}", "-autoplay-role-holder", "client",
                         "-autoplay-steal", pf, "-autoplay-max-days", days, "-autoplay-fast-fakes"] + COMMON,
                "analyzer": f"--expect-use \"{pf}\" --expect-seat client --min-day 2",
            }
    if "luma" in a.family:
        for role in chosen:
            if role in (luma, uges) or not any(not p["passive"] for p in role["powers"]):
                continue
            rf = fragment_for(role["name"], names)
            first = power_fragment(next(p["name"] for p in role["powers"] if not p["passive"]))
            yield {
                "name": f"copies-luma-{rf.lower()}", "label": f"Luma (client) copies the factice {role['name']}",
                "args": ["-autoplay-force-roles", f_luma, "-autoplay-role-holder", "client", "-autoplay-force-fakes", rf,
                         "-autoplay-target-focus", rf, "-autoplay-target-focus-power", "lange",
                         "-autoplay-max-days", days, "-autoplay-fast-fakes"] + COMMON,
                "analyzer": f"--expect-copy \"{'|'.join(power_fragment(p['name']) for p in role['powers'] if not p['passive'])}\"",
            }
    if "incomplet" in a.family:
        for target, tf in ((uges, f_uges), (luma, f_luma)):
            yield {
                "name": f"copies-incomplet-{tf.lower()}", "label": f"l'Incomplet (client) reincarnates into {target['name']}",
                "args": ["-autoplay-force-roles", f"{f_inc},{tf}", "-autoplay-role-holder", "client",
                         "-autoplay-target-focus", tf, "-autoplay-target-focus-power", "incarnation",
                         "-autoplay-max-days", days, "-autoplay-fast-fakes"] + COMMON,
                "analyzer": "--expect-copy \".\"",
            }
    if "host" in a.family:
        role, power = next((r, p) for r, p in actives if "Soin" in p["name"]) if any("Soin" in p["name"] for _, p in actives) else actives[0]
        pf, rf = power_fragment(power["name"]), fragment_for(role["name"], names)
        yield {
            "name": f"copies-uges-host-{pf.lower()}", "label": f"Ugës (host) steals {power['name']}",
            "args": ["-autoplay-force-roles", f_uges, "-autoplay-role-holder", "host", "-autoplay-steal", pf,
                     "-autoplay-max-days", days, "-autoplay-fast-fakes"] + COMMON,
            "analyzer": f"--expect-use \"{pf}\" --expect-seat host --min-day 2",
        }
    if "combos" in a.family:
        # Chains of copiers, in both orders. Ugës = Imposteur, Réincarnation = "incarnation", Mélange = "lange".
        U, I, L = f_uges, f_inc, f_luma
        combos = [
            ("incomplet-into-uges-late", "l'Incomplet reincarnates into Ugës on day 3, AFTER Ugës used copies (two Marques, two budgets)",
             ["-autoplay-force-roles", f"{U},{I}", "-autoplay-role-holder", "client", "-autoplay-steal", "Soin",
              "-autoplay-hold-power", "incarnation:3", "-autoplay-target-map", f"incarnation={U}", "-autoplay-max-days", "5"],
             "--expect-marque-seats 2 --expect-use . --min-day 4"),
            ("incomplet-into-uges-early", "l'Incomplet reincarnates into Ugës on night 1, BEFORE Ugës used anything",
             ["-autoplay-force-roles", f"{U},{I}", "-autoplay-role-holder", "client", "-autoplay-steal", "Soin",
              "-autoplay-target-map", f"incarnation={U}", "-autoplay-max-days", "4"],
             "--expect-marque-seats 2 --expect-use . --min-day 2"),
            ("both-ways", "Ugës steals the Incomplet's Réincarnation AND the Incomplet reincarnates into Ugës",
             ["-autoplay-force-roles", f"{U},{I}", "-autoplay-role-holder", "client", "-autoplay-steal", "incarnation",
              "-autoplay-target-map", f"incarnation={U}", "-autoplay-max-days", "5"],
             "--expect-marque-seats 2 --expect-use incarnation --min-day 2"),
            ("uges-reinc-into-luma", "Ugës's stolen Réincarnation into Luma, then the Mélange it gives copies a factice (chain under one Marque)",
             ["-autoplay-force-roles", f"{U},{L}", "-autoplay-role-holder", "client", "-autoplay-force-fakes", I,
              "-autoplay-steal", "incarnation", "-autoplay-target-map", f"incarnation={L};lange=fake", "-autoplay-max-days", "5"],
             "--expect-use incarnation --expect-copy lange"),
            ("uges-reinc-into-incomplet", "Ugës's stolen Réincarnation into the Incomplet: a one-shot Réincarnation from a one-shot one",
             ["-autoplay-force-roles", f"{U},{I}", "-autoplay-role-holder", "client", "-autoplay-steal", "incarnation",
              "-autoplay-target-map", f"incarnation={I}", "-autoplay-max-days", "5"],
             "--expect-use incarnation --min-day 2"),
            ("uges-melange-chain", "Ugës steals Mélange (factice Luma) and copies factices with it, one per night",
             ["-autoplay-force-roles", U, "-autoplay-role-holder", "client", "-autoplay-force-fakes", L,
              "-autoplay-steal", "lange", "-autoplay-target-map", "lange=fake", "-autoplay-max-days", "5"],
             "--expect-use lange --min-day 2"),
            ("luma-copies-incomplet-into-uges", "Luma copies the factice Incomplet, then that Réincarnation targets Ugës (a Marque is passive: nothing to take)",
             ["-autoplay-force-roles", f"{L},{U}", "-autoplay-role-holder", "client", "-autoplay-force-fakes", I,
              "-autoplay-target-map", f"lange={I};incarnation={U}", "-autoplay-max-days", "4"],
             "--expect-use incarnation"),
            ("incomplet-into-luma-copies-uges", "the Incomplet into a factice Luma, then his Mélange picks a factice Ugës (no active power)",
             ["-autoplay-force-roles", I, "-autoplay-role-holder", "client", "-autoplay-force-fakes", f"{L},{U}",
              "-autoplay-target-map", f"incarnation={L};lange={U}", "-autoplay-max-days", "4"],
             "--expect-use lange"),
        ]
        for name, label, args, analyzer in combos:
            yield {"name": f"copies-combo-{name}", "label": label, "args": args + ["-autoplay-fast-fakes"] + COMMON,
                   "analyzer": analyzer}
    if "fake" in a.family:
        # No fast-fakes here: the real timing of the factice is what is checked.
        yield {
            "name": "copies-fake-uges", "label": "a factice Ugës waits like a real role",
            "args": ["-autoplay-force-fakes", f_uges, "-autoplay-max-days", "2"] + COMMON,
            "analyzer": "",
            "fakeLayer": f_uges,
        }
    if "rejoin" in a.family:
        role, power = actives[0]
        pf, rf = power_fragment(power["name"]), fragment_for(role["name"], names)
        yield {
            "name": f"copies-uges-rejoin", "label": "Ugës's client drops at night 2 and rejoins",
            "args": ["-autoplay-force-roles", f_uges, "-autoplay-role-holder", "seat:1", "-autoplay-steal", pf,
                     "-autoplay-max-days", "4", "-autoplay-fast-fakes", "-autoplay-rejoin-grace", "60"] + COMMON,
            "clientArgs": ["-autoplay-connect-delay", "4"],
            "client1Args": ["-autoplay-connect-delay", "0", "-autoplay-quit-at", "AwakeningState day=2",
                            "-autoplay-rejoin-after", "5", "-autoplay-rejoin-via", "menu"],
            "analyzer": "--expect-use \".\" --expect-seat client --min-day 3",
            "extra": [{"type": "event", "kind": "rejoin.seat", "process": "clients", "min": 1, "max": 1},
                      {"type": "analyzer", "cmd": "python -X utf8 tools/autoplay/analyze_rejoin.py {run}"}],
        }


def fake_layer_ok(run, fragment):
    """Every awakening layer that woke the factice Ugës lasted more than 2 game seconds (none ended at once)."""
    host = (glob.glob(os.path.join(run, "*-host-*", "events.ndjson")) or [""])[0]
    if not host:
        return None, "no host events"
    events = [json.loads(l) for l in open(host, encoding="utf-8-sig") if l.strip()]
    durations, open_layer = [], None
    for e in events:
        if e["kind"] == "awake.layer":
            open_layer = e["detail"] if re.search(rf"fake:[^]]*?{fragment}", e["detail"], re.I) else None
        elif e["kind"] == "awake.layer.end" and open_layer:
            m = re.search(r"game=([\d.]+)s", e["detail"])
            if m:
                durations.append(float(m.group(1)))
            open_layer = None
    if not durations:
        return None, "the factice never woke"
    short = [d for d in durations if d < 2.0]
    return (not short), f"layers {durations} s" + (f", {len(short)} under 2 s" if short else "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roles")
    ap.add_argument("--family", default="uges,luma,incomplet,combos,host,fake,rejoin")
    ap.add_argument("--only")
    ap.add_argument("--seed", type=int, default=900)
    ap.add_argument("--days", type=int, default=3)
    ap.add_argument("--port", type=int, default=0)
    a = ap.parse_args()
    a.family = [f.strip() for f in a.family.split(",")]

    roles_path = a.roles or newest_roles()
    if not roles_path:
        sys.exit("no roles.json yet: run any host scenario first (every host run writes one)")
    pool = json.load(open(roles_path, encoding="utf-8-sig"))["roles"]
    todo = list(cases(pool, a))
    if a.only:
        todo = [c for c in todo if any(o.strip().lower() in (c["name"] + c["label"]).lower() for o in a.only.split(","))]

    out = os.path.join(PROJECT, "AutoplayRuns", f"copies-sweep-{datetime.datetime.now():%Y%m%d-%H%M%S}")
    os.makedirs(os.path.join(out, "scenarios"), exist_ok=True)
    print(f"copies sweep: {len(todo)} case(s), pool from {roles_path}", flush=True)
    rows = []
    for i, c in enumerate(todo):
        scenario = {
            "name": c["name"], "goal": c["label"], "mode": "net", "clients": 2, "seed": a.seed + i, "seedRetries": 4,
            "timescale": 3, "timeout": 1200, "args": c["args"],
            "expect": base_expect(c["analyzer"]) + c.get("extra", []),
        }
        for key in ("clientArgs", "client1Args"):
            if key in c:
                scenario[key] = c[key]
        if c.get("fakeLayer"):
            scenario["expect"] = [x for x in scenario["expect"] if x["type"] != "analyzer"]
        path = os.path.join(out, "scenarios", c["name"] + ".json")
        json.dump(scenario, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print(f"[{i + 1}/{len(todo)}] {c['label']} …", flush=True)
        res = subprocess.run([sys.executable, "-X", "utf8", RUNNER, path, "--ctl", os.path.join(HERE, "unityctl.sh"),
                              "--project", PROJECT] + (["--port", str(a.port)] if a.port else []), cwd=PROJECT,
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        open(os.path.join(out, c["name"] + ".log"), "w", encoding="utf-8").write(res.stdout + res.stderr)
        run = (re.findall(r"VERDICT: \w+\s+\((.+?)\)", res.stdout) or [""])[-1]
        fails = [l.strip() for l in res.stdout.splitlines() if l.strip().startswith("[FAIL]")]
        not_covered = [f for f in fails if "not covered" in f]
        if "composition mismatch" in res.stdout and not run:
            status, why = "NOT COVERED", "no seed gave the forced composition"
        elif not_covered and len(not_covered) == len(fails):
            status, why = "NOT COVERED", " / ".join(f[7:160] for f in not_covered)
        elif fails:
            status, why = "FAIL", " / ".join(f[7:160] for f in fails)
        else:
            status, why = "OK", ""
        if c.get("fakeLayer") and run and status == "OK":
            ok, detail = fake_layer_ok(run, c["fakeLayer"])
            status, why = ("OK" if ok else "FAIL" if ok is False else "NOT COVERED"), detail
        rows.append((c["label"], status, why, os.path.basename(run)))
        print(f"    {status}: {why}", flush=True)

    lines = ["# Copied-power sweep", "", "| Case | Verdict | Why | Run |", "|---|---|---|---|"]
    lines += [f"| {l} | {s} | {w.replace('|', '/')} | {r} |" for l, s, w, r in rows]
    open(os.path.join(out, "coverage.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    print(f"coverage: {os.path.join(out, 'coverage.md')}")


if __name__ == "__main__":
    main()
