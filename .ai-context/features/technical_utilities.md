# Technical Utilities & Extensions

## Rôle
Fournit un ensemble d'outils transverses (Helpers) pour simplifier le code, améliorer l'UX via des animations standardisées, et optimiser l'ergonomie de l'Inspecteur Unity.

## Extensions C# (Assets/Scripts/Extensions)

### 🖥️ UI & Feedback
- **`CanvasGroupExtensions`** :
    - `DoShowGroup(duration, interactable, blocksRaycasts, alpha)` : Animation `DOTween` unifiée pour faire apparaître un panneau.
    - `DoHideGroup(duration, interactable, blocksRaycasts)` : Animation de disparition.
- **`TransformExtensions` / `RectTransformExtensions`** : Divers helpers pour la manipulation spatiale.

### 🌐 Networking
- **`NetworkObjectExtensions`** : Facilite la gestion du cycle de vie des objets Netcode.
- **`UlongExtensions`** : Utilitaires pour la manipulation des ClientIDs et conversion.

### 🔊 Audio (FMOD)
- **`FmodEventReferenceExtensions`** : Raccourcis pour jouer des sons directement depuis une référence d'événement FMOD via le `GameAudioManager`.

## Outils de l'Inspecteur (Assets/Scripts/...)

### 🧬 Polymorphisme (`PolymorphicPropertyDrawer`)
- **`[PolymorphicAttribute]`** : Placé sur une liste ou un champ d'une classe de base (ex: `Power`), il permet de choisir la classe dérivée concrète directement depuis un menu déroulant dans l'Inspecteur Unity. 
- **Usage** : `[SerializedReference, Polymorphic] public List<BaseClass> items;`

### 🏷️ Attributs Personnalisés (`CustomAttributes`)
- **`[AddressableEnums]`** : Permet de mapper des Enums complexes ou des ressources adressables (si utilisé) de manière plus ergonomique dans l'inspecteur.

## Points d'attention
- **DOTween Kill** : Les extensions UI appellent systématiquement `DOKill(true)` sur l'objet avant de lancer un nouveau Tween pour éviter les conflits d'animation.
- **UniTask** : Beaucoup d'extensions sont conçues pour être "Awaitable" (via UniTask) pour s'intégrer dans les séquences asynchrones des GameStates.
- **Naming Convention** : Les méthodes d'extension commencent généralement par `Do...` pour les animations (convention DG.Tweening) ou `Get...` pour les résolutions de données.
