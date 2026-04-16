# Role Target System

## Rôle
Assurer le pont entre le système asymétrique de pouvoirs (souvent abstrait et invisible) et les interactions physiques du joueur, gérant la demande de ciblage et soumettant le choix réseau.

## Déclencheur (Point d'entrée)
- Souvent convoqué par les objets de type `Power` (ex: `GetTarget()`) lorsque le joueur utilise sa compétence active asymétrique.

## Composants Clés
- `RoleTargetSystem` : Le contrôleur logique qui requière, valide physiquement et retourne la cible choisie via le réseau.
- `RoleTargetViewPanel` : Visuel UI ou Overlay signifiant au joueur qu'il est en mode sélection.

## Données & État
- Attend une intéraction utilisateur (clic/focus) sur une entité (vraisemblablement une `Card` du `BoardManager`).
- Ne stocke qu'éphémèrement le `TargetID` (ulong) pour le passer au pouvoir.

## Couplage & Dépendances
- **Fortement couplé au Pouvoir originel :** C'est le pouvoir qui dicte combien de cibles sont attendues ou leur validité sémantique.
- S'interface avec l'InputSystem/BoardSystem pour relier le point-and-click à une action réseau confirmée.

## Points d'attention
- C'est un goulot d'étranglement de l'UI/UX. S'il ne gère pas bien ses états de focus (ex : le joueur tente de fermer le smartphone pendant qu'il doit cibler), des soft-locks peuvent apparaître côté client puisque l'état asynchrone attendrait indéfiniment une résolution de cible.
