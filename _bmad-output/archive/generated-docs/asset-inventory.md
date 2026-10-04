# Asset Inventory

> Counts exclude `.meta` companion files. Sourced from `Assets/` at scan time. Counts that exclude Library/PackageCache imports.

## Top file-type tally (under `Assets/`)

| Ext | Count | Category | Notes |
|---|---:|---|---|
| `.cs` | 670 | Code | Total — includes editor, runtime, tests, samples. `Scripts/`: ~344 (~272 runtime incl. the 19-type `Domain/` POCO core, ~72 tests) |
| `.shadersubgraph` | 145 | Shaders | Sub-graphs for ShaderGraph |
| `.png` | 132 | 2D art | UI / cards / portraits |
| `.prefab` | 121 | Prefabs | Reusable scene objects |
| `.asset` | 101 | ScriptableObjects + Unity assets | Includes role data |
| `.mat` | 92 | Materials | |
| `.shadergraph` | 80 | Shaders | ShaderGraph |
| `.asmdef` | 31 | Assemblies | Incl. auto-imported package asmdefs + the new `CorruptionDuPortail.Domain` |
| `.dll` | 26 | Native / managed plugins | FMOD, etc. |
| `.so` / `.a` | 25 / 22 | Native libs | Cross-platform binaries |
| `.fbx` / `.FBX` | 24 + 18 | 3D models | |
| `.jpg` | 19 | 2D art | |
| `.shader` | 18 | Shaders | HLSL |
| `.unity` | 9 | Scenes | 5 in `Scenes/` + samples |
| `.txt` | 8 | Text | |
| `.exr` | 8 | HDR textures / lightmaps | |
| `.hlsl` / `.cginc` | 6 / 4 | Shader includes | |
| `.json` | 5 | Configs | |
| `.uxml` / `.uss` | 4 / 4 | UI Toolkit | |
| `.terrainlayer` | 4 | Terrain | |

## Prefabs (121 total — by folder)

| Folder | Role |
|---|---|
| `Prefabs/CardEffects/` | Card VFX prefabs |
| `Prefabs/ChatSystem/` | Chat UI prefabs |
| `Prefabs/InfoTableUI/` | Smartphone info tables |
| `Prefabs/Lobby/` | Lobby UI |
| `Prefabs/NoteSystem/` | Note UI |
| `Prefabs/Powers/` | Power-bound prefabs |
| `Prefabs/PowersBar/` | Power bar UI + `3DModels/` |
| `Prefabs/RoleAttribution/` | Role picker UI |
| `Prefabs/Settings/` | Settings panels |
| `Prefabs/SpawnPanels/` | Spawn-time panels |
| `Prefabs/StateUI/` (+ `AwakeningRecap/`) | Per-game-state UI roots |
| `Prefabs/Tooltip/` | Tooltip templates |

## ScriptableObjects

Total: 47 `.asset` files under `Assets/ScriptableObjects/`.

### Characters (18 character SOs)

`Assets/ScriptableObjects/Characters/`:
- `Abyss`, `Croupiere`, `DrGloubi`, `Dryade`, `Gardien`, `Geolier`, `Incomplet`, `MageOcculte`, `Messager`, *(+ remaining)*

> Each character SO is an instance of `RoleDataObject` (see `Characters/RoleDataObject.cs`). Designers tune balance here without code changes.

## 2D art (under `Assets/Art/Sprites/`)

| Subfolder | Content |
|---|---|
| `Cards/` | Standard card sprites |
| `Cards(tarot)/` | Alternate tarot art |
| `Portraits/` | Character portraits |
| `TMP_SPRITEASSETS/` | Sprite assets for TextMeshPro |
| `UI/` | Generic UI sprites |

Loose top-level sprites: `Arriere_plan_du_jeu`, `Arrow`, `ChainedOverlay`, `InnerArrow`, `MeIcon`, `RondFlou`, `Square`, `Symbole_Corruption`, `FORMAT_YOUTUBE_DEF_14` (marketing).

## 3D models

`Assets/Art/Models/` — `.fbx` / `.FBX` (~42 files combined). Includes:
- Card 3D models (via `Prefabs/PowersBar/3DModels/`)
- Stage / scene-dressing meshes

## Materials & shaders

- `Assets/Art/Materials/` — 92 `.mat` (incl. `CardEffects/`, `Tablet/`)
- `Assets/Art/Shaders/` + `Assets/Shaders/` — 80 `.shadergraph`, 145 `.shadersubgraph`, 18 `.shader`, 6 `.hlsl`, 4 `.cginc`, 8 `.exr`

## Audio (FMOD)

Single banks live at `Assets/FMODBanks/Desktop/`:
- `Master.bank`
- `Master.strings.bank`

> **No `AudioSource`-driven gameplay sound.** Entry point: `Assets/Scripts/AudioSystem/GameAudioManager`.

Also under `Assets/Art/Audio/Music/` for music source files.

## Streaming + resources

- `Assets/StreamingAssets/` — FMOD banks again (mirrored as `Master.bank` / `Master.strings.bank` for runtime streaming).
- `Assets/Resources/` — `DOTweenSettings.asset` + `Prefabs/` (legacy `Resources.Load` fallbacks).

## Scenes (`Assets/Scenes/` — 5 files)

| File | Role |
|---|---|
| `BootScene.unity` | Bootstrap |
| `MainMenu.unity` | Main menu |
| `GameScene.unity` | Active gameplay |
| `(old)MenuScene.unity` | Legacy / reference |
| `GameScene_backup.unity` | Backup |

Other `.unity` files (4) come from imported package samples.

## Plugins / native

- `Assets/Plugins/` — 26 `.dll`, 25 `.so`, 22 `.a` (FMOD, Steam, Facepunch transport, etc.).
- `allowUnsafeCode: true` in `Game.asmdef` reflects native interop needs.

## Addressables

`Assets/AddressableAssetsData/` holds Addressables settings. Runtime loading is configured via Unity Services + Addressables 2.7.2 (+ Android variant 1.0.6).
