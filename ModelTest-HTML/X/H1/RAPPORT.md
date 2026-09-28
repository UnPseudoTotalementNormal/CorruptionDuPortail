# RAPPORT - H1 : écran de vote (HTML/CSS transposable UXML/USS)

Livrable : `index.html` (un seul fichier autonome, CSS dans `<style>`, aucun JavaScript, aucune ressource externe).
Version Unity lue dans `ProjectSettings/ProjectVersion.txt` : **6000.5.0f1**. Attention : `CLAUDE.md` et `_bmad-output/` indiquent 6000.2.6f2, mais c'est le fichier de version qui fait foi.

**Aucun test visuel n'a été fait.** Aucun navigateur headless n'est installé dans l'environnement : les tailles et le fait que tout tienne à l'écran viennent d'un calcul à la main, pas d'une capture. Rien n'a été testé dans Unity non plus (pas d'éditeur).

---

## 1. Contenu de l'écran

| Zone | Contenu | Correspondance avec le jeu (lecture seule) |
|---|---|---|
| En-tête gauche | "TOUR 3 · PHASE DE VOTE", titre "Qui sera enchaîné ?", consigne | `VoteState` : le joueur le plus voté est ajouté à la liste d'enchaînement (`chainingManager.AddCharacterToChainingList`). Il est enchaîné, pas tué. |
| En-tête droite | Participation : 8 / 11, une pastille par votant, liste des joueurs "En attente" | `CanVote` : les éliminés et les joueurs partis ne votent pas. Un joueur enchaîné mais présent vote. |
| Grille (12 cartes, 3 colonnes) | Avatar (initiales), nom, statut "A voté" / "N'a pas voté" / "Éliminé", nombre de votes reçus, une case par votant éligible | Libellés repris de `VoteCanvas.cs` : "A voté", "N'a pas voté", "Éliminé", "Enchaîné". |
| États de carte | `EN TÊTE` (rouge), `VOTRE VOTE` (or), `VOUS`, `ENCHAÎNÉE` (non désignable), `ÉLIMINÉ` (atténué) | Dans `VoteCanvas`, une carte enchaînée ou éliminée n'a pas de bouton de vote. |
| Colonne droite : compte à rebours | 02:47 "sur 05:00", barre de progression, "Clôture 5 s après le dernier bulletin" | `VoteState.asset` : `voteDuration: 300`. Quand tous les votes sont exprimés : `voteTimer = Mathf.Min(voteTimer, 5)`. |
| Colonne droite : "Votre vote" | Cible du vote local (Balthazar, 2 votes, 2e position), "Vote scellé, non modifiable." | `CanVote` refuse un second vote : le vote est définitif. |
| Colonne droite : "Derniers bulletins" | Qui a voté et quand, sans révéler pour qui | Cohérent avec `VoteCanvas`, qui n'affiche que "A voté". |
| Colonne droite : "Passer" | Nombre de votes "Passer", bouton **PASSER MON VOTE**, règle d'égalité | `SKIP_VOTE_ID` + `VoteTally.Resolve` : égalité, ou "Passer" en tête, donne `skipVoteId`, donc personne n'est enchaîné. |

Données factices, cohérentes entre elles : 12 joueurs, dont 1 éliminé (Rémi) et 1 enchaînée (Ophélie, qui a voté). Cela fait 11 votants éligibles et 8 bulletins : Ilyès 3, Balthazar 2, Garance 1, Noémie 1, Passer 1. Le joueur local (Hugo) a voté pour Balthazar.

**Choix d'état affiché** : l'écran montre le joueur local **après** son vote. Ainsi le bandeau "Votre vote" et la mise en avant de la carte sont visibles. Comme le jeu interdit de voter deux fois, le bouton "Passer mon vote" apparaît **désactivé** (`disabled`, soit `:disabled` / `SetEnabled(false)` en UITK), avec un contraste suffisant pour rester lisible. Le style actif (`:enabled`, `:enabled:hover`, `:enabled:active`) et le survol des cartes votables (`.H1_card--votable:hover`) sont écrits dans la feuille, mais on ne les voit pas sur la capture.

## 2. Univers visuel repris (lecture seule)

- La palette vient des littéraux de `LobbyRoles.uss` et des tokens `--cdp-color-journal-*` de `Assets/UI/Styles/variables.uss` : fond presque noir chaud `rgb(10,7,8)`, filets `rgb(58,44,41)`, texte blanc cassé `rgb(240,231,221)`, or `rgb(238,201,122)`, rouge `rgb(219,74,86)`, vert `rgb(95,217,135)`. Les bordures or reprennent `--cdp-color-role-border` `rgb(191,154,82)`.
- La structure suit celle des écrans existants (`.role-card`, `.journal`) : une racine `position: absolute` plein écran, puis uniquement du flexbox en px et %.
- Pas de dégradé ni d'ombre, comme le demandent les commentaires de `variables.uss`. La profondeur vient de teintes de fond et de bordures.

## 3. API UITK et propriétés USS utilisées (pour la transposition)

Il n'y a aucun code C#. Correspondance des éléments : `div` devient `VisualElement`, `span` devient `Label`, les cartes deviennent des `Button` (ou un `VisualElement` avec un `Clickable`), `button` devient `Button`, et la liste "Derniers bulletins" devient une `ScrollView`.

| Propriété / fonctionnalité USS | Statut |
|---|---|
| `position: absolute / relative`, `left`, `top`, `right`, `bottom` | certain |
| `width`, `height`, `min-width` (px et %) | certain |
| `flex-direction`, `flex-grow`, `flex-shrink`, `flex-wrap: wrap` | certain |
| `align-items`, `justify-content` (`center`, `space-between`, `flex-start`, `flex-end`) | certain |
| `align-content: stretch` (lignes d'un conteneur `wrap` étirées) | propriété certaine ; **incertain** que Yoga répartisse la hauteur des lignes exactement comme le navigateur |
| `margin` / `padding` (raccourcis 1, 2 et 4 valeurs, marges négatives) | certain |
| `background-color`, `color`, `opacity` (rgb / rgba) | certain |
| `border-width`, `border-left-width`, `border-bottom-width`, `border-color`, `border-radius` (px) | certain |
| `font-size` (px), `letter-spacing` (px) | certain |
| `white-space: normal / nowrap` | certain |
| `overflow: hidden` + `text-overflow: ellipsis` sur un Label | certain |
| `translate: -50% -50%` (anneaux décoratifs) | propriété certaine ; **incertain** que les pourcentages se comportent comme en CSS (en cas de doute, remplacer par des px : -750px, -500px, -280px) |
| Variables `:root { --h1-... }` + `var()` (jamais imbriqué dans une fonction) | certain |
| Pseudo-classes `:hover`, `:active`, `:disabled`, `:enabled`, et leur chaînage (`:enabled:hover`) | certain |
| Sélecteurs de classe composés (`.a.b`) et descendants (`.a .b`) | certain |
| Style inline UXML `style="width: 55.7%;"` (remplissage de la barre du chrono) | certain (à piloter plutôt en C# via `style.width = Length.Percent(...)`) |
| `-unity-font-style: bold` (remplace `font-weight: bold`) | certain |
| `-unity-font-definition: url("project://database/Assets/TextMesh%20Pro/Fonts/LiberationSans.ttf")` (remplace `font-family`) | certain (même chemin que `RoleCard.uss` / `MessageJournal.uss`) |
| `-unity-text-align: middle-center` (à ajouter sur les pastilles `.H1_tag` et sur les Labels du compteur, alignés par flexbox dans la maquette) | certain |

## 4. CSS utilisé qui ne se transpose PAS tel quel

Toutes ces règles sont regroupées dans le bloc "1. PREVIEW" de la feuille. Elles émulent le comportement par défaut de UITK dans le navigateur et **ne doivent pas être copiées dans l'USS** :

| CSS | Raison / équivalent Unity |
|---|---|
| Sélecteurs de type HTML `*`, `html`, `body`, `div`, `span`, `button` | En USS, les sélecteurs de type visent des types C# (`VisualElement`, `Label`, `Button`). Inutiles ici, puisque ces défauts sont natifs. |
| `display: flex` sur tous les blocs | Inutile : tout `VisualElement` est un conteneur flex (colonne par défaut). |
| `box-sizing: border-box` | Yoga compte déjà `width` / `height` bordure et padding compris. |
| `border-style: solid` | Pas de `border-style` en USS : les bordures sont toujours pleines. |
| `min-width: 0` / `min-height: 0` | Émule l'absence de taille minimale "min-content" dans Yoga. |
| `font-family` | Utiliser `-unity-font-definition` ou la police par défaut des Text Settings du PanelSettings. |
| `font-weight: bold` | Utiliser `-unity-font-style: bold`. |
| `font: inherit`, `text-align: inherit` (reset du `<button>`) | Sans objet. Il faut en revanche neutraliser le style du thème par défaut du `Button` UITK (fond, bordure, marges). |
| `cursor: pointer / default` | `cursor` existe en USS mais attend une texture ou un curseur éditeur. Ignoré en runtime ici. |
| Attribut HTML `disabled` | Équivaut à `Button.SetEnabled(false)`, qui active la pseudo-classe `:disabled`. |
| Textes en majuscules | Écrits directement en majuscules dans le DOM : pas de `text-transform` en USS. |

Fonctionnalités volontairement évitées : `gap` (remplacé par le padding de `.H1_cell` et des marges négatives), `calc()`, `em` / `rem` / `vw` / `vh`, media queries, `line-height`, `box-shadow`, dégradés, `:first-child` / `:last-child` / `:nth-child`, `text-decoration`, polices web, images.

## 5. Hypothèses

- **Mise à l'échelle** : la maquette utilise des px constants et reste lisible en 1280×720 (plus petit texte : 12 px pour les pastilles, sinon 14 px minimum). Or `PS_ScreenOverlay.asset` est en `ScaleWithScreenSize` (m_ScaleMode 2), référence 1920×1080, calé sur la largeur. Avec ce PanelSettings, tout serait réduit à ×0,667 en 720p (14 px deviendrait environ 9 px) et la mise en page de 720p aurait l'air identique à celle de 1080p. Deux options pour garder le comportement de la maquette :
  - soit un PanelSettings en `ConstantPixelSize` pour cet écran ;
  - soit garder le PanelSettings actuel en multipliant toutes les tailles par 1,5 dans l'USS (base de conception à 1080p).
  Décision à prendre par le design.
- Le joueur enchaîné peut voter mais ne peut pas être désigné (`VoteCanvas` + commentaire `[LEAVE]` de `VoteState.CanVote`). L'éliminé ne vote pas et n'est pas désignable.
- Les votes par joueur sont affichés **pendant** le vote, comme le demande la consigne. Le `VoteCanvas` actuel n'affiche le nombre qu'au récapitulatif et montre seulement "A voté" pendant le vote. Les données le permettent : `OnRefreshPlayerVotesRpc` envoie les compteurs à tous les clients.
- Pour qui chacun a voté n'est **pas** montré, même si les identifiants des votants circulent. Choix prudent pour la déduction sociale.
- Les heures du journal "Derniers bulletins" sont la valeur du chrono au moment du vote. C'est un ajout de présentation : le jeu n'horodate pas les votes aujourd'hui.
- Les avatars sont des initiales sur fond coloré. En jeu, ce serait `background-image` avec le portrait (`-unity-background-scale-mode: scale-to-fit`).
- Les noms des joueurs, le numéro du tour et la couleur des avatars sont factices.

## 6. Limites connues

- **Densité** : la grille est fixée à 3 colonnes, avec des lignes qui s'étirent sur toute la hauteur.
  - Jusqu'à 12 joueurs, tout tient en 720p (4 lignes, cartes d'environ 120 px de haut).
  - Au-delà (6 lignes pour 16 joueurs), les cartes seraient trop écrasées en 720p. Il faudrait alors une `ScrollView` ou une variante 4 colonnes basculée par classe depuis le contrôleur (pas de media query en USS).
- En 1920×1080 avec 12 joueurs, les cartes sont hautes (environ 220 px) : le contenu y est centré verticalement et il reste de l'espace vide.
- Le journal "Derniers bulletins" est coupé par `overflow: hidden` dans la maquette : 3 à 4 lignes visibles en 720p, et la dernière peut être coupée à moitié. En Unity, ce serait une `ScrollView`.
- Les noms longs sont tronqués avec une ellipse (environ 10 caractères à 21 px en 720p).
- Les styles par défaut du thème Unity (`unity-theme://default`) sur `Label` et `Button` (marges et padding internes) ne sont pas neutralisés dans la maquette. **Incertain** : il faudra sans doute une règle `.H1_screen Label { margin: 0; padding: 0; }` et un reset des classes `.unity-button`.
- Les glyphes utilisés (« », ·, —, lettres accentuées majuscules) sont supposés présents dans LiberationSans. Non vérifié dans l'atlas TextCore.
- L'état "chrono < 30 s" (`.H1_timer--urgent`) est défini mais pas affiché.
