# Lobby System

## Rôle
S'interface directement avec l'API *Unity Services Lobbies* pour créer, rejoindre et maintenir en vie l'instance de matchmaking (Lobby) avant le lancement d'une partie sur le serveur.

## Déclencheur (Point d'entrée)
- Menus d'UI de connexion (`LoginMenu.cs`, `MainMenu.cs`) qui invoquent directement des commandes asynchrones : `CreateLobby()`, `JoinLobby()`, ou `JoinLobbyByCode()`.

## Composants Clés
- `LobbyManager` : Singleton global persistant (`DontDestroyOnLoad`) encapsulant tout le boilerplate de l'API Unity (Modifications asynchrones, suppressions).

## Données & État
- Maintient des tâches asynchrones (`Task`) de fond protégées par des `CancellationTokenSource`.
- `Heartbeat` : Requête émise toutes les 15 secondes (`HEARTBEAT_INTERVAL`) pour signifier à Unity Lobbies que l'Host est toujours réactif.
- `Polling` : Sondage récursif toutes les 5.1 secondes (`LOBBY_POLL_INTERVAL`) pour update l'UI client sur les nouveaux joueurs ou settings.

## Couplage & Dépendances
- **Extrêmement découplé :** Agit de façon indépendante, reposant exclusivement sur le package `Unity.Services.Lobbies`. 
- Gère son propre système d'events (`OnLobbyCreated`, `OnLobbyUpdated`) que les UI interceptent.

## Points d'attention
- La dépendance au *Heartbeat* est vitale. Si ce script est désactivé ou que les *Tokens* sont accidentellement annulés, les lobbies sur l'infrastructure Unity s'effondreront silencieusement après ~30 secondes (Timeout Host natif de Unity).
