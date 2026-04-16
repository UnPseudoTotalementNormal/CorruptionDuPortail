# Tooltip System

## Rôle
Système UI avancé fournissant des boîtes de dialogue informatives (Tooltips) au survol de la souris. Il intègre un puissant parseur de texte balisé permettant l'injection dynamique de variables de jeu et la création de liens hypertextes imbriqués (Links).

## Déclencheur (Point d'entrée)
- **Survol UI/3D :** Déclenché automatiquement par tout GameObject possédant un composant implémentant l'interface `ITooltipTrigger`.
- L'instanciation de la fenêtre est déléguée à `TooltipManager.CreateNewTooltipFromGameObject`.

## Composants Clés
- `TooltipManager` : Calcule les ancrages écran (Screen Space vs World Space), instancie les fenêtres UI et gère les timers de fermeture au "Unfocus".
- `TooltipLinkParser` : Le moteur de résolution de texte. Il traduit les balises `<link=X>` pour générer de nouvelles références de tooltips imbriquées.
- `TooltipWindow` : Le visuel encapsulé (Canvas/TMP_Text) qui s'anime avec DOTween.

## Données & État
- Construit dynamiquement des dictionnaires `TooltipReference` (Title, Description).
- Supporte le formattage spécial de clés: 
  - `power_XXX` génère une description extraite du `PowerManager`.
  - `powercomponent_XXX` extrait spécifiquement un `PowerComponent`.
- Résolution par réflexion asynchrone des variables avec le format `{var:PropertyName}`.

## Couplage & Dépendances
- Ultra-couplé à *TextMeshPro* (gestion des Event de Links).
- Dépend du `CharacterManager` et du système de `Power` asymétrique pour déduire les données de ses liens hyper-textes (via un format combinant `[OwnerID]_[PowerNetworkID]`).

## Points d'attention
- **Coût de calcul (Reflection) :** Le module `TooltipLinkParser` utilise de la réflexion C# (`GetField`, `GetProperty`) pour mapper les `{var:xxx}`. S'il est sur-sollicité sur de longs textes avec beaucoup de liens, cela pourrait occasionner des spikes dans le jeu.
- Le positionnement (`GetScreenBoundingBoxAndCenter`) gère mathématiquement la conversion 3D World Bounds vers le Canvas Space. Toute modification arbitraire des RenderModes de Canvas risque de briser l'apparition des Tooltips (ex: apparition hors-champ).
