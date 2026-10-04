"""Animation recording -> one labelled contact sheet (and optionally a GIF).

    python Tools~/contact_sheet.py <rec-dir | run-dir> [--cols 6] [--every 1] [--gif] [--track frost --track card0.y]

- <rec-dir>: a `rec-NNN-…` folder written by AutoplayRecorder (f000.png…, tracks.csv, manifest.json).
  Given a run folder, every recording inside it is processed.
- Writes <rec-dir>/contact.png: frames in a grid, each labelled with its frame number, time and the chosen tracks'
  values — one image to look at instead of dozens. --gif also writes <rec-dir>/anim.gif (real-time speed).
Requires Pillow (`pip install pillow`).
"""
import argparse
import csv
import glob
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont


def load_tracks(rec):
    path = os.path.join(rec, "tracks.csv")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def sheet(rec, cols, every, tracks, gif):
    frames = sorted(glob.glob(os.path.join(rec, "f*.png")))[::max(1, every)]
    if not frames:
        print(f"{rec}: no frames")
        return None
    rows = load_tracks(rec)
    by_frame = {r["frame"]: r for r in rows}
    manifest = json.load(open(os.path.join(rec, "manifest.json"), encoding="utf-8-sig")) if os.path.exists(os.path.join(rec, "manifest.json")) else {}

    first = Image.open(frames[0])
    w, h = first.size
    label_h = 16 + 14 * len(tracks)
    n_rows = (len(frames) + cols - 1) // cols
    header = 24
    out = Image.new("RGB", (cols * w, header + n_rows * (h + label_h)), (24, 24, 30))
    draw = ImageDraw.Draw(out)
    font = ImageFont.load_default()
    draw.text((6, 5), f"{os.path.basename(rec)}  trigger={manifest.get('trigger', '?')}  {manifest.get('fps', '?')} fps", fill=(230, 230, 240), font=font)

    for i, path in enumerate(frames):
        x, y = (i % cols) * w, header + (i // cols) * (h + label_h)
        out.paste(Image.open(path), (x, y))
        fid = str(int(os.path.basename(path)[1:-4]))
        row = by_frame.get(fid, {})
        lines = [f"#{fid}  t={row.get('time', '?')}s"] + [f"{t}={row.get(t, '') or '-'}" for t in tracks]
        for j, line in enumerate(lines):
            draw.text((x + 4, y + h + 2 + 14 * j), line, fill=(220, 220, 120) if j == 0 else (200, 200, 210), font=font)

    target = os.path.join(rec, "contact.png")
    out.save(target)
    if gif:
        imgs = [Image.open(p).convert("P", palette=Image.ADAPTIVE) for p in sorted(glob.glob(os.path.join(rec, "f*.png")))]
        duration = int(1000 / max(1, manifest.get("fps", 20)))
        imgs[0].save(os.path.join(rec, "anim.gif"), save_all=True, append_images=imgs[1:], duration=duration, loop=0)
    print(f"{rec}: {len(frames)} frames -> {target}{' + anim.gif' if gif else ''}")
    return target


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path")
    ap.add_argument("--cols", type=int, default=6)
    ap.add_argument("--every", type=int, default=1, help="keep one frame out of N")
    ap.add_argument("--track", action="append", default=[], help="track value to print under each frame (repeatable)")
    ap.add_argument("--gif", action="store_true")
    a = ap.parse_args()
    recs = [a.path] if os.path.basename(a.path.rstrip("/\\")).startswith("rec-") else sorted(glob.glob(os.path.join(a.path, "rec-*")))
    if not recs:
        sys.exit(f"no recording under {a.path}")
    for rec in recs:
        sheet(rec, a.cols, a.every, a.track, a.gif)


if __name__ == "__main__":
    main()
