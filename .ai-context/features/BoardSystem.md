# Board & Card System

## Rôle
Désigner et actualiser la représentation physique et spatiale des joueurs participants dans la scène 3D/2D, via des objets "Cartes". 

## Déclencheur (Point d'entrée)
- **Instanciation :** S'abonne au `CharacterManager.onCharactersListUpdated` dès le Start.
- **Animations / Affichage :** Invoqué par les GameStates de vote ou de début/fin pour aligner visuellement les joueurs.

## Composants Clés
- `BoardManager` : Singleton NetworkBehaviour. Maintient la physicalité (les GameObjects) des joueurs connectés.
- `Card` : Le composant physique lié. Hérite potentiellement des données du `Character`.
- Animations asynchrones : Combinaison pure de `DOTween` et `UniTask` (`ShowAllPlayerCards`, `PlaceAllCardsToPosition`).

## Données & État
- `visibleCards` : (List<Card>) Suivi local des entités physiques de type Carte.
- `cancelTokens` : (List<CancellationTokenSource>) Empêche les animations de se marcher dessus via des cancellations agressives (`OnStartingNewAnim`).

## Couplage & Dépendances
- Purement cosmétique et dépendant du `CharacterManager` pour extraire la source de vérité (qui est qui, qui est vivant).
- **Injection des toiles (Canvas) :** Les cartes fournissent l'ancrage UI (`VoteCanvas` par exemple) manipulé ensuite par les logiques de l'état `VoteState`.

## Points d'attention
- Usage massif du Cancellation Token pour arrêter et redémarrer les tweens. Très bonne pratique (permet de spammer ou de rattraper un retard réseau sans casser la scène visuelle).
- La position des cartes `GetCardPlacedPosition` est codée en dur avec du Card_Spacing et de l'overflow rudimentaire, attention au design pour les grandes parties ou lobbies à forte densité de joueurs.
