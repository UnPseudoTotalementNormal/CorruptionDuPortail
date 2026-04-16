# Identity System (Possession)

## Rôle
Permet à un développeur (sur l'Host) de changer son identité locale pour "posséder" celle d'un autre joueur (réel ou simulé). Cela permet de tester l'UI et les interactions du point de vue de n'importe quel personnage sans relancer la partie.

## Déclencheur (Point d'entrée)
- **DevInput :** Géré par `DevIdentityController.cs`.
    - `F1` : Spawn un joueur simulé (ID >= 100).
    - `F2` / `F3` : Cycle entre les identités disponibles.
    - `F4` : Reset l'identité (retour à l'Host).

## Composants Clés
- `CharacterManager` :
    - `GetLocalClientId()` : Retourne l'ID possédé si présent, sinon le `LocalClientId` de Netcode. **C'est le point d'entrée unique pour toute l'UI.**
    - `SetPossessedIdentity(ulong? id)` : Définit l'ID de debug et déclenche `onLocalIdentityChanged`.
- `DevIdentityController` : Capture les inputs clavier pour cycler les identités.
- `MeIconCard` : Écoute `onLocalIdentityChanged` pour afficher/masquer l'icône "Moi" sur la carte correspondante.

## Données & État
- `_debugPossessedId` (ulong?) : Variable locale au `CharacterManager` (non synchronisée sur le réseau).

## Couplage & Dépendances
- **Netcode for GameObjects** : Utilise `NetworkManager.LocalClientId` comme base.
- **UI** : Presque tous les composants UI (PowerBar, Smartphone, Board) dépendent de `GetLocalClientId()` via le `CharacterManager`.

## Points d'attention
- **ID Convention** : Les joueurs simulés ont des IDs >= 100 par convention.
- **Localité** : La possession est purement visuelle et locale. Elle ne change pas l'autorité réseau (Ownership) des objets, mais "trompe" les scripts clients pour qu'ils se comportent comme s'ils étaient le joueur visé.
- **Events** : Toujours s'abonner à `onLocalIdentityChanged` pour rafraîchir les éléments d'interface qui dépendent de l'identité du joueur.
