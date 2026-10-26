"""Converts one texture of the artist's Drive to the game's format: an 8-bit PNG saved by Blender with the Standard
view transform (some Drive files are EXR or 16-bit PNG under a .png name). With --key, the blue background
(about 0, 0, 255) of a mouth becomes alpha: distance to the key colour, ramp 40 -> 100 / 255.
Usage: blender -b --python convert_texture.py -- <source> <destination.png> [--key]"""
import bpy, sys
import numpy as np

args = sys.argv[sys.argv.index('--') + 1:]
src, dst, key = args[0], args[1], '--key' in args

scene = bpy.context.scene
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
settings = scene.render.image_settings
settings.file_format = 'PNG'
settings.color_mode = 'RGBA'
settings.color_depth = '8'

image = bpy.data.images.load(src)
image.save_render(dst, scene=scene)

if key:
    out = bpy.data.images.load(dst)
    w, h = out.size
    px = np.array(out.pixels[:], dtype=np.float32).reshape(h, w, 4) * 255.0
    dist = np.sqrt(px[..., 0] ** 2 + px[..., 1] ** 2 + (px[..., 2] - 255.0) ** 2)
    px[..., 3] = np.clip((dist - 40.0) / 60.0, 0.0, 1.0) * 255.0
    out.pixels[:] = (np.round(px) / 255.0).ravel()
    out.filepath_raw = dst
    out.file_format = 'PNG'
    out.save()
print('CONVERTED', src, '->', dst, '(keyed)' if key else '')
