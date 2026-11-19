"""Cut Wouh's sticker sheets (dark background, white outline) into one transparent PNG per icon.

Flood-fill the dark background from the borders, then split what is left into connected blobs.
Usage: python cut_icons.py  (reads img/src/*.png, writes img/icons/<sheet>_<n>.png)
"""
from pathlib import Path
from collections import deque
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = Path(__file__).parent
SRC, OUT = HERE / "img/src", HERE / "img/icons"
OUT.mkdir(parents=True, exist_ok=True)


def background_mask(rgb: np.ndarray, tol: int = 40) -> np.ndarray:
    bg = np.median(np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]]), axis=0)
    near = np.abs(rgb.astype(int) - bg).max(axis=2) < tol
    lab, _ = ndimage.label(near)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    return np.isin(lab, list(border))


for sheet in ["pattes", "corrompu", "lotus_chapeau", "orpheline_etc"]:
    im = Image.open(SRC / f"{sheet}.png").convert("RGB")
    rgb = np.asarray(im)
    bg = background_mask(rgb)
    fg = ~bg
    # close small gaps so an icon and its spark lines stay one blob
    blobs, n = ndimage.label(ndimage.binary_dilation(fg, iterations=6 if sheet == "orpheline_etc" else 14))
    objs = ndimage.find_objects(blobs)
    found = []
    for i, sl in enumerate(objs, 1):
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if h * w < 2500:
            continue
        found.append((sl[0].start, sl[1].start, i, sl))
    # rows top-first, then left to right
    found.sort(key=lambda t: (round(t[0] / 200), t[1]))
    alpha = (fg * 255).astype(np.uint8)
    alpha = np.asarray(Image.fromarray(alpha).filter(__import__("PIL.ImageFilter").ImageFilter.GaussianBlur(0.8)))
    rgba = np.dstack([rgb, alpha])
    for k, (_, _, i, sl) in enumerate(found):
        part = rgba[sl].copy()
        part[..., 3] = np.where(blobs[sl] == i, part[..., 3], 0)
        Image.fromarray(part, "RGBA").save(OUT / f"{sheet}_{k}.png")
        print(sheet, k, part.shape[1], "x", part.shape[0])
