# Power Usage System

## Rôle
Gère la validation en direct et la machine à états de l'utilisation physique d'un pouvoir ciblé par le joueur humain depuis l'interface (Click sur la barre d'UI).

## Déclencheur (Point d'entrée)
- Le joueur sélectionne une capacité asymétrique dans la `GameManager.instance.powersBar` (déclenchant `OnPowerClicked`).

## Composants Clés
- `PowerUsageManager` : Contrôleur local attaché à la GameLogic qui valide le droit du joueur à utiliser le pouvoir sélectionné.
- Instances de `Power` (ex: héritières) qui possèdent des méthodes d'activation comme `CanUse()`, `StartUse()`, et `UsingPowerUpdate()`.

## Données & État
- `currentPower` : Mémorise quel pouvoir est actuellement "équipé" ou "en cours d'attribution" (pour ciblage par exemple). S'assure que *currentPower* est nettoyé via l'appel `Cancel()` si un clic annule l'ordre.

## Couplage & Dépendances
- Purement orienté sur le client local (`GetLocalCharacter()`), il ne manipule que les compétences du joueur appelant (il n'est pas Autoritaire ni Synchro Réseau, ce sont les conséquences du Pouvoir qui le seront).

## Points d'attention
- Le flow logique force une annulation explicite de l'ancien pouvoir (`currentPower.Cancel()`) si le joueur change de sélection. Si l'un des pouvoirs personnalisés (POO) oublie d'implémenter son propre reset d'états dans `Cancel()`, l'UI ou des filtres de focus pourraient rester bloqués.
