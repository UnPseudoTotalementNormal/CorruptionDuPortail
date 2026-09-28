# RAPPORT — T3 : Écran d'inventaire UI Toolkit

Unity **6000.5.0f1** (lu dans `ProjectSettings/ProjectVersion.txt`).
Note : le `CLAUDE.md` mentionne 6000.2.6f2, mais `ProjectVersion.txt` fait foi.

Namespace : `ModelTest.A.T3`. Toutes les classes USS sont préfixées `A_`.

## Fichiers produits

- `InventoryScreen.uxml` — coquille de l'écran : titre, corps (grille + panneau détail).
- `InventorySlot.uxml` — gabarit d'un slot, cloné une fois par objet.
- `InventoryScreen.uss` — toute la présentation (grille responsive, survol, sélection, transitions, reflow étroit).
- `InventoryScreen.cs` — `MonoBehaviour` (le C# strictement nécessaire).

## Comment ça marche

- **Grille adaptative** : `#grid` est en `flex-direction: row` + `flex-wrap: wrap`. Les slots
  ont une taille fixe (96×112) donc le nombre de colonnes s'ajuste tout seul à la largeur.
  Aucun C# nécessaire pour ça. Le tout est dans un `ScrollView` pour le débordement vertical.
- **Survol** : pur USS via `:hover` (scale + couleurs de bordure/fond).
- **Sélection animée** : le clic (C#) bascule la classe `A_inventory-slot--selected` ;
  l'animation vient de `transition-*` en USS.
- **Panneau détail responsive** : UI Toolkit n'a **pas** de media queries USS. Le reflow
  est donc piloté par un `GeometryChangedEvent` sur `#body` qui ajoute/retire
  `A_inventory__body--narrow`. Cette classe passe le corps de `row` à `column` (le détail
  descend sous la grille) et élargit le détail à toute la largeur. Seuil réglable dans l'Inspector.

## Utilisation

1. GameObject + `UIDocument`, Source Asset = `InventoryScreen.uxml`.
2. Ajouter le composant `InventoryScreen`.
3. Assigner `InventorySlot.uxml` au champ **Slot Template**.
4. Remplir la liste **Items** (nom, count, icône optionnelle `Texture2D`, description).

## API UI Toolkit utilisées

| API | État |
|---|---|
| `UIDocument`, `rootVisualElement` | certain |
| `VisualElement.Q<T>(name)` | certain |
| `VisualTreeAsset.CloneTree()` (retourne un `TemplateContainer`) | certain |
| Reparentage via `VisualElement.Add()` / `Clear()` | certain |
| `RegisterCallback<ClickEvent>` / `RegisterCallback<GeometryChangedEvent>` / `UnregisterCallback` | certain |
| `GeometryChangedEvent.newRect` | certain |
| `AddToClassList` / `RemoveFromClassList` / `EnableInClassList` | certain |
| `style.display = DisplayStyle.Flex / None` | certain |
| `style.backgroundImage = new StyleBackground(Texture2D)` | certain |
| `style.backgroundImage = StyleKeyword.None` (conversion implicite `StyleKeyword` → `StyleBackground`) | certain |
| `Label.text` | certain |

## Propriétés USS utilisées

| Propriété | État |
|---|---|
| `flex-grow`, `flex-shrink`, `flex-direction`, `flex-wrap`, `align-content`, `align-items`, `justify-content` | certain |
| `width`, `height`, `min-width` | certain |
| `margin-*`, `padding-*` | certain |
| `background-color` | certain |
| `border-*-width`, `border-*-color`, `border-*-radius` | certain |
| `color`, `font-size`, `-unity-font-style`, `-unity-text-align`, `white-space` | certain |
| `position: absolute`, `top`, `right` | certain |
| `display: none` | certain |
| `scale` (transform) | certain |
| `-unity-background-scale-mode: scale-to-fit` | certain |
| `transition-property`, `transition-duration`, `transition-timing-function` | certain |
| Pseudo-classe `:hover` | certain |
| Sélecteur descendant (`.A_inventory__body--narrow .A_inventory__detail`) | certain |

## Hypothèses

- La grille est peuplée depuis une liste `Item` sérialisée sur le composant (données simples,
  aucun asset requis). On aurait pu utiliser un `ScriptableObject` + `CreateAssetMenu` (préfixe
  `A_`) mais ce n'était pas strictement nécessaire pour la démo.
- Le seuil « écran étroit » par défaut est 620 px ; il correspond à la largeur du corps
  (grille + détail), pas à la résolution écran.
- `-unity-background-scale-mode` s'applique bien à un `background-image` de `VisualElement`
  (icônes). Correct dans Unity 6.

## Limites connues

- Non testé : pas d'éditeur Unity ni de compilation dans cet environnement. Aucune vérification runtime.
- Les `.meta` des nouveaux fichiers seront générés par l'éditeur à l'import ; ils ne sont pas créés ici.
- La navigation clavier/manette n'est pas câblée (les slots sont `focusable` mais la sélection
  passe uniquement par `ClickEvent`). L'état `:hover` ne réagit pas au focus clavier.
- `GeometryChangedEvent` se déclenche après la première mise en page : au tout premier rendu,
  un très bref flash dans la disposition non-étroite est théoriquement possible avant le premier reflow.
- Le nombre de colonnes n'est pas « justifié » (pas de répartition de l'espace résiduel) ;
  les slots sont alignés à gauche via `align-content: flex-start`.
