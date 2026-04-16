# Message System

## Rôle
Assurer le stockage et la révélation asynchrone ("Broadcasting") de notifications publiques à tous les joueurs à la fin d'un cycle précis (typiquement calculé par "Jour").

## Déclencheur (Point d'entrée)
- **Stockage Asynchrone :** `SendMessageRpc` peut être sollicité n'importe quand par le serveur pour empiler un événement caché.
- **Révélation Publique :** `RevealAllMessage()` est formellement appelé par l'autorité du serveur (State Machine) pour basculer les élements cachés vers la liste visible réseau.

## Composants Clés
- `MessageManager` : NetworkBehaviour (Singleton) qui maintient les listes réseau.
- `MessageInfo` : Structure sérialisée (`INetworkSerializable`) contenant le sender, un contenu lourd (`FixedString512Bytes`) et le jour d'envoi.

## Données & État
- Divisé en deux `NetworkList<MessageInfo>` distinctes :
  1. `messagesToReveal` (Tampons en l'attente d'une transition, bien que techniquement public par le NetworkList, logiquement privé)
  2. `revealedMessages` (Consolidée et affichable par l'UI)

## Couplage & Dépendances
- Extrait le timer/cycle via le couplage dur au `GameManager.instance.currentDay`.
- Agit de manière indépendante vis-à-vis du `ChatSystem` (qui gère l'instantanéité et l'audio spatial).

## Points d'attention
- Le type de struct contenant des strings non alloués dynamiquement (`FixedString512Bytes`) est excellent pour prévenir les attaques d'allocation, mais force les messages réseau à ne pas excéder 512 bytes de char.
- `NetworkList` se charge automatiquement de la propagation du delta de ces données, donc `RevealAllMessage` provoque nativement un `OnListChanged` chez les clients.
