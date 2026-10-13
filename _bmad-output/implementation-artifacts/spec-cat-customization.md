# Spec: cat customization (per-part skins)

Status: implemented 2026-10-13 (branch `claude/avec-le-better-0acddf`). Asked by Poyo: "le système de personnalisation
(on peut switch chaque partie du chat) et le menu de personnalisation (pour l'instant dispo que dans le menu
principal)", mockups first.

## Decisions (mockups `tools/maquettes/personnalisation/`, Poyo's answers in `retours.txt`)

- Screen: **C1** (v2): the cat on the left, tabs Pelage / Tenue / Yeux / Bouche on top of a 4-column vignette grid
  that scrolls (room for many future cosmetics). E2 (grimoire page) refused.
- Way in: **B2** (v1): the player's cat stands in the main menu, a click on it opens the screen.
- Network: **R1** (v2): every player sees everyone's cat in game.
- Art: "fais le maximum": Claude exported the artist's Drive (`Corrupted_Cats.blend`) himself.

## Art pipeline (reproducible)

- Source: artist's Drive folder "Cats" (4 skins: Black and White, Fiery, Galactic, Mage). The game had only Black and White.
- Blender (headless): the cape bones of the Fiery rig grafted onto the game's `BW Rig` (same rest pose), every new
  part re-rigged / re-parented on it, exported as `Assets/Art/Models/Cat_Cosmetics.fbx` (bones identical to
  `Cat_Animations_v1.fbx` up to a global offset, checked bone by bone).
- Textures: 8-bit PNG in `Assets/Art/Textures/Cat/Cosmetics/` (two Drive files were EXR named `.png`; the mouths' blue
  chroma key turned into alpha). Speckles = emission maps. Vignettes rendered per part in Blender
  (`Assets/Art/Sprites/CatCosmetics/`).
- `Tools/Cat Cosmetics/Rebuild assets` (`CatCosmeticsAssetBuilder`): materials (copies of the cat's own CatDitherLit
  materials), FBX material remap, catalog, `CatAppearance` on `Cat_Avatar`, `PlayerAvatar._catAppearance`.
  `Wire main menu`: the `CatCustomization` object of `MainMenu.unity`. `Render looks to Temp`: a check without play mode.
- Missing on the Drive (to ask the artist): the 6 "Jake" mouth frames (lip-sync) used by the Galactic / Mage mouth
  materials, `Button Texture.png`. Animations of the Drive (`Cat_Animations 2.blend`: Idle, Crying, Accusing, Chained…)
  are not exported yet: out of scope here.

## Code

- `CatLook` (Domain): one byte id per slot, packed in a `uint` (network) / "f.o.e.m" text (preferences), unknown id →
  catalog default (`Sanitized`).
- `CatCosmeticsCatalog` (SO, `Assets/ScriptableObjects/Avatars/`): items (stable id, slot, name, icon, fur material or
  part names). Ids are never changed or reused.
- `CatAppearance` (on `Cat_Avatar`): binds the cosmetics model's parts onto the cat once (skinned meshes take the cat's
  bones by name, static parts hang on the same-named bone, missing cape bones grafted), then `Apply` switches parts by
  **GameObject active** (never `Renderer.enabled`: owned by `AvatarVisibilityController`) and sets the fur material.
- `PlayerAvatar.CatLookPacked` (server-written NetworkVariable) + `SubmitCatLookRpc` (owner only, sanitized on the
  server); the owner sends its saved look when its avatar spawns (a rejoin spawns a new avatar: sent again).
- `CatLookStore`: PlayerPrefs `cdp.catLook`; `UseForThisSession` for autoplay processes sharing one PC.
- Menu: `CatPreviewStage` (the Cat_Avatar model stripped of its gameplay components, own camera + light, transparent
  RenderTexture), `CatCustomizationController` + `CatCustomization.uxml/uss`, `MainMenu.CreateMenuCat` (RawImage +
  button + line), menu content hidden while a UITK screen is open (same as the role book).

## Tests

- EditMode `CatLookTests`, `CatCosmeticsCatalogTests` (catalog coherence, every part exists, dressing the real
  Cat_Avatar shows exactly the chosen parts skinned on its own bones, PlayerAvatar wiring).
- PlayMode `AvatarSpawnTests.Avatar_CatLook_FromTheOwner_ReplicatesToEveryPeer_AndOthersCannotChangeIt` (red without
  the sender check).
- Autoplay levers `cat-custo` (menu tour by real input) and `cat-look` (per-process look).
