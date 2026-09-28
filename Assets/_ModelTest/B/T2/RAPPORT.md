# RAPPORT - T2 : PlayerStats + écran UI Toolkit en data binding runtime

Version Unity ciblée : **6000.5.0f1** (lue dans `ProjectSettings/ProjectVersion.txt`).
Le CLAUDE.md du projet indique 6000.2.6f2, mais c'est ProjectVersion.txt qui fait foi. Le système de liaison de données à l'exécution (`dataSource`, `DataBinding`, `SetBinding`) existe depuis 2023.2, donc le code vaut pour les deux versions.

Rien n'a été compilé ni testé : je n'avais ni éditeur Unity ni compilateur.

## Fichiers

| Fichier | Rôle |
|---|---|
| `PlayerStats.cs` | ScriptableObject `ModelTest.B.T2.PlayerStats` (nom, PV, PV max, niveau). Sert directement de source de données : `[CreateProperty]`, `INotifyBindablePropertyChanged`, `IDataSourceViewHashProvider` |
| `PlayerStatsScreen.cs` | MonoBehaviour posé à côté d'un `UIDocument`. Donne l'asset comme `dataSource` au conteneur et crée les `DataBinding` sur les enfants. **Pas d'`Update()`** |
| `PlayerStatsScreen.uxml` | Mise en page : nom, niveau, barre de PV, texte des PV et 3 boutons de démo |
| `PlayerStatsScreen.uss` | Styles, toutes les classes préfixées `B_` |

Aucun asmdef : le dossier est hors de `Assets/Scripts`, il compile donc dans `Assembly-CSharp`. Les `.meta` seront générés par Unity.

## Mise en place

1. Créer l'asset via `Create > B_ModelTest > T2 > Player Stats`.
2. Dans une scène, créer un GameObject avec un `UIDocument` (Panel Settings + Source Asset = `PlayerStatsScreen.uxml`) et y ajouter `PlayerStatsScreen`.
3. Assigner l'asset au champ `Stats`.
4. En Play Mode, modifier l'asset dans l'Inspector ou cliquer sur les boutons : l'UI se met à jour.

## Fonctionnement

- `PlayerStatsScreen.OnEnable` fait `container.dataSource = stats`. Chaque enfant hérite de cette source et reçoit un `DataBinding { dataSourcePath, bindingMode = ToTarget }` via `SetBinding`. L'`updateTrigger` reste à sa valeur par défaut, `OnSourceChanged`.
- Liaisons :

| Élément | Propriété cible | Chemin source |
|---|---|---|
| `Label#player-name` | `text` | `playerName` |
| `Label#player-level` | `text` | `levelLabel` ("Niveau N") |
| `Label#player-health-label` | `text` | `healthLabel` ("PV / PVmax PV") |
| `ProgressBar#player-health-bar` | `value` | `healthNormalized` (0..1, avec `lowValue = 0` / `highValue = 1`) |

- Détection des changements côté source :
  - Les setters C# (`playerName`, `currentHealth`, `maxHealth`, `level`, plus `TakeDamage` / `Heal` / `LevelUp`) incrémentent un compteur de version (`GetViewHashCode`). Ils lèvent aussi `propertyChanged` pour la propriété modifiée et ses propriétés dérivées : un changement de `currentHealth` notifie aussi `healthNormalized` et `healthLabel`.
  - Une modification dans l'Inspector ne passe pas par les setters. `OnValidate()` borne les valeurs, incrémente la version et notifie toutes les propriétés.
- Les propriétés dérivées sont des `string` et `float` prêtes à afficher. Il n'y a donc besoin d'aucun convertisseur (`ConverterGroup`) : on n'a pas à dépendre d'une conversion implicite `int -> string` ou `int -> float`.

## API UI Toolkit / C# utilisées

| API | Statut |
|---|---|
| `VisualElement.dataSource` (héritée par les enfants) | certain |
| `VisualElement.SetBinding(BindingId, Binding)` | certain |
| `DataBinding` : `dataSourcePath` (`PropertyPath`), `bindingMode` | certain |
| `BindingMode.ToTarget` | certain |
| Valeur par défaut `BindingUpdateTrigger.OnSourceChanged` (non écrite explicitement) | certain |
| `BindingId`, conversion implicite depuis `string` | certain |
| `Unity.Properties.PropertyPath(string)` | certain |
| `Unity.Properties.CreatePropertyAttribute` / `DontCreatePropertyAttribute` | certain |
| `INotifyBindablePropertyChanged` (`event EventHandler<BindablePropertyChangedEventArgs> propertyChanged`) | certain |
| `BindablePropertyChangedEventArgs(in BindingId)` (appelé avec un `string`, converti implicitement) | certain |
| `IDataSourceViewHashProvider.GetViewHashCode()` retournant `long` | certain |
| Liaison sur `Label.text` (propriété héritée de `TextElement`) | certain |
| Liaison sur `ProgressBar.value` (propriété de `AbstractProgressBar` exposée à la liaison) | incertain : je pense qu'elle est bien marquée `[CreateProperty]` en Unity 6, mais je ne l'ai pas vérifié dans cette version précise |
| Combinaison des deux interfaces : la version sert de filtre rapide, `propertyChanged` limite la mise à jour aux liaisons concernées | incertain dans le détail interne. Le résultat attendu reste le même, puisque la version change **et** les propriétés sont notifiées à chaque modification |
| `ProgressBar.lowValue` / `highValue` | certain |
| `Button.clicked` | certain |
| `UIDocument.rootVisualElement`, `UQueryExtensions.Q<T>(name)` | certain |
| `CreateAssetMenuAttribute(fileName, menuName)`, `MinAttribute`, `OnValidate` | certain |

UXML : `<Style src="PlayerStatsScreen.uss" />` avec un chemin relatif (certain). Attributs `low-value` / `high-value` / `value` de `ui:ProgressBar` (certain). Les liaisons sont créées en C#, pas dans l'UXML : je n'ai pas utilisé `<Bindings>` / `<ui:DataBinding>` ni `data-source` en UXML.

## Propriétés USS utilisées

| Propriété | Statut |
|---|---|
| `position: absolute`, `top`, `left`, `width` | certain |
| `padding`, `margin`, `margin-bottom` | certain |
| `background-color`, `color` (`rgb()` / `rgba()`) | certain |
| `border-width`, `border-color`, `border-radius` | certain |
| `flex-direction`, `justify-content: space-between`, `align-items`, `flex-grow` | certain |
| `font-size`, `-unity-font-style`, `-unity-text-align` | certain |
| Classes internes `.unity-progress-bar__background` et `.unity-progress-bar__progress` (sélecteur descendant) | certain sur les noms. Leur rendu exact dépend du thème par défaut |

## Hypothèses

- L'écran est lié **à l'asset lui-même**, pas à une copie (`Instantiate`). C'est ce qui permet à une modification de l'asset de se refléter dans l'UI. Contrepartie : en éditeur, les changements faits en Play Mode (boutons, gameplay) restent enregistrés dans l'asset après l'arrêt du Play Mode.
- Les invariants sont appliqués à l'écriture : `0 <= currentHealth <= maxHealth`, `maxHealth >= 1`, `level >= 1`. Baisser `maxHealth` sous `currentHealth` fait donc baisser `currentHealth`.
- Les textes (`"Niveau N"`, `"X / Y PV"`) sont en dur dans le ScriptableObject, faute de système de localisation imposé.
- Les boutons sont optionnels : s'ils manquent dans l'UXML, ils sont simplement ignorés. Ils ne font que modifier l'asset. Le rafraîchissement vient uniquement des liaisons.

## Limites connues

- Rien n'a été compilé ni testé en éditeur.
- `OnValidate` n'existe qu'en éditeur. Dans un build, seules les modifications par les setters C# sont notifiées, ce qui est le cas normal au runtime. Une écriture directe dans les champs sérialisés (réflexion, `SerializedObject` hors éditeur) ne serait pas détectée.
- Selon l'ordre d'exécution, `PlayerStatsScreen.OnEnable` peut s'exécuter avant que le `UIDocument` ait construit son arbre. C'est le schéma habituel de la doc, et il fonctionne dans le cas courant. Si le conteneur est introuvable, le script affiche une erreur explicite.
- Si le `UIDocument` est désactivé puis réactivé, il reconstruit son arbre. Il faut alors réactiver aussi `PlayerStatsScreen` pour recréer les liaisons.
- `playerName` modifié en C# n'est pas borné en longueur. Un nom très long peut déborder du header.
