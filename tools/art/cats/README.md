# Cat art from the artist's Drive

Source: the artist's Google Drive folder "Cats" (`Corrupted_Cats.blend`, `Animations/`, `Textures/`, `References/`,
`Deadlines/`), public. Keep its folder layout when downloading: the `.blend` files resolve textures, the animation
file's library and each other relatively. The asset tracker and references are copied in
`_bmad-output/art-references/cats/` (the tracker states what each animation is for).

## 1. Cosmetics (skins, parts, expressions, tears)

`blender -b Corrupted_Cats.blend --python export_cosmetics.py -- <repo>/Assets/Art/Models/Cat_Cosmetics.fbx`

On the game's skeleton (`BW Rig` + the Fiery rig's cape bones): the Fiery / Galactic / Mage parts, the Chef outfit
(apron, shirt, collar, bow tie, sleeves: the file leaves the bow tie and the sleeves unrigged, so the bow tie hangs on
`Spine3` and each sleeve vertex follows the nearest arm bone of its side), the black and white
eyes' blink mesh (`BW Eyes Closed`) and a copy keeping their `Angry` shape key (`BW Eyes Expressive`), the universal
eyes (`Simple`, `Bored`, `Full Button`: flat shapes at the origin in the file, placed one per side on the fiery cat's
plain eyes, mirrored), the universal mouths (`Choked`, `Drunk`, `Sad`: the mage mouth card with each texture), the
tears' emitter (Head bone) and card (from `Animations/Cat_Animations 2.blend`).

## 2. Animations

`blender -b "Animations/Cat_Animations 2.blend" --python export_animations.py -- <repo>/Assets/Art/Models/Cat_Animations_v2.fbx`

Skeleton-only FBX, one take per action: Idle, Wave (the full one, from the older `Cat_Animations.blend`), Crying,
Accusing, Accused Idle, Getting Chained, Getting Chained 2, Chained Idle, Chained Struggle, Pulls Tablet Out, Look at
Tablet Idle, Putting Table Away, Fiery Idle Test.

## 3. Textures and vignettes

- Textures → `Assets/Art/Textures/Cat/Cosmetics/Cat_<Skin>_<Part>.png`, 8-bit PNG saved by Blender with the
  *Standard* view transform (`Fiery Body Texture.png`, `Galactic Specles Texture.png` and `Mage Mouth.png` are EXR in
  disguise, `Chef Clothes Texture.png` is 16-bit):
  `blender -b --python convert_texture.py -- <Drive texture> <Cat_<Skin>_<Part>.png> [--key]`.
  Mouths (skins and universal): `--key`, the blue background (≈ 0, 0, 255) becomes alpha (distance to the key,
  ramp 40 → 100 / 255).
- Vignettes of the Blender-made parts: `blender -b Corrupted_Cats.blend --python render_layers.py -- <layers>`, then
  `python crop_vignettes.py <layers> Assets/Art/Sprites/CatCosmetics [outfit_chef mouth_mage …]` crops each layer to
  its part (boxes of `tools/maquettes/personnalisation/cat.js`, `BOX`, offset +205 / +220 in the 800 × 1000 render,
  fitted with a 0.92 margin) at 256 × 240 → `CatIcon_<Part>_<Skin>.png`. The parts assembled at export (universal
  eyes / mouths) get theirs rendered by Unity (`CatIcon_<Slot>_<id>.png`).
- The mouth cards of the file switch textures with a Value node (the artist previews other mouths on them):
  `render_layers.py` puts each back on its own texture.

## 4. Unity

- `Tools > Cat Cosmetics > Rebuild assets`: materials (copies of the cat's CatDitherLit materials), FBX remap, catalog,
  CatAppearance / CatBlink / CatFrown / CatTears on `Cat_Avatar`, PlayerAvatar wiring, missing vignettes.
- `Tools > Cat Cosmetics > Rebuild animations`: clip settings, the `Cat_Avatar` Animator (rebuilt from scratch), the
  emote wheel's entries.
- `Wire main menu` / `Wire game scene` once. Opening a scene in batchmode recomputes layout-driven RectTransform sizes
  (ContentSizeFitter → 0): commit only the wiring lines, never those.
- `Render looks to Temp`: every skin, mix and item rendered without play mode (`Temp/CatLooks`).
- A rebuild also rewrites the existing cat materials and re-renders every Unity-made vignette: commit only what the new
  art changed (`git checkout` the rest, then refresh the editor before running the tests).

## Not on the Drive / not usable

`Button Texture.png` (button eyes: flat colours instead), the 6 "Jake" lip-sync mouth frames, the tablet prop (a grey
placeholder cube in the animation file: the cat mimes the tablet), the hair particles of the fiery fur collar.

Blender traps: drivers force some objects' `hide_render` (remove them before hiding); render one part correctly
occluded with the others as `is_holdout`; shape keys survive the FBX export only on a mesh without modifiers.
