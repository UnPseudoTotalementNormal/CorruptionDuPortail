# T4 — Analyse du code UI Toolkit existant

Analyse statique (lecture seule) du code UI Toolkit du projet, hors `Assets/_ModelTest`,
`Assets/Samples` et `Assets/Plugins` (tiers). Aucun fichier n'a été modifié.

- **Version Unity** : `6000.5.0f1` (lue dans `ProjectSettings/ProjectVersion.txt`).
  Note : `CLAUDE.md` mentionne `6000.2.6f2`, ce qui contredit `ProjectVersion.txt`. Les
  vérifications d'API ci-dessous se basent sur `6000.5.0f1` (règle : source = ProjectVersion.txt).
- **Contrainte** : je n'ai ni éditeur ni compilateur. Rien n'a été testé. Les points marqués
  « incertain » demandent une vérification en éditeur.

## Fichiers analysés

| Fichier | Rôle |
|---|---|
| `Assets/Scripts/UI/RoleCard/RoleCardController.cs` | Carte de rôle consultable (UIDocument screen-space) |
| `Assets/Scripts/UI/Cards/RoleCardElement.cs` | Élément UITK réutilisable (face de carte) |
| `Assets/Scripts/UI/InfoTable/InfoTableUitkController.cs` | Grille de déduction (RenderTexture) |
| `Assets/Scripts/UI/InfoTable/InfoTableRtPresenter.cs` | Présentation panel → RenderTexture → RawImage |
| `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` | Grille d'attribution de rôles (RenderTexture) |
| `Assets/Scripts/UI/LobbyRoles/LobbyRolesRtPresenter.cs` | Présentation panel → RenderTexture → RawImage |
| `Assets/Scripts/UI/MessageJournal/MessageJournalController.cs` | Journal de messages (overlay) |
| `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs` | Roue d'emotes (Painter2D) |
| `Assets/Scripts/UI/Spike/SpikePanelController.cs` | Spike jetable |
| `Assets/Scripts/UI/Spike/SpikeRawImageRt.cs` | Spike jetable |
| `Assets/Scripts/UI/Spike/SpikeRenderTextureInput.cs` | Spike jetable |
| `Assets/Scripts/NoteSystem/NoteChoosePanel.cs` | Panneau de notes (uGUI, importe UIElements) |

Impression d'ensemble : code de bonne qualité, conventions homogènes (init gardée en
`OnEnable`+`Start`, Q-by-name, classes BEM, tokens en USS). Les problèmes ci-dessous sont surtout
des allocations récurrentes, quelques callbacks non désabonnés et une API potentiellement obsolète.

---

## Corrections priorisées

### P1 — À corriger (impact fonctionnel ou fuite/coût réel)

#### 1.1 `NoteChoosePanel` — désabonnements NoteManager perdus si l'objet est détruit sans `ClosePanel()`
`Assets/Scripts/NoteSystem/NoteChoosePanel.cs`
Les événements `NoteManager.instance.on*RolesByPlayerModified` sont abonnés dans `Init()` et
désabonnés **uniquement** dans `ClosePanel()` (l.132-134). `NoteManager` est un singleton
persistant ; si le GameObject est détruit par un autre chemin (changement de scène, fermeture
du parent, `Destroy` externe) sans passer par `ClosePanel()`, le délégué reste enregistré sur le
singleton et pointe vers un objet détruit → invocation sur objet mort / fuite.
**Correction** : déplacer les `-=` des trois événements dans `OnDestroy()` (ou `OnDisable()`) en
plus de `ClosePanel()`. **Certitude : élevée** (motif classique de fuite singleton).

#### 1.2 `NoteChoosePanel` — méthodes d'interface qui lèvent `NotImplementedException`
`Assets/Scripts/NoteSystem/NoteChoosePanel.cs` l.113-126
`SwitchPanelOpen()`, `TryOpenPanel()`, `OpenPanel()` (contrat `IPanelComponent`) lèvent
`NotImplementedException`. Si un appelant générique itère sur les `IPanelComponent` et appelle
l'une d'elles, crash runtime. **Correction** : implémenter a minima un no-op documenté ou lever
une exception plus explicite, ou retirer du contrat. **Certitude : moyenne** (dépend de l'usage
réel du contrat, non tracé ici).

#### 1.3 Spikes — mutation du `PanelSettings` **partagé** (asset) au lieu d'un clone
`Assets/Scripts/UI/Spike/SpikeRawImageRt.cs` (l.49,70) et `SpikeRenderTextureInput.cs` (l.52,63)
Les deux spikes écrivent `targetTexture` et `SetScreenToPanelSpaceFunction` directement sur
`_document.panelSettings`, c.-à-d. l'**asset partagé**. C'est exactement le piège que les versions
productives (`InfoTableRtPresenter`/`LobbyRolesRtPresenter`) corrigent en clonant via
`Instantiate(_sourcePanel)`. Effet : dirty de l'asset en éditeur et contamination de tout autre
UIDocument référençant le même asset. Ce sont des spikes annoncés « THROWAWAY », donc **faible
priorité en soi**, mais à supprimer/ne pas réutiliser. **Certitude : élevée.**

#### 1.4 `SpikeRenderTextureInput` — fuite de matériau instancié
`Assets/Scripts/UI/Spike/SpikeRenderTextureInput.cs` l.55-60
`_surfaceRenderer.material` instancie une copie du matériau (leak) qui n'est jamais détruite ;
`OnDisable` ne libère que la RenderTexture. **Correction** : utiliser `sharedMaterial` en éditeur,
ou détruire l'instance dans `OnDisable`. Spike jetable → **faible priorité**. **Certitude : élevée**
(comportement documenté de `Renderer.material`).

### P2 — Performance / allocations récurrentes

#### 2.1 `InfoTableUitkController.Render()` — allocations et coût par changement de modèle
`Assets/Scripts/UI/InfoTable/InfoTableUitkController.cs` l.408-456
`Render()` s'exécute à chaque `_model.OnChanged`. Points coûteux :
- `foreach (VisualElement seg in cell.Children())` (l.439) : `Children()` alloue un énumérateur
  pour **chaque** cellule, dans une double boucle joueurs×rôles, à chaque render. Les segments
  étant fixes (3 par cellule) et déjà créés en `AddSegment`, on peut stocker les références
  (`Label[3]` par cellule, ou `Label[][][]`) et éviter l'énumération.
- `Render()` repeint **toutes** les cellules même quand un seul état change ; envisager un rendu
  ciblé (cellule/ligne modifiée) si le nombre de cellules devient grand.
**Certitude : élevée** pour l'allocation de l'énumérateur ; **moyenne** pour l'impact réel
(dépend du nombre de joueurs/rôles et de la fréquence des changements).

#### 2.2 `LobbyRolesUitkController.Rebuild()` — reconstruction complète à chaque clic de stepper
`Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs`
Chaque `OnChanged` (donc chaque clic Max/Imposé/preset/onglet) fait `_root.Clear()` puis
reconstruit tout l'arbre : nouvelles `List`, `Dictionary`, tous les `RoleCardElement`, steppers,
tally, footer, `CompositionValidator.Validate` (l.148-209, 529-572). C'est un choix assumé
(commentaires explicites : préservation du scroll, animation d'opacité), mais c'est le point chaud
le plus lourd. Piste : diff ciblé (mettre à jour la valeur du stepper et l'opacité de la carte
concernée sans tout reconstruire). **Certitude : moyenne** (compromis documenté ; à valider au
profiler avant refonte).

#### 2.3 `RoleCardController.ApplyFactionTint()` — requête `Q()` du scrollbar à chaque ouverture
`Assets/Scripts/UI/RoleCard/RoleCardController.cs` l.369
`_root?.Q(null, "unity-scroller--vertical")?.Q("unity-dragger")` est ré-exécuté à chaque `Open()`.
Le dragger est stable après le premier build → le mettre en cache dans `TryInitialize`. Coût faible
(panneau on-demand), mais gratuit à corriger. **Certitude : élevée** (le résultat de Q ne change pas).

#### 2.4 `LobbyRolesUitkController.Update()` — boucle par frame jusqu'au premier build
`Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` l.118-121
`Update()` appelle `Rebuild()` chaque frame tant que `_everBuilt == false`. Fonctionnel (garde
`_initialized && !_everBuilt`), mais si le data source n'est jamais prêt, `Rebuild()` (qui
re-résout la racine et sort tôt) tourne à chaque frame. Impact faible ; envisager d'arrêter après
N tentatives ou de passer par le scheduler UITK. **Certitude : moyenne.**

### P3 — Callbacks / cycle de vie UITK

#### 3.1 Callbacks UITK jamais désabonnés (acceptable mais à noter)
Plusieurs contrôleurs enregistrent des callbacks sur des VisualElement sans jamais faire
`UnregisterCallback` :
- `RoleCardController` : `PointerDownEvent` sur `_root` (l.150), `closeButton.clicked` (l.136).
- `MessageJournalController` : `PointerDownEvent` sur `_root` (l.72).
- `EmoteWheelController` : `GeometryChangedEvent` par label (l.206), `generateVisualContent`
  (l.345).
- `InfoTableUitkController` : `ClickEvent` par cellule/segment (l.251,325).

Ce n'est pas une fuite tant que l'arbre visuel et le composant partagent la même durée de vie
(l'UIDocument possède l'arbre). **Mais** : `TryInitialize` est gardé par `_initialized` qui n'est
**jamais remis à false** dans `RoleCardController`, `InfoTableUitkController`, `MessageJournalController`,
`EmoteWheelController`. Si le `UIDocument` reconstruit son arbre (ex. changement de `panelSettings`,
`sortingOrder`, activation/désactivation du GO), la racine cachée (`_root`) devient **obsolète**
(détachée) : les callbacks pointent sur l'ancien arbre et l'UI n'est plus pilotée → panneau « mort ».
`LobbyRolesUitkController` a précisément anticipé ce cas en re-résolvant `_root` à chaque `Rebuild()`
et en remettant `_initialized = false` dans `OnDisable()` (l.123-128) ; les autres non.
**Correction suggérée** : soit re-résoudre la racine sur événement de reconstruction, soit remettre
`_initialized=false` en `OnDisable` et re-binder en `OnEnable` (comme LobbyRoles). **Certitude :
moyenne** — dépend de si ces UIDocument voient réellement leur arbre reconstruit ; RoleCard est
screen-space simple donc risque faible, mais l'incohérence de motif entre contrôleurs est réelle.

#### 3.2 `RoleCardController.OnDisable` n'annule pas les callbacks UITK ni ne réinitialise
`Assets/Scripts/UI/RoleCard/RoleCardController.cs` l.155-158
`OnDisable` ne désabonne que `charactersBar`. Cohérent avec le fait que l'arbre survit, mais
combiné à 3.1 (pas de remise à zéro de `_initialized`), un cycle disable→enable laisse l'état
potentiellement incohérent si l'arbre a été reconstruit entre-temps. **Certitude : faible/moyenne.**

### P4 — API potentiellement obsolète

#### 4.1 `style.unityBackgroundScaleMode` (C#) et `-unity-background-scale-mode` (USS)
- `Assets/Scripts/UI/Cards/RoleCardElement.cs` l.46 : `_art.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;`
- `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs` l.239 : `fade.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;`
- USS : `RoleCard.uss` l.146, `InfoTable.uss` l.108, `EmoteWheel.uss` l.92 (`-unity-background-scale-mode: scale-to-fit;`)

Depuis Unity 2022.2, les propriétés `background-size` / `background-position` / `background-repeat`
ont été introduites et `-unity-background-scale-mode` (et son pendant C# `unityBackgroundScaleMode`)
est présenté comme **déprécié/legacy**. Il fonctionne encore mais peut générer un avertissement de
compilation/USS. **À vérifier en éditeur** si un warning `[Obsolete]` apparaît sur cette version.
Migration : `background-size: cover` (≈ ScaleAndCrop), `contain` (≈ scale-to-fit),
`background-size: 100% 100%` (≈ StretchToFill). **Certitude : incertaine** — je ne peux pas
confirmer l'attribut `[Obsolete]` exact en `6000.5.0f1` sans compiler. À traiter comme « dette
probable », pas comme cassure.

### P5 — Nettoyage mineur (lisibilité, pas de bug)

- `RoleCardController._cInset` : champ assigné (l.349) mais **jamais lu** (la valeur `fInset` est
  utilisée directement) → champ mort. **Certitude : élevée.**
- `NoteChoosePanel` : `using UnityEngine.UIElements;` (l.12) apparemment **inutilisé** — la classe
  est du uGUI (RectTransform/DOTween/IPointerClickHandler). À retirer. **Certitude : moyenne**
  (aucun symbole UIElements repéré dans le corps).
- `NoteChoosePanel.DisplayCharacters` (l.79-106) : dédoublonnage en `GroupBy` + `FirstOrDefault`
  imbriqué → O(n²) et allocations LINQ à chaque modification de note. Hors UITK, mais coût inutile.
  **Certitude : moyenne.**
- Cohérence : `SpikeRawImageRt` / `SpikeRenderTextureInput` appliquent le flip Y différemment
  (RawImage : pas de flip, l.109-111 ; quad : `1 - uv.y`, l.101-102). C'est justifié par les deux
  chemins de rendu, mais à garder à l'esprit si l'un sert de base à du code productif.

---

## Points vérifiés — PAS de problème

- `InfoTableRtPresenter` / `LobbyRolesRtPresenter` : clonage correct du `PanelSettings`
  (`Instantiate`), restauration de l'asset source et `Release()`+`Destroy()` de la RenderTexture
  en `OnDisable`. Bon cycle de vie. **Certitude : élevée.**
- Fermetures de lambdas sur variables de boucle `foreach` (partout : InfoTable, LobbyRoles,
  NoteChoosePanel) : correctes (portée par itération depuis C# 5). **Certitude : élevée.**
- `EmoteWheelController` : `IVisualElementScheduledItem` mis en pause quand inactif
  (`_bandAnim.Pause()`, `_confirmAnim.Pause()`), pas de tick permanent. Bon. **Certitude : élevée.**
- `MessageJournalController` : garde `_subscribed` correcte, `OnDisable` désabonne. **Certitude : élevée.**
- Toutes les API UITK employées (`Q`/`Query`, `schedule.Execute().ExecuteLater()`,
  `RegisterCallback`, `generateVisualContent`, `Painter2D`, `MeshGenerationContext`,
  `experimental.animation` + `Easing`, `StyleKeyword.Null/None`, `EnableInClassList`) sont valides
  et non obsolètes en Unity 6. **Certitude : élevée** (sauf `unityBackgroundScaleMode`, cf. 4.1).

## Récapitulatif priorisé

| Prio | Point | Fichier |
|---|---|---|
| P1 | Désabonnement NoteManager hors ClosePanel | NoteChoosePanel.cs |
| P1 | NotImplementedException sur méthodes de contrat | NoteChoosePanel.cs |
| P1 | Mutation PanelSettings partagé (spikes) | SpikeRawImageRt/SpikeRenderTextureInput |
| P1 | Fuite `Renderer.material` (spike) | SpikeRenderTextureInput.cs |
| P2 | `Children()` alloué par cellule à chaque Render | InfoTableUitkController.cs |
| P2 | Rebuild total par clic | LobbyRolesUitkController.cs |
| P2 | Q() scrollbar par Open | RoleCardController.cs |
| P2 | Update() par frame jusqu'au build | LobbyRolesUitkController.cs |
| P3 | `_initialized` jamais reset → racine obsolète possible | RoleCard/InfoTable/MessageJournal/EmoteWheel |
| P4 | `unityBackgroundScaleMode` (obsolète probable) | RoleCardElement.cs / LobbyRoles + 3 USS |
| P5 | Champ mort `_cInset`, using inutilisé, LINQ O(n²) | RoleCardController / NoteChoosePanel |
