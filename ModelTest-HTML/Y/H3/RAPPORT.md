# RAPPORT — H3 : Écran de lobby (HTML/CSS)

## Contexte

- Projet : **Corruption du Portail** (déduction sociale type Loup-Garou).
- Version Unity cible pour la transposition UITK : **6000.5.0f1** (lue dans `ProjectSettings/ProjectVersion.txt`).
  - Note : `CLAUDE.md` et `_bmad-output/project-overview.md` mentionnent `6000.2.6f2`.
    Il y a une divergence entre la doc et le fichier de version réel. J'ai suivi
    `ProjectVersion.txt` (règle : « uniquement les API disponibles dans cette version »).
    Aucune fonctionnalité utilisée ici n'est spécifique à l'une ou l'autre de ces sous-versions
    de Unity 6 : la maquette reste transposable dans les deux cas.
- Livrable : un seul `index.html` autonome, CSS en `<style>`, JS facultatif (données factices
  uniquement), aucune ressource externe (pas de police, image, ni CDN).
- Préfixe de classes : `H3_`.
- Palette et intentions visuelles échantillonnées (en lecture seule) depuis
  `Assets/UI/Styles/variables.uss`, `Assets/UI/Screens/LobbyRoles/LobbyRoles.uss` et
  `Assets/UI/Screens/RoleCard/RoleCard.uss`. Tous les rôles/factions affichés viennent des enums
  réels du projet (`Assets/Scripts/Characters/RoleID.cs`, `Assets/Scripts/Domain/FactionType.cs`).

## Contenu de l'écran

- **En-tête** : titre du jeu, code de salon, compteur de joueurs.
- **Colonne gauche** : liste des joueurs connectés (avatar à initiales, nom, sous-ligne, badges
  HÔTE / BOT, badge d'état PRÊT / PAS PRÊT).
- **Colonne centrale** : composition des rôles regroupée par faction (Élus / Anomalies /
  Marginaux), chaque rôle sous forme de carte avec son nombre d'exemplaires (`x N`) ; les rôles
  exclus (count 0) sont grisés.
- **Colonne droite** : paramètres principaux de la partie (nombre de joueurs, durée des débats,
  mode de vote, anonymat, révélation à la mort, première nuit, bots simulés, transport Steam).
- **Pied de page** : statut de validité + actions. Vue **HÔTE** montrée : bouton « Lancer la
  partie » (désactivé tant que tous ne sont pas prêts) + « Quitter ». La variante joueur
  (« Se déclarer prêt ») est décrite ci-dessous, non rendue simultanément.

## Lisibilité 1920×1080 et 1280×720

- Layout 100 % flexbox, colonnes en `flex-basis`/`flex-grow`, hauteurs en `%`/`vh` : les deux
  formats 16:9 se remplissent sans rognage.
- Tailles de police fixes en px, choisies pour rester lisibles au plus petit format (1280×720) ;
  au plus grand format elles paraissent proportionnellement plus petites mais confortables.
- Les zones à contenu potentiellement long (liste joueurs, grille rôles, paramètres) ont
  `overflow: auto` — en UITK ce seraient des `ScrollView`.

## Correspondance API UITK / propriétés USS

Légende : **certain** = existe en USS pour Unity 6 ; **incertain** = à vérifier en éditeur.

### Propriétés USS utilisées (transposables)

| Propriété | Statut | Remarque |
|---|---|---|
| `display: flex` / `flex` implicite | certain | VisualElement est flex par défaut en USS |
| `flex-direction: row/column` | certain | |
| `flex-grow`, `flex-shrink`, `flex-basis` | certain | |
| `flex-wrap: wrap` | certain | utilisé pour la grille de rôles |
| `align-items`, `justify-content` | certain | |
| `width`, `height`, `min-width`, `min-height` (px et %) | certain | `min-height:0` supporté |
| `padding`, `margin` (et variantes directionnelles) | certain | espacement inter-blocs (USS n'a pas `gap`) |
| `background-color` (rgb/rgba) | certain | |
| `border-width` / `border-color` (+ variantes `-top/-bottom-...`) | certain | |
| `border-radius` (et coins individuels) | certain | |
| `color` | certain | |
| `font-size` (px) | certain | |
| `-unity-font-style: bold` | certain | équivalent USS de `font-weight: bold` |
| `-unity-text-align: middle-center / middle-left` | certain | équivalent USS de `text-align` |
| `white-space: normal` | certain | retour à la ligne des noms de rôle |
| `opacity` | certain | utilisé pour griser les rôles exclus |
| `letter-spacing` | certain | |
| pseudo-classe `:disabled` sur Button | certain | déjà utilisé dans `LobbyRoles.uss` |
| `overflow` | certain (en USS : `hidden`/`scroll`) | voir écarts ci-dessous |

### Écarts / propriétés HTML NON transposables telles quelles

| Élément HTML/CSS | Problème en USS | Transposition prévue |
|---|---|---|
| `font-family` / `font` | USS ne prend pas de familles génériques ; la police se règle via **PanelSettings** (Text Settings) ou `-unity-font-definition: url(...)`. | Retirer ; définir la police dans PanelSettings (LiberationSans, comme le reste du projet). |
| `font-weight: bold` | non reconnu par USS. | Remplacer par `-unity-font-style: bold` (déjà doublé dans le HTML). |
| `text-align` | non reconnu par USS. | Remplacer par `-unity-text-align: middle-center` (déjà doublé). |
| `box-sizing`, reset `* {}` | inutile/inexistant en USS (modèle boîte différent, tout est border-box). | Supprimer le bloc reset. |
| `height: 100vh` | `vh` n'existe pas en USS. | Utiliser `height: 100%` (le root remplit son panneau). |
| `overflow: auto` | USS accepte `hidden`/`scroll`, pas `auto`. | Remplacer les conteneurs scrollables par des **`ScrollView`** UXML (comme `LobbyRoles.uss` le fait déjà). |
| `border-style: solid` | non nécessaire en USS (le trait apparaît dès `border-width`). | Supprimer ; garder `border-width` + `border-color`. |
| `<button>` / `<span>` / `<div>` | balises HTML. | `Button`, `Label`, `VisualElement` en UXML. |
| Le JS d'injection | pas de JS en UITK. | Remplacé par un contrôleur C# alimenté par une source de données (cf. `LobbyRolesUitkController` existant). |

### Fonctionnalités volontairement évitées (car non supportées en USS)

- Pas de `display: grid` (la grille utilise `flex-wrap`).
- Pas de `gap` (espacement par marges).
- Pas de `linear-gradient` / dégradé (aplats + bordures, comme les tokens `--cdp-color-role-*`).
- Pas de `box-shadow` / `text-shadow`.
- Pas d'`aspect-ratio` (cartes de rôle en `width`/`height` fixes).
- Pas de `transform` complexe, pas de `calc()`, pas de `@media`.
- Aucune police, image ou icône externe : avatars/arts figurés par des initiales et des aplats.

## Hypothèses

- Factions du jeu = enum `FactionType { anomaly, chosen, marginal, unknown }`. Étiquettes FR
  choisies : `chosen` → **Élus**, `anomaly` → **Anomalies**, `marginal` → **Marginaux**
  (« Anomalies » et « Marginaux » sont confirmés par les libellés de `LobbyRoles`,
  cf. `grep`). « Élus » pour `chosen` est une **hypothèse** de traduction (non confirmée dans le
  code) ; à valider avec l'équipe.
- Codes couleur de faction (rouge/vert/orange) alignés sur les tokens `--cdp-color-journal-*`
  (corrompus rouge, robot orange, accent lobby vert). Les rôles restent **provisoires /
  design-owned** comme tout le reste du projet.
- Noms de rôles tirés de `RoleID` (Oracle, Gardien, Dryade, Robot, Mage Occulte, Abyss, etc.).
  La répartition par faction et les nombres d'exemplaires sont **factices/illustratifs**, pas
  issus d'une configuration réelle de partie.
- Le concept de « bots simulés » (`clientId >= 100`) vient de `CLAUDE.md` ; utilisé ici seulement
  comme donnée factice réaliste dans la liste et les paramètres.
- Vue rendue = celle de l'**hôte**. Pour la vue joueur, remplacer le bouton primaire par
  « Se déclarer prêt » / « Annuler prêt » (même classe `H3_btn-primary`) ; le reste est identique.

## Limites connues

- Je n'ai ni éditeur Unity ni compilateur : **rien n'a été testé** ni dans un navigateur ni dans
  Unity. Le rendu décrit est déduit du CSS.
- Les tailles px fixes sont un compromis pour couvrir 1920×1080 et 1280×720 sans `@media` ; en
  UITK on pourrait affiner avec des tokens de la rampe `--cdp-font-rt-*` et/ou des breakpoints C#
  (comme `InfoTableUitkController`).
- Le fichier est autonome et ne référence aucun fichier du projet ; les valeurs de palette sont
  recopiées en littéral (pas de `var()` / import de `variables.uss`, pour rester un HTML unique).
- Aucune donnée réelle : la composition, les joueurs et les paramètres sont des exemples plausibles.
