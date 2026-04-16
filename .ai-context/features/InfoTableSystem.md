# Info Table System (Deduction)

## Rôle
Système de "carnet de bord" partagé mais à données locales, permettant au joueur de traquer ses déductions sur l'identité des autres. Il inclut une logique d'analyse pour détecter les impossibilités (conflits) dans les déclarations/déductions du joueur.

## Déclencheur (Point d'entrée)
- **Ouverture :** Via l'application dédiée dans le Smartphone.
- **Initialisation :** `OnGameStarted` déclenche la construction dynamique de la grille (Joueurs en lignes, Rôles en colonnes).

## Composants Clés
- `InfoTableSystem` : Manager principal. Gère la construction de l'UI et la vérification globale des conflits.
- `InfoTablePlayerRoleHandler` : Gère une ligne (un joueur). Émet des événements quand le conflit local change.
- `InfoRoleChecker` : Gère une cellule (un bouton à état). Cycles : `None` -> `Sure` -> `Not` -> `Maybe`.
- `ConflictType` : Enum (`None`, `PlayerMultipleRoles`, `RoleOverCapacity`).

## Logique de Conflit
1. **Conflit Local (`PlayerMultipleRoles`)** : Si un joueur est marqué comme "Sûr" (`Sure`) sur deux rôles différents.
2. **Conflit Global (`RoleOverCapacity`)** : Si le nombre de joueurs marqués comme "Sûr" pour un rôle spécifique dépasse le nombre réel de joueurs possédant ce rôle dans la partie.

## Données & État
- `roleCounts` : Dictionnaire mémorisant le nombre total de chaque rôle présent dans la partie (extrait au démarrage).
- `onCharacterInfoRevealedChanged` : Le système s'abonne au `GameInfoRevealer`. Si le serveur révèle officiellement le rôle d'un joueur, la ligne correspondante est **verrouillée** (`LockWithRevealedRole`) avec la valeur réelle.

## Couplage & Dépendances
- **UI Table System** : Utilise des composants génériques `Header`, `ChildHeader`, et `HorizontalLayoutGroup` pour la mise en page dynamique.
- **GameInfoRevealer** : Indispensable pour la synchronisation des données "officielles" (révélations serveur).

## Points d'attention
- **Verrouillage** : Une fois un rôle révélé par le serveur, le joueur ne peut plus modifier ses notes sur cette ligne (sécurité de cohérence).
- **Réinitialisation** : La grille est entièrement détruite et reconstruite à chaque nouveau démarrage de jeu (`Clean()` -> `BuildGameUi()`).
