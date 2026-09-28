# RAPPORT — R1 : extraction des règles de vote de `VoteState` vers la couche Domain

## Point d'attention préalable : version de l'éditeur

`ProjectSettings/ProjectVersion.txt` indique **6000.5.0f1**, alors que `CLAUDE.md` et `_bmad-output/project-context.md` annoncent **6000.2.6f2 (version exacte)**. D'après project-context, en cas d'écart il faut s'arrêter et le signaler à l'utilisateur. Je le signale ici. J'ai quand même poursuivi, parce que le travail ne touche que du C# pur (BCL + LINQ) et du NUnit, sans aucune API Unity propre à une version. Cet écart reste à trancher par l'équipe ; je n'ai rien modifié dans `ProjectVersion.txt`.

## Fichiers créés / modifiés

| Fichier | Nature | Pourquoi |
|---|---|---|
| `Assets/Scripts/Domain/VoteRules.cs` (+ `.meta`) | **nouveau** | POCO Domain, décisionnel uniquement (`sealed class`, sans état, constructeur public sans paramètre, comme `VoteTally` et `PowerUsability`). Il contient : `HasAlreadyVoted`, `CanVote` (la chaîne d'éligibilité ordonnée), `CountCastVotes`, `ShouldCloseEarly` (`castVotes >= eligibleVoters`), `ClampTimerForEarlyClose` et la constante `EarlyCloseTimerSeconds = 5f`. |
| `Assets/Scripts/Domain/VoterStatus.cs` (+ `.meta`) | **nouveau** | Struct en lecture seule qui porte l'état du votant sous forme de valeurs simples (`Exists`, `IsEliminated`, `IsFake`). `default` représente un votant introuvable. Placé dans son propre fichier pour respecter la règle « un type de premier niveau par fichier ». |
| `Assets/Scripts/GameLogic/GameStates/VoteState.cs` | modifié | Adaptateur allégé (Humble Object). `CanVote` garde son assert serveur et délègue le reste à `VoteRules.CanVote`. Nouveaux helpers privés : `ReadVoterStatus` (lookup du Character et null-check Unity), `HasVoterDeparted` (expression `_playerId < 100 && gameManager.HasClientLeft(_playerId)` reprise **mot pour mot**, avec son commentaire [LEAVE]) et `IsEarlyCloseConditionMet` (supprime la duplication entre `OnPlayerVotedRpc` et `OnPlayerLeftServer`). `Mathf.Min(voteTimer, 5)` devient `_voteRules.ClampTimerForEarlyClose(voteTimer)`. Fins de ligne CRLF conservées. |
| `Assets/Scripts/Tests/Editor/VoteRulesTests.cs` (+ `.meta`) | **nouveau** | Tests EditMode, `[Category("VoteRules")]`. |
| `Assets/Scripts/Tests/Editor/LeafPocoNoFacadeGuardTests.cs` | modifié | Ajout de `typeof(VoteRules)` dans `LeafCores`. Le garde vérifie ainsi que le nouveau cœur reste un POCO instanciable, sans `instance` statique ni état statique mutable. |
| `_bmad-output/refactor-architecture-poco.md` | modifié | Ajout d'une ligne `VoteRules` dans le tableau des POCO extraits. |

Aucune signature publique de `VoteState` n'a changé : `CanVote(ulong, bool = false)`, `OnPlayerLeftServer(ulong)`, `votesForPlayer`, `voteTimer`, `SKIP_VOTE_ID` et `mostVotedPlayer` sont intacts. Les méthodes appelées par réflexion via `DoStateMethodRpc` (`OnPlayerVotedRpc`, `OnRefreshPlayerVotesRpc`, `UpdateVoteTimerRpc`, `UpdateMostVotedPlayer`) ne sont ni renommées ni surchargées, donc `GetMethod(name)` ne peut pas devenir ambigu. Aucun `[SerializeField]` n'a été ajouté : le nouveau champ `private readonly VoteRules _voteRules = new();` n'est pas sérialisé, donc rien à câbler dans la scène.

## Comment le comportement est préservé (refactoring)

1. **Ordre des règles et court-circuit d'éligibilité.** L'ordre d'origine est conservé : assert serveur → « a déjà voté » (sauf si `_ignoreAlreadyVoted`) → Character absent / éliminé / fake → client parti. Les deux accès au moteur sont passés en **délégués paresseux**, comme dans le précédent `PowerUsability` :
   - `ReadVoterStatus` (donc `CharacterQuery.GetCharacter(id, false)`) n'est appelé que si la règle « déjà voté » est passée ;
   - `HasVoterDeparted` (donc `gameManager.HasClientLeft`) n'est appelé que si le votant existe, n'est pas éliminé et n'est pas fake.
   Les tests le vérifient avec des délégués qui lèvent une exception s'ils sont appelés.
2. **Null-check Unity.** `_character == null` reste dans l'adaptateur, donc c'est l'`operator ==` surchargé de `UnityEngine.Object` qui s'applique : un Character détruit compte toujours comme « absent ».
3. **Seul écart d'évaluation.** Dans l'ancien `a || b || c`, `isFake` n'était pas lu quand le Character était éliminé. Maintenant `ReadVoterStatus` lit `isEliminated.Value` **et** `isFake` avant de construire le snapshot. Les deux lectures sont pures (`NetworkVariable.Value` en lecture ; `isFake` → `UlongExtensions.IsFakeClientId`, une simple comparaison), donc rien de différent n'est observable.
4. **Fermeture anticipée.** `IsEarlyCloseConditionMet` calcule, dans le même ordre que l'ancien code en ligne, (a) la somme des bulletins (`Sum` LINQ sur les mêmes `List<ulong>`) puis (b) `CharacterQuery.GetCharacters().Count(_c => CanVote(id, true))`. C'est le même appel qu'avant, y compris `GetCharacters()` avec `triggerUpdate = true` par défaut et l'assert serveur dans chaque `CanVote`. La condition reste `>=`. Dans `OnPlayerLeftServer`, le `Debug.Log` affiche toujours les deux mêmes valeurs.
5. **Réduction du timer.** Unity implémente `Mathf.Min(float a, float b)` comme `a < b ? a : b`. `ClampTimerForEarlyClose` reproduit exactement cette expression, même pour NaN (renvoie 5). J'ai volontairement évité `System.Math.Min`, qui renverrait NaN. Le littéral `5` (int, converti en float) devient `5f`, ce qui donne la même valeur.
6. **Règle NFR5.** La discrimination des bots (`_playerId < 100`) est un élément d'autorité réseau que project-context interdit de réécrire. Elle **n'a pas été déplacée dans Domain** : elle reste mot pour mot dans l'adaptateur, et le POCO ne connaît que la notion générique de « votant parti ».

## Tests ajoutés

`Assets/Scripts/Tests/Editor/VoteRulesTests.cs` (EditMode, POCO pur, sans hôte NGO), 30 tests :
- `HasAlreadyVoted` : votant présent dans une liste, dans le bucket skip, absent, candidat mais pas votant, aucun bucket.
- `CanVote` : cas valide ; déjà voté (aucun lookup moteur appelé) ; le flag `ignoreAlreadyVoted` contourne seulement ce garde, pas les autres règles ; votant inconnu, éliminé ou fake (le check « parti » n'est pas appelé) ; votant parti ; l'id du votant est bien transmis aux deux lookups ; ordre d'appel statut → parti ; `default(VoterStatus)` représente un votant absent.
- `CountCastVotes` : somme de tous les buckets, skip compris ; zéro.
- `ShouldCloseEarly` : `<` → false, `==` → true, `>` → true, `0/0` → true.
- `ClampTimerForEarlyClose` : au-dessus de 5, en dessous, égal à 5, négatif, NaN → 5, +∞ → 5 ; constante égale à 5.
- Scénario qui reprend au niveau POCO le cas AC4 de `LeaveUnblockSeamTests` (le départ d'un non-votant réduit le dénominateur et déclenche la fermeture anticipée).

Garde étendu : `LeafPocoNoFacadeGuardTests` couvre maintenant `VoteRules`.

Les tests PlayMode existants (`VoteStateCanVoteTests`, `LeaveUnblockSeamTests.VoteState_*`, `VoteTallyGoldenMasterTests`, `VoteInsertionOrderTests`) n'ont pas été modifiés. Ils servent de filet de non-régression de bout en bout pour l'adaptateur.

**Je n'ai rien compilé ni exécuté** : pas d'éditeur Unity dans cet environnement.

## Risques et hypothèses

| # | Hypothèse / risque | Statut |
|---|---|---|
| 1 | Aucun comportement observable ne change (ordre des règles, court-circuits, `>=`, valeur du timer, logs, RPC). | **certain** (vérifié en relisant le code ligne à ligne, pas par exécution) |
| 2 | La lecture anticipée de `isFake` quand le Character est éliminé n'a aucun effet de bord. | **certain** (`IsFakeClientId` est une simple comparaison) |
| 3 | `Mathf.Min(float, float)` vaut `a < b ? a : b` (sémantique NaN reproduite). | **certain** d'après le code de référence Unity. Je n'ai pas pu le vérifier sur la version 6000.5 installée. |
| 4 | Conversion covariante implicite de `Dictionary<ulong, List<ulong>>.ValueCollection` vers `IEnumerable<ICollection<ulong>>` (variance de `IEnumerable<out T>`, `List<T>` étant un type référence). | **certain** (règle du langage C#), non compilé |
| 5 | L'initialiseur de champ `_voteRules = new()` s'exécute pour un ScriptableObject créé par `CreateInstance` ou cloné par `Instantiate` (`SetupGameStates`). Même motif que `Power._usability`. | **certain**. À confirmer quand même par les tests PlayMode, qui créent `VoteState` via `CreateInstance`. |
| 6 | Chaque appel à `CanVote` alloue deux délégués (method groups). Ce chemin n'est pas chaud (un appel par vote ou par départ, ×N personnages), et le code d'origine allouait déjà des lambdas LINQ. | **certain** que c'est négligeable hors `Update` |
| 7 | Les `.meta` ont été créés à la main avec des GUID aléatoires, au format minimal des `.meta` existants. Unity devrait les accepter et les compléter. | **incertain** : Unity peut les réécrire à l'import (diff attendu, sans conséquence) |
| 8 | Écart de version de l'éditeur (6000.5.0f1 contre 6000.2.6f2 documenté). | **incertain** : décision d'équipe |

## À vérifier dans Unity

1. `read_console` : aucune erreur de compilation dans `CorruptionDuPortail.Domain`, `Game` et `Tests.Editor`.
2. EditMode : catégories `VoteRules`, `VoteTally`, `LeafPocoGuard`, `DomainPurity`, `WireFormat`, `DiSeamGuard`, `StaticAbsenceGuard`, puis la suite complète.
3. PlayMode : catégorie `VoteState` (`VoteStateCanVoteTests`), `LeaveUnblockSeamTests.VoteState_DepartedNonVoterLeaves_ReducesDenominator_CollapsesTimer`, les golden masters `VoteTallyGoldenMasterTests` (catégorie `GoldenMaster`, **ils ne doivent pas bouger**), `VoteInsertionOrderTests`, `MultiClientGameFixture`.
4. Vérifier que les `.meta` générés sont acceptés sans conflit de GUID.
5. Test manuel en partie hôte + bots : on vote, le timer tombe à 5 s quand tout le monde a voté ; un vote en double est refusé (warning) ; un joueur réel quitte en cours de vote et la fermeture anticipée se déclenche si tous les présents ont voté.
