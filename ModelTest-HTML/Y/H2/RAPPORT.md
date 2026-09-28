# RAPPORT — H2 : écran de révélation du rôle (HTML/CSS transposable en UXML/USS)

## Livrable

- `index.html` : un seul fichier autonome. Le CSS est dans une balise `<style>`. Pas de JavaScript, pas de police, d'image ni de CDN externes. Toutes les classes CSS commencent par `H2_`.
- Contenu de l'écran :
  - **Carte du rôle** (5:7) : pastille de faction, étoiles de difficulté, emblème du Portail dessiné uniquement avec des formes CSS (aucune image), nom du rôle en or.
  - **Bloc objectif**.
  - **Panneau d'information** : faction et accroche, épithète, nom du rôle, pastilles méta (difficulté, archétype, ordre de réveil), description.
  - **Liste des 5 pouvoirs**, chacun avec un badge ACTIF ou PASSIF, ses conditions d'utilisation et sa description, plus une légende des deux types.
  - **Pied de page** : consigne de secret, minuterie « La nuit tombe dans 14 s » et bouton de confirmation **J'AI COMPRIS**.

## Version Unity et contexte

- `ProjectSettings/ProjectVersion.txt` indique **6000.5.0f1**. `CLAUDE.md` et `_bmad-output/` parlent encore de 6000.2.6f2. J'ai suivi ProjectVersion.txt, et je me suis limité à des propriétés USS déjà présentes dans les versions 6000.x antérieures. La maquette reste donc valable même si le projet utilise en réalité 6000.2.
- Mise à l'échelle : lue dans `Assets/UI/PanelSettings/PS_ScreenOverlay.asset` (`m_ScaleMode: 2` = Scale With Screen Size, référence 1920x1080, `m_Match: 0` = ajustement sur la largeur). Toute la maquette est donc écrite **en px à 1920x1080**. En 1280x720, le PanelSettings réduira l'ensemble à 2/3. Le HTML reproduit ce comportement avec les classes `H2_emu-*` : une scène de 1920x1080 à laquelle on applique `scale: 0.666667` via `@media`.
- Univers visuel repris de l'existant, en lecture seule :
  - `Assets/UI/Styles/variables.uss` : jetons `--cdp-color-role-*`, recopiés tels quels dans `:root`.
  - `Assets/UI/Screens/RoleCard/RoleCard.uss` : grammaire « actif en relief / passif en creux », panneau sombre chaud avec bordure or, rayon de 18px.
  - `Assets/Scripts/UI/Cards/RoleCardElement.cs` : carte 5:7, matte noire, illustration incrustée, nom en or dessous, cadre teinté par la faction et assombri.
  - `Assets/ScriptableObjects/FactionDatabase.asset` : couleurs de faction.
- Données factices tirées du projet :
  - `MageOcculte.asset` : nom « Va'ahl, Le Mage Occulte », faction Anomalie, difficulté 3, archétype `detective` (roleType 2).
  - Prefabs `EmbraceOfShadows`, `CorruptingMark`, `AutoCorruption`, `CorruptionInsight` (noms, type passif ou actif, `maxPowerUse`, composant « En chaîne »).
  - GDD : « Abattre le portail », 6e couche de réveil, condition de victoire `WAnomalyCorruption`.

## Vérification effectuée

- **Aucun test dans Unity** : pas d'éditeur, pas de compilation. Je n'affirme pas que le rendu UITK sera identique.
- Seul le **rendu HTML** a été vérifié, dans un Chromium headless installé temporairement hors du projet, en **1920x1080 et en 1280x720**.
  - Tout tient à l'écran dans les deux formats. Il reste environ 30px de marge (à l'échelle de référence) dans la liste des pouvoirs.
  - Les variantes `H2_screen--elu` et `H2_screen--marginal` ont aussi été affichées.
- En 1280x720, les plus petits textes font 19–20px à la référence, soit environ 13px affichés (badges et libellés en majuscules). Les textes courants font 22–24px à la référence, soit environ 15–16px affichés.

## Correspondance HTML vers UXML

| HTML | UXML | Certitude |
|---|---|---|
| `<div>` conteneur | `ui:VisualElement` | certain |
| `<div>` qui ne contient que du texte | `ui:Label` (attribut `text`) | certain |
| `<button class="H2_confirm">` | `ui:Button` avec un `ui:VisualElement` enfant (la flèche). Un Button peut recevoir des enfants car c'est un VisualElement. | certain |
| `.H2_powers` (liste des pouvoirs) | `ui:ScrollView` (mode vertical) | certain |
| Emblème `.H2_emblem*` | `VisualElement` en `position: absolute`. En jeu, on le remplace par `background-image` = portrait du rôle, avec `-unity-background-scale-mode: scale-and-crop`. | certain |
| Variables `:root` | `:root { --x: ... }` en USS. Les `--cdp-*` sont déjà fournies par `theme.tss` : ne recopier que les `--h2-*`. | certain |

Aucune API C# n'a été écrite ; la tâche ne demandait pas de code C#.

## Propriétés USS utilisées (partie B du `<style>`)

| Propriété / fonctionnalité | Certitude | Remarque |
|---|---|---|
| `position` (absolute / relative), `left`, `top`, `right`, `bottom` | certain | |
| `width`, `height`, `min-width`, `min-height` (px ; `%` pour la barre de minuterie) | certain | |
| `margin`, `padding` (raccourcis à 1, 2 ou 4 valeurs, et côtés séparés) | certain | |
| `flex-direction`, `flex-wrap`, `flex-grow`, `flex-shrink`, `align-items`, `justify-content` (dont `space-between`) | certain | |
| `background-color`, `color`, `opacity` | certain | |
| `border-width`, `border-*-width`, `border-color`, `border-*-color`, `border-radius`, `border-*-*-radius` | certain | Cercles : rayon en px égal à la demi-taille, jamais en %. |
| `overflow: hidden` | certain | |
| `font-size`, `letter-spacing`, `white-space` (normal / nowrap) | certain | `letter-spacing` est déjà utilisé dans RoleCard.uss. |
| `-unity-font-style` (bold / italic), `-unity-text-align` (middle-center / middle-right) | certain | Placées à côté de leurs équivalents CSS, que le navigateur ignore. |
| `rotate` (en deg) | certain | Losanges, fissures, flèche du bouton. |
| `scale`, `transform-origin` | certain | Déjà utilisés dans RoleCard.uss. |
| `transition-property`, `transition-duration`, `transition-timing-function` | certain | Survol et appui du bouton. |
| `var()` non imbriqué, propriétés personnalisées redéfinies sur une classe (`.H2_screen--elu`) et héritées par les enfants | certain | |
| Sélecteurs : classe, descendant, enfant `>`, `:hover`, `:active`, `:root` | certain | Aucun `:first-child` ni `:nth-child` (absents d'USS) : la dernière ligne porte la classe `H2_power--last`. |
| Glyphe `★` avec LiberationSans | incertain | RoleCard.uxml l'utilise déjà, mais je n'ai pas vérifié qu'il existe dans LiberationSans (sinon, fallback TextCore). Idem pour `·` et `×`. |

## CSS qui ne se transposerait pas en USS

Tout se trouve dans la **partie A** du `<style>`, qui sert uniquement à l'émulation :

| CSS | Pourquoi | Équivalent Unity |
|---|---|---|
| `@media` et `scale` sur `.H2_emu-stage` | USS n'a pas de media queries | Le PanelSettings (Scale With Screen Size 1920x1080) s'en charge. |
| `100vw`, `100vh`, `left: 50%` avec marges négatives | Pas d'unités de viewport en USS | La racine `.H2_screen` en `position: absolute` sur les 4 bords. |
| `html`, `body`, `font-family` | Pas de sélecteurs de type HTML, pas de `font-family` en USS | `-unity-font-definition: url("project://database/Assets/TextMesh%20Pro/Fonts/LiberationSans.ttf")` sur `.H2_screen`, comme dans RoleCard.uss. Je ne l'ai pas mise dans le HTML pour éviter toute URL. |
| `display: flex` et `flex-direction: column` sur `*` | Ce sont les valeurs par défaut en UITK | À supprimer. |
| `box-sizing: border-box`, `min-width` / `min-height: 0`, `position: relative` sur `*` | Imitent le comportement par défaut de Yoga : dimension bordure comprise, minimum 0, ordre de peinture = ordre du DOM | À supprimer. |
| `border-style: solid`, `border-color: transparent` | Pas de `border-style` en USS (bordures toujours pleines) | À supprimer. |
| `cursor: pointer`, `outline: none` sur le bouton | `cursor` existe en USS mais attend une texture | À supprimer, ou remplacer par un curseur du projet. |
| `justify-content: center` sur un **Label** de hauteur fixe (badges, nom de carte, pastille « ! », compteur) | En HTML, cela centre le texte verticalement. Sur un Label UITK, c'est `-unity-text-align` qui aligne le texte. | Garder uniquement `-unity-text-align: middle-center`. |
| `font-weight`, `font-style`, `text-align` | Pas en USS | Garder les `-unity-*` placés à côté. |
| Pas utilisés (absents d'USS) : dégradés, `box-shadow`, `line-height`, `text-transform`, `em` / `rem` / `vw`, `gap`, `aspect-ratio`, `@keyframes`, bordures pointillées | — | Les majuscules sont écrites en dur dans le texte. Les espacements passent par des marges. La carte 5:7 a une taille fixe (480x672). |

## Hypothèses

1. **Rôle affiché** : le Mage Occulte, pour tester le cas le plus chargé (5 pouvoirs, difficulté 3). Les descriptions des prefabs ont été reformulées à la 2e personne et raccourcies à une ligne. « Corruption Insight » garde son nom anglais, tel qu'il figure dans le prefab.
2. **Description du rôle** : la classe `Role` n'a pas de champ description. Le texte d'ambiance est inventé et il faudrait ajouter un champ de données pour l'alimenter.
3. **Objectif** : déduit de `WAnomalyCorruption` et de `WChosenChainedAllAnomaly` (GDD).
4. **« Abattre le portail »** apparaît comme passif. RoleCard.uxml indique que certains pouvoirs sont masqués sur la carte consultable (`Power.hideFromRoleCard`). Je ne sais pas si ce pouvoir-là l'est.
5. **Minuterie** : je suppose que l'état d'introduction avance automatiquement (`GameIntroductionUI` a des durées). La valeur de 14 s est factice, et la largeur de `.H2_timer-fill` serait pilotée en C#.
6. **Paramètres visuels** :
   - Rouge du texte de faction éclairci à rgb(255,104,104) pour rester lisible sur fond sombre.
   - Cadre de carte = couleur de faction assombrie, comme `RoleCardElement.SetAccent` (Lerp vers le noir, 0.45).
   - Fond plat rgb(14,10,9), qui remplace le plateau flouté du jeu.
7. **Variantes de faction** : `.H2_screen--elu` et `.H2_screen--marginal` redéfinissent les variables `--h2-color-faction*`. Les valeurs des variantes (texte, cadre, remplissage du losange) sont calculées à la main à partir de FactionDatabase.

## Limites connues

- **Métriques de texte** : TextCore et Chromium ne mesurent pas le texte exactement de la même façon. La marge d'environ 30px dans la liste des pouvoirs devrait absorber l'écart. Au-delà, la ScrollView prend le relais, mais sa barre de défilement n'est pas stylée ; on peut reprendre celle de RoleCard.uss.
- **Textes longs** : le nom sur la carte est en `nowrap` à 30px. Un nom plus long que « Va'ahl, Le Mage Occulte » déborderait. Il faudrait un modificateur `--long`, comme `role-card__name--long`. Même chose pour les noms de pouvoirs (`nowrap`).
- **Emblème** : il sert de repli ; le vrai visuel est le portrait du rôle. Ses anneaux et fissures sont dessinés avec des éléments `rotate`. L'anticrénelage des bords tournés par UITK n'a pas été vérifié.
- **Animations** : aucune animation d'entrée (pas de `@keyframes` en USS). Elle passerait par des classes d'état et des `transition` (par exemple une paire `is-hidden` comme dans RoleCard.uss), ou par DOTween côté C#.
- **Accessibilité** : navigation clavier et manette non traitée (focus du bouton, `:focus`).
