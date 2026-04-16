# Tech Stack

- **Engine** : Unity (C#)
- **Networking** : Unity Netcode for GameObjects (NGO)
- **UI** : Canvas (A priori, avec des BoardManagers et Canvas spécifiques pour les interactions)
- **Utilities** : UniTask (pour l'asynchrone), AYellowpaper.SerializedCollections (pour les dictionnaires sérialisés dans l'inspecteur)

# Global Architecture

- **State Machine Centralisée (GameManager) :** Le coeur du jeu tourne sur un système de `GameState` géré par le `GameManager`. Chaque état gère sa logique côté serveur (`StateUpdateServer`, `OnStartStateServer`) et côté client (`StateUpdateClient`, `OnStartStateClient`).
- **Autorité Serveur (Server-Authoritative) :** La logique critique (transitions d'états, attribution des rôles, calcul des votes) est réalisée uniquement sur le serveur.
- **Réplication et RPC :** Les informations sont propagées via des RPC Customs (`DoStateMethodRpc`) qui passent par le GameManager pour atteindre le bon State et invoquer la méthode par réflexion (`ReflectionHelper.cs` potentiel, ou `MethodInfo.Invoke`). L'utilisation intensive de RPC assure la synchronisation.

# Main Dependencies & Routing

- `GameManager` : Gère l'initialisation, la séquence de `GameState`, et le routage des actions réseau (NetworkActions / Custom RPCs).
- `CharacterManager` : Gère le pool des joueurs connectés (`Character`), leur cycle de vie, et la synchronisation de leurs données (`ownerClientId`, etc.).
- `ChainingManager` : Intervient à la fin d'états spécifiques (ex: système de vote) pour mettre en file d'attente les joueurs ou événements.
- `PowerManager` : Gère le cycle de vie des pouvoirs conférés par les rôles attribués lors du Game Loop.

# Core Business Flow (Game Loop)

Le cycle du jeu est une succession stricte définie par les `GameState` :

1. Lobby (Attente des joueurs)
2. Introduction & Role Attribution
3. Awakening (Utilisation des pouvoirs)
4. Chaining / TakeDownThePortal (Révélations ou Evènements liés au portail)
5. Vote
6. Recap & Victory Condition Check
7. Game Ending

# Modules & Features détaillés (`.ai-context/features/`)

Pour éviter de surcharger cette cartographie globale, la logique métier de chaque sous-système est isolée et documentée dans le dossier `features/` :

- `CharacterSystem.md` : Entités, Factions, et cycle de vie.
- `PowerSystem.md` : Architecture Asymétrique (POO) des capacités.
- `RoleDistribution.md` : Algorithme asynchrone d'attribution des rôles.
- `AwakeningSystem.md` : File d'attente (Chaining) des pouvoirs asynchrones la nuit.
- `VoteSystem.md` : Cycle de vote, timer, et élimination.
- `SmartphoneOS.md` : Interface hub 2D, swipe UX.
- `NoteSystem.md` : Application locale de traçabilité des déductions.
- `ChatSystem.md` : Messaging par canaux et FMOD Event.
- `BoardSystem.md` : Représentation 3D (Cartes), et animations UniTask/DOTween.
- `RoleTargetSystem.md` : Interface de ciblage (pont UI vers Réseau).
- `InputSystem.md` : File (LIFO) de capture des Input natifs (ex: Escape button stack).
- `FocusSystem.md` : Feedback visuel Canvas et Particules pour les focus d'interface.
- `RevealInfoSystem.md` : Référentiel des informations secrètes vs publiques (Reflection enum).
- `MessageSystem.md` : Décalage asynchrone des notifications publiques.
- `NetworkDictionary.md` : [Tech Tool] Surcouche Netcode vitale palliant à l'absence de dictionnaires réseau natifs dans NGO.
- `TooltipSystem.md` : Moteur de fenêtres flottantes avec parser dynamique (Réflexion C# et TextMeshPro Links).
- `AudioSystem.md` : Wrapper RPC pour FMOD Studio (Layering de musiques et prévention de fuite mémoire).
- `LobbySystem.md` : Integration Unity Services Lobbies (Heartbeat asynchrone).
- `PowerUsageSystem.md` : Validation locale d'assignation et reset d'un ciblage de Pouvoir par un joueur humain.
- `BoardCameraSystem.md` : Orchestration des cinématiques et Focus Cinemachine.
- `LightAndFXSystem.md` : Réaction chromatique (Jour/Nuit/Corruption) interfacée sur les GameStates.
