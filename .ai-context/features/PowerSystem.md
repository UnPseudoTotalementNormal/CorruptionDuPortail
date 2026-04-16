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
- **Lien ID** : Le `Power` est lié à un `ownerClientId`. Pour adresser des RPC à ce propriétaire, il est **impératif** d'utiliser le système de Gateway (voir `NetworkGatewaySystem.md`).

## Couplage & Dépendances
- `CharacterManager` : Injection du pouvoir et routage des identités via `GetSafeRpcTarget`.

## Points d'attention
- **Stack Overflow Prevention** : Ne jamais appeler une méthode RPC qui boucle sur le déclencheur original sans passer par le switch Server/Client dédié. C'est crucial car l'Host exécute souvent le code pour ses bots.
- **Routage Gateway** : N'utilisez pas `RpcTarget.Single(clientId)` directement si le pouvoir peut appartenir à un bot (ID >= 100).
