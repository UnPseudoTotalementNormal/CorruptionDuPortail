# T4 — Analyse du code UI Toolkit existant

> Lecture statique uniquement : je n'avais ni éditeur Unity ni compilateur, donc **rien de ce qui suit n'a été exécuté ou testé**.
> Aucun fichier du projet n'a été modifié. Les numéros de ligne renvoient à l'état du dépôt au moment de l'analyse.

## 0. Périmètre et méthode

- **Version Unity** : `ProjectSettings/ProjectVersion.txt` indique `6000.5.0f1`.
  **Écart à signaler** : `CLAUDE.md` indique encore `6000.2.6f2`. L'analyse suit `ProjectVersion.txt`.
- **Fichiers analysés** (code du projet, hors `Assets/_ModelTest`) :
  - C# : `Assets/Scripts/UI/InfoTable/InfoTableUitkController.cs`, `InfoTableRtPresenter.cs`, `InfoTableModel.cs`, `GameInfoTableDataSource.cs` ;
    `Assets/Scripts/UI/LobbyRoles/LobbyRolesUitkController.cs`, `LobbyRolesRtPresenter.cs`, `GameLobbyRolesDataSource.cs` ;
    `Assets/Scripts/UI/RoleCard/RoleCardController.cs` ; `Assets/Scripts/UI/Cards/RoleCardElement.cs` ;
    `Assets/Scripts/UI/MessageJournal/MessageJournalController.cs`, `GameMessageJournalDataSource.cs` ;
    `Assets/Scripts/UI/EmoteWheel/EmoteWheelController.cs` (+ son appelant `Assets/Scripts/Avatars/EmoteWheelInput.cs`) ;
    `Assets/Scripts/UI/Spike/*.cs` ; `Assets/Scripts/NoteSystem/NoteChoosePanel.cs` (importe `UnityEngine.UIElements`, mais c'est du uGUI).
  - UXML/USS/TSS : `Assets/UI/Screens/*/*.uxml|uss`, `Assets/UI/Styles/theme.tss`, `variables.uss`.
  - PanelSettings : `Assets/UI/PanelSettings/*.asset`.
- **Exclus** : les plugins et samples tiers (`Assets/Plugins/SerializedCollections`, `Assets/Plugins/TimeRecorder`,
  `Assets/Samples/...`, `Packages/dev.ameye.linework-lite`). C'est du code éditeur tiers que l'équipe ne maintient pas.
- **Utilisation dans les scènes** (vérifiée via les GUID des `.meta`) : InfoTable, LobbyRoles, RoleCard, MessageJournal et EmoteWheel
  sont dans `Scenes/GameScene.unity`. Les scripts `Spike*` ne sont que dans `Scenes/Spikes/*` et `EmoteWheelSpike.unity`.

Légende des priorités :
- **P0** : bug visible par le joueur, ou problème de sécurité.
- **P1** : robustesse, ou performance qui compte en jeu.
- **P2** : API obsolète ou expérimentale, dette qui va coûter plus tard.
- **P3** : hygiène, cohérence.

Confiance : **[certain]** = lu dans le code, et le mécanisme UITK est bien connu. **[probable]** = raisonnement fiable mais pas vérifié
en éditeur. **[à vérifier]** = à confirmer en Play Mode.

---

## 1. Tableau de synthèse (trié par priorité)

| ID | Prio | Fichier(s) | Problème | Correction proposée |
|----|------|-----------|----------|---------------------|
| B1 | P0 | EmoteWheelController, RoleCardController, MessageJournalController | Si `Open()` et `Close()` tombent dans la même frame, l'overlay reste affiché, figé, sans pouvoir se fermer | Garder les `IVisualElementScheduledItem`, ou tester un flag `_isOpen` dans le callback différé |
| B2 | P0 | EmoteWheel.uss | `.emote-wheel__label--hidden` est écrasé par `.emote-wheel__slot-label` (même spécificité, règle déclarée après) : le label du secteur sélectionné ne se masque jamais | Sélecteur composé `.emote-wheel__slot-label.emote-wheel__label--hidden` |
| B3 | P0 | MessageJournalController (+ InfoTable pour les pseudos) | Injection de rich text : les messages anonymes des joueurs (et les pseudos Steam) passent dans des `Label` où `enableRichText` vaut `true` par défaut | `enableRichText = false` sur les labels qui affichent du contenu joueur |
| B4 | P0 | InfoTableUitkController | Le `tooltip` sert à afficher le nom complet des rôles et des camps, mais les tooltips ne s'affichent pas sur un panel runtime. Au palier `roles-icon`, les noms tronqués deviennent illisibles | Remplacer par une barre d'info alimentée par `PointerEnterEvent`/`PointerLeaveEvent`, ou par une légende |
| R1 | P1 | RoleCard, MessageJournal, EmoteWheel, InfoTable | Les références `VisualElement` sont mises en cache une seule fois (`_initialized`) et jamais invalidées. Si l'arbre de l'UIDocument est recréé, elles deviennent obsolètes | Remettre à zéro dans `OnDisable` et re-résoudre dans `OnEnable`/`Start` (comme le fait déjà LobbyRoles) |
| R2 | P1 | LobbyRolesUitkController | `_everBuilt` n'est pas remis à zéro dans `OnDisable` : après un disable/enable, le polling de secours ne se relance plus et le panel reste vide | `_everBuilt = false;` dans `OnDisable` |
| R3 | P1 | InfoTableUitkController vs LobbyRolesUitkController | Hypothèses contradictoires sur l'effet du swap de `panelSettings` par le RtPresenter : l'un met la racine en cache, l'autre la re-résout à chaque fois | Unifier (voir §3.3) |
| P1 | P1 | LobbyRolesUitkController | Toute l'UI est détruite puis reconstruite à chaque `OnChanged` (clic sur un stepper, ready, liste des joueurs) | Construire une fois, garder une référence par `RoleID`, mettre à jour sur place |
| P2 | P1 | LobbyRoles, MessageJournal, InfoTable | Les rafales d'événements réseau déclenchent N reconstructions ou N rendus complets dans la même frame | Flag « dirty » + un seul rebuild différé |
| P3 | P1 | InfoTableUitkController / InfoTableModel | `Render()` coûte O(P·R·(P+R)) et alloue à chaque rendu (énumérateur de `Children()`, chaînes) | Tableaux précalculés par rendu, segments en cache, textes en cache |
| P4 | P1 | LobbyRolesUitkController | Les steppers envoient une valeur absolue capturée au moment du build : un double clic avant l'écho réseau ne compte qu'une fois côté client | Envoyer un delta, ou garder une valeur optimiste locale |
| P5 | P2 | MessageJournalController | Rebuild même panel fermé ; `ScrollToBottom` passe par `ExecuteLater(1)` avant le layout ; allocations LINQ | Rebuild paresseux à l'ouverture, `GeometryChangedEvent` pour le scroll |
| P6 | P2 | LobbyRoles, MessageJournal | Restauration du scroll via `ExecuteLater(0/1)` : fragile si le layout n'est pas encore calculé | Attendre un `GeometryChangedEvent` du `contentContainer` |
| O1 | P2 | RoleCardElement, LobbyRolesUitkController, RoleCard.uss, InfoTable.uss, EmoteWheel.uss | `unityBackgroundScaleMode` / `-unity-background-scale-mode` sont dépréciés depuis 2022.2 | `background-size` / `background-repeat` / `background-position-x/y` |
| O2 | P2 | LobbyRolesUitkController, MessageJournalController | `experimental.animation` (namespace `UnityEngine.UIElements.Experimental`) | Des transitions USS, une fois le rebuild complet supprimé (P1) |
| O3 | P2 | RoleCardElement | Contrôle personnalisé sans `[UxmlElement]` (méthode Unity 6), donc impossible à placer dans UI Builder | `[UxmlElement] public sealed partial class RoleCardElement` (optionnel) |
| H1 | P3 | EmoteWheel.uss, InfoTable.uss, RoleCard.uss, MessageJournal.uss | Classes utilitaires `.cdp-is-hidden` / `.cdp-is-collapsed` redéfinies écran par écran | Une feuille `utilities.uss` importée par `theme.tss` |
| H2 | P3 | LobbyRoles.uss / .cs | Classes USS et constantes mortes (`__card*`, `__tab-badge`), et `.lobby-roles__controls` défini deux fois | Supprimer ou fusionner |
| H3 | P3 | 3 × RtPresenter | Le même code copié trois fois (InfoTable, LobbyRoles, Spike) | Une classe de base `RenderTexturePanelPresenter` |
| H4 | P3 | Plusieurs | Couleurs codées en dur en C# qui doublonnent les tokens USS (risque de divergence) | Classes modificatrices USS, ou une seule source C# |
| H5 | P3 | RoleCardController | `Q()` sur le dragger interne (`unity-dragger`) à chaque `Open()`, et dépendance au nommage interne | Mettre en cache après la première résolution, ou styler par classe USS |
| H6 | P3 | PS_InfoTable_RT / PS_LobbyRoles_RT | `m_ClearColor: 0` sur des panels rendus en RenderTexture | Activer « Clear Color » (voir §5) |
| H7 | P3 | Toutes les USS + PanelSettings | Glyphes ✓ ✗ ★ ⚠ ✕ ▾ ● ○ − avec LiberationSans et `textSettings` nul : risque de glyphes manquants (carrés vides) | Vérifier le rendu, et ajouter une police de secours si besoin |
| H8 | P3 | Spike* | Les scripts de spike modifient l'asset PanelSettings partagé ; `Renderer.material` instancie un matériau jamais détruit | Scènes de spike uniquement : supprimer ou archiver les spikes |

---

## 2. P0 — Bugs visibles par le joueur

### B1 — Open/Close dans la même frame : overlay bloqué à l'écran [certain pour le mécanisme ; reproduction à vérifier]

**Où** :
- `EmoteWheelController.cs` L474-498 : `Open()` diffère `RemoveFromClassList(HiddenClass)` avec `schedule.Execute(...)`, et `Close()` diffère `CollapseIfHidden` de 220 ms.
- Même motif dans `RoleCardController.cs` L167-204 et `MessageJournalController.cs` L102-142.

**Scénario** (EmoteWheel, le cas le plus probable) : `EmoteWheelInput.Update()` (L65-95) lit `wasPressedThisFrame` **et** `wasReleasedThisFrame`.
Sur un tap très rapide, ou à bas framerate, les deux valent `true` dans la même frame, donc `OpenWheel()` puis `CloseWheel()` s'exécutent
dans la même frame :
1. `Open()` retire `cdp-is-collapsed` et programme « retirer `cdp-is-hidden` » pour le prochain tick du scheduler du panel.
2. `Close()` ajoute `cdp-is-hidden` (déjà présente) et programme `CollapseIfHidden` dans 220 ms.
3. Au tick du scheduler, l'élément différé de `Open()` s'exécute et **retire `cdp-is-hidden`** : la roue devient visible.
4. À 220 ms, `CollapseIfHidden` voit que `cdp-is-hidden` est absente et **ne replie pas** la roue.

→ La roue reste affichée par-dessus la vue FPS jusqu'au prochain cycle d'ouverture/fermeture. RoleCard et MessageJournal ont le même défaut
(par exemple si un `Close()` programmatique suit un `Open()` dans la même frame).

**Défaut associé** : les appels successifs à `Close()` empilent des `CollapseIfHidden` jamais annulés. Avec la séquence Close → Open → Close
en moins de 420 ms, le premier collapse tombe pendant l'animation de sortie du second Close et la coupe.

**Correction** (même principe pour les trois contrôleurs) :

```csharp
private bool _isOpen;
private IVisualElementScheduledItem _revealItem;
private IVisualElementScheduledItem _collapseItem;

public void Open()
{
    TryInitialize();
    if (_root == null) return;
    _isOpen = true;
    _collapseItem?.Pause();                       // annule un collapse en attente
    _root.RemoveFromClassList(CollapsedClass);
    _revealItem ??= _root.schedule.Execute(() => { if (_isOpen) _root.RemoveFromClassList(HiddenClass); });
    _revealItem.ExecuteLater(0);                  // (re)programme, en annulant l'exécution précédente
}

public void Close()
{
    if (_root == null) return;
    _isOpen = false;
    _revealItem?.Pause();                         // un reveal en attente ne doit plus s'exécuter
    _root.AddToClassList(HiddenClass);
    _collapseItem ??= _root.schedule.Execute(() => { if (!_isOpen) _root.AddToClassList(CollapsedClass); });
    _collapseItem.ExecuteLater(ExitCollapseDelayMs);
}
```

Effet secondaire utile : on n'alloue plus de délégué ni de `ScheduledItem` à chaque ouverture ou fermeture.

### B2 — EmoteWheel : le label du secteur sélectionné ne se masque jamais [certain]

**Où** : `Assets/UI/Screens/EmoteWheel/EmoteWheel.uss`
- L76 : `.emote-wheel__label--hidden { opacity: 0; }`
- L108 : `.emote-wheel__slot-label { ...; opacity: 0.82; ... }`

`SetSelection()` (`EmoteWheelController.cs` L516-519) ajoute `emote-wheel__label--hidden` au label du secteur pointé, pour ne pas le doubler
avec la légende centrale. Les deux sélecteurs ont la même spécificité (une classe chacun). En cascade USS, c'est la règle déclarée **en dernier**
qui gagne, donc `opacity: 0.82` et le label reste visible.
La légende centrale (`.emote-wheel__caption`, L60) n'est pas touchée, parce que sa règle est déclarée **avant** la classe `--hidden`.

**Correction** :

```css
.emote-wheel__slot-label.emote-wheel__label--hidden {
    opacity: 0;
}
```

(Ou déplacer `.emote-wheel__label--hidden` en fin de fichier. Le sélecteur composé est plus sûr face aux réordonnancements.)

### B3 — Injection de rich text dans les messages anonymes [certain]

**Où** : `MessageJournalController.cs` L212-217 : `new Label($"« {_message} »")`. `_message` vient de `MessageInfo.message`, un texte saisi par
un joueur et répliqué par NGO (`GameMessageJournalDataSource.cs`). Aucun assainissement dans le projet (aucune occurrence de
`enableRichText` / `noparse` dans `Assets/Scripts`).

`TextElement.enableRichText` vaut `true` par défaut. Un joueur peut donc envoyer `<size=300>`, `<color=#00000000>` (message invisible),
`<mark>`, `<sprite ...>`, ou casser la mise en page du journal que **tous** les joueurs voient pendant la révélation de nuit.
Dans un jeu de déduction sociale, ça permet aussi de maquiller un message en message « système » (ligne de stats colorée).

Même risque, en plus faible, pour les pseudos affichés dans l'InfoTable (`InfoTableUitkController.cs` L270 : `new Label(_model.Players[pi].Pseudo)`),
qui viennent de Steam et sont contrôlés par le joueur.

**Correction** :

```csharp
var _msg = new Label($"« {_message} »") { enableRichText = false };
// InfoTable :
var name = new Label(_model.Players[pi].Pseudo) { enableRichText = false };
```

La ligne de stats (`BuildStatText`) garde le rich text : elle est générée par le code et ne contient pas de saisie joueur.
À terme, il faut aussi filtrer côté serveur (longueur et balises), parce que les écrans uGUI/TMP qui affichent ces messages ont
probablement le même problème. Ils sont hors du périmètre UITK de cette analyse.

### B4 — Des tooltips qui ne s'affichent pas sur un panel runtime [probable]

**Où** : `InfoTableUitkController.cs` L211 (`roleHead.tooltip = text; // full name on hover — survives the compact/icon role tiers`)
et L470 (`camp.tooltip = CampLabel(g)`).

À ma connaissance, la propriété `VisualElement.tooltip` n'est affichée que par les panels **éditeur**. Un panel runtime (UIDocument),
et à plus forte raison un panel rendu en RenderTexture, n'affiche rien. Le projet le sait déjà : `LobbyRolesUitkController.cs` L293-294 dit
*« unlike a UITK tooltip which doesn't render inside the RenderTexture panel »*.

**Conséquence** : au palier `roles-icon` (9 rôles ou plus), les en-têtes passent en 18 px avec un padding réduit. Les noms longs se coupent
ou se replient, et le nom complet « au survol » annoncé par le commentaire n'apparaît jamais. Le libellé du camp (« Élu », « Marginal »…) n'a
pas d'équivalent visible non plus quand une icône est affichée.

**Correction** : un petit bandeau d'info dans le footer (qui existe déjà), alimenté par les événements pointeur :

```csharp
roleHead.RegisterCallback<PointerEnterEvent>(_ => _hint.text = text);
roleHead.RegisterCallback<PointerLeaveEvent>(_ => _hint.text = string.Empty);
```

(Ou, plus simple : une abréviation + une légende dans le footer au palier `roles-icon`.) Si on garde les tooltips, retirer le commentaire,
qui est trompeur.

---

## 3. P1 — Robustesse

### R1 — Arbre UIDocument mis en cache et jamais invalidé [probable ; latent]

**Où** : `RoleCardController.TryInitialize` (L114-153), `MessageJournalController.TryInitialize` (L52-78),
`EmoteWheelController.TryInitialize` (L123-147), `InfoTableUitkController.TryInitialize` (L155-165).

Tous suivent le même motif : `if (_initialized) return;`, puis `Q()` et mise en cache. `_initialized` n'est **jamais remis à `false`**.
Quand on désactive puis réactive un `UIDocument` (ou son GameObject), il reconstruit son `rootVisualElement` à partir du `VisualTreeAsset`.
Toutes les références en cache (`_root`, `_panel`, `_turns`, `_ring`, les callbacks `clicked`/`PointerDownEvent`, l'élément `_confirm` ajouté
à la racine) pointent alors vers un arbre détaché : `Open()` s'exécute sans erreur mais **rien ne s'affiche**.

État actuel : je n'ai trouvé aucun `SetActive(false)` sur ces objets (le changement d'app de la tablette passe par `SmartphoneApp.IsOpen`),
donc le bug est **latent**. Il se déclenchera dès qu'un flux (retour au lobby sans rechargement de scène, pooling, désactivation d'un canvas
parent) coupera le GameObject.

**Correction** (ce que LobbyRoles fait déjà en partie) :

```csharp
private void OnDisable()
{
    // ... désabonnements existants ...
    _initialized = false;
    _root = null;           // + les autres références en cache
}
```

Les abonnements UITK (`closeButton.clicked`, `RegisterCallback`) meurent avec l'ancien arbre, donc il n'y a pas de fuite. Il faut seulement
reconstruire. Pour l'EmoteWheel, remettre aussi `_built = false`, `_donut = null`, `_confirm = null`, et mettre en pause `_bandAnim`/`_confirmAnim`.

### R2 — LobbyRoles : `_everBuilt` n'est pas remis à zéro [certain]

**Où** : `LobbyRolesUitkController.cs` L118-128. `OnDisable` remet `_initialized = false` mais pas `_everBuilt`.
Après un disable/enable, `TryInitialize()` → `Rebuild()`. Si l'arbre n'est pas encore prêt (même problème d'ordre `OnEnable` que celui que
le commentaire décrit), `Rebuild` sort tôt, et `Update()` ne relance plus rien parce que `_everBuilt == true` : **le panel reste vide**.
Par ailleurs, `_fadeTex` est détruite dans `OnDisable` alors que les éléments de fondu peuvent encore la référencer.

**Correction** : `_everBuilt = false; _scroll = null;` dans `OnDisable`.

### R3 — Hypothèses contradictoires sur le swap de PanelSettings [à vérifier]

`InfoTableRtPresenter` et `LobbyRolesRtPresenter` remplacent `UIDocument.panelSettings` par un clone dans leur `OnEnable`.
- `LobbyRolesUitkController.cs` L152-155 affirme que ce swap « rebuilds the visual tree — a root cached at init would be stale/detached (black panel) »,
  et re-résout donc la racine à chaque `Rebuild`.
- `InfoTableUitkController` met la racine en cache une fois (L155-165) et ne la re-résout jamais.

L'une des deux hypothèses est fausse. Si LobbyRoles a raison, l'InfoTable ne fonctionne que grâce à l'ordre d'exécution des `OnEnable` sur le
GameObject, ce qui est fragile. **À vérifier en Play Mode** : loguer `_root.panel` après le premier `Rebuild()` de l'InfoTable.
Dans tous les cas, il faut unifier : un helper commun `ResolveRoot()` appelé au début de chaque build, qui vérifie `_root?.panel != null`.

---

## 4. P1/P2 — Performance

### P1 — LobbyRoles : reconstruction complète à chaque changement [certain]

**Où** : `LobbyRolesUitkController.Rebuild()` L148-209, branché sur `ILobbyRolesDataSource.OnChanged`.
`GameLobbyRolesDataSource` lève `OnChanged` sur `OnSettingsChanged` **et** sur chaque `playerInfos.OnListChanged` (L64-65) :
arrivée d'un joueur, toggle ready, etc.

À chaque fois, `_root.Clear()` détruit puis recrée :
- par rôle : environ 12 éléments (unit, `RoleCardElement` + 2 enfants, controls, 2 × (ctl, label, stepper, 2 boutons, valeur)) et 5 closures ;
- plus les onglets, le tally, la barre de presets et le popover (N boutons), le footer, une `Dictionary`, plusieurs `List`, un `CompositionSnapshot`.

Conséquences :
- De l'allocation GC et un restyle/relayout complet du panel RT à chaque clic ou toggle ready d'**un autre** joueur.
- Hover, état « pressed » et focus perdus ; le popover de presets se referme ; la position de scroll doit être restaurée à la main (P6).
- Les transitions USS ne peuvent pas fonctionner (l'élément vient de naître). D'où le contournement par `experimental.animation`
  (L465-474) et le dictionnaire `_cardOpacity`.

**Correction** :
1. Séparer **structure** et **valeurs** : ne reconstruire que si l'ensemble des `RoleID` ou l'onglet change.
2. Garder un `Dictionary<RoleID, RoleUnitRefs>` (card, maxLabel, forcedLabel, 4 boutons) et, sur `OnChanged`, ne mettre à jour que
   `text`, `SetEnabled`, `EnableInClassList(...)` et l'opacité (via une classe `lobby-roles__unit--dimmed` avec `transition: opacity 200ms`).
3. Brancher les boutons une seule fois sur un handler qui lit l'état **courant** (voir P4) au lieu de closures capturées au build.
4. Tally et footer : même principe, des labels en cache.

### P2 — Rafales d'événements : plusieurs rebuilds dans la même frame [certain]

- **MessageJournal** : les messages d'un tour arrivent en autant d'événements `OnListChanged` qu'il y a de messages
  (le commentaire L174-178 le confirme). Chacun déclenche un `Rebuild()` complet, plus un `ScrollToBottom` (closure + ScheduledItem).
- **InfoTable** : `ScanReveals()` (L395-406) appelle `_model.LockRowToRole()` pour chaque ligne révélée. Chaque appel fait `Recompute()`
  → `OnChanged` → `Render()` complet. Avec k lignes révélées, ça fait k rendus complets d'affilée. `Rebuild()` fait en plus `BuildGrid()` →
  `Render()`, puis `ScanReveals()`.
- **LobbyRoles** : voir P1. `ApplyPreset` peut produire plusieurs notifications réseau.

**Correction** (motif générique) :

```csharp
private bool _dirty;
private IVisualElementScheduledItem _flush;

private void MarkDirty()
{
    _dirty = true;
    _flush ??= _root.schedule.Execute(() => { if (_dirty) { _dirty = false; Rebuild(); } });
    _flush.ExecuteLater(0);   // une seule exécution au prochain tick, quel que soit le nombre d'appels
}
```

Pour l'InfoTable, on peut aussi ajouter au modèle une méthode `LockRows(IEnumerable<(int, string)>)` qui ne fait qu'un seul `Recompute()`,
ou un `BeginBatch()/EndBatch()` qui retient `OnChanged`.

### P3 — InfoTable : coût et allocations de `Render()` [certain pour la complexité ; probable pour l'allocation de l'énumérateur]

**Où** : `InfoTableUitkController.Render()` L408-456, `RenderCamp` L460-497, `RenderFooter` L596-608 ; `InfoTableModel.IsCellDimmed` L107-113.

- `IsCellDimmed(pi, ri)` rappelle `IsRowFound` (O(R)) et `IsColumnClaimed` → `GetSureCountForRole` (O(P)) **pour chaque cellule**.
  `FirstSureRole(pi)` est calculé deux fois par ligne (L423 et dans `RenderCamp`). `RenderFooter` recompte chaque colonne.
  Au total, O(P·R·(P+R)) par rendu, et un rendu part à chaque clic.
- `foreach (VisualElement seg in cell.Children())` (L439) : `Children()` renvoie un `IEnumerable<VisualElement>`, donc le `foreach`
  passe par l'interface et boxe l'énumérateur. Ça fait une allocation par cellule et par rendu (P×R allocations).
- `$"{sure}/{capacity}"` (L603) alloue une chaîne par chip à chaque rendu, même quand la valeur ne change pas.
- `PaintFaction`/`ClearFaction*` réécrivent 2 à 5 propriétés de style inline sur chaque cellule, à chaque rendu.

**Correction** :

```csharp
// Une seule fois au build : garder les 3 segments de chaque cellule.
private Label[][][] _segEls;          // [pi][ri][0..2]
// Par rendu : précalculer une fois.
int[] sureByRole  = new int[R];       // réutiliser un buffer membre
int[] firstSure   = new int[P];       // -1 si aucun
// dimmed(pi,ri) = state != Sure && (firstSure[pi] >= 0 || sureByRole[ri] >= capacity[ri])
// Footer : ne changer .text que si la valeur a changé (int[] _lastSure).
```

Ça ramène le coût à O(P·R) sans allocation. Au-delà : ne re-rendre que la ligne et la colonne touchées par un `SetCell`, en exposant
`OnCellChanged(pi, ri)` dans le modèle.

Même thème, côté source de données : `GameInfoTableDataSource.GetRevealedRoleName` (L69-77) fait un `GetCharacters(false).FirstOrDefault(...)`
(LINQ + closure) **par joueur** et par scan, soit O(P²) avec allocations. Un dictionnaire `clientId → role` construit une fois par scan suffit.

### P4 — LobbyRoles : valeurs absolues capturées dans les closures [certain pour le code ; impact réseau à vérifier]

**Où** : L482-487. `() => _data.RequestSetMax(role.Id, role.Max + 1)` capture `role.Max` **au moment du build**. Sur un client (pas l'hôte),
deux clics rapides avant l'écho réseau envoient deux fois la même valeur, `Max + 1` : on ne gagne qu'un cran. L'hôte n'est pas touché,
parce que `OnChanged` → `Rebuild` est immédiat chez lui.
**Correction** : envoyer une intention relative (`RequestStepMax(id, +1)`, clampée côté serveur), ou lire une valeur optimiste locale
mise à jour au clic.

### P5 — MessageJournal : travail inutile et allocations [certain]

- `Rebuild()` part sur chaque `OnChanged`, **même quand le journal est replié** (`display: none`). Il vaudrait mieux marquer dirty et
  reconstruire dans `Open()`.
- `AnimateWriteIn` : `_entry.Children().ToList()` (LINQ) et une closure par ligne. Acceptable une fois par tour, mais la boucle
  `for (i < childCount) _entry[i]` fait la même chose sans allocation.
- `BuildTurn` L197-200 : couleur « newest » posée en inline (`new StyleColor(...)`). Une classe `journal__turn-label--newest` en USS serait
  plus propre et cohérente avec les tokens.

### P6 — Restauration du scroll avant le layout [à vérifier]

- `LobbyRolesUitkController.cs` L202-206 : `scrollOffset` restauré via `schedule.Execute(...).ExecuteLater(0)` sur un `ScrollView` qu'on vient de créer.
- `MessageJournalController.cs` L262-276 : `verticalScroller.value = highValue` via `ExecuteLater(1)`.

Si le callback passe avant que le layout du nouveau contenu soit calculé, `highValue` vaut encore 0, `scrollOffset` est clampé à 0 et on
revient en haut, ou on n'arrive pas en bas. Plus robuste :

```csharp
void OnContentGeometry(GeometryChangedEvent _)
{
    _scroll.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnContentGeometry);
    _scroll.scrollOffset = new Vector2(_scroll.scrollOffset.x, savedY);
}
_scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(OnContentGeometry);
```

(Unity 6 fournit peut-être `RegisterCallbackOnce<T>`, voir le RAPPORT : incertain. La variante ci-dessus n'utilise que des API sûres.)
Avec le refactor P1, le `ScrollView` n'est plus recréé et ce contournement disparaît pour LobbyRoles.

### Remarque — coût des panels RenderTexture [à vérifier]

InfoTable et LobbyRoles rendent en permanence dans une RT de 1440×912, même quand l'app n'est pas visible (le mapping pointeur est bien
coupé via `_ownerApp.IsOpen`, mais pas le rendu). À mesurer au Profiler. Si c'est significatif, passer la racine en `display: none` quand
l'app est hors écran, et la réafficher à l'ouverture.

---

## 5. P2 — API obsolètes ou expérimentales

### O1 — `unityBackgroundScaleMode` / `-unity-background-scale-mode` [probable : déprécié depuis 2022.2]

Depuis 2022.2, UITK expose `background-position(-x/-y)`, `background-repeat` et `background-size`, et l'ancienne propriété est marquée dépréciée.

| Emplacement | Actuel | Remplacement |
|---|---|---|
| `RoleCardElement.cs` L46 | `_art.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;` | `background-size: cover` + position centrée |
| `LobbyRolesUitkController.cs` L239 | `fade.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;` | `background-size: 100% 100%` |
| `RoleCard.uss` L146 (`.role-card__faction-icon`) | `-unity-background-scale-mode: scale-to-fit;` | `background-size: contain` |
| `InfoTable.uss` L108 (`.info-table__camp-icon`) | idem | idem |
| `EmoteWheel.uss` L92 (`.emote-wheel__slot-icon`) | idem | idem |

USS pour `scale-to-fit` :

```css
background-position-x: center;
background-position-y: center;
background-repeat: no-repeat;
background-size: contain;      /* cover pour scale-and-crop ; 100% 100% pour stretch-to-fill */
```

En C#, les types `BackgroundSize`, `BackgroundRepeat` et `BackgroundPosition` existent. Les signatures exactes des constructeurs sont
à confirmer à la compilation, d'où le marquage « incertain » dans le RAPPORT. Le plus simple et le plus sûr est de sortir ces propriétés
du C# et de les mettre dans des classes USS (`RoleCardElement` pourrait charger sa propre feuille, ou s'appuyer sur une classe du thème).

### O2 — `experimental.animation` [certain : API du namespace `Experimental`]

`LobbyRolesUitkController.cs` L474 et `MessageJournalController.cs` L254-258 utilisent `VisualElement.experimental.animation`.
Ce n'est pas marqué obsolète, mais l'API est explicitement expérimentale et peut changer ou disparaître d'une version à l'autre.
Dans LobbyRoles, elle ne sert qu'à contourner le rebuild complet (P1). Une fois l'UI mise à jour sur place, une transition USS
`opacity` suffit. Pour le journal (apparition décalée des lignes), `transition-delay` posé inline par ligne, puis retrait d'une classe
`--pre` au tick suivant, donne le même effet avec des API stables.

### O3 — `RoleCardElement` sans `[UxmlElement]` [certain pour l'existence de l'attribut en Unity 6]

Pas un bug : l'élément est construit en C#. Mais Unity 6 remplace `UxmlFactory`/`UxmlTraits` par `[UxmlElement]` sur une classe `partial`.
L'ajouter permettrait de poser la carte dans UI Builder, et d'exposer `[UxmlAttribute]` pour la largeur, etc.

### Divers

- `Spike.uxml` L1 déclare `xmlns:uie="UnityEditor.UIElements"` dans un UXML runtime. Inutilisé et inoffensif, mais à retirer.
- `NoteChoosePanel.cs` L12 : `using UnityEngine.UIElements;` inutilisé dans un composant uGUI. C'est un risque d'ambiguïté (`Image`, `Button`…)
  si le fichier évolue.

---

## 6. P3 — Hygiène et cohérence

- **H1 — Classes utilitaires dupliquées.** `.cdp-is-hidden` et `.cdp-is-collapsed` sont définies globalement dans `EmoteWheel.uss` (L20-26)
  et `InfoTable.uss` (L26), et par combinaison dans `RoleCard.uss` / `MessageJournal.uss`. Les feuilles UXML sont limitées au sous-arbre de
  leur document, donc pas de fuite entre écrans aujourd'hui, mais la convention est éparpillée. Il vaut mieux une `Assets/UI/Styles/utilities.uss`
  importée par `theme.tss`. Même chose pour `-unity-font-definition: url(".../LiberationSans.ttf")`, répété dans RoleCard.uss L25,
  InfoTable.uss L23 et MessageJournal.uss L17 (absent de LobbyRoles.uss) : à centraliser dans le thème ou dans le `PanelTextSettings`.
- **H2 — Code mort (LobbyRoles).** Les constantes `CardClass`, `CardActiveClass`, `CardArtClass`, `CardNameClass` et `TabBadgeClass`
  (`LobbyRolesUitkController.cs` L44, L55-58) ne sont jamais utilisées. Les règles correspondantes dans `LobbyRoles.uss` (L136-146, L210-238)
  sont mortes depuis le passage à `RoleCardElement`. `.lobby-roles__controls` est défini deux fois (L206 et L241). `.lobby-roles__preset-wrap
  { position: relative }` ne sert plus depuis que le popover est rattaché à la racine.
- **H2 bis — InfoTable USS.**
  - `.info-table__player-row--locked { background-color }` (L149) est probablement invisible, parce que les enfants (camp, nom, cellules)
    ont tous un fond opaque qui couvre la ligne. [à vérifier]
  - `.info-table__name-cell--locked { background-color: transparent }` (L176) perd contre `.info-table__player-row--alt .info-table__name-cell`
    (2 classes) sur les lignes paires, et de toute façon contre le style inline `PaintFaction`. C'est le même piège de spécificité que celui
    déjà corrigé pour `--conflict` (L205-209).
  - `.is-dense/.is-scroll .info-table__name-cell` utilise le token `--cdp-info-rolehead-compact` : un token de rôle appliqué au nom, à renommer.
  - `--cdp-info-scrollbar-pad: 13px` vs `--cdp-info-scrollbar-w: 10px` : une compensation magique, fragile.
- **H3 — Trois RtPresenters copiés-collés.** `InfoTableRtPresenter` et `LobbyRolesRtPresenter` ne diffèrent que par des chaînes (diff vérifié).
  `SpikeRawImageRt` en est l'ancêtre. Une classe abstraite `RenderTexturePanelPresenter`, avec un nom de RT et un tag de log sérialisés,
  suffirait. Une correction (voir H6) ne serait alors faite qu'une fois.
- **H4 — Couleurs dupliquées C#/USS.** `MessageJournalController` (L24-27, hex « mirror » des tokens), `InfoTableUitkController`
  (`SureGreenFallback`, `BoardDarkBg`, « mirrors --cdp-color-info-* »), `LobbyRolesUitkController.FactionColor` (fallbacks),
  `RoleCardElement` (palette entière), `MessageJournalController.BuildTurn` L199. Toute retouche des tokens désynchronise ces valeurs.
  Là où c'est possible, passer par des classes modificatrices USS. Pour le rich text, c'est inévitable, mais on peut au moins lire les
  valeurs via `customStyle.TryGetValue(new CustomStyleProperty<Color>("--cdp-color-journal-number"), out var c)` dans un
  `CustomStyleResolvedEvent`. [incertain : à valider, et `ColorUtility.ToHtmlStringRGB` pour produire la balise]
- **H5 — `RoleCardController.ApplyFactionTint`** (L369-370) : `_root.Q(null, "unity-scroller--vertical")?.Q("unity-dragger")` à chaque `Open()`,
  en s'appuyant sur le **nom interne** `unity-dragger` (non contractuel, il peut changer entre versions). La classe publique
  `.unity-base-slider__dragger`, déjà utilisée dans InfoTable.uss et LobbyRoles.uss, est plus stable. À mettre en cache après la première résolution.
- **H5 bis — `RoleCardElement`** : le champ `_accent` (L28) est écrit mais jamais lu. `SetPortrait(null)` (L74) et
  `RoleCardController.BindFaction` (L328) utilisent `new StyleBackground()`, alors que l'InfoTable utilise `StyleKeyword.Null`.
  Le premier pose une valeur inline « aucune image » (qui masque l'USS), le second retire l'inline (l'USS s'applique).
  Le résultat est équivalent aujourd'hui, mais il faut choisir une sémantique et s'y tenir. [probable]
- **H6 — PanelSettings RT sans Clear Color.** `PS_InfoTable_RT.asset` / `PS_LobbyRoles_RT.asset` : `m_ClearColor: 0`. Aujourd'hui les racines
  ont un fond opaque 100 % (`.info-table`, `.lobby-roles`), donc pas d'artefact. Mais la moindre zone transparente (coins arrondis de la racine,
  fondu, marge) laisserait des traînées de la frame précédente dans la RT. Activer « Clear Color » (couleur transparente) est une assurance
  peu coûteuse. [probable]
- **H7 — Glyphes spéciaux.** ✓ ✗ ⚠ (InfoTable), ★ (RoleCard), ✕ (bouton de fermeture), ▾ ● ○ − (LobbyRoles) avec
  `-unity-font-definition: LiberationSans.ttf`, et `textSettings: {fileID: 0}` sur `PS_ScreenOverlay`. Je ne peux pas confirmer que
  LiberationSans contient ces codepoints ni quelle police de secours les `PanelTextSettings` par défaut fournissent. **À vérifier visuellement.**
  Si des carrés vides apparaissent, créer un `PanelTextSettings` avec une police de secours (par exemple une police de symboles) et l'assigner
  aux PanelSettings.
- **H8 — Spikes.** `SpikeRawImageRt` (L49) et `SpikeRenderTextureInput` modifient `targetTexture` et `SetScreenToPanelSpaceFunction` **sur l'asset
  partagé**. C'est exactement ce que les presenters de production évitent en clonant. `SpikeRenderTextureInput` (L55-60) utilise
  `_surfaceRenderer.material`, qui instancie un matériau jamais détruit. Scènes de spike uniquement, donc impact nul en jeu. À archiver ou
  supprimer si les spikes sont clos.
- **Divers.**
  - LobbyRoles : le popover de presets ne se ferme pas sur un clic extérieur, seulement via le bouton ou un rebuild.
  - `Update()` fait du polling tant que `_everBuilt == false` : si `RootName` est faux, `Rebuild()` part à chaque frame, sans fin et sans log.
    Ajouter un log unique, ou désactiver le polling après N tentatives.

---

## 7. Plan de correction proposé (ordre conseillé)

1. **B2** (1 ligne USS) et **B3** (2 lignes C#) : immédiat, sans risque.
2. **B1** : le motif `_isOpen` + items programmés en cache dans les 3 contrôleurs d'overlay. Tester un tap ultra-rapide sur la touche
   emote, et `Open()` puis `Close()` dans la même frame.
3. **B4** : bandeau d'info au survol dans l'InfoTable (ou légende).
4. **R1/R2/R3** : invalidation du cache dans `OnDisable` et helper commun de résolution de la racine. Tester un disable/enable du GameObject
   en Play Mode pour chaque écran.
5. **P2** (coalescence) puis **P1** (LobbyRoles incrémental). P1 supprime au passage le besoin d'O2 et de P6 pour le lobby.
6. **P3** (Render de l'InfoTable sans allocation), **P4**, **P5**.
7. **O1** (migration des propriétés background), **H1-H8** au fil de l'eau.

## 8. Ce qui est déjà bien fait (à conserver)

- Les presenters RT clonent le `PanelSettings` au lieu de modifier l'asset partagé, et restaurent la référence et nettoient la RT dans `OnDisable`.
- Abonnements et désabonnements symétriques aux sources de données (`OnEnable`/`OnDisable`, flag `_subscribed` côté journal,
  garde `IsSpawned` sur les `NetworkList`).
- Discipline de `pickingMode` (racines en `Ignore` tant qu'elles sont repliées, scrim en `Position` quand c'est modal) et fermeture au clic
  extérieur filtrée par `evt.target == _root`.
- EmoteWheel : animation du donut par un scheduler mis en **pause au repos**, `Painter2D` redessiné seulement via `MarkDirtyRepaint`,
  positionnement des labels sur `GeometryChangedEvent` avec `translate` (qui ne relance pas le layout, donc pas de boucle).
- Séparation modèle pur / source de données / contrôleur (InfoTable, LobbyRoles, Journal), qui rend les corrections P1-P3 testables en EditMode.
