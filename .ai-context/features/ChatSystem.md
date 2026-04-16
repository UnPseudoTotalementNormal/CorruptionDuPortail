# Chat & Communication System

## Rôle
Fournit un système de messagerie textuelle multicanaux permettant aux joueurs de communiquer ensemble, avec le serveur ou au sein de groupes privés ou spécifiques.

## Déclencheur (Point d'entrée)
- **Découverte :** Invoquée dynamiquement (ex: au boot ou via scripts) via `DiscoverChat` ou `DiscoverChatRpc`.
- **Envoi de message :** Le joueur interagit avec une invite UI (non explicitée dans le core manager mais connectée à `TrySendChatMessage`).

## Composants Clés
- `ChatManager` : Singleton NetworkBehaviour, gère la liste globale des messages, les canaux actifs, et les envois réseaux.
- `ChatWindow` / `ChatMessage` : Structures de données conservant la hiérarchie Canaux -> Messages localement.
- **AudioSystem (FMOD)** : S'abonne aux événements d'envoi, de réception et de changement de canal pour jouer des retours sonores configurables (`EventReference`).

## Données & État
- `discoveredChatIds` : (HashSet<int>) ID des canaux que le client est autorisé à lire/cibler.
- `chatWindows` : (List<ChatWindow>) Stockage asynchrone des messages filtrés par ID de canal.
- `activeChatId` : ID du canal où l'input du joueur partira.

## Couplage & Dépendances
- **Fortement couplé à FMOD :** L'audio est codé en dur dans les callbacks du singleton via `GameAudioManager.instance.PlayOneShot`.
- Déborde vis-à-vis des ID : Utilise un Enum explicite (`ChatWindowIDs.General`, `.Server`) tout en permettant de l'override de nom local.
- Dépendance asymétrique : L'interface visuelle (`ChatPanel`) s'abonne aux événements `onChatMessageReceived` et `onActiveChatChanged`.

## Points d'attention
- **Sécurité serveur :** `ReceiveChatMessageRpc` est par défaut `[Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]`, appelé lui-même juste après que le serveur ait reçu `SendChatMessageServerRpc`. Le paramètre d'override de cible pourrait représenter une faille permettant au serveur de potentiellement rediriger ou à un client falsifié d'abuser du scope si la validation n'est pas imperméable.
- **Fuite mémoire potentielle :** La collection `chatWindows.chatMessages` n'a pas l'air de disposer de limite maximale documentée dans le manager (List.Add infini).
- Les événements sonores ne gèrent pas le focus/mute globaux au-delà de FMOD même (s'abonnant via des lambda brutes `_ => { ... }`), difficiles à dé-référencer potentiellement à la destruction.
