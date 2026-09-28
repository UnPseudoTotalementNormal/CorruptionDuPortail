# RAPPORT — T4 : Analyse du code UI Toolkit existant

Tâche en **lecture seule** : aucune modification des fichiers du projet. Livrables écrits
uniquement dans `Assets/_ModelTest/A/T4/` : `ANALYSE.md` (l'analyse priorisée) et ce `RAPPORT.md`.

## Contexte

- Version Unity retenue : **6000.5.0f1** (`ProjectSettings/ProjectVersion.txt`). `CLAUDE.md`
  indique `6000.2.6f2` → **incohérence signalée** ; j'ai suivi ProjectVersion.txt (règle 1).
- Périmètre : code UITK du projet hors `Assets/_ModelTest`, `Assets/Samples`, `Assets/Plugins`.
- Aucune compilation, aucun test, aucun éditeur : analyse **statique** uniquement.

## API UI Toolkit rencontrées (certitude de validité en Unity 6)

| API | Statut |
|---|---|
| `UIDocument.rootVisualElement`, `panelSettings`, `PanelSettings.targetTexture`, `SetScreenToPanelSpaceFunction` | certain |
| `VisualElement.Q<T>()` / `Query<T>()` / `.ForEach` | certain |
| `RegisterCallback<T>` / `TrickleDown` (ClickEvent, PointerDownEvent, GeometryChangedEvent) | certain |
| `schedule.Execute(...).ExecuteLater(ms)` / `.Every(ms)` / `IVisualElementScheduledItem.Pause/Resume` | certain |
| `experimental.animation.Start(...).Ease(Easing.OutCubic)` (namespace `UnityEngine.UIElements.Experimental`) | certain (API expérimentale, présente et non obsolète) |
| `generateVisualContent` + `MeshGenerationContext` + `Painter2D` (`Arc`, `BeginPath`, `Fill`, `Stroke`, `Angle`, `AngleUnit`, `ArcDirection`) | certain |
| `AddToClassList` / `RemoveFromClassList` / `EnableInClassList` / `ClassListContains` | certain |
| `style.*` inline + `StyleKeyword.Null` / `StyleKeyword.None` / `StyleBackground` / `StyleColor` / `StyleTranslate` / `StyleScale` / `Length` / `LengthUnit` | certain |
| `PickingMode.Ignore/Position` | certain |
| `ScrollView` / `ScrollViewMode` / `verticalScroller` / `scrollOffset` / `contentContainer` | certain |
| `Button`, `Label`, `VisualElement`, `Children()`, `BringToFront`, `RemoveFromHierarchy`, `worldBound`, `resolvedStyle`, `contentRect` | certain |
| `style.unityBackgroundScaleMode` (C#) | **incertain** — probablement `[Obsolete]` depuis 2022.2 au profit de `background-size` ; non vérifiable sans éditeur |

## Propriétés / valeurs USS rencontrées

| Propriété USS | Statut |
|---|---|
| `background-color`, `border-*-color`, `border-radius`, `border-*-width`, `padding*`, `margin*`, `color`, `justify-content`, `flex-*` | certain (valides UITK) |
| `transition-property` / `transition-duration` / `transition-timing-function` / `transition-delay` (sur `opacity`, `translate`, `scale`) | certain |
| `var(--token)` (variables custom) | certain |
| `-unity-background-scale-mode: scale-to-fit` | **incertain** — legacy/probablement déprécié au profit de `background-size` |
| `-unity-font-style`, `-unity-text-align` (via `unityFontStyleAndWeight`/`unityTextAlign` côté C#) | certain |

Aucune propriété non supportée (box-shadow, gradient, gap, @media, ::before/::after, calc(), etc.)
n'a été trouvée dans les USS du projet — les commentaires d'en-tête des USS rappellent d'ailleurs
explicitement ces limites de UITK.

## Hypothèses posées

1. « Code UI Toolkit existant du projet » = `Assets/Scripts/UI/**` + `Assets/Scripts/NoteSystem/NoteChoosePanel.cs` ;
   `Assets/Samples` et `Assets/Plugins` sont du tiers et exclus.
2. `NoteChoosePanel` est majoritairement uGUI ; inclus car il importe `UnityEngine.UIElements`.
3. Les fichiers `Spike*` sont annoncés « THROWAWAY » dans leurs propres commentaires → leurs
   défauts sont réels mais rétrogradés en priorité.
4. La gravité des points « perf » dépend du nombre réel de joueurs/rôles et de la fréquence des
   événements — non mesurable sans profiler ; estimations qualitatives.

## Limites connues de cette analyse

- Aucune compilation ni exécution : les diagnostics de type « racine obsolète après reconstruction
  d'arbre » (P3) et l'obsolescence de `unityBackgroundScaleMode` (P4) demandent une vérification en
  éditeur.
- Je n'ai pas lu tous les data sources (`IInfoTableDataSource`, `ILobbyRolesDataSource`,
  `IMessageJournalDataSource`, `InfoTableModel`) ni l'usage réel de `IPanelComponent` : certaines
  conclusions sur les impacts (fréquence des rebuilds, appelants des méthodes non implémentées)
  sont donc conditionnelles.
- L'analyse est ciblée bugs / API obsolètes / perf / callbacks, conformément à la demande ; elle
  n'est pas une revue exhaustive de tout le code (logique métier, accessibilité, layout visuel).
