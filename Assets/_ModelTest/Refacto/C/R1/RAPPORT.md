# RAPPORT — R1 : Refactor de la logique de vote de VoteState

## Objectif

Extraire, depuis `VoteState`, (1) les **règles d'éligibilité au vote** et (2) la **condition de
fermeture anticipée du vote**, vers un POCO testable de la couche Domain, **sans changer aucun
comportement observable**, et ajouter des tests EditMode.

## Fichiers créés

- `Assets/Scripts/Domain/VoteRules.cs` (+ `.meta`) — nouveau POCO de la couche Domain
  (`CorruptionDuPortail.Domain`, `noEngineReferences: true`). Contient :
  - `struct VoterFacts` : les faits Unity-free d'un votant candidat (`HasAlreadyVoted`,
    `CharacterExists`, `IsEliminated`, `IsFake`, `HasDeparted`).
  - `class VoteRules` :
    - `bool CanVote(in VoterFacts, bool ignoreAlreadyVoted = false)` — règle d'éligibilité.
    - `bool ShouldCloseVoteEarly(int castVotes, int eligibleVoters)` — condition de fermeture
      anticipée (`castVotes >= eligibleVoters`).
- `Assets/Scripts/Tests/Editor/VoteRulesTests.cs` (+ `.meta`) — 14 tests EditMode.

## Fichiers modifiés

- `Assets/Scripts/GameLogic/GameStates/VoteState.cs`
  - Ajout d'un champ `private readonly VoteRules _voteRules = new();`.
  - `CanVote(ulong, bool)` : l'adaptateur résout désormais les faits vivants (déjà-voté, existence
    du personnage, éliminé, fake, départ réel) puis délègue la **décision** au POCO. Le
    discriminant bot `_playerId < 100` reste **verbatim** dans l'adaptateur (NFR5 : le code
    d'autorité réseau/`clientId` n'est pas déplacé dans le Domain).
  - `OnPlayerVotedRpc` : la comparaison `Sum >= Count(...)` devient
    `_voteRules.ShouldCloseVoteEarly(_castVotes, _eligibleVoters)`. L'application de la décision
    (`voteTimer = Mathf.Min(voteTimer, 5)`) reste dans l'adaptateur.
  - `OnPlayerLeftServer` : même substitution de la condition `_castVotes >= _eligibleVoters` par
    `_voteRules.ShouldCloseVoteEarly(...)`. Le `Debug.Log` et le collapse du timer restent
    inchangés.

## Comment le comportement est préservé

- **Éligibilité** : `VoteRules.CanVote` reproduit à l'identique l'ordre et les court-circuits de
  l'ancien `CanVote` : (1) déjà-voté sauf `ignoreAlreadyVoted`, (2) personnage absent / éliminé /
  fake, (3) client réel parti. Le drapeau `ignoreAlreadyVoted` conserve le même rôle (comptage du
  dénominateur des votants éligibles).
- **Évaluation anticipée des faits** : l'ancien code court-circuitait avant d'appeler
  `GetCharacter`/`HasClientLeft` ; le nouvel adaptateur les évalue tous en amont. C'est sans
  incidence observable car ce sont des **lectures pures** : `GetCharacter(_playerId, false)` passe
  `_triggerUpdate = false` (un simple `FirstOrDefault`, aucune coroutine — vérifié dans
  `CharacterManager.GetCharacter`/`GetCharacters`) et `HasClientLeft` est un `HashSet.Contains`
  (vérifié dans `GameManager`). Les accès à `_character.isEliminated.Value`/`.isFake` sont gardés
  par `_characterExists` — pas de NRE possible, comme l'ordre original le garantissait.
- **Fermeture anticipée** : le prédicat `>=` est déplacé tel quel ; le seuil `5` et le
  `Mathf.Min` (application de la décision) restent côté adaptateur.
- Aucune signature publique modifiée (`CanVote(ulong, bool)` conservée) : les appelants externes
  ne voient aucun changement.

## Tests ajoutés

`Assets/Scripts/Tests/Editor/VoteRulesTests.cs` (`[Category("VoteRules")]`), EditMode pur, aucun
host NGO :
- `CanVote` : votant éligible ; déjà-voté (rejeté / ignoré via le drapeau / drapeau qui ne masque
  que la porte déjà-voté) ; personnage absent ; éliminé ; fake ; client parti ; client présent non
  exclu.
- `ShouldCloseVoteEarly` : tous les éligibles ont voté ; plus de votes que d'éligibles (denominator
  réduit) ; votes en attente ; pool vide (`0 >= 0`).

## Risques et hypothèses

- **Certain** : le POCO ne contient aucune référence UnityEngine/NGO — conforme à l'asmdef
  `CorruptionDuPortail.Domain` (`references: []`, `noEngineReferences: true`). `Tests.Editor`
  référence déjà `CorruptionDuPortail.Domain`, et `Game` consomme déjà le Domain (via `VoteTally`).
- **Certain** : `GetCharacter(_, false)` et `HasClientLeft` sont des lectures sans effet de bord →
  l'évaluation anticipée ne modifie pas le comportement.
- **Incertain** : les fichiers `.meta` créés utilisent des GUID générés et le format minimal
  observé dans le dépôt (`fileFormatVersion` + `guid`). À confirmer dans Unity que l'import se fait
  correctement (régénération/refresh des `.meta`).

## À vérifier dans Unity

- Compilation sans erreur (`read_console`) après réimport.
- `run_tests` EditMode filtré `category: VoteRules` (nouveaux tests) et `VoteTally` (non
  régressés), puis la suite EditMode complète.
- Les tests PlayMode touchant le flux de vote (host + bots), pour confirmer que l'auto-close et
  l'éligibilité restent identiques bout-en-bout.
