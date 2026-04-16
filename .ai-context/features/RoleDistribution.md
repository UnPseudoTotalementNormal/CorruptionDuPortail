# Role Distribution / Attribution System

## Rôle
Assigne aléatoirement un "Rôle" (donnée `RoleDataObject`) aux joueurs connectés (et aux bots/personnages simlués) au début de la partie. Confère les pouvoirs associés (`Power`) et réplique l'attribution.

## Déclencheur (Point d'entrée)
- **Serveur uniquement :** Exécution de la méthode `OnStartStateServer` du `RoleAttributionState` qui initialise la distribution.

## Composants Clés
- `RoleAttributionState` : L'état gérant la distribution aléatoire et le filtrage via un dictionnaire de configurations (`roleAttributionDictionary`).
- `RoleDataObject` : Données statiques sur un rôle, incluant les pouvoirs inhérents liés à ce dernier.
- `CharacterManager` : Instancie, manipule et synchronise sur le réseau l'objet `Character` qui recevra le Rôle assigné.
- `PowerManager` (et base conceptuelle) : Traite et attribue la logique des pouvoirs associés aux nouveaux rôles.

## Données & État
- `roleAttributionDictionary` : Configurations côté serveur définissant le quota ('roleToAttribute') et les permissions de rôles ('canBeFake' pour les bots).
- `role` (sur l'objet `Character`) : La classe de base clonée dynamiquement représentant le runtime d'un rôle.
- Dictionnaires temporaires : Logique de soustraction pour manipuler le tirage avec remise (aléatoire contraint).

## Couplage & Dépendances
- **Couplé au Système de Personnages :** Il dépend de la disponibilité des objets `Character` instanciés par le `CharacterManager`.
- **Méga-instanciation :** Déclenche plusieurs requêtes lourdes (clonage du Rôle, attribution des objets Power à travers le code backend et le réseau).

## Points d'attention
- Risques de sécurité/Synchronisation : Tout est attribué asynchrone côté serveur/client. De grandes quantités de RPC massifs (`GiveRoleToCharacterRpc`, instanciation de Pouvoirs) peuvent engorger le réseau.
- "Debt Technique" avérée : Des "TODO: TEMP FIX MAYBE" (notamment un WaitAndNextState avec un WaitForSeconds codé en dur) pour bypasser d'éventuels temps de synchronisation côté Netcode, au lieu de s'assurer de callbacks de réception.
- Manipulation des classes ScriptableObjects via Clone() peut générer des soucis de garbage collection si non correctement géré à grande échelle.
