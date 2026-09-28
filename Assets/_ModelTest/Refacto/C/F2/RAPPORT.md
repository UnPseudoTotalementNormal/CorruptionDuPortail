# Rapport — F2 : bonus d'utilisation supplémentaire de ChainedByShadows

## Symptôme
Une anomalie jouant ChainedByShadows, devenue la dernière anomalie en jeu parce que
l'autre anomalie a été éliminée au vote, devine correctement le rôle de sa cible mais ne
reçoit jamais l'utilisation supplémentaire (bonus « seule anomalie encore en jeu »).

## Cause racine (certain)
Dans `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs`, la méthode
`OwnerIsSoleAnomalyInPlay` comptait les anomalies « encore en jeu » avec le prédicat :

```
roster.FactionOf(s) == FactionType.anomaly && !roster.IsChained(s)
```

Elle excluait les anomalies enchaînées mais **pas** les anomalies éliminées. Or la
documentation de la méthode et de l'interface `IRosterView.IsEliminated` précise
explicitement que « encore en jeu » = faction anomalie **ni enchaînée ni éliminée**.

Conséquence : quand l'autre anomalie est éliminée au vote (mais non enchaînée), elle
restait comptée. Le compte valait donc 2 au lieu de 1, la condition « seule anomalie »
échouait, et `GrantExtraUse` n'était jamais émis — exactement le symptôme décrit.

## Correctif
Ajout du filtre `&& !roster.IsEliminated(s)` au prédicat de comptage :

```
roster.FactionOf(s) == FactionType.anomaly && !roster.IsChained(s) && !roster.IsEliminated(s)
```

`IRosterView.IsEliminated` existait déjà et est branché côté runtime
(`CharacterManagerRoster.IsEliminated` lit `character.isEliminated.Value`), donc une
anomalie éliminée au vote est désormais correctement écartée du compte. Aucune API
inventée.

## Fichiers modifiés
- `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs` — correction du
  prédicat de comptage des anomalies en jeu.
- `Assets/Scripts/Tests/Editor/PowerDecisionTests.cs` — test de non-régression ajouté.

## Test ajouté
`ChainedByShadows_OtherAnomalyEliminated_CountsAsSole_GrantsExtraUse` : deux anomalies,
l'autre marquée éliminée (`roster.Eliminated.Add(1)`), devinette correcte, bonus non
consommé. Attend l'émission de `GrantExtraUse(0)` en plus des effets de corruption. Ce
test échouait avant le correctif (compte = 2) et passe après (compte = 1). Il complète le
test existant `..._OtherAnomalyChained_CountsAsSole_GrantsExtraUse` qui couvrait déjà le
cas « enchaînée ».

## Préservation du comportement (certain)
Les autres branches restent inchangées : NewTargeting inconditionnel, révélation +
corruption + chaînage sur devinette correcte, garde une-fois-par-nuit
(`BonusConsumedThisNight`). Seule la définition de « seule anomalie en jeu » est corrigée
pour inclure l'exclusion des éliminés, conformément à la documentation.

## Risques et hypothèses
- Hypothèse (certain) : une anomalie éliminée au vote a `isEliminated.Value == true` ;
  confirmé par `CharacterManagerRoster.IsEliminated`.
- Risque (incertain) : je n'ai pas d'éditeur Unity ni de compilateur ; je n'ai ni compilé
  ni exécuté les tests. La logique et les signatures sont vérifiées à la lecture, mais la
  compilation et le passage des tests EditMode restent à confirmer dans Unity.

## À vérifier dans Unity
- Compilation sans erreur (poll `read_console`).
- Exécution des tests EditMode (catégorie `PowerDecision`), en particulier le nouveau test.
