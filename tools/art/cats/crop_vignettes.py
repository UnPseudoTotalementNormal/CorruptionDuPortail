"""Crops the layers rendered by render_layers.py into the customization screen's vignettes (256 x 240): each part's
box (tools/maquettes/personnalisation/cat.js, BOX, offset +205 / +220 in the 800 x 1000 render), scaled to fit with a
0.92 margin, centred. Usage: python crop_vignettes.py <layers dir> <Assets/Art/Sprites/CatCosmetics> [part_skin ...]"""
import os, sys
from PIL import Image

BOX = {'body': (5, 7, 434, 550), 'outfit': (50, 300, 330, 570), 'eyes': (39, 150, 321, 351), 'mouth': (80, 300, 226, 380)}
SLOT = {'body': 'Fur', 'outfit': 'Outfit', 'eyes': 'Eyes', 'mouth': 'Mouth'}
SKIN = {'bw': 'BlackWhite', 'fiery': 'Fiery', 'galactic': 'Galactic', 'mage': 'Mage', 'chef': 'Chef'}
W, H = 256, 240

def vignette(layer):
    part = os.path.basename(layer).split('_')[0]
    x0, y0, x1, y1 = BOX[part]
    cx, cy = (x0 + x1) / 2 + 205, (y0 + y1) / 2 + 220
    k = min(W / (x1 - x0), H / (y1 - y0)) * 0.92
    src = Image.open(layer).convert('RGBA')
    box = (cx - W / 2 / k, cy - H / 2 / k, cx + W / 2 / k, cy + H / 2 / k)
    return src.transform((W, H), Image.Transform.EXTENT, box, Image.Resampling.BICUBIC)

layers, out = sys.argv[1], sys.argv[2]
names = sys.argv[3:] or [f[:-4] for f in os.listdir(layers) if f.endswith('.png') and not f.startswith('full')]
for name in names:
    part, skin = name.split('_')
    path = os.path.join(out, f'CatIcon_{SLOT[part]}_{SKIN[skin]}.png')
    vignette(os.path.join(layers, name + '.png')).save(path)
    print('VIGNETTE', path)
