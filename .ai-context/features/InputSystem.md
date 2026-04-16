# Input & Action Stack

## Rôle
Servir d'entonnoir unique et de filtre (middleware) entre les touches raw du InputSystem Unity et les fonctions métier du projet, fournissant ainsi une architecture par événements pour les annulations/pauses (Escape Button).

## Déclencheur (Point d'entrée)
- Reçoit en live les callbacks natifs du Unity Input System (`InputAction.CallbackContext`).

## Composants Clés
- `InputManager` : Singleton central où d'autres composants (`SmartphoneController`, etc.) viennent enregistrer (`RegisterAction`) ou désenregistrer leurs callbacks.
- `ActionStack` : Une implémentation de pile (LIFO) typiquement conçue pour le bouton d'annulation/retour (Escape), afin que seule l'interface active supérieure se ferme.
- `InputActions` : Proxy typé pour le routage de chaque touche.

## Données & État
- Mapping Enum->Event : Utilise des dictionnaires ou switch-case (`InputID.ArrowUp`) qui redirigent l'activation native vers les Actions C#.

## Couplage & Dépendances
- Seules les couches très frontales du jeu y ont accès ; le code backend ne s'en soucie pas.
- Assainit entièrement les intéractions des controlleurs (`Controller` enfant) pour respecter le focus sans écraser leurs comportements.

## Points d'attention
- La pile `onEscapePressedStack` exige des `UnregisterAction` scrupuleux par les composants s'ils se détruisent ou entrent en sommeil, sans quoi des Memory Leaks d'event handlers apparaîtront et briseront l'UI lors de l'appui sur Escape.
- Risque potentiel : l'InputManager tourne côté local purement, mais pourrait dénaturer le Game Loop Serveur s'il était directement couplé à de gros retours asynchrones en l'absence de vérification Client/Network authority.
