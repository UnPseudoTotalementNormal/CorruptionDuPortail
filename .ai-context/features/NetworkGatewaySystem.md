# Network Gateway System (RPC Routing)

## Rôle
Permet au serveur de communiquer avec des "joueurs simulés" (bots) qui n'ont pas de connexion client réelle. Ce système reroute dynamiquement les RPC destinés à ces bots vers la machine Host, tout en maintenant l'illusion d'une communication individuelle.

## Concepts Clés
- **Client ID Convention** :
    - `0` : Host (Serveur + Client 0).
    - `1 - 99` : Clients réels connectés.
    - `100+` : Joueurs simulés (Bots).

- **Gateway Routing (`GetSafeRpcTarget`)** :
    Lorsqu'on veut envoyer un RPC à un ID de bot (>=100), Netcode échouerait car il n'y a pas de `clientId` correspondant. `GetSafeRpcTarget(clientId)` intercepte ces IDs et les remplace par `0` (l'Host), qui est responsable de l'exécution au nom du bot.

- **Identity Abstraction (`IsLocalOrSimulated`)** :
    Utilisé dans les gardes de sécurité (ex: `if (!IsLocalOrSimulated(ownerId)) return;`). Cela permet à l'Host de passer les vérifications d'identité pour ses bots tout en restant bloqué pour les actions des autres joueurs réels.

## Composants Clés
- `CharacterManager` : Implémente `GetSafeRpcTarget` et `IsLocalOrSimulated`.
- **Custom RPC Pattern** : Les scripts (notamment les `Power`) utilisent `GetSafeRpcTarget` pour obtenir l'objet `RpcParams` correct avant d'appeler un `[Rpc]`.

## Flux type d'un Pouvoir simulé
1. Le bot (côté serveur) déclenche un pouvoir.
2. Le `Power` appelle un RPC `OnUsedClientRpc` en utilisant `CharacterManager.instance.GetSafeRpcTarget(botId)`.
3. Le message est routé vers l'Host.
4. L'Host reçoit le RPC, voit qu'il "possède" (virtuellement) l'identité du bot via `IsLocalOrSimulated`, et exécute la logique visuelle/client.

## Points d'attention
- **Persistent RpcTarget** : Utilise `RpcTargetUse.Persistent` dans le gateway pour assurer la stabilité du routage.
- **Stack Overflow** : Faire attention aux boucles RPC. Si l'Host exécute un RPC pour un bot qui redéclenche un RPC serveur, on peut créer une boucle infinie. Utiliser le split Server/Client explicite.
