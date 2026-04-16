# Power System

## Rôle
Incarne la logique asymétrique du jeu : chaque compétence, effet passif, ou attaque de personnage découle d'une classe de pouvoir modulaire. Il gère l'attribution et le cast.

## Déclencheur (Point d'entrée)
- **Instanciation :** Le `PowerManager` clone un `Power` depuis un rôle et le parente à un `Character` via `GivePowerToCharacter`.
- **Exécution :** L'AwakeningState requière un targetting via des appels réseaux (`OnPowerUsedServer`).

## Composants Clés
- `PowerManager` : Singleton Server-Authoritative qui trace la parenté (`ReparentPowerToCharacterServer`) et les spawns.
- `Power` (Classe parente + Enfants `P_...`) : Composant monolithique contenant l'identifiant asymétrique et la logique brute de son exécution.
- `PowerDataObject` : ScriptableObject ou structure contenant les métadonnées pour l'UI, le nom et le clonage.

## Données & État
- Chaque instance de `Power` instancié possède dynamiquement un ID vers son `ownerCharacter` et est parente (Unity Hierarchy) de son maître.

## Couplage & Dépendances
- Extrêmement dispersé. Les enfants de `Power` (ex: `PCorruptingMark`) dépendent potentiellement de toutes sortes de systèmes externes (`ChainingManager`, faction logic, etc.) pour réaliser leur effet unique.
- Couplé au `CharacterManager` pour injecter physiquement l'objet GameObject du pouvoir sous celui du personnage.

## Points d'attention
- Des douzaines de composants `Power` sont conçus en POO (`PCardsShuffling.cs`, `PReincarnation.cs`). L'architecture NetworkBehaviour pour tous les pouvoirs peut vite surcharger le Netcode si les NetworkObjectId explosent en nombre et en appels non-essentiels.
- Complexité potentielle d'équilibrage : il faut scruter de très près l'Event `OnPowerReparentedServer` car certains objets Networked peuvent mal supporter un changement de parenté dynamique multi-clients en pleine game-loop.
