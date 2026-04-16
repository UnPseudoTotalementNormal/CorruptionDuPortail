# Power System

## Rôle
Incarne la logique asymétrique du jeu : chaque compétence, effet passif, ou attaque de personnage découle d'une classe de pouvoir modulaire. Il gère l'attribution et le cast.

## Déclencheur (Point d'entrée)
- **Instanciation :** Le `PowerManager` clone un `Power` depuis un rôle et le parente à un `Character` via `GivePowerToCharacter`.
- **Exécution :** L'AwakeningState requière un targetting via des appels réseaux (`OnPowerUsedServer`).

## Composants Clés
- `PowerManager` : Singleton Server-Authoritative qui trace la parenté et les spawns.
- `Power` : Classe de base gérant le flux d'exécution. Utilise désormais un split **OnUsedServerRpc** / **OnUsedClientRpc** pour éviter les boucles infinies (Stack Overflow) lors de l'exécution par l'Host au nom d'un joueur simulé.

## Données & État
- `CharacterManager.instance.GetSafeRpcTarget(ownerClientId)` : Gateway indispensable pour adresser les RPC aux joueurs simulés (IDs >= 100) en redirigeant le flux vers l'Host (ID 0).

## Couplage & Dépendances
- Couplé au `CharacterManager` pour injecter physiquement l'objet GameObject du pouvoir sous celui du personnage et pour le routage des identités.

## Points d'attention
- **Stack Overflow Prevention** : Ne jamais appeler une méthode RPC qui boucle sur le déclencheur original sans passer par le switch Server/Client dédié.
- Les clones de `Power` sont des NetworkBehaviours persistants. Leur cycle de vie est lié à celui du `Character`.
