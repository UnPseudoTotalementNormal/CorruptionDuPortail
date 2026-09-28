# RAPPORT — T1 : Contrôle UI Toolkit réutilisable "StatBar"

Namespace : `ModelTest.A.T1`
Version Unity (source de vérité `ProjectSettings/ProjectVersion.txt`) : **6000.5.0f1** (Unity 6).
Note : le `CLAUDE.md` mentionne 6000.2.6f2, mais j'ai suivi la règle 1 et utilisé la version du fichier `ProjectVersion.txt`.

## Fichiers produits
- `StatBar.cs` — le contrôle personnalisé (VisualElement).
- `StatBar.uss` — les styles (classes préfixées `A_`).
- `StatBarExample.uxml` — un exemple d'utilisation en UXML.

## API UI Toolkit utilisées
- `[UxmlElement]` sur classe `partial` — **certain** (générateur de fabrique UXML, disponible depuis Unity 2023.2 / Unity 6).
- `[UxmlAttribute("nom")]` sur propriétés — **certain** (même système, remplace `UxmlTraits`/`UxmlFactory`).
- `Color` comme type d'attribut UXML (`fill-color`) — **certain** : `Color` fait partie des types sérialisables pris en charge par le système `UxmlSerializedData` (Unity 6). Accepte `#RRGGBB`/`#RRGGBBAA` en UXML.
- `float`/`string` comme types d'attribut UXML — **certain**.
- `VisualElement`, `Label`, `Add`, `AddToClassList`, `pickingMode = PickingMode.Ignore` — **certain**.
- `IStyle.width` avec `Length.Percent(...)` — **certain**.
- `IStyle.backgroundColor` (assignation d'un `Color` -> `StyleColor` implicite) — **certain**.

## Propriétés / concepts USS utilisés
- `flex-direction`, `flex-grow`, `flex-shrink`, `align-items` — **certain**.
- `width`, `height`, `min-width` — **certain**.
- `margin-*`, `padding` — **certain**.
- `background-color` — **certain**.
- `border-*-radius` (les 4 coins) — **certain** (UI Toolkit ne supporte pas le raccourci `border-radius`, d'où les 4 propriétés).
- `overflow: hidden` — **certain**.
- `color`, `-unity-text-align`, `-unity-font-style`, `font-size` — **certain**.
- Fonctions couleur `rgb(...)` — **certain**.

## UXML
- `<Style src="StatBar.uss" />` pour charger la feuille de style — **certain**.
- Espace de noms XML `xmlns:mt="ModelTest.A.T1"` pointant vers l'élément généré `<mt:StatBar .../>` — **certain** (mécanisme standard des `[UxmlElement]`).
- `noNamespaceSchemaLocation` : chemin relatif indicatif vers le schéma généré par l'éditeur — **incertain** (le chemin peut ne pas exister/résoudre selon la structure du projet ; sans impact au runtime, purement pour l'auto-complétion en éditeur). À régénérer via l'éditeur si besoin.

## Hypothèses
- Attributs UXML nommés en kebab-case (`max-value`, `fill-color`) mappés vers des propriétés PascalCase — comportement par défaut du système `[UxmlAttribute]` quand un nom explicite est fourni.
- La couleur de remplissage est appliquée en style inline depuis le C# (pas via USS) pour rester pilotable par attribut/API.
- La largeur du remplissage est exprimée en pourcentage de la piste (`track`), qui utilise `overflow: hidden` + rayons pour un rendu arrondi propre.

## Limites connues
- Non compilé ni testé : pas d'éditeur Unity ni de compilation disponibles dans cet environnement.
- Le formatage de la valeur numérique est fixe (`entier / entier` via `Mathf.RoundToInt`). Aucun attribut de format décimal exposé.
- Pas d'animation/tween sur les changements de valeur (hors scope de la tâche).
- Aucun fichier `.meta` généré (ils seront créés par l'éditeur à l'import).
- `MaxValue` est bornée à un minimum de `0.0001` pour éviter une division par zéro ; les valeurs `<= 0` ne sont donc pas honorées telles quelles.
