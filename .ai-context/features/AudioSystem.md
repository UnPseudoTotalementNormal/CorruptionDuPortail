# Audio System & FMOD API

## Rôle
Agit comme un pont (wrapper) entre l'intégrité réseau (Unity Netcode) et le moteur de son externe FMOD Studio. Il distribue les instructions de Play/Stop de musique et d'effets sonores à travers le réseau tout en gardant une trace de la mémoire FMOD locale.

## Déclencheur (Point d'entrée)
- Méthodes locales (`PlayMusic`, `PlayOneShot`) déclenchées typiquement par des events d'UI ou des animations locales.
- Méthodes RPC (`PlayMusicRpc`, `PlayEventInstanceRpc`) déclenchées par la logique serveur ou les requêtes asynchrones nécessitant la synchronisation de l'ambiance pour les clients ciblés (`SendTo.SpecifiedInParams`).

## Composants Clés
- `GameAudioManager` : NetworkBehaviour (Singleton) qui englobe l'API bas niveau de FMOD (`FMOD.Studio.EventInstance`, `RuntimeManager`).
- `MusicPlayerStartSource` : Composant accessoire (potentiellement dans la hiérarchie) pour l'activation au Start.

## Données & État
- `activeMusicInstances` (List) : Agit comme une pile contextuelle pour les pistes musicales. Lors de l'arrêt d'une musique, la liste relance la précédente (`activeMusicInstances[^1].start()`), simulant un système de Layering musical très robuste pour le changement d'états (ex: Musique d'Awakening State écrasant puis restaurant la Musique du Lobby).
- `eventInstances` (Dictionary) : Permet de logger manuellement un SFX continu (ex: bruit de timer) grâce à une `_instanceKey`, pour pouvoir explicitement lui envoyer un "Stop" plus tard.

## Couplage & Dépendances
- Partiellement découplé. Le serveur n'intègre pas FMOD, il ne fait que forward des `FixedString128Bytes` représentant les chemins virtuels FMOD (ex: "event:/Music/VoteTheme") aux clients.

## Points d'attention
- **Gestion stricte de la mémoire FMOD :** La fonction `Release()` est appelée systématiquement après chaque appel à `stop()` sur une Instance FMOD dans les dictionnaires. Oublier de faire ce Release sur les nouvelles fonctions asynchrones pourrait provoquer une fuite de mémoire critique côté C++ Core de FMOD.
- Conversion obligatoire de `string` géré en C# vers `FixedString128Bytes` ou `FixedString64Bytes` pour la sérialisation réseau des RPC. On ne peut pas envoyer aveuglément les chemins FMOD complets s'ils dépassent 128 caractères.
