# Vote / Elimination System

## Rôle
Permet aux joueurs de soumettre individuellement un vote (ou un vote blanc) pour éliminer un joueur via un timer synchronisé, déterminant le joueur avec la majorité des voix.

## Déclencheur (Point d'entrée)
- **Serveur :** Déclenché par le transitionnement du `GameManager` vers l'état `VoteState`.
- **Client :** Intéraction UI (clic sur un bouton de vote via `VoteCanvas`) déclenchant `OnVoteButtonClicked`.

## Composants Clés
- `VoteState` : Logique serveur/client pour la phase de vote, gérant les timers, comptant les voix et désignant le joueur élu.
- `VoteCanvas` : Affichage côté client des options de vote sur les cartes des joueurs (`Card` lié au `BoardManager`).
- `ChainingManager` : Réceptionne le joueur le plus voté pour appliquer l'effet post-vote (ex: exécution/enchaînement).

## Données & État
- `votesForPlayer` : Dictionnaire `ulong` (ID Cible) -> `List<ulong>` (IDs Votants). Géré par le serveur.
- `voteTimer` : Float synchronisé du serveur vers le client via `UpdateVoteTimerRpc` asynchrone (Coroutine/UniTask).
- `mostVotedPlayer` : ulong représentant le `ClientId` du joueur éliminé ou `SKIP_VOTE_ID` (en cas de vote non concluant).

## Couplage & Dépendances
- **Fortement couplé** au `GameManager` (pour les RPC dynamiques) et au `CharacterManager` (récupération des informations sur l'état d'élimination des joueurs).
- Dépend du `BoardManager` côté client pour la manifestation visuelle des options de vote (`Card.voteCanvas`).

## Points d'attention
- La sécurité côté serveur vérifie uniquement si le votant a le droit de voter dans `CanVote`, mais l'identification dépend des RPC.
- L'utilisation de `GetMethod` par réflexion dans `GameManager.DoStateMethodRpc` (invoquée depuis `OnPlayerVoted`) peut entraîner une surcharge modérée de la CPU et une perte de la sécurité de type (Type Safety) si le nom de la méthode change sans être refactorisé globalement.
- Risque potentiel : gestion de la déconnexion pendant un vote non décrite dans l'état lui-même (risque de désynchronisation de l'avancement si un membre bloquant se déconnecte).
