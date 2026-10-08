"""Card visibility verdict of a run made with -autoplay-card-visibility (AutoplayDriverVisibility).

    python -X utf8 tools/autoplay/analyze_card_visibility.py <run dir> [<run dir> ...] [--cards] [--min 99.5]
        [--parts face,button,text] [--views top,top-hover,fps,fps-hover]

Per layout x view: the least visible card face, vote button and vote count text over every card (percent of sample
points where the card's flat paint reaches the screen), the share of vote button points where a click reaches that
button (the UI raycast can hand it to a canvas behind the card), the share of face points where pointing hovers the
card itself (reach), and what covered the hidden points. --cards lists every card
below the threshold. The verdict (exit 0 = PASS) requires every listed part >= --min in every listed view; a part not
drawn in a view is not judged there, nor is the vote button outside a hover (by design it stays tucked under
the card until the card is hovered). Ends with one PASS / FAIL line per layout.
"""
import argparse
import glob
import json
import os
import re
import sys


def fields(detail):
    out = {}
    for m in re.finditer(r'([\w-]+)=("[^"]*"|\[[^\]]*\]|\S+)', detail):
        out[m.group(1)] = m.group(2).strip('"')
    return out


def events(run):
    files = glob.glob(os.path.join(run, "events.ndjson")) or glob.glob(os.path.join(run, "*", "events.ndjson"))
    for f in files:
        with open(f, encoding="utf-8-sig") as fh:
            for line in fh:
                try:
                    yield json.loads(line)
                except json.JSONDecodeError:
                    continue


def pct(v):
    try:
        return float(re.match(r"[\d.]+", v).group(0))
    except (AttributeError, TypeError):
        return None


def judged_parts(parts, view):
    # The vote button (seen, and reached by a click) is judged hovered only: it is tucked under the card otherwise.
    if view == "fps-reticle":
        return ["click"]  # reticle stability on the vote button (fps-reticle), reported in the click column
    return [p for p in parts if p not in ("button", "click") or view.endswith("-hover")]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--cards", action="store_true")
    ap.add_argument("--min", type=float, default=99.5)
    ap.add_argument("--parts", default="face,button,text,click,reach")
    ap.add_argument("--views", default="")
    ap.add_argument("--effects-see-through", action="store_true",
                    help="judge as if other cards' markers (the Technomancien's beacon) were see-through: what the layout alone hides")
    a = ap.parse_args()
    parts = a.parts.split(",")
    views = set(a.views.split(",")) if a.views else None
    ok = True
    for run in a.runs:
        evs = list(events(run))
        start = next((e["detail"] for e in evs if e["kind"] == "vis.start"), None)
        print(f"== {os.path.basename(os.path.normpath(run))}: {start or 'NO vis.start (probe did not run)'}")
        if start is None:
            ok = False
            continue
        for e in evs:
            if e["kind"] == "vis.error":
                print("   ERROR", e["detail"])
                ok = False
        layouts = {}
        print(f"   {'layout':<8} {'view':<10} {'face':>6} {'button':>6} {'text':>6} {'click':>6} {'reach':>6} worst  hidden-by")
        for e in evs:
            if e["kind"] != "vis.view":
                continue
            f = fields(e["detail"])
            judged = views is None or f["view"] in views
            suffix = "-fx" if a.effects_see_through and f.get("min-face-fx") is not None else ""
            vals = {p: pct(f.get(f"min-{p}{suffix}")) for p in ("face", "button", "text")}
            vals["click"] = pct(f.get("min-click"))
            vals["reach"] = pct(f.get("min-reach"))
            if f["view"] == "fps-reticle":
                vals["click"] = pct(f.get("min-reticle"))
            bad = judged and any(vals[p] is not None and vals[p] < a.min for p in judged_parts(parts, f["view"]))
            layouts.setdefault(f["layout"], [f.get("spec", ""), True])
            if bad:
                layouts[f["layout"]][1] = False
            show = lambda v: "-" if v is None else f"{v:.1f}"
            print(f" {'X' if bad else ' '} {f['layout']:<8} {f['view']:<10} {show(vals['face']):>6} {show(vals['button']):>6} "
                  f"{show(vals['text']):>6} {show(vals['click']):>6} {show(vals.get('reach')):>6} {f['worst']:>5}  {f['hidden-by']}")
        if a.cards:
            for e in evs:
                if e["kind"] != "vis.card":
                    continue
                f = fields(e["detail"])
                if views is not None and f["view"] not in views:
                    continue
                key = lambda p: f"{p}-fx" if a.effects_see_through and f"{p}-fx" in f else p
                if any(pct(f[key(p)]) is not None and pct(f[key(p)]) < a.min for p in judged_parts(parts, f["view"]) if p in f):
                    print("     ", e["detail"])
        if not any(e["kind"] == "vis.done" for e in evs):
            print("   probe did not finish (no vis.done)")
            ok = False
        for name, (spec, good) in layouts.items():
            print(f"   {'PASS' if good else 'FAIL'} {name}: {spec}")
            ok = ok and good
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
