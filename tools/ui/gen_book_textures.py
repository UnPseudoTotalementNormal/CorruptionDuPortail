"""Generate the procedural textures of the grimoire UI (role book + role overlay page).

Placeholder art, design-owned: an artist can replace any PNG in Assets/Art/Sprites/Book/ by a painted one with
the same name and size ratio (the USS 9-slices the page / leather borders, see RoleSheet.uss / RoleBook.uss).
Deterministic (fixed seed). Run from the repo root:  python tools/ui/gen_book_textures.py
Needs Pillow + numpy.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join("Assets", "Art", "Sprites", "Book")
RNG = np.random.default_rng(1307)


def fractal_noise(w, h, octaves=6, persistence=0.55):
    """Sum of upscaled random grids: soft cloudy noise in [0, 1]."""
    acc = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = 2 ** (o + 2)
        grid = RNG.random((cells, cells)).astype(np.float32)
        layer = np.asarray(Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
        acc += layer * amp
        total += amp
        amp *= persistence
    acc /= total
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-6)


def edge_distance(w, h):
    """0 at the border, 1 deep inside (normalised by the short side)."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.minimum.reduce([x, y, w - 1 - x, h - 1 - y])
    return d / (min(w, h) * 0.5)


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    img.save(path, optimize=True)
    print("wrote", path, img.size)


def parchment(w=1024, h=1024):
    base = np.array([226, 205, 160], np.float32)          # aged paper
    dark = np.array([150, 108, 62], np.float32)            # foxing / burnt tone
    n = fractal_noise(w, h)
    fine = fractal_noise(w, h, octaves=3, persistence=0.7)
    t = 0.55 * n + 0.15 * fine
    # Burnt, irregular edges: darker as the (noisy) edge distance shrinks.
    ed = edge_distance(w, h) + (fractal_noise(w, h, 4) - 0.5) * 0.12
    burn = np.clip(1.0 - ed / 0.22, 0, 1) ** 1.6
    t = np.clip(t * 0.5 + burn * 0.85, 0, 1)
    rgb = base[None, None, :] * (1 - t[..., None]) + dark[None, None, :] * t[..., None]
    # A few faint stains.
    img = Image.fromarray(rgb.clip(0, 255).astype(np.uint8), "RGB")
    stains = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(stains)
    for _ in range(7):
        cx, cy, r = RNG.integers(80, w - 80), RNG.integers(80, h - 80), RNG.integers(30, 110)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=int(RNG.integers(12, 26)), width=int(RNG.integers(2, 6)))
    stains = stains.filter(ImageFilter.GaussianBlur(5))
    img = Image.composite(Image.new("RGB", (w, h), (140, 98, 52)), img, stains)
    # Paper fibres.
    fib = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(fib)
    for _ in range(900):
        x, y = RNG.integers(0, w), RNG.integers(0, h)
        dx, dy = RNG.normal(0, 14), RNG.normal(0, 14)
        d.line([x, y, x + dx, y + dy], fill=int(RNG.integers(10, 30)), width=1)
    img = Image.composite(Image.new("RGB", (w, h), (120, 84, 44)), img, fib.filter(ImageFilter.GaussianBlur(0.6)))
    # Ragged alpha on the very edge.
    alpha = np.clip((edge_distance(w, h) + (fractal_noise(w, h, 5) - 0.5) * 0.03) / 0.012, 0, 1)
    out = img.convert("RGBA")
    out.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    return out


def leather(w=1024, h=1024):
    base = np.array([44, 24, 18], np.float32)
    lite = np.array([86, 48, 32], np.float32)
    grain = fractal_noise(w, h, octaves=7, persistence=0.65)
    cells = np.asarray(Image.fromarray((RNG.random((h // 3, w // 3)) * 255).astype(np.uint8)).resize((w, h), Image.NEAREST)
                       .filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255
    t = np.clip(0.65 * grain + 0.35 * cells, 0, 1)
    rgb = base * (1 - t[..., None]) + lite * t[..., None]
    # Worn, darker borders.
    ed = np.clip(edge_distance(w, h) / 0.18, 0, 1)
    rgb *= (0.55 + 0.45 * ed)[..., None]
    img = Image.fromarray(rgb.clip(0, 255).astype(np.uint8), "RGB").convert("RGBA")
    # Gold tooling: a double fillet inset from the edge (lives in the 9-slice border, so it never stretches oddly).
    d = ImageDraw.Draw(img)
    gold = (196, 150, 72, 255)
    for inset, width in ((46, 3), (58, 1)):
        d.rounded_rectangle([inset, inset, w - 1 - inset, h - 1 - inset], radius=18, outline=gold, width=width)
    return img


def spine(w=192, h=8):
    """Horizontal gradient for the book's gutter: transparent -> dark -> transparent."""
    x = np.linspace(-1, 1, w, dtype=np.float32)
    a = np.exp(-(x / 0.28) ** 2) * 0.75 + np.exp(-(x / 0.06) ** 2) * 0.25
    rgba = np.zeros((h, w, 4), np.uint8)
    rgba[..., 0:3] = (40, 24, 12)
    rgba[..., 3] = (np.clip(a, 0, 1) * 255).astype(np.uint8)[None, :]
    return Image.fromarray(rgba, "RGBA")


def cast_shadow(w=256, h=8):
    """One-sided soft shadow a turning page casts on the page under it: dark at x=0, fading to the right."""
    x = np.linspace(0, 1, w, dtype=np.float32)
    a = np.exp(-x * 4.5) * (1 - x) ** 0.5
    rgba = np.zeros((h, w, 4), np.uint8)
    rgba[..., 0:3] = (30, 18, 8)
    rgba[..., 3] = (np.clip(a, 0, 1) * 255).astype(np.uint8)[None, :]
    return Image.fromarray(rgba, "RGBA")


def divider(w=640, h=40):
    """White ornamental rule (tinted in USS): tapered line with a centre lozenge and two dots."""
    big = 4
    img = Image.new("L", (w * big, h * big), 0)
    d = ImageDraw.Draw(img)
    cy = h * big // 2
    cx = w * big // 2
    for side in (-1, 1):
        pts = [(cx + side * 70 * big // 4 * 1.0, cy - 6), (cx + side * (w * big // 2 - 8), cy), (cx + side * 70 * big // 4 * 1.0, cy + 6)]
        d.polygon(pts, fill=255)
        dot = cx + side * 52 * big // 4 * 1.0
        d.ellipse([dot - 9, cy - 9, dot + 9, cy + 9], fill=255)
    r = 30
    d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=255)
    d.polygon([(cx, cy - r + 12), (cx + r - 12, cy), (cx, cy + r - 12), (cx - r + 12, cy)], fill=0)
    img = img.resize((w, h), Image.LANCZOS)
    out = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    out.putalpha(img)
    return out


def corner(w=160):
    """Dog-eared page corner (bottom-right orientation); flipped in USS for the left page."""
    big = 4
    s = w * big
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Shadow under the fold.
    shadow = Image.new("L", (s, s), 0)
    ImageDraw.Draw(shadow).polygon([(s, s * 0.18), (s, s), (s * 0.18, s)], fill=110)
    shadow = shadow.filter(ImageFilter.GaussianBlur(s * 0.04))
    img.paste((30, 18, 8, 255), (0, 0), shadow)
    # The folded flap (back of the paper, slightly darker), its fold line on the diagonal.
    d.polygon([(s * 0.34, s), (s, s * 0.34), (s * 0.34, s * 0.34)], fill=(205, 180, 132, 255))
    d.line([(s * 0.34, s), (s, s * 0.34)], fill=(120, 86, 46, 255), width=big * 2)
    return img.resize((w, w), Image.LANCZOS)


def seal(w=256):
    """Wax seal disc in grey levels (tinted with the faction colour in USS), with a raised rim."""
    big = 4
    s = w * big
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    cx = cy = s / 2
    ang = np.arctan2(yy - cy, xx - cx)
    wobble = 1 + 0.035 * np.sin(ang * 7 + 1.3) + 0.02 * np.sin(ang * 13)
    r = np.hypot(xx - cx, yy - cy) / (s * 0.46 * wobble)
    alpha = np.clip((1 - r) * 40, 0, 1)
    shade = 0.78 + 0.22 * np.clip(1 - np.hypot(xx - cx * 0.8, yy - cy * 0.8) / s, 0, 1)
    rim = np.exp(-((r - 0.80) / 0.06) ** 2) * 0.25
    lum = np.clip(shade + rim - (r > 0.86) * 0.08, 0, 1)
    rgba = np.zeros((s, s, 4), np.uint8)
    rgba[..., 0:3] = (lum * 255).astype(np.uint8)[..., None]
    rgba[..., 3] = (alpha * 255).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA").resize((w, w), Image.LANCZOS)


def ribbon(w=96, h=320):
    """Bookmark ribbon (white, tinted per faction) with a forked tail at the bottom."""
    big = 4
    W, H = w * big, h * big
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    notch = int(W * 0.42)
    d.polygon([(0, 0), (W, 0), (W, H), (W // 2, H - notch), (0, H)], fill=255)
    # A soft vertical sheen so the silk does not look flat.
    x = np.linspace(-1, 1, W, dtype=np.float32)
    sheen = (0.82 + 0.18 * np.cos(x * 2.2)).astype(np.float32)
    lum = (np.ones((H, W), np.float32) * sheen[None, :] * 255).astype(np.uint8)
    out = Image.merge("RGBA", [Image.fromarray(lum)] * 3 + [img])
    return out.resize((w, h), Image.LANCZOS)


if __name__ == "__main__":
    save(parchment(), "Parchment.png")
    save(leather(), "Leather.png")
    save(spine(), "Gutter.png")
    save(cast_shadow(), "CastShadow.png")
    save(divider(), "Divider.png")
    save(corner(), "PageCorner.png")
    save(seal(), "WaxSeal.png")
    save(ribbon(), "Ribbon.png")
