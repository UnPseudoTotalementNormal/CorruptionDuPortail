# Turns the artist's textures (Drive folder "Textures/") into the game's: Assets/Art/Textures/Props/Prop_<Object>[_Kind].png,
# 8-bit, 2048 at most (16-bit PNGs and EXRs saved as .png weigh up to 270 MB on the Drive).
#
#   python prepare_textures.py <drive folder> <repo>/Assets/Art/Textures/Props
#
# Needs ffmpeg on the PATH. Per object, from the .blend's Principled BSDF (tools/art/props/README.md):
#   base        Base Color, sRGB (the board's Alpha map goes in its alpha)
#   _Normal     Normal Map node (OpenGL, same as Unity)
#   _Gloss      URP's metallic-smoothness map: black, smoothness in alpha = 1 - roughness. The file reads its roughness
#               maps as sRGB, so they are linearised first (the look the artist sees in Blender).
#   _Emission   Emission Color, sRGB
import os
import subprocess
import sys

TEXTURES = {
    "ArcaneCube": {"base": "Arcane Cube/Arcane Cube Texture.png"},
    "Bell": {"base": "Bell/Bell Texture.png"},
    "ChainPile": {"base": "Chain/Chain Pile Texture.png"},
    "ChainLink": {"base": "Chain/Single Chain.png"},
    "Chair": {"base": "Table/Chair Texture.png"},
    "Corruptor": {"base": "Corruptor/Corruptor Texture.png", "normal": "Corruptor/Corruptor Normals.png",
                  "rough": "Corruptor/Corruptor Roughness.png"},
    "CrystalBallBase": {"base": "Crystal Ball/Crystal Ball Base Texture.png"},
    "CrystalSkull": {"base": "Skull/Crystal Skull Texture.png", "normal": "Skull/Crystal Skull Normals.png",
                     "rough": "Skull/Crystal Skull Roughness.png"},
    "PurpleHand": {"base": "Purple Hand/Purple Hand Diffuse.png", "normal": "Purple Hand/Purple Hand Normals.png",
                   "rough": "Purple Hand/Purple Hand Roughness.png"},
    "Sniper": {"base": "Sniper/Sniper Texture.png"},
    "Table": {"base": "Table/Table Texture.png"},
    "TeddyBear": {"base": "Teddy Bear/Teddy Bear Texture.png"},
    "Timer": {"base": "Timer/Timer Texture.png"},
    "WoodenBoard": {"base": "Wooden Board/Wooden Board Texture.png", "alpha": "Wooden Board/Wooden Board Alpha.png",
                    "normal": "Wooden Board/Wooden Board Normals.png", "rough": "Wooden Board/Wooden Board Roughness.png"},
    "BeaconRobot": {"base": "Robot/Robot Diffuse.png", "normal": "Robot/Robot Normals.png", "rough": "Robot/Robot Roughness.png",
              "emission": "Robot/Robot Eyes Open Emission.png"},
    "Beacon": {"base": "Robot/Beacon Texture.png", "emission": "Robot/Beacon Emission.png"},
    "Totem": {"base": "Totem/Totem Diffuse.png", "normal": "Totem/Totem Normals.png", "rough": "Totem/Totem Roughness.png",
              "emission": "Totem/Totem Emission.png"},
}

SIZE = 2048
SCALE = f"scale='min({SIZE},iw)':'min({SIZE},ih)':flags=lanczos"
# sRGB -> linear, close enough for a roughness or an alpha mask.
LINEAR = "pow(lum(X,Y)/255,2.2)"


def ffmpeg(inputs, graph, out, pix_fmt):
    command = ["ffmpeg", "-v", "error", "-y"]
    for path in inputs:
        command += ["-i", path]
    command += ["-filter_complex", graph, "-frames:v", "1", "-pix_fmt", pix_fmt, out]
    subprocess.run(command, check=True)


def main(drive, out):
    textures = os.path.join(drive, "Textures")
    os.makedirs(out, exist_ok=True)
    for name, maps in TEXTURES.items():
        src = {kind: os.path.join(textures, path) for kind, path in maps.items()}
        target = os.path.join(out, f"Prop_{name}")
        if "alpha" in src:
            ffmpeg([src["base"], src["alpha"]],
                   f"[0]{SCALE},format=rgb24[c];[1]{SCALE},format=gray,geq=lum='255*{LINEAR}'[a];[c][a]alphamerge",
                   target + ".png", "rgba")
        else:
            ffmpeg([src["base"]], f"[0]{SCALE},format=rgb24", target + ".png", "rgb24")
        if "normal" in src:
            ffmpeg([src["normal"]], f"[0]{SCALE},format=rgb24", target + "_Normal.png", "rgb24")
        if "rough" in src:
            ffmpeg([src["rough"]],
                   f"[0]{SCALE},format=gray,geq=lum='255*(1-{LINEAR})'[g];"
                   f"color=black:s={SIZE}x{SIZE},format=rgb24[k];[k][g]scale2ref[k2][g2];[k2][g2]alphamerge",
                   target + "_Gloss.png", "rgba")
        if "emission" in src:
            ffmpeg([src["emission"]], f"[0]{SCALE},format=rgb24", target + "_Emission.png", "rgb24")
        print("PREPARED", name, sorted(maps))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
