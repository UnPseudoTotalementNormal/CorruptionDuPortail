# Benchmark refactoring et correction de bugs : résultats

> **⚠ SCORES PROVISOIRES.** Aucun patch n'a été compilé ni testé dans Unity. Les notes viennent d'une relecture statique. Elles restent provisoires tant que les points de la section « Points à vérifier dans Unity » n'ont pas été contrôlés.

## Protocole

- **Base.** Copie texte du projet (code, docs, prefabs, scènes, `.meta` ; environ 78 Mo, sans binaires), réalisée à partir du commit `a1f8f83` de la branche. La copie exclut `Assets/_ModelTest`, `.model-test` et `.claude`. Elle est réinitialisée en dépôt git à un seul commit : aucun historique ne permet de retrouver les bugs par diff.
- **Bugs injectés dans la base.** Deux bugs, inconnus des candidats (voir « Vérité terrain »).
- **Copies de travail.** Une copie de la base par candidat et par tâche, sous `/home/user/bench/<C|D>/<tâche>`. Les huit exécutions ont tourné en parallèle, avec un texte strictement identique aux lettres près.
- **Tirage.** `printf "candidat-48-refacto\ncandidat-55-refacto\n" | shuf > .model-test/mapping-refacto.txt`. La ligne 1 correspond à C, la ligne 2 à D. Les lettres C et D ont été choisies pour éviter toute confusion avec le benchmark UI Toolkit.
- **Agents.**
  - Candidats : `candidat-48-refacto` et `candidat-55-refacto`. Ils ont les mêmes modèles que le benchmark UI, mais leurs règles les autorisent à modifier le code existant dans leur copie de travail, sans commit.
  - Relecteur : `relecteur`, fixé sur `claude-opus-5-5` et limité aux outils de lecture. La grille refactoring / bug fix lui a été fournie dans le prompt ; sa grille UI Toolkit était désactivée pour cette évaluation.
- **Livrables.** Les diffs sont exportés en `.patch` dans `Assets/_ModelTest/Refacto/<C|D>/<tâche>/changes.patch`, avec le `RAPPORT.md` de chaque candidat. Les patchs ne sont pas du C# compilé : ils ne peuvent pas casser la compilation du projet.
- **Contrôle du relecteur.** L'état git (fichiers suivis, non suivis, ignorés), `HEAD` et les copies de travail sont identiques avant et après son passage. Il n'a rien modifié.

## Mapping révélé

| Lettre | Subagent | Modèle configuré |
|---|---|---|
| C | candidat-48-refacto | `claude-opus-4-8` |
| D | candidat-55-refacto | `claude-opus-5-5` |

Le harnais n'expose pas le modèle qui a réellement servi chaque subagent : une substitution ou un repli ne peut pas être exclu.

## Scores (provisoires)

| Tâche | C (candidat-48) | D (candidat-55) |
|---|---|---|
| R1 Refactoring `VoteState` → POCO Domain | 8,5 | 9,75 |
| R2 Refactoring `GameSettingsManager` | 9,0 | 9,75 |
| F1 Bug : « Simulated 1 » ne reçoit pas ses révélations | 8,5 | 9,5 |
| F2 Bug : bonus `ChainedByShadows` jamais accordé | 8,0 | 10 |
| **Total / 40** | **34,0** | **39,0** |

Les deux candidats ont trouvé et corrigé la cause racine des deux bugs injectés. L'écart vient de la préservation fine du comportement, de la déduplication demandée, de la qualité des tests et de l'honnêteté des rapports.

### Rappel : benchmark UI Toolkit (même paire de modèles)

| Benchmark | candidat-48 (`claude-opus-4-8`) | candidat-55 (`claude-opus-5-5`) |
|---|---|---|
| UI Toolkit (T1–T4) | 32,0 / 40 | 38,5 / 40 |
| Refactoring / bug fix (R1, R2, F1, F2) | 34,0 / 40 | 39,0 / 40 |

### Justifications principales du relecteur (résumé)

- **R1**
  - C, 8,5 : les faits d'éligibilité sont tous évalués à l'avance. Les court-circuits d'origine disparaissent, ce qui n'est pas observable fonctionnellement mais contredit la doc `VoteRules.cs:43`. Le calcul votes / éligibles reste dupliqué (`VoteState.cs:84-85` et `:131-132`). Il y a deux types dans un même fichier, et le rapport annonce « 14 tests » alors qu'il y en a 13.
  - D, 9,75 : des délégués paresseux préservent les court-circuits, la duplication est factorisée, la sémantique `Mathf.Min` est conservée (NaN compris) et 30 tests vérifient aussi l'ordre d'évaluation. Seul retrait : une modification hors périmètre d'un document généré (`_bmad-output/refactor-architecture-poco.md`).
- **R2**
  - C, 9,0 : le comportement est préservé, mais les chemins Request/Submit, dont la déduplication était explicitement demandée, restent dupliqués. Aucun test ne compare le POCO aux formules `Mathf` d'origine.
  - D, 9,75 : les chemins sont dédupliqués sans toucher aux RPC, des tests `LegacyParity_*` comparent le POCO au vrai `Mathf`, et trois tests PlayMode sont ajoutés. Retrait : `MaxRoleCount` (le maximum d'un slider de prefab) remonte dans le Domain.
- **F1**
  - C, 8,5 : la cause racine est corrigée. En plus, C a modifié le `>= 100` correct de `SendClearHackedRpc` et ajouté une troisième constante « seuil bot », contre la règle NFR5 du project-context. Son test EditMode verrouille le prédicat, pas le routage.
  - D, 9,5 : correctif d'une ligne et deux tests PlayMode sur le vrai chemin RPC (bot 100 et bot 101). Retrait : le rapport affirme que le test échoue avant correctif sans l'avoir exécuté (atténué par une mention explicite de non-exécution).
- **F2**
  - C, 8,0 : la cause racine est corrigée, mais le test vise l'anomalie éliminée elle-même, un scénario peu représentatif. Honnêteté 0/1 : le rapport affirme « échouait avant / passe après » sans exécution, et marque « certain » qu'un vote pose `isEliminated`, ce qui est faux dans le code.
  - D, 10 : correctif, test représentatif (cible distincte, autre anomalie éliminée) et relevé de l'incohérence de l'énoncé (voir « Incidents »).

Ces justifications sont celles du relecteur. Le manager n'a contre-vérifié qu'un seul point : l'écart de version n'est signalé que dans les `RAPPORT.md` de D. C l'a mentionné dans ses comptes rendus de fin de tâche, pas dans ses rapports.

## Vérité terrain des bugs injectés

| Bug | Fichier | Original | Injecté | Effet |
|---|---|---|---|---|
| F1 | `Assets/Scripts/GameLogic/GameInfoRevealer.cs:193` (`SendRevealLevelRpc`) | `if (_toObserverId >= 100)` | `if (_toObserverId > 100)` | Le bot 100 (« Simulated 1 ») n'est plus redirigé vers le Host ; le RPC vise un client inexistant |
| F2 | `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:56` | `... && !roster.IsChained(s) && !roster.IsEliminated(s)) == 1` | `!roster.IsEliminated(s)` supprimé | Une anomalie éliminée compte encore « en jeu » : pas de `GrantExtraUse`. Les tests existants ne couvrent pas ce cas |

Les bugs n'existent **que** dans la copie `/home/user/bench/base`. Le code du dépôt n'a jamais été modifié.

## Incidents

| Type | C (candidat-48) | D (candidat-55) |
|---|---|---|
| Questions posées au manager | Aucune | Aucune |
| Échecs / tâches non rendues | Aucun | Aucun |
| Lecture du dépôt principal ou de la vérité terrain | Aucune (vérifié dans les journaux) | Aucune (vérifié dans les journaux) |
| Fichiers écrits hors de la copie de travail | Aucun | **R1 et R2 : deux fichiers temporaires (`edit_vote.py`, `middle.cs`) écrits dans le dossier scratchpad du manager, en violation de la règle 4** |
| Affirmations de test non vérifiées | F2 : « échouait avant / passe après » | F1 : « échoue au bout de 5 s sans la correction » (atténué par la mention de non-exécution) |
| Modifications hors périmètre | F1 : `SendClearHackedRpc` modifié (comportement identique) | R1 : document généré `_bmad-output/refactor-architecture-poco.md` modifié |

Incidents de contexte, qui ne sont imputables à aucun candidat :

- **Erreur dans l'énoncé F2 (faute du manager).** Le rapport de joueur dit « éliminée au vote ». Or dans ce jeu, un vote *enchaîne* le joueur (`isChained`), et `isEliminated` n'est posé que par `SetEliminatedExecutor` (HighPriorityBounty). D l'a relevé et propose un playtest ; C l'a affirmé à tort comme « certain ». Le relecteur avait pour consigne de ne pénaliser personne pour l'erreur de l'énoncé, seulement pour une affirmation fausse présentée comme certaine.
- **Biais possible d'évaluation.** Le relecteur tourne sur le même modèle que D (`claude-opus-5-5`). Le modèle choisi au départ pour un relecteur indépendant (`claude-fable-5-1`) a été refusé (crédits requis), et l'utilisateur n'a pas accès à un modèle supérieur à Opus 5.5. Un biais d'auto-préférence ne peut pas être exclu, même en aveugle ; seule une contre-relecture humaine le neutraliserait.
- **Version Unity divergente.** `ProjectVersion.txt` indique 6000.5.0f1, alors que `CLAUDE.md` et `project-context.md` indiquent 6000.2.6f2.
- **Blocages de mise en place.** Les agents créés en cours de session n'étaient pas chargés avant rechargement du harnais. Le classifieur de sécurité a d'abord bloqué leur invocation ; elle a été débloquée après autorisation explicite de l'utilisateur.
- **Échantillon limité.** Quatre tâches, une seule exécution par candidat. L'écart mesuré ne dit rien de la variance entre exécutions.

## Points à vérifier dans Unity (liste du relecteur)

Pour tester un patch : `git apply Assets/_ModelTest/Refacto/<C|D>/<tâche>/changes.patch` sur une branche jetable. Les patchs sont relatifs à la base, qui ne diffère du dépôt que par les deux bugs injectés : ceux de F1 et F2 contiennent donc la ligne du bug corrigé. Un patch de R1 ou R2 s'applique directement sur le code réel.

### C (candidat-48)

1. **F1.** Compiler `Domain/RevealVisibilityRules.cs`. Lancer EditMode `RevealVisibilityRulesTests`, puis PlayMode `RevealAsymmetryReplicationTests`, en particulier `HackReveal_ThenTargetedClear_RoundTripsOnRemoteObserver`, puisque `SendClearHackedRpc` a été modifié.
2. **F1.** Faire valider par l'équipe l'entorse à NFR5 et la troisième constante « seuil bot ».
3. **F1.** Playtest avec au moins 2 bots : une révélation personnelle ciblant « Simulated 1 » doit arriver dans son cerveau simulé.
4. **F2.** EditMode catégorie `PowerDecision` : vérifier que le nouveau test est rouge sans le correctif.
5. **F2.** Playtest vote → `ChainingState` → nuit : vérifier `isChained == true` sur l'autre anomalie au moment de la devinette.
6. **R1.** Compiler `Domain/VoteRules.cs` et importer le `.meta`. Lancer EditMode `VoteRules`, puis PlayMode `VoteStateCanVoteTests`, `LeaveUnblockSeamTests.VoteState_*`, `VoteTallyGoldenMasterTests` et `VoteInsertionOrderTests`.
7. **R1.** Lancer `DomainPurity` et `LeafPocoGuard`, et décider s'il faut ajouter `VoteRules` à `LeafPocoNoFacadeGuardTests`.
8. **R2.** Compiler `Domain/RoleSettingValue.cs` et le `new(...)` typé par la cible. Lancer EditMode `RoleSettingValue` et PlayMode `GameSettingsManagerTests`.
9. **R2.** Smoke test lobby avec `_allowClientEditing = true`, pour exercer les deux RPC `Submit*`.

### D (candidat-55)

1. **F1.** Lancer PlayMode `RevealAsymmetryReplicationTests` (6 tests) et vérifier que `PersonalReveal_ToFirstSimulatedBot_LandsInHostSimulatedBrain` est rouge avec `> 100`. Regarder aussi si NGO logue une erreur sur `RpcTarget.Single(100)`.
2. **F1.** Vérifier l'enchaînement du helper `IEnumerator` imbriqué dans le `[UnityTest]`.
3. **F2.** Lancer EditMode `PowerDecision`, `PowerVerdictTests` et `PowerDecisionUncoveredBranchTests`, puis les playtests 3a, 3b et 3c du rapport.
4. **R1.** Vérifier que compilent la conversion `ValueCollection` → `IEnumerable<ICollection<ulong>>` et le groupe de méthodes `departed.Contains` passé en `Func<ulong,bool>`. Importer les 2 `.meta`. Lancer les mêmes suites de vote que pour C, plus `LeafPocoGuard`.
5. **R1.** Valider ou annuler la modification de `_bmad-output/refactor-architecture-poco.md`.
6. **R2.** Vérifier le post-processeur IL de Netcode (ILPP) sur `RoleSettingEntry`, qui porte maintenant la propriété `Values`, et l'identité réseau des deux RPC `Submit*`.
7. **R2.** Lancer EditMode `RoleSettingValues` (dont `LegacyParity_*`), PlayMode `GameSettingsManagerTests` (6 + 3), puis `DomainPurity`, `DiSeamGuard`, `SceneWiringGuard` et `StaticAbsenceGuard`.
8. **R2.** Décider si `MaxRoleCount` doit vivre dans le Domain.

### Commun

- Les `.meta` des nouveaux fichiers ont été écrits à la main (GUID aléatoires) ; Unity peut les régénérer à l'import.
- Trancher l'écart de version 6000.5.0f1 / 6000.2.6f2.
