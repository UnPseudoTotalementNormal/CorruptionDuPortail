# Awakening & Chaining Flow

## Rôle
Organise le tour principal d'action asynchrone (souvent la phase de Nuit/Awakening). Il résout les effets superposés ou ordonnés (qui meurt en premier, etc.) de manière séquentielle et sécurisée.

## Déclencheur (Point d'entrée)
- Initialisé quand le `GameManager` transite l'état de jeu sur `AwakeningState` ou `ChainingState`.

## Composants Clés
- `AwakeningState` : Phase d'attente d'interactions asymétriques des rôles (actions des pouvoirs).
- `ChainingManager` : File d'attente (NetworkList) et coordinateur de morts / chaînes d'événements (TargetLink, actions récursives).
- `TakeDownThePortalState` : Séquence conditionnelle spécifique déclenchée à l'issue de certaines chaînes de pouvoirs.

## Données & État
- `chainingPlayers` : `NetworkList<ulong>` gérant explicitement l'ordre de priorité ou les cibles retenues pour mourir/être corrompues.
- Variantes conditionnelles asynchrones (`shouldActivate` dans le portail).

## Couplage & Dépendances
- Hautement couplé avec le `PowerSystem` puisque les pouvoirs déclenchent des entrées dans le `chainingPlayers`.
- S'interface avec le `GameInfoRevealer` pour dévoiler publiquement qu'un personnage précis a été exécuté ou chaîné.

## Points d'attention
- L'utilisation combinée d'états purs de `GameState` avec un `ChainingManager` persistant indique une architecture scindée : la boucle du jeu a dû confier la mémoire court-terme au ChainingManager pour dépasser les limitations des `GameStates` éphémères. Il faut assurer le nettoyage (Clear) de ces listes à chaque rotation de phase majeure.
