# Plan — Cartes du picker au-dessus du flou (verre dépoli)

## Objectif

Quand le `CardPickerManager` ouvre un picker (rôle **ou** personnage), les cartes
concernées doivent apparaître **nettes, au-dessus du voile flou** (`_BackgroundBlurSource`),
pendant que le reste du plateau (3D + canvases World Space) reste flouté derrière.

Contrainte absolue : ne casser aucun autre visuel.

## Décisions validées (questions posées)

1. **Méthode** : caméra overlay dédiée empilée (approche d'origine du user).
2. **Portée cartes** : role picker **et** character picker montent au-dessus du flou.
3. **Portée voile** : pour l'instant uniquement le card picker, mais le mécanisme doit
   rester **générique / réutilisable** pour d'autres UI ou objets plus tard.
4. **Outline survol** : acceptable de la perdre sur les cartes montées (FreeOutline reste
   sur la base cam).

## Architecture actuelle (rappel)

Ordre de composition réel (arrière → avant) :

1. **Base cam `CameraBrain`** (depth -1, pilotée par CinemachineBrain, FOV/transform
   dynamiques) — rend le plateau 3D + canvases World Space des cartes (layer 0).
   `BackgroundCaptureFeature` capture la couleur à `AfterRenderingTransparents`, applique
   le flou gaussien, écrit `_BackgroundBlurSource`. Culling mask = `0xE0000077`.
2. **Overlay cam `739575389`** (depth 0, Don't Clear) — rend le layer 8 (Phone).
3. **`InstructionCanvas`** en **Screen Space - Overlay** (`m_RenderMode: 0`) — contient
   l'Image de voile (`Background`, shader `Custom/UI_Blur`) **et** le texte d'instruction.
   Composité APRÈS toutes les caméras → c'est le plus au-dessus.

Clic carte : via **GraphicRaycaster** sur les canvases World Space de la carte
(`m_Camera: 0` → fallback `Camera.main`). Le `PhysicsRaycaster` de la base cam
(eventMask `128` = layer 7 « 3DPhysical ») ne sert pas à sélectionner la carte.

## Le verrou à comprendre

Un Screen Space - Overlay canvas est composité **après tout le stack de caméras**.
Donc **aucune** caméra empilée ne peut dessiner par-dessus lui. Tant que le voile est en
Overlay, impossible de mettre des cartes au-dessus.

→ Le voile **doit quitter le mode Overlay** et entrer dans le stack, pour qu'une caméra
picker puisse être empilée *après* lui.

## Architecture cible

Nouveau stack de la base cam (arrière → avant) :

```
1. Base cam (CameraBrain)   : plateau 3D + canvas world. Capture → flou.
                              Culling mask EXCLUT le layer 'AboveBlur'.
2. FrostCamera   (overlay)  : rend FrostCanvas (Screen Space - Camera) = le voile
                              qui sample _BackgroundBlurSource. Clear depth.
3. AboveBlurCamera (overlay): rend UNIQUEMENT le layer 'AboveBlur' (cartes montées).
                              Don't clear color, clear depth. Lentille synchro base.
4. PhoneCamera (739575389)  : inchangé, reste en haut du stack.
+ InstructionCanvas (Overlay): garde le TEXTE d'instruction seul → au-dessus de tout.
```

Pourquoi ça marche :
- Le voile (FrostCamera) est rendu *après* la base → jamais capturé par
  `AfterRenderingTransparents` → **pas de feedback loop** (le bug du voile noir).
- Les cartes montées sont sur le layer `AboveBlur`, **exclu de la base cam** → absentes
  du flou → redessinées nettes par AboveBlurCamera *après* le voile.
- Clic carte = GraphicRaycaster via `Camera.main` (base), **indépendant du layer de rendu
  et du culling**. Tant que AboveBlurCamera a la même vue que la base, ce que le joueur
  voit coïncide avec ce que le raycaster teste → **clic préservé sans rien changer à
  l'input**.

## Références scène concrètes (vérifiées)

- Layer libre → **`AboveBlur` = layer 9** (`TagManager.asset` : libres = 3, 9–27).
- Voile = GameObject **`Background`** (fileID `981665434`), **1er enfant** de
  `InstructionCanvas`. Composants : RectTransform `981665435`, CanvasRenderer `981665437`,
  Image `981665436` (material `Custom_UI_Blur`, guid `6d3dee56b48f98840ba1387621606437`).
  → c'est ce GO à déplacer vers FrostCanvas.
- `instructionCanvasGroup` = CanvasGroup `36138300` sur la **racine** `InstructionCanvas`
  → fade aujourd'hui le voile inclus. Après split, FrostCanvas reçoit **son propre**
  CanvasGroup (le voile sort de la portée de `36138300`).
- 3 autres enfants d'`InstructionCanvas` (`962588891`, `1600648899`, `802840500`) =
  texte/titre/holders → **restent** dans InstructionCanvas (Overlay).
- Stack actuel base cam : `m_Cameras: [739575389]` (ligne 1542) → devient
  `[FrostCamera, AboveBlurCamera, 739575389]`.

## À valider AVANT de coder (2 spikes rapides)

1. **UGUI World Space dans une overlay cam** : créer un Cube + un canvas World Space sur
   un layer test, une overlay cam empilée qui ne rend que ce layer. Vérifier que le canvas
   ET le mesh s'affichent. (L'échec passé venait d'une caméra *séparée* hors-stack, pas
   d'une overlay empilée — à confirmer.) **Bloquant** : si non, basculer sur l'approche
   « passe RenderObjects dans la base cam ».
2. **Sync lentille Cinemachine** : confirmer que copier `baseCam.projectionMatrix` en
   LateUpdate (après le brain) aligne parfaitement une overlay cam parentée à `CameraBrain`.

## Étapes d'implémentation

### 1. Layer
- Ajouter un user layer `AboveBlur` (premier index libre, p.ex. 9). Voir
  `ProjectSettings/TagManager.asset`.
- Vérifier qu'il **n'est pas** dans le culling mask de la base cam (`0xE0000077` ne
  contient pas le bit 9 → OK par défaut).

### 2. Sortir le voile de l'Overlay → FrostCamera
- Créer GameObject **FrostCamera** (Camera + UniversalAdditionalCameraData en `Overlay`).
- Créer **FrostCanvas** en **Screen Space - Camera**, `renderCamera = FrostCamera`,
  layer dans le culling mask de FrostCamera.
- **Déplacer** l'Image `Background` (voile, shader `Custom/UI_Blur`, material
  `Custom_UI_Blur.mat`) depuis `InstructionCanvas` vers FrostCanvas. Le shader sample
  toujours la globale `_BackgroundBlurSource` → aucun changement shader.
- Donner à FrostCanvas son propre `CanvasGroup` pour le fade.
- Ajouter FrostCamera au stack de la base : `CameraBrain` →
  UniversalAdditionalCameraData `m_Cameras` = `[FrostCamera, AboveBlurCamera, 739575389]`
  (ordre = ordre de rendu).
- Screen Space - Camera remplit l'écran quel que soit le FOV → FrostCamera **n'a pas
  besoin** de sync lentille.

### 3. AboveBlurCamera
- Créer GameObject **AboveBlurCamera** (Camera + additional data `Overlay`).
- **Parenter à `CameraBrain`** (transform local identité) → hérite position/rotation
  pilotées par Cinemachine.
- Culling mask = **uniquement** `AboveBlur`. Clear : Don't clear color, clear depth.
- HDR/MSAA alignés sur la base ; post-process géré par la base (laisser off sur l'overlay).
- Nouveau script `MatchBaseCameraProjection` (sur AboveBlurCamera) :

```csharp
using UnityEngine;

[DefaultExecutionOrder(10000)] // après CinemachineBrain (LateUpdate)
[RequireComponent(typeof(Camera))]
public class MatchBaseCameraProjection : MonoBehaviour
{
    [SerializeField] private Camera baseCamera; // CameraBrain
    private Camera cam;

    private void Awake() => cam = GetComponent<Camera>();

    private void LateUpdate()
    {
        if (!baseCamera) return;
        cam.projectionMatrix = baseCamera.projectionMatrix; // FOV/lens Cinemachine
        // transform géré par le parenting sous CameraBrain.
    }
}
```

- L'ajouter au stack juste après FrostCamera (cf. étape 2).
- Optionnel perf : activer/désactiver AboveBlurCamera avec le picker (une overlay cam
  sur layer vide est peu coûteuse → on peut la laisser allumée pour simplifier).

### 4. CardPickerManager — monter / restaurer les cartes
Fichier : `Assets/Scripts/UI/BoardUI/CardPickerManager.cs`.

- Constante `ABOVE_BLUR_LAYER = "AboveBlur"`.
- Ref sérialisée vers le `CanvasGroup` de FrostCanvas (pour fade synchro).
- **Role picker** (`ShowRolePicker`) : après `BoardManager.AddNewCard(...)` et le setup,
  `_card.gameObject.SetLayerRecursively(ABOVE_BLUR_LAYER);` (les cartes spawnées ne
  passent jamais par le flou).
- **Character picker** (`ShowCharacterPicker`) : pour chaque carte soulevée, mémoriser son
  layer d'origine puis `SetLayerRecursively(ABOVE_BLUR_LAYER)`.
- **CancelPicker** : restaurer le layer d'origine des cartes character (les cartes role
  sont détruites de toute façon). Utiliser `GameObjectExtension.SetLayerRecursively`
  (déjà présent et testé).
- **Fade voile** : dans `ShowInstructionPanel` / `HideInstructionPanel`, faire un
  `DoShowGroup` / `DoHideGroup` sur le CanvasGroup de FrostCanvas en parallèle de
  `instructionCanvasGroup` (mêmes durées : `INSTRUCTION_FADE_DURATION`).

> Note clic : `SetLayerRecursively` déplace aussi le `BoxCollider` de la carte, mais la
> sélection passe par GraphicRaycaster (`Camera.main`), pas par le PhysicsRaycaster layer
> 7 → clic préservé. Vérifier en play mode quand même (spike input).

### 5. InstructionCanvas
- Reste en **Screen Space - Overlay**, ne contient plus que le texte (titre/description).
  → le texte d'instruction reste au-dessus des cartes montées (souhaité pour un header).
- Si jamais on veut les cartes au-dessus du texte : déplacer le texte sur un canvas de
  AboveBlurCamera. Pas nécessaire a priori.

## Réutilisabilité (décision 3)

Le mécanisme est générique :
- **Tout** GameObject déplacé sur le layer `AboveBlur` apparaît net au-dessus du voile.
- FrostCanvas peut être affiché par n'importe quel système (pas couplé au picker) : il
  suffit de fade son CanvasGroup.
- Garder des noms génériques (`AboveBlur`, pas `CardPicker`) pour réemploi futur (menus,
  mise en avant d'objets, etc.).

## Tradeoffs / points d'attention

- **FreeOutline** ne s'applique pas aux cartes sur `AboveBlur` (tourne sur la base cam) —
  accepté (décision 4).
- **1 frame de transition** possible quand le character picker déplace une carte du
  plateau vers `AboveBlur` (sort du flou capturé la frame suivante) — négligeable.
- **Sync lentille** : si CinemachineBrain change d'`UpdateMethod`, réviser l'exécution du
  script (doit tourner après le brain). Scène actuelle : `UpdateMethod: 2` (LateUpdate).
- **Stack URP** : le post-process s'applique une fois sur le résultat du stack (base) —
  ne pas activer le post sur les overlays.
- Deux overlays cams en plus = léger coût ; acceptable, mais profiler si besoin.

## Tests

- Role picker : cartes nettes au-dessus du voile, plateau flou derrière, clic sélectionne.
- Character picker : carte soulevée passe nette au-dessus, les autres restent floues, clic OK.
- Annulation : cartes character reviennent à leur layer/position, voile fade out, plateau net.
- Cinemachine : déclencher un blend/zoom pendant un picker → cartes montées restent alignées.
- Non-régression : Phone (layer 8) s'ouvre toujours correctement par-dessus ; aucun visuel
  cassé hors picker (voile masqué = scène normale).

## Rollback

- Remettre l'Image `Background` dans `InstructionCanvas` (Screen Space - Overlay).
- Retirer FrostCamera + AboveBlurCamera du stack, supprimer le layer `AboveBlur`,
  le script `MatchBaseCameraProjection`, et les ajouts au `CardPickerManager`.
- `BackgroundCaptureFeature` + shaders flou restent inchangés (indépendants de ce plan).
```