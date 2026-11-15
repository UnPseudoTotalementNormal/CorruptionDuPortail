# Props from the artist's Drive

Source: the artist's Google Drive folder (`Corrupted - Assets.blend` + `Textures/`, board task T33,
https://drive.google.com/drive/folders/183xXMqQ7lBPT0q9MAM05Y0Li0Xw6MJlo). Download it with `gdown --folder` into an empty
folder, keep its layout: the `.blend` resolves its textures relatively.

## 1. Models

`blender -b --disable-autoexec "Corrupted - Assets.blend" --python export_props.py -- <repo>/Assets/Art/Models/Props`

One FBX per object, same names at each run: Unity keeps every reference. The file's real sizes are kept. The chain is
one link: `CatChains` lays the links itself. The timer is exported but unused for now (objets-alex v1 H0).

## 2. Textures

`python prepare_textures.py <drive folder> <repo>/Assets/Art/Textures/Props` (ffmpeg): `Prop_<Object>.png`, 8-bit, 2048 at
most (the Drive has 16-bit PNGs and EXRs named `.png`, up to 270 MB). From the maps of the `.blend`'s nodes, when there are
some: `_Normal`, `_Gloss` (URP metallic-smoothness, smoothness = 1 - roughness in alpha) and `_Emission`; the board's alpha
map goes in its base map's alpha.

## 3. Unity

`Tools > Props > Rebuild assets` (`PropsAssetBuilder`): URP Lit materials with the values of the `.blend`'s nodes, FBX
remaps, one prefab per power object (`Assets/Prefabs/PowersBar/3DModels/PowerObject_*`: identity root, collider on layer
3DPhysical, size and tilt under it) and per scene prop (`Assets/Prefabs/Props/`).

A new material in the file: add its line in `Looks` (the rebuild logs an error for a slot without a look); its `_Normal`,
`_Gloss` and `_Emission` maps are picked up by name. The rebuild also assigns each power its object (`PowerAssignments`)
and wires the chair and the chain on `PlayerAvatar`.

**Who gets which object = the artist's asset tracker** (`Corrompu_Suivi_Assets_3D.xlsx`, linked in the T33 thread: one
3D-model row per role's power-up, with its reference image). Its rows are also in `PropsAssetsTests.TrackerRows`. Not in
it: the Oracle and the Jailer (objects kept from the mockups objets-alex v1); the Doorman's bell row pairs with the
Messenger's animation row.

`Tools > Props > Wire game scene` (once): the table, the power row, the floor, the candle. Opening a scene in batchmode
recomputes layout sizes: commit only the objects it changed, `python merge_scene.py <old scene> <saved scene> <ids…>`.

## Not on the Drive

- `Leaves.jpg`, `LeafB2.jpg` (leaf cards of the Dryad's heart, outside the Drive folder in the artist's machine): the
  heart is flat gold, square cards, until they come.
- `wood_cabinet_worn_*` (the chair's roughness and normal): flat roughness instead.
- Luma's object (the file's `Card` has no texture, it is not exported): the default model. The power-ups' animations
  of the asset tracker were not delivered.
- Ugës's wooden board is the stand of his copied powers in the tracker, not a power's object: exported, not used yet.
