# Smartphone OS System

## Rôle
Constitue l'interface utilisateur Diegétique principale du joueur. Ce système window manager englobe et gère les affichages des différentes "Apps" (Chat, Notes, etc.) via une mécanique de Swipes géométriques.

## Déclencheur (Point d'entrée)
- Interactions des `InputManager` externes sur les flèches directionnelles (`ArrowLeft`, `ArrowUp`...) via `OnSwipe()`.
- Un trigger de Caméra (`openOnCamera`) de Cinemachine activant l'overlay global.

## Composants Clés
- `SmartphoneController` : Manipule le Canvas du smartphone (Tweening) et l'aiguillage entre les Apps actives.
- `SmartphoneApp` : Classe abstraite / interface des applications individuelles (Logique de rendu).
- `BoardCamera` / `Cinemachine` : Gère le point de vue en perspective requérant l'affichage contextuel.

## Données & État
- `currentApp` : Pointeur sur le `SmartphoneApp` actuellement focus.
- `IsOpen` : Boolean contrôlant la rétractation physique (CanvasGroup et position Z/Y) du smartphone à l'écran.
- Dictionnaires Vectoriels (`swipeDirectionVectors`) déterminant la structuration 2D des applications (Grille spatiale Haut/Bas/Gauche/Droite).

## Couplage & Dépendances
- Se couple organiquement à l'`InputManager` pour s'injecter dans la stack des actions claviers sans polluer son propre code avec de la détection de frappe.
- Emploie `DOTween` pour l'intégralité du ressentit visuel organique (Animation d'ancrages UI).

## Points d'attention
- Dépendance forte et implicite à une résolution/structure d'Ancre UI stricte (`anchoredPosition = phoneCanvasTransform.sizeDelta`). Tout recadrage d'UI natif Unity sans DOTween pourrait casser l'ergonomie.
- Le polling récursif sur `nextApp.GetNeighborApp()` lors des désactivations d'app pourrait déclencher des crashs (boucle infinie) si la grille spatiale des apps se retrouve refermée sur elle-même (cycle) et que toutes les apps sont inactives.
