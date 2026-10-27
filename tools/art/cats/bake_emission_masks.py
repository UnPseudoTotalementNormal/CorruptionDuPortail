"""Bakes the glow masks of the cats' Blender materials into textures for "Corruption/Cat Toon" (CatToon.hlsl).

In Corrupted_Cats.blend the glowing parts are a Mix Shader towards an Emission whose factor (or strength) is a texture
read as a number: Blender converts the colour to its luminance (scene linear, Rec. 709 weights), sometimes through a
Color Ramp. Each mask below redoes that conversion once, stored linear in one 8-bit grey PNG (import: sRGB off).

Usage: python bake_emission_masks.py <Drive "Cats" folder> <repo>
"""
import os
import sys

import numpy as np
from PIL import Image

DRIVE, REPO = sys.argv[1], sys.argv[2]
UNITY = os.path.join(REPO, "Assets", "Art", "Textures", "Cat")
OUT = os.path.join(UNITY, "Cosmetics")


def luminance(path):
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    return linear @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def ramp(value, low, high):
    return np.clip((value - low) / (high - low), 0.0, 1.0)


def save(mask, name):
    Image.fromarray(np.round(mask * 255.0).astype(np.uint8), "L").save(os.path.join(OUT, f"{name}.png"))
    print(f"{name}: {mask.shape[1]}x{mask.shape[0]}, lit {np.mean(mask > 0.5) * 100:.2f} %")


# Black and white body: Color Ramp 0.317 -> 0.368 on "Black and White Body Orange.png" (the orange dots).
save(ramp(luminance(os.path.join(DRIVE, "Textures", "Bodies", "Black and White Body Orange.png")), 0.317, 0.368),
     "Cat_BlackWhite_GlowMask")
# Galactic body: the cracks, the body texture itself through a 0 -> 1 ramp; then the speckles texture.
save(luminance(os.path.join(OUT, "Cat_Galactic_Body.png")), "Cat_Galactic_CracksMask")
save(luminance(os.path.join(OUT, "Cat_Galactic_Speckles.png")), "Cat_Galactic_SpecklesMask")
# Mage body: the speckles texture.
save(luminance(os.path.join(OUT, "Cat_Mage_Speckles.png")), "Cat_Mage_SpecklesMask")
# Fiery and galactic eyes: emission strength = the eyes texture through a 0 -> 1 ramp.
save(luminance(os.path.join(OUT, "Cat_Fiery_Eyes.png")), "Cat_Fiery_EyesMask")
save(luminance(os.path.join(OUT, "Cat_Galactic_Eyes.png")), "Cat_Galactic_EyesMask")
