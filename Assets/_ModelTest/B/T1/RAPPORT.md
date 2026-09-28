# RAPPORT - T1 : contrôle StatBar

Version Unity ciblée : **6000.5.0f1** (lue dans `ProjectSettings/ProjectVersion.txt`).
Note : le CLAUDE.md du projet mentionne 6000.2.6f2 ; c'est ProjectVersion.txt qui fait foi. Toutes les API utilisées existent depuis Unity 6000.0 (voire 2023.2), donc le code vaut pour les deux versions.

Rien n'a été compilé ni testé : je n'avais ni éditeur Unity ni compilateur.

## Fichiers

| Fichier | Rôle |
|---|---|
| `StatBar.cs` | Contrôle `ModelTest.B.T1.StatBar` (`[UxmlElement]`, attributs UXML, `INotifyValueChanged<float>`, liaison de données à l'exécution) |
| `StatBar.uss` | Styles, toutes les classes préfixées `B_` |
| `StatBarExample.uxml` | Exemple avec 4 barres (couleur USS par défaut, couleur par attribut, format personnalisé, barre vide) |
| `StatBarDemo.cs` | Optionnel : MonoBehaviour qui anime la barre "health" d'un `UIDocument` |

Aucun asmdef : le dossier est sous `Assets/` hors de `Assets/Scripts`, donc il compile dans `Assembly-CSharp`. Le module `com.unity.modules.uielements` est bien présent dans le manifest.

## Attributs UXML

| Attribut | Type | Défaut | Effet |
|---|---|---|---|
| `label` | string | `"Stat"` | Texte à gauche. Chaîne vide = label masqué (`display: none`) |
| `max-value` | float | `100` | Valeur max. `<= 0` donne une barre vide |
| `value` | float | `100` | Valeur courante |
| `bar-color` | Color | transparent | Couleur du remplissage. Si alpha = 0, c'est la couleur USS de `.B_stat-bar__fill` qui s'applique |
| `value-format` | string | `{0:0}/{1:0}` | Format composite : `{0}` = valeur, `{1}` = max. Culture invariante |

Classes USS : `B_stat-bar`, `B_stat-bar__label`, `B_stat-bar__track`, `B_stat-bar__fill`, `B_stat-bar__value`, plus les modificateurs `B_stat-bar--empty` et `B_stat-bar--full`.

## API UI Toolkit / C# utilisées

| API | Statut |
|---|---|
| `[UxmlElement]` sur une classe `partial` dérivée de `VisualElement` | certain |
| `[UxmlAttribute]` sur des propriétés (nommage kebab-case automatique : `maxValue` -> `max-value`) | certain |
| `[UxmlAttribute]` avec des types `string`, `float`, `Color` | certain |
| Format accepté par `Color` en UXML (`#RRGGBB` / `#RRGGBBAA`) | incertain : le hexadécimal est sûr, les autres notations (noms de couleur, `rgb()`) ne sont pas garanties |
| `VisualElement.Add`, `AddToClassList`, `EnableInClassList`, `pickingMode`, `PickingMode.Ignore` | certain |
| `Label`, `TextElement.text` | certain |
| `IStyle.width = Length.Percent(...)` (conversion implicite en `StyleLength`) | certain |
| `IStyle.backgroundColor = new StyleColor(Color)` / `new StyleColor(StyleKeyword.Null)` | certain |
| `IStyle.display = DisplayStyle.None/Flex` | certain |
| `INotifyValueChanged<float>` (`value`, `SetValueWithoutNotify`) | certain |
| `ChangeEvent<float>.GetPooled(prev, new)`, `evt.target = this`, `SendEvent` | certain |
| `RegisterValueChangedCallback` / `UnregisterValueChangedCallback` (extensions `INotifyValueChanged<T>`) | certain |
| `Unity.Properties.CreatePropertyAttribute` | certain |
| `BindingId` (conversion implicite depuis `string`) | certain |
| `VisualElement.NotifyPropertyChanged(in BindingId)` (protected) | certain (API de liaison de Unity 6, ajoutée en 2023.2) |
| `UIDocument.rootVisualElement`, `UQueryExtensions.Q<T>(name)` | certain |

## Propriétés USS utilisées

| Propriété | Statut |
|---|---|
| `flex-direction`, `align-items`, `flex-grow` | certain |
| `min-height`, `min-width`, `height`, `width` (via inline) | certain |
| `margin-*`, `padding-*` | certain |
| `background-color`, `color` | certain |
| `border-width`, `border-color`, `border-radius` | certain |
| `overflow: hidden` | certain |
| `-unity-text-align`, `-unity-font-style` | certain |
| `transition-property`, `transition-duration`, `transition-timing-function` (`ease-out`) | certain |
| Sélecteur descendant `.a .b` | certain |

UXML : balise `<Style src="StatBar.uss" />` avec un chemin relatif. Statut : certain (les chemins relatifs sont acceptés ; UI Builder réécrira sans doute le chemin au format `project://database/...`). Préfixe `xmlns:b="ModelTest.B.T1"` pour la balise `<b:StatBar>` : certain.

## Hypothèses

- La valeur est stockée **sans bornage**. Elle n'est ramenée à [0, max] que pour l'affichage. Raison : l'ordre d'application des attributs UXML n'est pas garanti. Si `value="300"` était appliqué avant `max-value="500"`, un bornage dans le setter tronquerait la valeur à 100 (le max par défaut).
- La couleur passée par attribut est posée en style inline, qui l'emporte toujours sur l'USS. Pour qu'un thème USS puisse agir, la valeur par défaut est `Color.clear` : dans ce cas on retire le style inline (`StyleKeyword.Null`).
- Le `ChangeEvent<float>` n'est envoyé que si l'élément est attaché à un panel. Rien n'est émis pendant la désérialisation UXML.
- Pas de valeur minimale : la barre va de 0 à `max-value`.

## Limites connues

- Rien n'a été compilé ni testé en éditeur.
- Si on instancie le contrôle uniquement en C# (`new StatBar()`), il faut ajouter `StatBar.uss` soi-même (`styleSheets.Add(...)`). Le contrôle ne charge pas sa feuille de style tout seul.
- La transition sur `width` anime aussi le premier affichage, et tous les changements de `value`. Pour une mise à jour instantanée, retirer `width` de `transition-property`.
- `value-format` accepte n'importe quel format composite .NET. Un format invalide (`FormatException`) retombe sur le format par défaut.
- Les fichiers `.meta` ne sont pas fournis : Unity les générera à l'import.
- `StatBarDemo` utilise `Debug.Log` : c'est une démo, pas du code de production.
