# Game Info Revealer System

## Rôle
Agit comme la source de vérité pour déterminer quelles informations cachées (Rôles, état de corruption) sont actuellement révélées à un client spécifique ou publiquement à tous. C'est l'arbitre du "brouillard de guerre" asymétrique.

## Déclencheur (Point d'entrée)
- **Initialisation :** S'abonne à la fin de la distribution des rôles (`RoleAttributionState`). Par défaut, seul chaque joueur connaît son propre rôle (`RevealLevel.Personal`).
- **Révélation :** Invoqué par d'autres systèmes (ex: la mort d'un joueur, un pouvoir asymétrique) via `SetRevealLevel` ou potentiellement son RPC `SetRevealLevelRpc`.

## Composants Clés
- `GameInfoRevealer` : NetworkBehaviour agissant en tant que "Dictionary Manager" des révélations pour synchroniser l'affichage public/privé.
- `CharacterInfoReveal` : Classe de données encapsulant les variables de révélation d'un joueur (`isRoleRevealed`, `isCorruptRevealed`).
- `RevealLevel` : Énumérateur délimitant la portée de l'information (`False`, `Personal`, `Public`).

## Données & État
- `charactersInfoRevealed` : Dictionnaire Client -> Données de révélation.
- `simulationsKnowledge` : Dictionnaire spécifique stocké sur l'Host pour mémoriser les révélations faites aux joueurs simulés (IDs >= 100).
- Utilise la **Réflexion C#** pour mapper les variables ciblées. Les appels vers des IDs >= 100 sont redirigés vers l'Host via `SetRevealLevelSimulatedRpc`.

## Couplage & Dépendances
- **Couplage Front-end agressif :** Dépend du `BoardManager.instance.visibleCards`. Par exemple, le déblocage visuel de la carte d'un rôle (retournement complet) est hard-codé dans un `switch-case` qui observe le nom de variable "isRoleRevealed".

## Points d'attention
- **Réflexion C#** : Risque en cas de fautes de frappe dans les chaînes de caractères transmises par RPC.
- **Routage de Simulation** : Les révélations destinées aux simulés ne sont PAS perdues ; elles sont centralisées sur l'Host. Lors d'un switch d'identité, l'Host recharge le visuel local en fonction de ce dictionnaire de simulation.
- `GetCharacterInfo` s'assure qu'une clé existe en l'ajoutant si manquante. 

