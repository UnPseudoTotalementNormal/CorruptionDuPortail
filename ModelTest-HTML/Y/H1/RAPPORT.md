# RAPPORT — H1 : Écran de Vote (HTML/CSS)

## Contexte

- **Cible Unity** : `ProjectSettings/ProjectVersion.txt` indique **6000.5.0f1** (Unity 6, UI Toolkit).
  Remarque : `CLAUDE.md` et `_bmad-output/project-overview.md` mentionnent 6000.2.6f2 ; conformément
  aux règles, c'est le fichier `ProjectVersion.txt` qui fait foi. Les deux appartiennent à la même
  génération Unity 6 : le sous-ensemble USS visé ci-dessous est valable dans les deux cas.
- **Livrable** : `index.html` autonome, CSS inline dans `<style>`, aucune ressource externe
  (police système uniquement, aucune image, aucun CDN, aucun JavaScript).
- **Écran** : phase de Vote du jeu (boucle `Vote` de la doc). Liste des joueurs votables avec
  décompte de votes, compte à rebours, indication du vote du joueur local, bouton de vote par ligne,
  bouton « Passer mon vote ».
- **Préfixe de classes** : `H1_`.

## Sources d'inspiration (lecture seule)

- `Assets/UI/Styles/variables.uss` — palette « Message journal » réutilisée verbatim :
  fond near-black warm `rgb(10,7,8)`, texte off-white `rgb(240,231,221)`, vert lobby `rgb(95,217,135)`,
  or `rgb(238,201,122)`, rouge corrompus `rgb(219,74,86)`, orange Robot `rgb(226,150,58)`.
- `Assets/UI/Screens/LobbyRoles/LobbyRoles.{uss,uxml}` — conventions structurelles (flex-shrink:0 sur
  bandeaux fixes, zone centrale flex-grow, boutons border-radius, états `--active`/`--disabled`).
- `_bmad-output/project-overview.md` — genre (déduction sociale type Loup-Garou), boucle de jeu.

## API UITK / propriétés USS utilisées

Chaque entrée est marquée « certain » (équivalent USS documenté et courant, déjà présent dans le
projet) ou « incertain ».

### Layout (flexbox — seul modèle de positionnement d'UITK)
- `display: flex` → en USS tout VisualElement est déjà flex ; ne pas porter la propriété. **certain**
- `flex-direction: row | column` — **certain**
- `align-items`, `justify-content` (`center`, `flex-start`, `flex-end`, `space-between`) — **certain**
- `flex-grow`, `flex-shrink` — **certain**
- `width`, `height`, `min-width`, `max-width` en `px` et `%` — **certain**
- `padding`, `margin` (et variantes directionnelles) en `px` — **certain**
- `overflow: hidden` — **certain**

### Décoration
- `background-color` (rgb/rgba) — **certain**
- `color` — **certain**
- `border-width` / `border-color` (+ variantes `border-bottom-*`, `border-top-*`) — **certain**
- `border-radius` en `px` (cercles d'avatars = rayon = moitié du côté) — **certain**
- `letter-spacing` — **certain**

### Texte
- `font-size` en `px` — **certain**
- `font-weight: bold` → à porter en `-unity-font-style: bold` en USS — **certain** (transposition)
- `text-align: center` → à porter en `-unity-text-align: middle-center` en USS — **certain** (transposition)

### Interaction (pseudo-classes)
- `:hover` — **certain** (supporté en USS)

## Points de transposition (à adapter au passage en UXML/USS)

Ces propriétés **existent** en CSS standard mais portent un nom différent en USS. Le rendu HTML les
utilise ; il faudra les renommer lors du portage :

| CSS (dans index.html) | USS équivalent | Note |
|---|---|---|
| `font-weight: bold` | `-unity-font-style: bold` | certain |
| `text-align: center` | `-unity-text-align: middle-center` | certain |
| `font-family` | asset de police défini dans le **PanelSettings** (pas en USS) | certain |
| `box-sizing: border-box` | comportement natif en USS (à supprimer) | certain |
| `cursor: pointer` | `cursor` existe en USS mais prend une valeur/asset différente | incertain — à retirer ou valider |
| `border-*-style: solid` | non requis en USS (le style de bordure n'est pas exprimé ainsi) | à supprimer au portage |
| attribut `style="width: NN%"` inline | en USS/UXML, la largeur des barres de vote se pilote par code (data-binding) ou classe | certain |

## Propriétés CSS **non transposables** en USS (volontairement évitées)

Aucune des propriétés suivantes n'a été utilisée, car elles n'ont pas d'équivalent USS :
- `box-shadow`, `text-shadow` — absents d'UITK.
- `linear-gradient` / `radial-gradient` / `conic-gradient` — pas de gradient en USS (le compte à
  rebours utilise donc une **barre de progression** plutôt qu'un anneau conique).
- `gap` (flex/grid) — absent d'UITK ; les espacements passent par `margin`.
- `display: grid`, `aspect-ratio`, `float` — absents/non pertinents en UITK.
- Unités `em`, `rem`, `vw`, `vh` — non supportées ; seuls `px` et `%` sont utilisés.
- `transform` avancé, `filter`, `backdrop-filter` — non utilisés.

## Choix de conception

- **Compte à rebours** : grand chiffre or + barre de temps horizontale (pas d'anneau, car pas de
  gradient conique en USS). Classe `H1_countdown-value--urgent` (rouge) prévue pour les dernières
  secondes mais non appliquée dans la maquette statique.
- **Vote du joueur local** : triple signalement — pastille « Votre vote : Baptiste » en en-tête,
  liseré vert sur la ligne concernée (`H1_row--myvote`), bouton passé en état vert `Votre vote ✓`
  (`H1_vote-btn--active`).
- **Joueur local non votable** : ligne `H1_row--self` avec badge « VOUS » et bouton `Indisponible`
  désactivé (`H1_vote-btn--disabled`), reflétant la règle « on ne peut pas se voter ».
- **Décompte** : barre proportionnelle (rouge pour la cible majoritaire, or atténué pour les votes
  faibles/nuls) + nombre en or + mini-avatars des voteurs (le voteur local est teinté en vert).
- **Progression globale** : `6 / 8 joueurs ont voté` en pied, à côté du bouton « Passer mon vote ».

## Données factices

8 joueurs (Baptiste, Amélie, Gaspard, Clément, Doriane + le joueur local Léa ; 3 voteurs cités par
initiales). Vote local porté sur Baptiste (3 votes). Aucune donnée réelle, aucun JavaScript : tout
est écrit en dur dans le HTML pour une capture déterministe.

## Lisibilité 1920×1080 et 1280×720

- Contenu borné par `max-width: 1600px` centré → à 1920 la liste ne s'étire pas exagérément.
- Tailles en `px` calibrées pour rester lisibles à 720p (titres 46px, secondes 66px, noms 27px,
  décomptes 34px). Largeurs des blocs identité/bouton fixes ; la zone de décompte est en `flex-grow`
  et absorbe la différence de largeur entre les deux formats.

## Limites connues

- **Non testé dans Unity** : aucune compilation ni rendu UITK effectués (pas d'éditeur disponible).
  Le fichier n'a été validé que comme HTML/CSS.
- Le débordement vertical de la liste est en `overflow: hidden` (maquette) ; en UITK réel il faudra un
  `ScrollView` si le nombre de joueurs dépasse la hauteur.
- La police système diffère de la police finale du jeu (définie dans le PanelSettings) : le rendu
  exact des largeurs de texte pourra varier au portage.
- `cursor: pointer` est présent pour le confort d'aperçu HTML mais devra être revu au portage USS
  (voir tableau ci-dessus).
