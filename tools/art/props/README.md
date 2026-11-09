# Props from the artist's Drive

Source: the artist's Google Drive folder (`Corrupted - Assets.blend` + `Textures/`, board task T33,
https://drive.google.com/drive/folders/183xXMqQ7lBPT0q9MAM05Y0Li0Xw6MJlo). Download it with `gdown --folder` into an empty
folder, keep its layout: the `.blend` resolves its textures relatively.

## 1. Models

`blender -b --disable-autoexec "Corrupted - Assets.blend" --python export_props.py -- <repo>/Assets/Art/Models/Props`

One FBX per object, same names at each run: Unity keeps every reference. The file's real sizes are kept. The chain is
one link: `CatChains` lays the links itself. The timer is exported but unused for now (objets-alex v1 H0).

## 2. Textures

Copied as they are into `Assets/Art/Textures/Props/Prop_<Object>.png` (8-bit sRGB PNG, 2048).

## 3. Unity

`Tools > Props > Rebuild assets` (`PropsAssetBuilder`): URP Lit materials with the values of the `.blend`'s nodes, FBX
remaps, one prefab per power object (`Assets/Prefabs/PowersBar/3DModels/PowerObject_*`: identity root, collider on layer
3DPhysical, size and tilt under it) and per scene prop (`Assets/Prefabs/Props/`).

A new material in the file: add its line in `Looks` (the rebuild logs an error for a slot without a look). The rebuild also
assigns each power its object (`PowerAssignments`) and wires the chair and the chain on `PlayerAvatar`.

`Tools > Props > Wire game scene` (once): the table, the power row, the floor, the candle. Opening a scene in batchmode
recomputes layout sizes: commit only the objects it changed, `python merge_scene.py <old scene> <saved scene> <ids…>`.

## Not on the Drive

- `Leaves.jpg`, `LeafB2.jpg` (leaf cards of the Dryad's heart, outside the Drive folder in the artist's machine): the
  heart is flat gold, square cards, until they come.
- `wood_cabinet_worn_*` (the chair's roughness and normal): flat roughness instead.
- The Robot's and Luma's objects (the file's `Card` has no texture, it is not exported): they keep the default model. The
  power-ups' animations of the asset tracker were not delivered.
