# RAPPORT — H3 : écran de salon (lobby) « Corruption du Portail »

Livrable : `index.html` (autonome, CSS dans `<style>`, **aucun JavaScript**, aucune ressource externe).
Version Unity lue dans `ProjectSettings/ProjectVersion.txt` : **6000.5.0f1**. Attention : `CLAUDE.md` et
`_bmad-output/project-overview.md` indiquent encore 6000.2.6f2. J'ai pris le fichier ProjectVersion comme
référence. Je n'ai rien testé, ni dans Unity ni dans un navigateur : aucun navigateur headless n'est installé
dans l'environnement. La maquette n'a donc jamais été affichée. Les budgets de place en 1280×720 et
1920×1080 sont calculés à la main (voir « Limites »).

## 1. Contenu de l'écran

| Zone | Contenu (données factices) |
|---|---|
| En-tête | Titre du jeu, nom du salon (« Salon de Vesper »), phase (« Salon — en attente des joueurs »), code du salon, nombre de joueurs 12 / 14 |
| Colonne 1 — Joueurs | 12 joueurs connectés et 2 places libres. Pour chacun : avatar à initiales, pseudo, badges `HÔTE` / `VOUS`, état `PRÊT` / `PAS PRÊT`. La ligne du joueur local est surlignée en vert |
| Colonne 2 — Composition des rôles | Preset « Classique · 12 joueurs », récapitulatif (Joueurs 12, Imposés 4, Tirage 8, Pool max 15), rôles groupés par faction (Anomalies / Élus / Marginaux), avec nombre d'exemplaires (`×1`, `×2`) et étiquette `IMPOSÉ` |
| Colonne 3 — Paramètres | Preset, joueurs maximum, durée du vote, récapitulatif du vote, réveil par rôle, visibilité, langue. Bouton `MODIFIER` réservé à l'hôte |
| Colonne 3 — Prêt / Lancer | Compteur 9 / 12 prêts et barre à 12 segments, liste des joueurs en attente, état de la composition, bouton bascule `JE SUIS PRÊT` (tous les joueurs), bouton `LANCER LA PARTIE` (hôte uniquement, désactivé tant que tout le monde n'est pas prêt) |

**Vue non-hôte** : ajouter la classe `H3_lobby--guest` à la racine. Les éléments `H3_host-only` disparaissent
(`LANCER LA PARTIE`, `MODIFIER`) et un message d'attente `H3_guest-only` s'affiche à la place. En UITK, cela
correspond à un `AddToClassList("H3_lobby--guest")` sur la racine.

## 2. Sources dans l'univers du jeu (lecture seule)

- **Palette** : la voie « lobby vert » de `Assets/UI/Screens/LobbyRoles/LobbyRoles.uss` et des jetons
  `--cdp-color-journal-*` de `Assets/UI/Styles/variables.uss` : fond rgb(10,7,8), panneaux rgb(20,16,15),
  filets rgb(42,33,31) / rgb(58,44,41), texte rgb(240,231,221), accent vert rgb(95,217,135), chiffres dorés
  rgb(238,201,122), « imposé » rgb(233,193,90), avertissement orange rgb(224,151,58).
- **Couleurs de faction** : `Assets/ScriptableObjects/FactionDatabase.asset`. Anomalie (1, 0.19, 0.19),
  Élu (0.49, 0.85, 0.34), Marginal (1, 0.74, 0.35).
- **Rôles et exemplaires** : le preset réel `Preset_Classique_12.asset`. Imposés : Va'ahl ×1, Abyss ×2,
  Le Robot ×1. L'Incomplet est à ×2 et les autres rôles à ×1. Pool total : 15. Les noms viennent des
  `roleName` des assets `Assets/ScriptableObjects/Characters/*.asset`, découpés en nom + épithète
  (ex. « Abyss » / « L'Extension du Néant »).
- **Vocabulaire** : « Imposé », « Pool max », « Prêt / Pas prêt », « Anomalies / Élus / Marginaux », repris de
  `LobbyRolesUitkController.cs` et de `spec-lobby-ready-system.md`.
- **Paramètres** : la durée du vote (300 s) et le récapitulatif du vote (10 s) viennent de `VoteState.asset` et
  `VoteRecapState.asset`. La langue et la visibilité privée viennent de `LobbyCreationSettings`.

## 3. API UITK et propriétés USS visées par la transposition

Correspondance des éléments HTML : `div` donne `VisualElement`, `span` donne `Label`, `button` donne `Button`.

### Éléments et API
| Élément / API | Usage | Certitude |
|---|---|---|
| `VisualElement` | conteneurs, avatars, segments, pastilles | certain |
| `Label` | tous les textes | certain |
| `Button` (avec un `VisualElement` et un `Label` enfants) | bascule prêt, lancer, modifier | certain |
| Texte riche dans `Label` (`<b>`) | note sous la composition | certain |
| `VisualElement.SetEnabled(false)` → pseudo-classe `:disabled` | bouton « Lancer » désactivé | certain |
| `AddToClassList` / `RemoveFromClassList` | variante invité, états prêt / pas prêt | certain |
| `ScrollView` | recommandé si la liste de joueurs dépasse 14 lignes (non utilisé dans la maquette) | certain |
| `Toggle` | autre option possible pour « Je suis prêt ». La maquette utilise un `Button` stylé, comme le contrôleur existant | certain |

### Propriétés USS
| Propriété USS | Équivalent CSS dans la maquette | Certitude |
|---|---|---|
| `flex-direction`, `flex-grow`, `flex-shrink`, `flex-basis`, `flex-wrap` | identiques | certain |
| `align-items`, `justify-content` (`space-between`, `center`, `flex-end`) | identiques | certain |
| `width`, `height`, `min-width`, `min-height`, `max-height` (px et %) | identiques | certain |
| `margin-*`, `padding-*` (px) | identiques | certain |
| `border-width`, `border-*-width`, `border-color`, `border-*-color`, `border-radius` (px) | identiques | certain |
| `background-color` (rgb / rgba) | identique | certain |
| `color`, `font-size` (px), `letter-spacing` (px) | identiques | certain |
| `-unity-font-style: bold` | `font-weight: 700` | certain |
| `-unity-text-align` (`middle-center`, `middle-right`, `middle-left`) | `text-align` | certain |
| `white-space: nowrap / normal` | identique | certain |
| `text-overflow: ellipsis` (avec `overflow: hidden`) | identique | certain |
| `overflow: hidden` | identique | certain |
| `display: none / flex` | identique | certain |
| `opacity` | non utilisé | — |
| `transition-property`, `transition-duration` | survol des boutons | certain |
| Pseudo-classes `:hover`, `:disabled` | identiques | certain |
| Sélecteurs de classe, descendant, classes multiples sur un élément | identiques | certain |
| Découpe des coins arrondis des enfants par `overflow: hidden` sur un parent à `border-radius` (`.H3_tally`) | identique | **incertain** : dans le pire cas, les coins des cellules débordent de quelques pixels |
| `-unity-font-definition` | remplace `font-family` | certain (la police du projet est LiberationSans, voir `RoleCard.uss`) |

## 4. Fonctionnalités CSS qui ne se transposent pas telles quelles

| CSS utilisé | Pourquoi | Que faire en USS |
|---|---|---|
| `* { box-sizing: border-box }`, `margin: 0; padding: 0` global | reset navigateur | à supprimer : UITK calcule déjà `width` bordures et padding compris, sans marges par défaut |
| `div { display: flex; flex-direction: column }` | émule le défaut de `VisualElement` | à supprimer : c'est le défaut en UITK |
| `span { display: block }`, `button { display: flex }` | reset navigateur | à supprimer |
| `border-style: solid` (et variantes par côté) | n'existe pas en USS, les bordures sont toujours pleines | à supprimer |
| `font-family: "Liberation Sans", Arial…` | USS attend une ressource police | `-unity-font-definition: url("project://database/Assets/TextMesh%20Pro/Fonts/LiberationSans.ttf")` ou police par défaut du PanelSettings |
| `font-weight: 700` | pas de `font-weight` en USS | `-unity-font-style: bold` |
| `text-align` | nom différent | `-unity-text-align` |
| `<b>` en ligne dans la note | balise HTML | texte riche `<b>` dans le `text` du `Label` (même rendu) |
| Attribut `disabled` de `<button>` | attribut HTML | `SetEnabled(false)` en C# |
| `html, body { width/height: 100%; overflow: hidden }` | cadre de page | racine en `width/height: 100%` sous le PanelSettings |

Volontairement **non utilisés** car sans équivalent en USS : `gap`, grid, `box-shadow`, dégradés,
`::before` / `::after`, `text-transform` (majuscules écrites en dur), `em` / `rem` / `vw` / `vh`, `calc()`,
media queries, `:nth-child`, marges `auto`, `cursor: pointer` (en USS le mot-clé est `link`, et il ne sert
qu'à l'éditeur), `border-style: dashed`, caractères absents de LiberationSans (✓, ♛, emoji). Seuls « · »,
« × », « — » et les accents français sont utilisés.

## 5. Hypothèses

1. **Bouton « Lancer » pour l'hôte.** Il est présent parce que la consigne le demande. Or le projet réel
   (`spec-lobby-ready-system.md`, statut done) a **supprimé** le bouton « Démarrer » : la partie démarre
   automatiquement quand tous les joueurs sont prêts et que la composition est valide, et il ne reste qu'un
   « Démarrage forcé » réservé au développement. Dans la maquette, le bouton est donc désactivé tant que tout
   le monde n'est pas prêt, avec une légende explicative. Pour s'aligner sur le jeu réel, il suffit de retirer
   `H3_btn--primary`.
2. L'hôte est aussi un joueur et doit lui-même se déclarer prêt, d'où la bascule visible aussi côté hôte.
3. **Données inventées** : les pseudos, le code de salon, « Joueurs maximum 14 », « Réveil par rôle 20 s »
   (aucune valeur par défaut trouvée dans les assets) et l'épithète affichée sous les rôles qui n'en ont pas.
   Pour ces derniers, j'ai affiché le nom de faction accordé (« Élu » / « Élue » / « Marginal ») : l'accord au
   féminin est un choix de ma part.
4. Les couleurs de faction sont prises telles quelles dans FactionDatabase. Le rouge rgb(255,49,49) sur fond
   quasi noir reste lisible pour les titres de section.
5. **Mise à l'échelle Unity.** La maquette HTML garde des tailles en px fixes dans les deux formats, pour que
   la version 1280×720 reste lisible (texte ≥ 11 px pour les badges et ≥ 15 px pour le texte courant). Dans
   Unity, je recommande un PanelSettings en *Scale With Screen Size* avec une résolution de référence de
   1280×720 : l'écran 1080p est alors une homothétie ×1,5 de la version 720p. Avec une référence 1920×1080,
   les textes passeraient sous 10 px en 720p.

## 6. Limites connues

- **Aucun rendu vérifié.** Les calculs à la main en 1280×720 donnent une colonne de rôles pleine à environ
  96 % (≈ 547 px sur 569 disponibles) et des lignes joueurs d'environ 35 px. Une police de repli plus large
  que Liberation Sans ou Arial pourrait tronquer quelques épithètes (points de suspension), notamment
  « L'Extension du Néant ». Rien ne passe à la ligne de façon imprévue : les libellés longs sont en
  `nowrap` + `ellipsis`.
- En 1920×1080, les lignes joueurs grandissent jusqu'à 54 px (`max-height`) et les paramètres jusqu'à 48 px.
  Il reste alors de l'espace vide en bas des panneaux « Composition » et « Paramètres ». C'est voulu (pas de
  mise à l'échelle en HTML) mais moins équilibré qu'en 720p.
- La liste des joueurs n'est pas scrollable : au-delà de 14 lignes, il faudrait une `ScrollView`
  (`overflow: auto` n'a pas d'équivalent USS direct).
- Pas d'états visuels `:focus` ni de navigation manette ou clavier.
- La variante invité (`H3_lobby--guest`) n'est pas affichée par défaut : elle ne sera pas visible sur les
  captures sans modifier la classe de la racine.
- La classe `H3_badge--bot` (joueur simulé `clientId >= 100`) est définie mais inutilisée dans les
  données factices.
