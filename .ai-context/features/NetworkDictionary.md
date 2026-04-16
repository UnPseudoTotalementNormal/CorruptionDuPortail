# NetworkDictionary (Utilitaire Netcode)

## Rôle
C'est un module utilitaire ultra-spécifique (surcouche bas-niveau) qui vient combler une restriction majeure dans Unity Netcode for GameObjects (NGO) : l'absence native de dictionnaires synchronisés.

## Déclencheur (Point d'entrée)
- S'initialise et se manipule côté Serveur exactement comme une `NetworkVariable` standard (avec Permissions de lecture/écriture).

## Composants Clés
- `NetworkDictionary<TKey, TValue>` : Hérite de `NetworkVariableBase` et surcharge `WriteDelta`, `ReadDelta` pour forcer des paquets "diffs".
- `NetworkDictionaryEvent` : Struct interne notifiant si l'opération était un Ajout, une Suppression, ou un Reset, pour réduire la bande passante utilisée.

## Données & État
- L'utilisation de `NativeList` (Allocation Native persistante) et d'un Garbage Collector "Zéro allocation" en font un outil hautement performant requérant un désallocation (`Dispose()`) propre.

## Couplage & Dépendances
- **Agnostique :** N'a aucune dépendance envers la logique du jeu (GameLogic). C'est du pure Unity Netcode Serialization (utilisation de `FastBufferWriter`/`Reader`).

## Points d'attention
- **Contrainte de Typage strict :** Pour fonctionner, la `TKey` et la `TValue` DOIVENT impérativement être `unmanaged` (blittable au sens mémoire C# : int, float, struct pur). On ne peut pas propager de classes ou d'objets C# managés complexes via ce dictionnaire.
- Tout composant de jeu utilisant ce wrapper doit s'assurer que sa clé est unique (l'Event interne "Add" lèvera violemment une Exception si la clé est détectée comme un doublon).
