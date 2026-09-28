# RAPPORT - T3 : écran d'inventaire UI Toolkit

Version Unity ciblée : **6000.5.0f1** (lue dans `ProjectSettings/ProjectVersion.txt`). Le CLAUDE.md du projet indique 6000.2.6f2, mais c'est ProjectVersion.txt qui fait foi. Tout ce qui est utilisé ici existe depuis Unity 2022.3 au plus tard.

Rien n'a été compilé ni testé : je n'avais ni éditeur Unity ni compilateur.

## Fichiers

| Fichier | Rôle |
|---|---|
| `InventoryScreen.uxml` | Structure : racine, colonne principale (titre + `ScrollView` de la grille), panneau de détail (icône, nom, quantité, description, texte d'attente) |
| `InventoryScreen.uss` | Tout le visuel : grille qui passe à la ligne, tailles des slots, animations de survol et de sélection, bascule du panneau de détail. Toutes les classes sont préfixées `B_` |
| `InventoryScreen.cs` | `InventoryItem` (données sérialisables) et `InventoryScreen` (MonoBehaviour). Ne fait que ce que l'USS ne sait pas faire (voir plus bas) |

Pas d'asmdef : le dossier compile dans `Assembly-CSharp`. Les `.meta` seront générés par Unity. Pas de `CreateAssetMenu` : aucun ScriptableObject n'était nécessaire.

## Mise en place

1. Créer un GameObject avec un `UIDocument` (Panel Settings + Source Asset = `InventoryScreen.uxml`).
2. Y ajouter `InventoryScreen`. Le champ `Document` se remplit tout seul (`Reset`) ou est retrouvé au `OnEnable`.
3. La liste `Items` contient 5 objets de démo sans sprite : les slots affichent alors l'initiale du nom. On peut y assigner des `Sprite`.
4. En Play Mode, redimensionner la Game View pour voir le nombre de colonnes changer puis, sous 760 px, le panneau de détail passer sous la grille.

## Répartition USS / C#

| Besoin | Où | Comment |
|---|---|---|
| Grille qui s'adapte à la largeur | USS seul | Slots de taille fixe (72 px + 4 px de marge), `flex-direction: row` + `flex-wrap: wrap` sur `.unity-scroll-view__content-container`. Le nombre de colonnes suit la largeur disponible. Défilement vertical quand il y a trop de lignes |
| Survol animé | USS seul | `:hover` sur `.B_slot` (`scale`, `background-color`, `border-color`) avec `transition-*` |
| Sélection animée | C# (classe) + USS (animation) | Le C# ajoute ou retire `B_slot--selected`. L'USS anime `scale` (courbe `ease-out-back` qui dépasse légèrement puis revient) ainsi que les couleurs |
| Panneau de détail à droite / en dessous | C# (classe) + USS (layout) | L'USS n'a pas de media queries. Le C# écoute `GeometryChangedEvent` sur la racine et active `B_inventory--narrow` si la largeur est sous `m_NarrowBreakpoint` (760 par défaut, en unités du panel). L'USS passe alors la racine en `flex-direction: column` et le panneau en `width: auto`, `max-height: 45%`, avec l'icône à gauche du texte |
| Contenu du détail | C# | Remplit les `Label` et l'icône. `B_detail--empty` fait apparaître et disparaître le contenu en fondu, et le texte d'attente à l'inverse |
| Slots | C# | Créés à partir de la liste (le nombre dépend des données), avec seulement des classes. Aucun style inline, sauf `backgroundImage` pour l'icône, qui vient des données |

Interaction : clic (`ClickEvent`) ou validation clavier/manette sur un slot qui a le focus (`NavigationSubmitEvent`). Cliquer sur un slot vide efface la sélection. Recliquer sur le slot sélectionné le laisse sélectionné.

## API UI Toolkit / C# utilisées

| API | Statut |
|---|---|
| `UIDocument.rootVisualElement` | certain |
| `UQueryExtensions.Q<T>(string name)` | certain |
| `VisualElement.AddToClassList` / `RemoveFromClassList` / `EnableInClassList` | certain |
| `VisualElement.Add`, `VisualElement.Clear` (sur un `ScrollView`, ils ciblent le `contentContainer`) | certain |
| `VisualElement.focusable`, `VisualElement.pickingMode` / `PickingMode.Ignore` | certain |
| `RegisterCallback<T>` / `UnregisterCallback<T>` | certain |
| `ClickEvent` | certain |
| `NavigationSubmitEvent` (envoyé à l'élément qui a le focus) | certain |
| `GeometryChangedEvent.newRect` | certain |
| `IStyle.backgroundImage`, `new StyleBackground(Sprite)` | certain |
| Conversion implicite `StyleKeyword.Null` vers `StyleBackground` (retour à la valeur de l'USS) | certain |
| `Label(string)`, `Label.text` | certain |
| `ScrollView` (type utilisé par `Q<ScrollView>`) | certain |
| `RequireComponent`, `SerializeField`, `Min`, `TextArea`, `Tooltip`, `Serializable` | certain |

UXML :

| Élément / attribut | Statut |
|---|---|
| `<Style src="InventoryScreen.uss" />` (chemin relatif) | certain |
| `ui:ScrollView` avec `mode="Vertical"` | certain |
| `horizontal-scroller-visibility="Hidden"` | certain |
| `picking-mode="Ignore"` | certain |
| `name`, `class`, `text` | certain |

## Propriétés USS utilisées

| Propriété / sélecteur | Statut |
|---|---|
| `flex-direction`, `flex-wrap: wrap`, `flex-grow`, `flex-shrink`, `flex-basis`, `align-content`, `align-items`, `align-self`, `justify-content` | certain |
| `width`, `height`, `width: auto`, `max-height` en `%` | certain |
| `margin`, `padding` (y compris le raccourci à 4 valeurs), `margin-left`, `margin-top`, `margin-right`, `margin-bottom` | certain |
| `position: absolute`, `left`, `right`, `top`, `bottom` | certain |
| `overflow: hidden` | certain |
| `background-color`, `color`, `opacity` | certain |
| `border-width`, `border-color`, `border-radius` | certain |
| `font-size`, `-unity-font-style`, `-unity-text-align`, `white-space: normal` | certain |
| `-unity-background-scale-mode: scale-to-fit` | certain. Encore supportée en Unity 6, même si `background-size` et les propriétés voisines existent depuis 2022.2 |
| `scale` (2 valeurs), `translate` (valeurs en `px`) | certain |
| `transition-property`, `transition-duration`, `transition-timing-function` | certain |
| Courbes `ease-out-cubic` et `ease-out-back` | certain |
| Transition des longhands `border-*-color`, de `scale`, de `translate`, de `opacity`, de `background-color` | certain |
| Pseudo-classes `:hover` et `:focus` | certain |
| Classe interne `.unity-scroll-view__content-container`, en wrap pour faire une grille (même principe que l'exemple « Wrap content inside a scroll view » de la doc) | certain sur le nom de la classe. Incertain sur un point de détail : ce pattern suppose un `ScrollView` vertical sans défilement horizontal, sinon le contenu ne passe pas à la ligne |
| Spécificité : `.B_slot.B_slot--selected` (2 classes) pour l'emporter sur `.B_slot:hover` (classe + pseudo-classe), et l'ordre des règles pour départager les cas à égalité | certain. UI Toolkit calcule la spécificité comme CSS |

J'ai volontairement évité `gap` / `row-gap` / `column-gap`. Je ne suis pas sûr que ces propriétés soient supportées en USS dans cette version, donc l'espacement passe par les marges des slots. J'ai aussi évité `:active` : sur un `VisualElement` simple sans manipulateur `Clickable`, je ne suis pas sûr que cette pseudo-classe soit activée.

## Hypothèses

- La racine du `UIDocument` occupe tout l'écran, et `.B_inventory { flex-grow: 1; }` suffit donc à remplir le panel. C'est le fonctionnement habituel en runtime.
- Le seuil est exprimé **en unités du panel**, pas en pixels physiques. Avec `Scale With Screen Size` dans les Panel Settings, 760 correspond à une largeur de référence, pas à une largeur d'écran réelle. Il se règle dans l'Inspector.
- Une taille de slot fixe convient. Le brief demande que la grille s'adapte à la largeur : ici, c'est le nombre de colonnes qui change, pas la taille des slots.
- Les slots vides ne sont pas sélectionnables : cliquer dessus efface la sélection.
- Les textes (« Inventaire », « Quantité : N », etc.) sont en dur, faute de système de localisation imposé.

## Limites connues

- Rien n'a été compilé ni testé en éditeur.
- **Espace perdu à droite de la grille** : avec des slots de taille fixe, il reste une bande libre à droite, plus petite qu'un slot. Pour que les slots remplissent exactement la largeur, il faudrait calculer leur taille en C# à chaque `GeometryChangedEvent` de la grille. Je ne l'ai pas fait, puisque le brief demande de limiter le C# au strict nécessaire.
- **Première image** : la classe `B_inventory--narrow` n'est posée qu'après le premier calcul de layout. Sur écran étroit, la mise en page large peut donc apparaître pendant une image.
- Le passage large / étroit est instantané : les propriétés de layout (`flex-direction`) ne sont pas animées.
- Au survol, un slot agrandi (`scale`) peut passer sous ou sur ses voisins selon l'ordre de dessin. La marge de 4 px absorbe l'agrandissement (jusqu'à x1,12), donc il ne chevauche pas les voisins et n'est pas coupé par le viewport.
- Navigation clavier/manette : les slots peuvent recevoir le focus et la validation fonctionne. En revanche, je n'ai pas vérifié que les flèches suivent bien les lignes d'une grille en wrap. La navigation directionnelle par défaut d'UI Toolkit n'est pas garantie sur ce type de layout.
- Modifier `items` en code ne met pas l'écran à jour : il faut appeler `Rebuild()`. Une modification dans l'Inspector en Play Mode n'est pas non plus prise en compte avant un `Rebuild()` ou une désactivation puis réactivation du composant.
- Si le `UIDocument` est désactivé puis réactivé, il reconstruit son arbre. Il faut alors réactiver aussi `InventoryScreen`.
- Quand on efface la sélection, l'ancien contenu reste en place le temps du fondu, à opacité 0. C'est voulu.
