# RAPPORT — T2 : ScriptableObject "PlayerStats" + écran UITK en data binding runtime

Namespace : `ModelTest.A.T2`
Version Unity (source de vérité `ProjectSettings/ProjectVersion.txt`) : **6000.5.0f1** (Unity 6).
Note : le `CLAUDE.md` mentionne 6000.2.6f2, mais la règle 1 impose la version du fichier `ProjectVersion.txt`. Le data binding runtime de UI Toolkit existe depuis Unity 6 (6000.0), donc disponible ici.

## Fichiers produits
- `PlayerStats.cs` — le ScriptableObject (PV, PV max, niveau, nom) + propriétés dérivées, source de données pour le binding.
- `PlayerStatsScreen.cs` — MonoBehaviour qui relie le SO à l'UI via `SetBinding` (aucun `Update()` manuel).
- `PlayerStatsScreen.uxml` — la mise en page de l'écran (éléments nommés).
- `PlayerStatsScreen.uss` — les styles (classes préfixées `A_`).

## Principe retenu (répond à la contrainte "pas de mise à jour manuelle dans Update()")
- Le SO expose ses données au système de binding via `[CreateProperty]` (`Unity.Properties`).
- Le SO implémente `INotifyBindablePropertyChanged` : chaque modification déclenche `propertyChanged`, ce qui pousse la mise à jour vers l'UI liée. Aucun polling par frame.
- Les bindings utilisent `BindingMode.ToTarget` (source -> UI), déclenchés par la notification (comportement par défaut `updateTrigger = OnSourceChanged`, non surchargé).
- Les modifications faites **dans l'Inspector** (qui écrivent les champs sérialisés sans passer par les setters C#) sont propagées via `OnValidate()` qui appelle une notification de toutes les propriétés.

## API UI Toolkit / binding utilisées
- `ScriptableObject`, `[CreateAssetMenu(fileName, menuName)]` (menuName préfixé `A_ModelTest/...`) — **certain**.
- `Unity.Properties.CreatePropertyAttribute` (`[CreateProperty]`) — **certain** (expose une propriété au binding runtime, Unity 6).
- `Unity.Properties.INotifyBindablePropertyChanged` avec `event EventHandler<BindablePropertyChangedEventArgs> propertyChanged` — **certain**.
- `Unity.Properties.BindablePropertyChangedEventArgs(string propertyName)` — **certain**.
- `Unity.Properties.PropertyPath(string)` — **certain**.
- `UnityEngine.UIElements.DataBinding` avec `dataSourcePath`, `bindingMode`, `sourceToUiConverters` — **certain**.
- `UnityEngine.UIElements.BindingMode.ToTarget` — **certain**.
- `VisualElement.dataSource` (héritée par les descendants) — **certain**.
- `VisualElement.SetBinding(BindingId, Binding)` avec conversion implicite `string -> BindingId` : `SetBinding("text", ...)` — **certain**.
- `SetBinding("style.width", ...)` (binding vers une propriété de style résolue) — **incertain** : le binding de propriétés de style existe en Unity 6, mais je n'ai pas pu vérifier la casse/l'identifiant exact `style.width` sans éditeur. À valider ; repli possible : binder sur une propriété custom ou utiliser un `[CreateProperty]` renvoyant directement une largeur.
- `ConverterGroup.AddConverter(TypeConverter<TSource,TDestination>)` avec un délégué `(ref float ratio) => new StyleLength(...)` — **incertain** : la signature `delegate TDestination TypeConverter<TSource,TDestination>(ref TSource)` est celle documentée, mais la surcharge exacte de `AddConverter` est à confirmer en compilation.
- `UIDocument`, `UIDocument.rootVisualElement`, `[RequireComponent(typeof(UIDocument))]` — **certain**.
- `VisualElement.Q<T>(name)` — **certain**.
- `StyleLength`, `Length.Percent(...)` — **certain**.
- `[ContextMenu]` pour les hooks de test dans l'Inspector — **certain**.

## Propriétés / concepts USS utilisés
- `flex-grow`, `flex-shrink`, `flex-direction`, `align-items`, `justify-content` — **certain**.
- `width`, `height`, `min-width` (dont `width: 100%`, `width: 0`) — **certain**.
- `padding`, `margin-*` — **certain**.
- `background-color`, fonctions `rgb(...)` — **certain**.
- `border-*-radius` (4 coins), `border-*-width` (4 côtés), `border-*-color` (4 côtés) — **certain** (UI Toolkit ne supporte pas les raccourcis `border-radius`/`border-width`).
- `overflow: hidden` — **certain**.
- `color`, `font-size`, `-unity-font-style`, `-unity-text-align` — **certain**.

## UXML
- `<Style src="PlayerStatsScreen.uss" />` — **certain**.
- Éléments standard `ui:VisualElement`, `ui:Label` avec `name`/`class`/`text` — **certain**.
- Les bindings sont posés côté C# (pas en UXML) pour pouvoir attacher le convertisseur float->pourcentage et affecter la source (l'asset SO) au runtime.
- `noNamespaceSchemaLocation` : chemin indicatif vers le schéma éditeur — **incertain** (peut ne pas résoudre selon la structure ; sans impact runtime, uniquement auto-complétion éditeur).

## Hypothèses
- Les scripts sont compilés dans `Assembly-CSharp` (aucun `.asmdef` dans `Assets/_ModelTest/`), qui référence par défaut le module `UnityEngine.UIElements` et `Unity.Properties`. Si un `.asmdef` était ajouté plus tard, il faudrait référencer ces modules explicitement.
- Le binding est installé dans `Start()` : pour les objets présents au chargement de scène, tous les `OnEnable` (dont celui de `UIDocument` qui construit l'arbre) ont déjà tourné, donc `rootVisualElement` est disponible.
- Les propriétés dérivées (`HealthText`, `HealthRatio`, `LevelText`) sont notifiées explicitement lorsque leurs sources changent, et toutes via `OnValidate`.

## Limites connues
- Non compilé ni testé : ni éditeur Unity ni compilation dans cet environnement.
- Deux points marqués **incertain** ci-dessus (`SetBinding("style.width", ...)` et la surcharge `AddConverter`) sont à valider en compilation dans l'éditeur.
- Si l'`UIDocument` est désactivé puis réactivé, il reconstruit son arbre et perd les bindings posés en code : appeler `PlayerStatsScreen.Bind()` à nouveau (méthode publique fournie) dans ce cas.
- Aucun asset `.asset` de `PlayerStats` n'est créé (il doit l'être via le menu `Assets/Create/A_ModelTest/T2/Player Stats`), ni le GameObject/`UIDocument` de scène (à assembler dans l'éditeur : assigner `PlayerStatsScreen.uxml` comme Source Asset et référencer l'asset `PlayerStats`).
- Aucun fichier `.meta` généré (créés par l'éditeur à l'import).
- Le binding est unidirectionnel (SO -> UI). Pas d'édition depuis l'UI (hors scope).
