# RAPPORT — T4 (dossier B)

## Livrable

- `Assets/_ModelTest/B/T4/ANALYSE.md` : analyse priorisée du code UI Toolkit existant (bugs, API obsolètes ou expérimentales,
  performance, hygiène), avec les corrections proposées.
- Aucun fichier C# produit, donc le namespace `ModelTest.B.T4` n'est utilisé nulle part. Aucune classe USS créée, donc le préfixe `B_`
  n'a pas servi. Les extraits de code de l'analyse sont indicatifs et ne sont pas des fichiers du projet.
- **Aucun fichier du projet n'a été modifié.** Rien n'a été compilé ni testé : pas d'éditeur Unity disponible.

## Version Unity

`ProjectSettings/ProjectVersion.txt` : **6000.5.0f1**. `CLAUDE.md` mentionne `6000.2.6f2` ; cet écart est signalé dans l'analyse.

## API UI Toolkit citées (dans le code analysé ou dans les corrections proposées)

| API | Statut |
|---|---|
| `UIDocument.rootVisualElement`, `UIDocument.panelSettings` | certain |
| `PanelSettings.targetTexture`, `PanelSettings.SetScreenToPanelSpaceFunction` | certain |
| `PanelSettings` « Clear Color » (champ sérialisé `m_ClearColor`, propriété `clearColor`) | incertain (nom exact de la propriété C#) |
| `PanelTextSettings`, `PanelSettings.textSettings` | certain pour l'existence ; incertain pour les polices de secours par défaut |
| `VisualElement.Q<T>(name)`, `Q(name, className)`, `Query<T>(className:).ForEach` | certain |
| `VisualElement.Add/Insert/Clear/RemoveFromHierarchy/BringToFront`, `childCount`, indexeur `this[int]` | certain |
| `VisualElement.Children()` (retourne `IEnumerable<VisualElement>`) | certain ; allocation de l'énumérateur boxé : incertain (probable) |
| `AddToClassList/RemoveFromClassList/EnableInClassList/ClassListContains` | certain |
| `VisualElement.pickingMode` / `PickingMode` | certain |
| `VisualElement.tooltip` (non affiché sur un panel runtime) | incertain (probable ; le code du projet le constate déjà dans LobbyRoles) |
| `VisualElement.schedule.Execute(...)`, `IVisualElementScheduledItem.ExecuteLater/Every/Pause/Resume`, `TimerState.deltaTime` | certain |
| Sémantique « `ExecuteLater` annule l'exécution programmée précédente » | incertain (probable) |
| `VisualElement.experimental.animation.Start(...).Ease(Easing.OutCubic)` (namespace `Experimental`) | certain pour l'existence ; statut expérimental |
| `RegisterCallback/UnregisterCallback` : `ClickEvent`, `PointerDownEvent`, `PointerEnterEvent`, `PointerLeaveEvent`, `GeometryChangedEvent`, `CustomStyleResolvedEvent` | certain |
| `CallbackEventHandler.RegisterCallbackOnce<T>` | incertain (non utilisé dans les correctifs principaux) |
| `TrickleDown.TrickleDown` | certain |
| `Button.clicked`, `Button(Action)`, `SetEnabled` | certain |
| `Label`, `TextElement.text`, `TextElement.enableRichText` (vrai par défaut) | certain |
| `ScrollView(ScrollViewMode)`, `contentContainer`, `scrollOffset`, `verticalScroller.highValue/value` | certain |
| `IStyle` inline : `backgroundColor`, `border*Color/Width/Radius`, `color`, `opacity`, `display`, `position`, `left/top/right/bottom`, `translate`, `scale`, `width/height`, `flexGrow/flexShrink`, `backgroundImage` | certain |
| `StyleKeyword.Null / None / Undefined`, `new StyleBackground(Sprite/Texture2D)`, `new StyleBackground()` | certain pour l'existence ; incertain pour la sémantique exacte de `new StyleBackground()` (valeur « vide » vs retrait de l'inline) |
| `StyleTranslate(new Translate(x, y))`, `StyleScale(new Scale(Vector2))`, `Length(…, LengthUnit.Percent)` | certain |
| `IStyle.unityBackgroundScaleMode` (dépréciée) | incertain pour le niveau exact d'obsolescence (probable : dépréciée depuis 2022.2) |
| `IStyle.backgroundSize / backgroundRepeat / backgroundPositionX/Y`, types `BackgroundSize`, `BackgroundRepeat`, `BackgroundPosition` | certain pour l'existence ; incertain pour les signatures exactes des constructeurs |
| `BackgroundPropertyHelper` (conversion ScaleMode → background-*) | incertain (seulement mentionné, pas recommandé en priorité) |
| `MeshGenerationContext.painter2D`, `Painter2D.Arc/Fill/Stroke/BeginPath/MoveTo/LineTo/ClosePath`, `Angle`, `AngleUnit`, `ArcDirection`, `generateVisualContent`, `MarkDirtyRepaint` | certain |
| `[UxmlElement]` / `[UxmlAttribute]` (Unity 6) | certain |
| `CustomStyleProperty<Color>` + `customStyle.TryGetValue` | certain pour l'existence ; incertain pour la lecture d'une variable `--xxx` définie sur `:root` depuis un élément enfant |

## Propriétés USS citées

| Propriété USS | Statut |
|---|---|
| `opacity`, `display`, `position`, `left/top/right/bottom`, `width/height`, `min-height`, `max-width/max-height` | certain |
| `flex-grow/flex-shrink/flex-basis/flex-direction/flex-wrap`, `align-items`, `justify-content` | certain |
| `background-color`, `border-*-width/color/radius`, `padding`, `margin`, `color`, `font-size`, `letter-spacing`, `white-space` | certain |
| `-unity-font-style`, `-unity-text-align`, `-unity-text-outline-width/color`, `-unity-font-definition` | certain |
| `-unity-font-definition` pointant vers un `.ttf` (au lieu d'un FontAsset) | incertain (utilisé tel quel par le projet ; non modifié) |
| `translate`, `scale`, `rotate`, `transform-origin` | certain |
| `transition-property/duration/timing-function/delay` | certain |
| `transition-duration: var(--…)` | incertain (utilisé par le projet ; comportement non vérifié) |
| `-unity-background-scale-mode` (dépréciée) | incertain pour le niveau exact d'obsolescence (probable) |
| `background-size` (`contain`, `cover`, `100% 100%`), `background-repeat: no-repeat`, `background-position-x/y: center` | certain pour les propriétés ; incertain pour la syntaxe exacte des mots-clés |
| `var(--…)`, `:root`, pseudo-classes `:hover`, `:disabled`, `:active` | certain |
| `@import url("unity-theme://default")` dans un `.tss` | certain |
| Règle de cascade « à spécificité égale, la dernière règle déclarée gagne » (base du bug B2) | certain |

## Hypothèses

1. Le périmètre « code UI Toolkit du projet » se limite à `Assets/Scripts`, `Assets/UI` et aux PanelSettings. Les plugins et samples tiers
   (SerializedCollections, TimeRecorder, Samples URP/ShaderGraph, linework-lite) sont exclus parce que l'équipe ne les maintient pas.
2. Les messages anonymes (`MessageInfo.message`) sont du texte libre saisi par les joueurs, sans filtrage serveur : aucun assainissement
   trouvé dans `Assets/Scripts`.
3. Aujourd'hui, les apps de la tablette ne sont pas activées ou désactivées via `SetActive` (bascule via `SmartphoneApp.IsOpen`), donc R1
   est jugé latent et non actif.
4. Dans `Update()`, Input System peut rapporter `wasPressedThisFrame` et `wasReleasedThisFrame` à `true` dans la même frame lors d'un tap
   très bref. C'est le déclencheur de B1 pour l'EmoteWheel.
5. Désactiver puis réactiver un `UIDocument` recrée son `rootVisualElement` (base de R1).

## Limites connues

- Analyse **statique uniquement** : rien n'a été compilé, exécuté ni profilé. Les points marqués [à vérifier] dans l'analyse (R3, P6, H2 bis,
  H7, coût des RT) demandent une vérification en Play Mode ou au Profiler.
- Je n'ai pas pu confirmer, sans documentation locale, le niveau exact d'obsolescence de `unityBackgroundScaleMode` en 6000.5, la couverture
  des glyphes de LiberationSans, ni le comportement exact des tooltips runtime en 6000.5.
- Les extraits de correction sont des esquisses : noms de champs et intégration à adapter, et à compiler avant usage.
- Les chiffres d'allocation et de complexité (P3) sont déduits de la lecture du code, pas mesurés.
- Les problèmes uGUI/TMP voisins (par exemple l'affichage des messages anonymes hors UITK, ou le désabonnement de `NoteChoosePanel` si
  l'objet est détruit sans `ClosePanel`) sont hors périmètre et ne sont signalés qu'au passage.
