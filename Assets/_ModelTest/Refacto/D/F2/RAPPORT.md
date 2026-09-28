# RAPPORT — F2 : bonus d'usage de ChainedByShadows jamais accordé à la dernière anomalie

## Symptôme

Une anomalie (Abyss) qui a le pouvoir *Enchaîné par les Ombres* (`PChainedByTheShadows`) devine juste le rôle de sa cible alors que l'autre anomalie a été éliminée. Elle ne reçoit jamais l'usage supplémentaire prévu par le Lot B (`GrantExtraUse`).

## Cause racine

`ChainedByShadowsDecision.OwnerIsSoleAnomalyInPlay`
(`Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs`) compte les anomalies « encore en jeu ». Le code ne retirait que les personnages **enchaînés** :

```csharp
roster.FactionOf(s) == FactionType.anomaly && !roster.IsChained(s)
```

Il manquait l'exclusion des personnages **éliminés** (`!roster.IsEliminated(s)`). La spec la demande pourtant : `spec-role-adjustments.md` §B.2 dit « en excluant les enchaînés/éliminés », la checklist parle de « anomalies non-chained/non-eliminated == 1 », et le commentaire de la méthode dit « neither chained nor eliminated ». `IRosterView.IsEliminated` avait même été ajouté pour ce comptage, mais la décision ne l'appelait jamais.

`CharacterManagerRoster.Slots` s'appuie sur `CharacterManager.GetCharacters(false)`, qui ne filtre pas les éliminés. Une anomalie éliminée (`isEliminated == true`, `isChained == false`) restait donc comptée : le total valait 2 au lieu de 1, et `GrantExtraUse` n'était jamais émis.

## Fichiers modifiés

| Fichier | Changement |
|---|---|
| `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs` | Ajout de `&& !roster.IsEliminated(s)` dans le prédicat de comptage, avec un commentaire qui explique pourquoi. Rien d'autre ne change : ordre des effets, verdict et condition « bonus pas encore pris cette nuit » sont identiques. |
| `Assets/Scripts/Tests/Editor/PowerDecisionTests.cs` | Ajout du test de non-régression `ChainedByShadows_OtherAnomalyEliminated_CountsAsSole_GrantsExtraUse`. |

Aucune API nouvelle. `IRosterView.IsEliminated` existait déjà, avec toutes ses implémentations (runtime `CharacterManagerRoster` et les trois `FakeRoster` EditMode).

## Test ajouté

`PowerDecisionTests.ChainedByShadows_OtherAnomalyEliminated_CountsAsSole_GrantsExtraUse` (EditMode, catégorie `PowerDecision`) :
- roster à 4 slots : l'owner (anomalie), la cible (marginale, pour ne pas déclencher le chaînage), le porteur du rôle choisi (élu) et une seconde anomalie **éliminée mais pas enchaînée** ;
- devinette juste, bonus pas encore consommé ;
- résultat attendu : `[NewTargeting(0,1), RevealInfo(1, RoleRevealed, Personal, 0, true), CorruptPlayer(1), GrantExtraUse(0)]`.

Sans le correctif, l'anomalie éliminée est comptée (total 2) et `GrantExtraUse(0)` manque : le test doit échouer. Avec le correctif, il doit passer. Je n'ai rien compilé ni exécuté : ce raisonnement se fait sur le code seul.

Les tests existants ne devraient pas bouger. Dans `TwoAnomaliesInPlay` et `OtherAnomalyChained`, personne n'est éliminé, donc le nouveau prédicat donne le même résultat qu'avant.

## Risques et hypothèses

- **Certain** : le comptage oubliait les anomalies éliminées, contrairement à la spec et au commentaire du code. Le correctif les exclut.
- **Certain** : le correctif ne touche qu'une décision POCO pure du Domain. Pas de réseau, de RPC, de `NetworkVariable`, de sérialisation ni de câblage de scène en jeu, et la contrainte `noEngineReferences` est respectée.
- **Incertain** : ce que le joueur appelle « éliminée au vote ». Dans le code actuel, un vote ne met pas `isEliminated`. `VoteState.OnEndStateServer` ajoute le joueur à la liste de chaînage, puis `ChainingState` → `ChainingManager.ChainCharacterRpc` → `Character.ChainCharacterServer` pose `isChained = true`. Le seul endroit qui écrit `isEliminated` est `SetEliminatedExecutor` (HighPriorityBounty). Donc :
  - si l'autre anomalie avait `isEliminated` (ou si le jeu est censé marquer ainsi les exclus), ce correctif règle le problème ;
  - si l'autre anomalie a seulement été votée et que `ChainingState` n'est pas passé avant la nuit, ou si `isChained` n'était pas encore répliqué ou posé au moment de la devinette, le symptôme pourrait avoir une deuxième cause que ce correctif ne couvre pas. En lisant le code, je n'ai trouvé aucune autre anomalie sur ce chemin : `GrantExtraUseExecutor` est découvert par réflexion, `PowerStateAdapter` résout bien `IExtraUseState` et `IExtraUseGrant` sur le pouvoir, et le drapeau est remis à zéro sur `isAwakened` → true.
- **Incertain** : `ProjectSettings/ProjectVersion.txt` indique `6000.5.0f1`, alors que `CLAUDE.md` et `project-context.md` exigent `6000.2.6f2`. Je n'ai pas touché au fichier, conformément à la règle, mais ce décalage doit être remonté à l'équipe.

## À vérifier dans Unity

1. `mcp__UnityMCP__read_console` : aucune erreur de compilation (ni `CorruptionDuPortail.Domain`, ni `Tests.Editor`).
2. `mcp__UnityMCP__run_tests` en EditMode, catégorie `PowerDecision`. Le nouveau test et les six tests `ChainedByShadows_*` existants doivent passer. Lancer aussi `PowerVerdictTests` et `PowerDecisionUncoveredBranchTests`.
3. Playtest à 2 clients ou plus, avec deux anomalies dont Abyss :
   a. éliminer l'autre anomalie par HighPriorityBounty (`isEliminated`), puis à la nuit suivante faire deviner juste à Abyss : un usage supplémentaire doit apparaître ;
   b. faire voter contre l'autre anomalie, laisser passer `ChainingState` et vérifier `isChained == true` sur son `Character` avant la nuit, puis faire deviner juste à Abyss : un usage supplémentaire doit apparaître. S'il n'apparaît pas, la séquence vote → chaînage → nuit a un problème distinct à analyser (voir la réserve « incertain » ci-dessus) ;
   c. une deuxième bonne devinette dans la même nuit ne doit pas donner de deuxième bonus (drapeau `BonusConsumedThisNight`).
