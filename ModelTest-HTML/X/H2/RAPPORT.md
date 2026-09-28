# RAPPORT — H2 : Écran de révélation de rôle « Corruption du Portail »

## Livrable
- `index.html` autonome (CSS dans `<style>`, JS optionnel de données factices), aucune ressource externe.
- Écran : emblème/illustration composée, carte de rôle (nom, faction, difficulté, description), liste de pouvoirs typés (Actif/Passif) et bouton de confirmation.
- Rôle factice : **Le Mage Occulte** (faction Anomalie), données tirées du GDD (`_bmad-output/planning-artifacts/gdds/.../gdd.md`, sections « Roles and Factions » et « Power glossary »). Pouvoirs réels du rôle : Étreintes des ombres, Corruption ciblée (actifs), Auto Corruption, Corruption Insight, Abattre le portail (passifs).

## Version Unity
- `ProjectSettings/ProjectVersion.txt` = **6000.5.0f1** — c'est la version utilisée comme référence pour les API.
- NB : `CLAUDE.md` mentionne 6000.2.6f2. Divergence signalée ; je me suis conformé à `ProjectVersion.txt` (règle 1). Les fonctionnalités USS employées existent dans les deux versions.

## Contraintes USS respectées (issues des commentaires du projet dans `Assets/UI/Styles/variables.uss` et `RoleCard.uss`)
« px/% seulement (pas de em/rem), flexbox seulement, pas de gradient, pas de box-shadow, propriétés -unity-*, var() ne s'imbrique pas ». Le fichier a été écrit pour être directement transposable.

## API UITK visées à la transposition (structure UXML)
| Élément HTML | Équivalent UXML | Certitude |
|---|---|---|
| `<div>` conteneur | `VisualElement` | certain |
| `<div>` texte (nom, faction, desc, titre pouvoir…) | `Label` | certain |
| `.H2_powers` (overflow-y:auto) | `ScrollView` | certain |
| `<button>` | `Button` | certain |
| `<span>` étoile difficulté | `Label` | certain |

## Propriétés USS utilisées
Toutes vérifiées comme existantes en USS (Unity 6.x).

| Propriété CSS | Transposition USS | Certitude |
|---|---|---|
| `display: flex` / `flex-direction` / `align-items` / `justify-content` | identiques | certain |
| `flex-grow` / `flex-shrink` | identiques | certain |
| `width` / `height` / `max-width` / `max-height` (px, %) | identiques | certain |
| `margin*` / `padding*` (px) | identiques | certain |
| `border-width` / `border-color` / `border-left-width` / `border-left-color` | identiques | certain |
| `border-radius` (px) | identique | certain |
| `background-color` (rgb/rgba) | identique | certain |
| `color` (rgb/rgba) | identique | certain |
| `font-size` (px) | identique | certain |
| `font-weight: bold` | `-unity-font-style: bold` | certain |
| `text-align: center` | `-unity-text-align: middle-center` | certain |
| `letter-spacing` (px) | identique (USS accepte `letter-spacing`) | certain |
| `white-space: normal` / `nowrap` | identique | certain |
| `position: absolute` / `relative` ; `top/right/bottom/left` | identiques | certain |
| `opacity` | identique | certain |
| `overflow: hidden` / `overflow-y: auto` | `overflow` (le scroll passe par `ScrollView`) | certain |
| `transition-property` / `transition-duration` | identiques | certain |
| `cursor: pointer` | `cursor` existe en USS | incertain (valeur/format à confirmer ; sinon retirer, non critique) |
| `:hover` | pseudo-classe supportée en USS | certain |
| `box-sizing: border-box` | non requis (USS est déjà border-box) ; à supprimer à la transposition | certain |

### Fonctions / données JS (hors transposition — remplies par un contrôleur C# en Unity)
- `render()` / `textContent` / `createElement` : uniquement pour injecter les données factices et fermer l'écran. En UITK, un `MonoBehaviour`/contrôleur (cf. `RoleCardController` existant) peuplerait les `Label` et clonerait les lignes de pouvoir depuis un `Role`.

## Éléments NON transposables tels quels (à adapter en UXML/USS)
- **Reset `* { margin/padding: 0 }` et `box-sizing`** : sans objet en USS (pas de feuille par défaut agressive) ; à ignorer.
- **`html, body`, centrage via `body { display:flex }`** : en Unity, le plein écran vient d'un `VisualElement` racine `position:absolute` étiré (déjà `.H2_reveal`) ; le fond de scène réel serait le backdrop de jeu.
- **`font-family` (stack système)** : en USS -> `-unity-font-definition: url(...ttf)` pointant sur un asset Font (aucune police externe embarquée ici, conforme à la consigne). À brancher via le PanelSettings du projet.
- **`cursor: pointer`** : existe en USS mais le format de valeur diffère ; marqué incertain, non essentiel.
- **`::-webkit-scrollbar`** : NON utilisé (le style de scrollbar se fait via `.unity-scroller*` en USS, comme dans `RoleCard.uss`).
- **`@media` / requêtes responsive** : NON utilisées (USS ne les supporte pas). Voir « Responsive » ci-dessous.

## Absents volontairement (non supportés USS, exigence de la tâche)
- Aucun `linear-gradient` / `radial-gradient`.
- Aucun `box-shadow` ni `text-shadow`.
- Aucun `transform` composé, `filter`, `backdrop-filter`, `grid`, `em`/`rem`.
- L'illustration est un **emblème composé** (cercles concentriques + glyphe Unicode ◆/★), aucune image raster/SVG externe.

## Responsive (1920×1080 et 1280×720)
- Layout à taille fixe (px) centré, pensé pour tenir dans 720 px de haut ; `max-height` + `ScrollView` sur les pouvoirs évitent tout débordement.
- Pas de `@media` (non transposable). En Unity, l'adaptation aux deux résolutions passerait par le `PanelSettings` (scale mode : Constant Physical/Pixel Size ou Scale With Screen Size). Ici le rendu est identique aux deux formats, avec plus de marge de scrim à 1920×1080.

## Hypothèses
- Palette et grammaire visuelle (or/sombre, passif inset vs actif relevé, badges de type) reprises de `Assets/UI/Screens/RoleCard` et `variables.uss` — valeurs `--cdp-*` marquées PLACEHOLDER/design-owned dans le projet, donc approximées en littéraux ici.
- Contenu (description, libellés « EXPERT », compteurs d'utilisations) : réaliste et cohérent GDD, mais rédactionnel non figé côté design.
- Glyphes Unicode (◆, ★) supposés disponibles dans la police système du navigateur pour la capture ; en Unity ils dépendront de la police du PanelSettings.

## Limites connues
- Non testé dans l'éditeur Unity ni compilé (pas d'éditeur/compilation disponibles) — la transposition UXML/USS reste théorique mais alignée sur les fichiers UITK existants du projet.
- Le `cursor` et le format exact des glyphes/police devront être validés à la transposition.
- Aucune animation d'entrée mise en place (le projet en a via `transition` staggered sur RoleCard) ; seul un `transition` de survol du bouton est présent.
