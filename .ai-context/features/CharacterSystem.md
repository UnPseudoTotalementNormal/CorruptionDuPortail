# Character System

## Rôle
Identifie, stocke, et gère le cycle de vie de chaque participant à la partie, qu'il s'agisse d'un vrai joueur connecté (lié par le réseau) ou d'une simulation/bot ("Fake").

## Déclencheur (Point d'entrée)
- L'instanciation de base se fait via `CharacterManager.AddNewCharacter()` (généralement à la connexion réseau ou via le Lobby).
- Les modifications d'état (élimination, altération) peuvent être commanditées par le `GameManager` ou le `PowerManager`.

## Composants Clés
- `CharacterManager` : NetworkBehaviour (Singleton) qui maintient la collection de tous les personnages. Gère l'identité abstraite (voir `IdentitySystem.md`) et le routage RPC (voir `NetworkGatewaySystem.md`).
- `Character` : L'entité fondamentale synchronisée. Possède la propriété `isFake` (robots IA) vs joueurs (réels/simulés).

## Données & État
- `_characters` : Liste dynamique maintenue par le `CharacterManager`.
- `ownerClientId` : Clé de résolution (0-99 réels, >=100 simulés).
- `IsLocalOrSimulated(ulong id)` : Helper d'identité centralisé.
- Rôle / Factions / États : `Role`, `isEliminated`, etc., exposés pour la réplication ciblée.

## Couplage & Dépendances
- **Ultra-couplé :** C'est le carrefour des données pour le `GameManager`, `PowerManager`, et `BoardManager`. Presque tous les systèmes y piochent l'état de validation ("Est-ce qu'il peut voter ?", "Est-il mort ?").
- Le système abstrait la notion de "vrai joueur" vs "bot", forçant les autres systèmes à requêter `!_c.isFake`.

## Points d'attention
- **Gestion des références dynamiques :** L'approche des *Fake Characters* impose une génération d'ID localisés `GameValues.FAKE_CLIENT_ID - N` qui peut être fragile lors d'une reconnexion ou d'une migration d'Host.
- Des coroutines comme `TriggerOnCharactersListUpdatedAtEndOfFrame()` orchestrent l'update UI pour éviter l'engorgement, ce qui induit un micro-lag volontaire par rapport au réseau.
