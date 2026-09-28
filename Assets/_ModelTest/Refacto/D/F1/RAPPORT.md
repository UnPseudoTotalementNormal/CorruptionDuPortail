# RAPPORT — F1 : « Simulated 1 » ne reçoit jamais ses révélations personnelles

## Cause racine

`GameInfoRevealer.SendRevealLevelRpc` (`Assets/Scripts/GameLogic/GameInfoRevealer.cs`) choisit entre deux chemins :
- un observateur réel reçoit `SetRevealLevelRpc` ciblé sur `RpcTarget.Single(observerId)` ;
- un bot simulé est redirigé vers l'Host (`RpcTarget.Single(0)`) via `SetRevealLevelSimulatedRpc`, qui écrit dans le « cerveau » simulé du bot (`simulationsKnowledge[botId]`).

Le test de bascule était `if (_toObserverId > 100)`, avec une **inégalité stricte**. Or la convention du projet, c'est bot = `clientId >= 100` (`GetSafeRpcTarget`, `IsLocalOrSimulated`, `SendClearHackedRpc`, `GetCharacterInfo`…). De plus, `CharacterManager` attribue les ids de bots avec `100 + nombreDeBots`. Le premier bot, nommé `Simulated {id - 99}` = **« Simulated 1 »**, a donc exactement l'id **100**.

Pour l'id 100, le code passait dans la branche « client réel ». La révélation partait vers `RpcTarget.Single(100)`, un client transport qui n'existe pas. Elle n'arrivait jamais dans le cerveau simulé de ce bot. Les bots 101 et plus passaient la condition et recevaient bien leurs infos, ce qui correspond exactement au rapport de QA. Les révélations publiques n'étaient pas touchées, car elles passent par la diffusion `SendTo.Everyone` qui parcourt tous les `simulationsKnowledge`.

## Fichiers modifiés

| Fichier | Changement |
|---|---|
| `Assets/Scripts/GameLogic/GameInfoRevealer.cs` | `_toObserverId > 100` → `_toObserverId >= 100` dans `SendRevealLevelRpc`, avec un commentaire qui explique la frontière. Cette condition est maintenant la même que dans `SendClearHackedRpc` et `GetSafeRpcTarget`. |
| `Assets/Scripts/Tests/PlayMode/Desingleton/RevealAsymmetryReplicationTests.cs` | Deux tests de régression PlayMode ajoutés (voir plus bas). |
| `RAPPORT.md` | Ce rapport. |

Je n'ai touché ni à `GetSafeRpcTarget`, ni à `IsLocalOrSimulated`, ni à aucun autre point de routage des bots (règle du project-context : ne pas les modifier).

## Tests ajoutés

Dans `RevealAsymmetryReplicationTests` (fixture 2 NetworkManagers `MultiClientGameFixture`, avec un vrai `GameInfoRevealer` répliqué) :
- `PersonalReveal_ToFirstSimulatedBot_LandsInHostSimulatedBrain` : c'est la régression. Une révélation Personal ciblée sur `SimulatedBotClientId` (= 100) doit arriver dans le cerveau simulé du bot 100 côté Host. Elle ne doit arriver ni dans le store du joueur Host (id 0), ni dans le cerveau du bot 101. Sans la correction, l'attente échoue au bout de 5 s (et NGO pourrait aussi logger une erreur d'envoi vers un client inconnu).
- `PersonalReveal_ToSecondSimulatedBot_LandsInHostSimulatedBrain` : même scénario pour le bot 101, pour vérifier que le cas qui marchait déjà reste correct.

Les deux tests passent par un helper privé commun. Je ne les ai pas exécutés : il n'y a ni éditeur ni compilation dans cet environnement.

## Risques et hypothèses

- **Certain** : « Simulated 1 » a l'id 100 (`CharacterManager.cs`, `_debugId = 100 + count` et `AddDebugPlayer(_debugId, $"Simulated {_debugId - 99}")`). C'était le seul id de bot à tomber dans la mauvaise branche.
- **Certain** : c'était la seule comparaison `> 100` du code de production. Tous les autres points de routage des bots utilisent `>= 100`.
- **Certain** : rien ne change pour les clients réels (ids < 100) ni pour les bots 101 et plus.
- **Incertain** : le comportement exact de NGO avant la correction quand il envoie vers `RpcTarget.Single(100)` (abandon silencieux ou log d'erreur). Ça ne change rien à la correction. Le nouveau test échoue dans les deux cas avant la correction.
- **Incertain** : les nouveaux tests supposent que le RPC ciblé `Single(0)` s'exécute sur l'Host en quelques frames au plus. L'attente bornée `WaitUntilOrTimeout` couvre aussi un exécution différée.

## À vérifier dans Unity

1. Compilation : `read_console` ne doit afficher aucune erreur.
2. Lancer les tests PlayMode `Tests.PlayMode.Desingleton.RevealAsymmetryReplicationTests` : les 6 tests doivent être verts. Idéalement, vérifier aussi que le test 5 est rouge si on remet `> 100`.
3. Lancer le reste de la suite (`MultiClientGameFixture`, guards `DiSeamGuard` / `SceneWiringGuard`) pour vérifier qu'il n'y a pas de régression.
4. Test manuel : partie de debug avec au moins 2 bots. Possédez « Simulated 1 » et utilisez un pouvoir qui révèle un rôle à titre personnel (vision, etc.). La carte ciblée doit afficher le rôle pour Simulated 1, et rester cachée pour le joueur Host et pour les autres bots.
