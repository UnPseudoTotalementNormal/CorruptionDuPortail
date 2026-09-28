# Benchmark UI Toolkit — Résultats

> **⚠ SCORES PROVISOIRES.** Aucun fichier de ce benchmark n'a été compilé ni exécuté dans Unity. Les notes ci-dessous reposent uniquement sur une relecture statique. Elles restent provisoires tant que le projet n'a pas été compilé dans Unity et que les points de la section « Points à vérifier à la compilation » n'ont pas été contrôlés. Une seule erreur de compilation confirmée peut modifier sensiblement une note.

## Protocole

- **Tirage** : `printf "candidat-48\ncandidat-55\n" | shuf > .model-test/mapping.txt`. La ligne 1 correspond à A, la ligne 2 à B.
- **Consignes** : pour chaque tâche, les deux candidats ont reçu le même texte en parallèle : la tâche mot pour mot, le dossier de sortie et le namespace. Aucune aide ni correction n'a été apportée.
- **Commits** : un commit par tâche (T1 `7835419`, T2 `aa2c4ca`, T3 `4be8deb`, T4 `aecca4a`).
- **Évaluation** : le subagent `relecteur` a évalué A et B à l'aveugle, sans recevoir le mapping. Sa grille : API (4), obsolète (2), cohérence (2), honnêteté du RAPPORT (1), consigne (1). Pour T4 : pertinence (5), faux positifs (3), priorisation (2).
- **Contrôle** : l'état de l'arbre git (fichiers suivis, non suivis et ignorés) et `HEAD` sont identiques avant et après le passage du relecteur. Il n'a modifié aucun fichier.

## Mapping révélé

| Lettre | Subagent | Modèle configuré |
|---|---|---|
| A | candidat-48 | `claude-opus-4-8` |
| B | candidat-55 | `claude-opus-5-5` |

Le harnais n'expose pas le modèle qui a effectivement servi chaque subagent. Il est donc impossible de confirmer qu'il n'y a eu aucune substitution ou fallback.

## Scores (provisoires)

| Tâche | A — candidat-48 | B — candidat-55 |
|---|---|---|
| T1 StatBar | 9,0 | 10 |
| T2 PlayerStats + data binding runtime | 8,0 | 10 |
| T3 Inventaire responsive | 8,5 | 9,0 |
| T4 Analyse du code UITK existant | 6,5 | 9,5 |
| **Total / 40** | **32,0** | **38,5** |

### Justifications principales du relecteur (résumé)

- **T1**
  - A perd 0,5 pt sur l'honnêteté. Son RAPPORT affirme comme « certain » que le raccourci `border-radius` n'existe pas en USS. Le projet l'utilise pourtant (`Assets/UI/Screens/EmoteWheel/EmoteWheel.uss:63`).
  - A perd 0,5 pt sur la consigne : le setter `MaxValue` ne re-borne pas la valeur, ce qui peut afficher « 80 / 50 ».
  - B : aucun retrait.
- **T2**
  - A perd 1 pt d'API. Il manque `using UnityEngine.UIElements;` dans `A/T2/PlayerStats.cs`, alors que le fichier utilise `INotifyBindablePropertyChanged` et `BindablePropertyChangedEventArgs`. Erreur CS0246 probable ; à confirmer à la compilation.
  - A perd 1 pt d'honnêteté : cette erreur est marquée « certain » dans son RAPPORT.
  - B : aucun retrait.
- **T3**
  - Les deux candidats perdent 1 pt pour l'usage de `-unity-background-scale-mode`, jugé obsolète au profit de `background-size`/`-position`/`-repeat`. Le niveau d'avertissement en 6000.5 reste à vérifier.
  - A perd en plus 0,5 pt d'honnêteté pour avoir marqué cette propriété « certain » sans citer d'alternative.
- **T4**
  - A ne relève aucun faux positif, mais manque les bugs visibles par le joueur trouvés par B :
    - masquage du label de la roue d'emotes neutralisé par la cascade USS ;
    - course Open/Close dans la même frame ;
    - injection de rich text ;
    - `_everBuilt` jamais remis à zéro.
  - La priorisation de A est jugée incohérente : son P1 contient du uGUI hors périmètre et des spikes que A qualifie lui-même de faible priorité.
  - B perd 0,5 pt pour un faux positif : `_accent` est bien lu en `RoleCardElement.cs:88`.

Ces justifications sont celles du relecteur. Le manager ne les a pas contre-vérifiées.

## Incidents

| Type | A — candidat-48 | B — candidat-55 |
|---|---|---|
| Questions posées au manager | Aucune | Aucune |
| Échecs / tâches non rendues | Aucun | Aucun |
| Fichiers écrits hors du dossier de sortie | Aucun (vérifié par `git status` après chaque tâche) | Aucun (idem) |
| Fichiers existants du projet modifiés (T4) | Aucun (`git diff` vide) | Aucun (`git diff` vide) |
| Livrables non demandés | — | `B/T1/StatBarDemo.cs` : MonoBehaviour de démo facultatif, placé dans son dossier, qui utilise `Update()` |
| Namespace non utilisé | T4 : aucun C# produit, signalé par le candidat | T4 : aucun C# produit, signalé par le candidat |

Incidents de contexte, qui ne sont imputables à aucun candidat :

- **Version Unity divergente.** `ProjectSettings/ProjectVersion.txt` indique **6000.5.0f1**, alors que `CLAUDE.md` indique **6000.2.6f2**. Les deux candidats ont signalé l'écart et suivi `ProjectVersion.txt`, comme le demandait leur consigne. `CLAUDE.md` est à mettre à jour.
- **Biais possible d'évaluation.** Le relecteur est configuré sur le même modèle que candidat-55 (`claude-opus-5-5`). Un biais d'auto-préférence ne peut pas être exclu, même en aveugle. Pour neutraliser ce biais, il faudrait une contre-relecture par un autre modèle ou par un humain.
- **Exécution en arrière-plan.** Le harnais a exécuté la plupart des invocations en arrière-plan malgré une demande de premier plan. Le seul effet est le délai d'attente ; les textes transmis sont restés identiques.
- **Échantillon limité.** Quatre tâches, une exécution par candidat. L'écart mesuré ne dit rien de la variance d'une exécution à l'autre.

## Points à vérifier à la compilation (liste du relecteur)

### A — candidat-48

1. **T2, bloquant probable** : `A/T2/PlayerStats.cs:22,30,115`. On attend CS0246 sur `INotifyBindablePropertyChanged` et `BindablePropertyChangedEventArgs` (`using UnityEngine.UIElements;` manquant).
2. **T2** : `A/T2/PlayerStatsScreen.cs:113`. Vérifier que `SetBinding("style.width", ...)` est accepté et met la largeur à jour.
3. **T2** : `A/T2/PlayerStatsScreen.cs:110-111`. Vérifier l'inférence de type de `sourceToUiConverters.AddConverter((ref float ratio) => new StyleLength(...))`.
4. **T2** : vérifier qu'une édition dans l'Inspector en Play Mode (`OnValidate` → notification) rafraîchit l'UI.
5. **T1** : vérifier que `fill-color="#4AA859"` est bien parsé en `Color`, et que l'ordre de désérialisation `max-value` / `value` ne tronque pas `value="640"` avec `max-value="1000"` (`A/T1/StatBarExample.uxml:19`).
6. **T1/T2/T3** : vérifier que les attributs d'en-tête UXML `xsi` / `noNamespaceSchemaLocation` ne produisent pas d'avertissement d'import.
7. **T3** : vérifier les avertissements éventuels sur `-unity-background-scale-mode` (`A/T3/InventoryScreen.uss:111,200`) et la transition sur le raccourci `border-color` (l.78).
8. **T3** : vérifier que le wrap de `#grid` dans le ScrollView vertical fonctionne et que le reflow sous 620 px bascule sans oscillation.
9. **T4** : vérifier le warning `[Obsolete]` sur `style.unityBackgroundScaleMode` (`Assets/Scripts/UI/Cards/RoleCardElement.cs:46`) et tester disable puis enable des UIDocument.

### B — candidat-55

1. **T1** : vérifier que le générateur UXML dérive bien `max-value`, `bar-color` et `value-format` des propriétés `maxValue`, `barColor` et `valueFormat`.
2. **T1** : vérifier que la valeur `value-format="{0:P0}"` n'est pas altérée par le parseur UXML (`B/T1/StatBarExample.uxml:9`).
3. **T1** : vérifier que `NotifyPropertyChanged(in BindingId)` est accessible depuis la sous-classe et que la transition sur `width` en pourcentage fonctionne.
4. **T2** : vérifier que `SetBinding("value", ...)` sur `ProgressBar` fonctionne (`B/T2/PlayerStatsScreen.cs:30,95`).
5. **T2** : vérifier la combinaison `IDataSourceViewHashProvider` + `INotifyBindablePropertyChanged` : chaque clic et chaque édition dans l'Inspector doivent rafraîchir l'UI.
6. **T2** : vérifier que `[DontCreateProperty]` sur des champs privés du ScriptableObject ne gêne pas la génération du property bag.
7. **T2/T3** : vérifier l'ordre `OnEnable` de `PlayerStatsScreen` / `InventoryScreen` par rapport à `UIDocument`, pour que `Q()` ne renvoie pas null au premier chargement.
8. **T3** : vérifier le wrap via `.unity-scroll-view__content-container`, les easings `ease-out-back` / `ease-out-cubic`, l'attribut `horizontal-scroller-visibility="Hidden"` et le déclenchement de `NavigationSubmitEvent` sur un slot qui a le focus.
9. **T3** : vérifier les avertissements éventuels sur `-unity-background-scale-mode` (`B/T3/InventoryScreen.uss:75,209`).
10. **T4** (vérifications en Play Mode) :
    - reproduire le label de roue d'emotes toujours visible et la course Open/Close (tap très rapide) ;
    - vérifier que `tooltip` ne s'affiche pas sur un panel runtime en 6000.5 ;
    - vérifier le comportement de `_root.panel` de l'InfoTable après changement de PanelSettings ;
    - vérifier la couverture des glyphes de LiberationSans.

### Commun

- Les fichiers `.meta` n'ont pas été générés. Unity les créera à l'import, ce qui provoquera une première recompilation de `Assembly-CSharp`.
- Aucun asmdef n'a été créé : tout le code du benchmark compile dans `Assembly-CSharp`. Une erreur dans `Assets/_ModelTest` (par exemple le point A-1) **bloquera la compilation de tout le projet**. Pour que le jeu reste compilable pendant les vérifications, il faut isoler ou supprimer le dossier en cas d'erreur.
