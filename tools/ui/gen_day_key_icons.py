"""Draws the T23 day key hints (1 = players' row, 2 = roles' row, T = the tablet's table) in the style of the other
key icons of Assets/UI/KeyHints: cream line drawings on transparent, 184 x 152 (4x the 46 x 38 hint)."""
import os
from PIL import Image, ImageDraw

INK = (234, 220, 191, 255)          # #eadcbf
FILL = (234, 220, 191, 64)
W, H, S = 184, 152, 4               # canvas, scale from the 46 x 38 design grid
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "KeyHints")


def canvas():
    im = Image.new("RGBA", (W * 2, H * 2), (0, 0, 0, 0))   # drawn at 2x then reduced: smooth lines
    return im, ImageDraw.Draw(im)


def box(d, x, y, w, h, width=2.4, r=1.5, fill=None):
    k = S * 2
    d.rounded_rectangle([x * k, y * k, (x + w) * k, (y + h) * k], radius=r * k, outline=INK, width=round(width * k),
                        fill=fill)


def save(im, name):
    im.resize((W, H), Image.LANCZOS).save(os.path.join(OUT, name))


# 1: the players' cards, three side by side, the middle one raised
im, d = canvas()
box(d, 4, 10, 11, 18)
box(d, 17.5, 6, 11, 18, fill=FILL)
box(d, 31, 10, 11, 18)
save(im, "KeyIcon_players.png")

# 2: the roles of the game, small portraits in a row under their side's mark
im, d = canvas()
k = S * 2
d.polygon([(23 * k, 4 * k), (27 * k, 8.5 * k), (23 * k, 13 * k), (19 * k, 8.5 * k)], outline=INK, width=round(2.2 * k))
for x in (5, 15.5, 26, 36.5 - 2):
    box(d, x, 17, 8, 12, width=2.2, r=1)
save(im, "KeyIcon_roles.png")

# T: the tablet's table, a screen with rows and a column
im, d = canvas()
box(d, 6, 5, 34, 26, width=2.5, r=2)
for y in (12, 18, 24):
    d.line([(10 * k, y * k), (36 * k, y * k)], fill=INK, width=round(1.6 * k))
d.line([(19 * k, 9 * k), (19 * k, 28 * k)], fill=INK, width=round(1.6 * k))
save(im, "KeyIcon_spreadsheet.png")
print("ok")

# P: skip the vote, a ballot card passed over by an arrow
im, d = canvas()
box(d, 13, 8, 14, 22, width=2.2, r=1.2)
d.line([(6 * k, 19 * k), (38 * k, 19 * k)], fill=INK, width=round(2.6 * k))
d.line([(32 * k, 13.5 * k), (38.5 * k, 19 * k), (32 * k, 24.5 * k)], fill=INK, width=round(2.6 * k), joint="curve")
save(im, "KeyIcon_skipvote.png")
print("skip ok")
