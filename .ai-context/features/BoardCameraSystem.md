# Board Camera System

## Rôle
Orchestre l'enchaînement et les priorités des différentes caméras virtuelles (Cinemachine) de la Scène de jeu pour réagir aux menus et à l'ouverture du Smartphone.

## Composants Clés
- `BoardCameraManager` : Superviseur des angles de caméra.
- Machines d'états Cinemachine locales.

## Couplage & Dépendances
- Couplé aux actions du joueur pour transiter d'une caméra de Focus Carte vers une caméra globale avec un fondu lisse (Damping / Blend settings de Cinemachine).

## Points d'attention
- Tout recadrage (FOV) dépend directement des paramètres hard-codés des Virtual Cameras. L'ajout d'une nouvelle dimension (ex: vue d'un chat log agrandi) requiert un preset Cinemachine explicite pour ne pas briser la mise en scène diégétique.
