# Focus System

## Rôle
Fournit un feedback visuel dynamique et centralisé pour mettre en évidence certaines entités UI ou 3D (comme les Cartes des joueurs ou les Barres d'états) selon des critères logiques métier complexes (lorsqu'un joueur doit effectuer un choix valide).

## Déclencheur (Point d'entrée)
- Souvent appelé par le `RoleTargetSystem` ou lors de phases de choix via les méthodes `SetFocusOnType`. Ce point d'entrée autorise une fonction lambda (`Func<ulong, bool>`) ou des `TargetIncludeFlags` pour déterminer sur le champ la validité visuelle d'une cible.

## Composants Clés
- `FocusManager` : Singleton contrôlant l'opacité temporelle globale (Fade) via DOTween et élevant la hiérarchie de rendu (`sortingOrder += FOCUS_ORDER_IN_LAYER`).
- `FocusParticleUpdater` & `TransformFollower` : S'attachent aux éléments focalisés pour simuler une "aura" de bordure (ParticleSystemShapeType.BoxEdge) suivant dynamiquement les Canvas locaux.

## Données & État
- `currentFocusObjects` : Liste d'instances de la surcouche `FocusObject`, qui mémorise la référence du `GameObject` d'origine et son `ParticleSystem` lié, permettant son nettoyage ultérieur (`UnfocusAll`).

## Couplage & Dépendances
- **Extrêmement couplé au visuel global** : Traverse le `GameManager` (`charactersBar`) et le `BoardManager` (`visibleCards`) pour itérer sur l'intégralité des représentations de joueurs disponibles dans la scène à ce moment précis.

## Points d'attention
- Modifie directement les `sortingOrder` des Canvas enfants des objets ciblés. S'il n'est pas proprement annulé par un `UnfocusObject(GameObject)`, des bugs sévères de profondeur d'UI (Z-Index cassé, particules fantômes) vont persister dans la scène et se cumuler à chaque invocation mal nettoyée.
