# Cat cosmetics from the artist's Drive

Source: the artist's Google Drive folder "Cats" (`Corrupted_Cats.blend`, `Animations/`, `Textures/`), public. Keep its
folder layout when downloading: the `.blend` files resolve textures and the animation file's library relatively.

1. `blender -b Corrupted_Cats.blend --python export_cosmetics.py -- <repo>/Assets/Art/Models/Cat_Cosmetics.fbx`
2. Textures → `Assets/Art/Textures/Cat/Cosmetics/Cat_<Skin>_<Part>.png`, 8-bit PNG saved by Blender with the
   *Standard* view transform (`Fiery Body Texture.png` and `Galactic Specles Texture.png` are EXR in disguise).
   Mouths: the blue background (0, 0, 255) is a chroma key → alpha (distance to the key, ramp 40 → 100 / 255).
3. Vignettes: `blender -b Corrupted_Cats.blend --python render_layers.py -- <out>`, then crop each layer to its part
   (boxes in `tools/maquettes/personnalisation/cat.js`, `BOX`, offset +205 / +220 in the 800 × 1000 render) at
   256 × 240 → `Assets/Art/Sprites/CatCosmetics/CatIcon_<Part>_<Skin>.png`.
4. Unity: `Tools > Cat Cosmetics > Rebuild assets` (materials, FBX remap, catalog, prefabs), `Wire main menu` once,
   `Render looks to Temp` to check without play mode.

Blender traps: drivers force some objects' `hide_render` (remove them before hiding); render one part correctly
occluded with the others as `is_holdout`.
