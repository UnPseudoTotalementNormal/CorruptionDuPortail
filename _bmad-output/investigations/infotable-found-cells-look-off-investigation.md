# Investigation: InfoTable — les cases "trouvées" paraissent bizarres (liseré + flood)

## Hand-off Brief

1. **What happened.** Le remplissage couleur de faction edge-to-edge des grandes cases name/camp d'une ligne
   "trouvée" produit un liseré/cadre et un rendu que Poyo juge mauvais ("ça ne va pas du tout").
2. **Where the case stands.** Cause racine **Confirmed** en deux couches : (a) mécanique — les hairlines de grille
   (surtout `.info-table__player-row border-top`, pleine largeur) ressortent en cadre contre un fill vif ; (b) design
   — flooder toute la grande case en aplat plat est étranger au langage "tuiles/biseaux" du board. Les 2 patchs de
   bordures ne peuvent pas atteindre le séparateur de ligne pleine largeur → liseré persistant.
3. **What's needed next.** Repenser le traitement "trouvé" (accent-bar faction façon reveal-lock, pas de flood) plutôt
   que re-patcher — décision visuelle à cadrer avec Sally + valider l'intention "case du pseudo en vert" avec Wouh.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A (Play-test Poyo 2026-07-10)                                            |
| Date opened      | 2026-07-10                                                                 |
| Status           | Active — symptom-driven, root cause Confirmed                             |
| System           | Unity 6000.5.0f1 · UITK · screen-space → RenderTexture 1440×912 → RawImage |
| Evidence sources | Screenshots Play-test ; InfoTable.uss ; InfoTableUitkController.cs ; commits 8ce8df08 (feat UI) |

## Problem Statement

Verbatim Poyo : "tes cases sont trop bizarres, ça ne va pas du tout". Contexte : après ajout du remplissage couleur
de faction (mutée, `Lerp(faction, dark, 0.55)`) sur les cases name/pseudo + camp d'une ligne trouvée. Symptôme :
liseré/cadre inset plus sombre autour des grandes cases vertes ; l'ensemble paraît "off". Deux patchs (neutraliser
les bordures propres de name-cell puis camp-cell en les peignant à la couleur du fill) N'ONT PAS résolu.

## Evidence Inventory

| Source                          | Status    | Notes                                                                    |
| ------------------------------- | --------- | ------------------------------------------------------------------------ |
| InfoTable.uss (bordures grille) | Available | `.player-row border-top` L137-138 ; `.name-cell` border-top/right L159-163 ; `.cell` border-left L188 ; `.camp-cell` border-top/right L92-95 |
| InfoTableUitkController.cs       | Available | `PaintFaction`/`PaintFactionTile` peignent bg + 4 border-colors = fill (inline) |
| Screenshots Play-test           | Available | Cadre inset sur les cases vertes ; look "patch" contre les cases-tuiles sombres |
| Inspection pixel runtime UITK   | Missing   | Pas d'outil MCP pour lire les styles calculés/pixels d'un panel RT (screenshot montre uGUI, pas UITK-RT) |

## Confirmed Findings

### Finding 1: Le fill couleur est edge-to-edge ; les hairlines de grille vivent PILE sur ces bords

**Evidence:** `.info-table__name-cell` bg + `border-top: 1px bevel-hi` (`InfoTable.uss:162-163`) + `border-right: 1px
frame-soft` (`:159-160`) ; `.info-table__cell border-left: 1px frame-soft` (`:188`) ; `.info-table__camp-cell`
border-top/right (`:92-95`). Le controller peint le fond ET les 4 border-colors = fill (`PaintFactionTile`,
`InfoTableUitkController.cs`).

**Detail:** Sur le board sombre, `frame-soft` = `rgba(150,120,64,0.22)` et `bevel-hi` = `rgba(255,244,214,0.06)` sont
QUASI-INVISIBLES (c'est voulu : biseaux discrets). Contre un fill vert vif, ces mêmes hairlines gold-α ressortent
nettement → cadre/liseré. Peindre les bordures PROPRES d'une case à la couleur du fill les masque, mais…

### Finding 2: Le séparateur de ligne est pleine largeur → INATTEIGNABLE par une neutralisation par-case

**Evidence:** `.info-table__player-row { border-top-width: 1px; border-top-color: var(--cdp-color-info-frame-soft) }`
(`InfoTable.uss:137-138`).

**Detail:** Ce hairline est porté par l'élément LIGNE (pas la case), il traverse camp+name+toutes les cases de rôle.
On ne peut pas le verdir seulement sur la portion "case colorée" sans tracer un trait vert au-dessus des cases
sombres à droite. C'est pourquoi les 2 patchs de bordures par-case n'enlèvent PAS le liseré haut/bas des cases
vertes : il vient de ce séparateur de ligne, pas des bordures de case. **Cause mécanique du liseré résiduel.**

## Deduced Conclusions

### Deduction 1: Ce n'est pas un mauvais paramètre, c'est le mauvais TRAITEMENT

**Based on:** Findings 1-2 + langage visuel du board (cases sombres à tuiles-segments biseautées arrondies,
`--cdp-radius-info`, ghost tiles).

**Reasoning:** Flooder toute une grande case en aplat plat de couleur est étranger au langage "Dossier" du board
(tout le reste = tuiles inset/biseautées sur fond sombre). Un grand rectangle plat et vif, forcément bordé par les
hairlines de grille, lit comme un "patch" rapporté. Plus on le mute → plus c'est boueux ; plus il est vif → plus le
cadre ressort. Aucun réglage de saturation/opacité ne sort de ce piège tant que le fill est un aplat edge-to-edge.

**Conclusion:** Le liseré n'est qu'un symptôme. Le vrai problème = flood plein-case. Fix = changer de traitement, pas
re-patcher les bordures.

## Hypothesized Paths

### Hypothesis 1: Contribution d'artefact de scaling RenderTexture au liseré

**Status:** Open (secondaire)

**Theory:** Le panel est rendu en RT 1440×912 puis affiché redimensionné → une couture d'AA plus sombre peut
apparaître aux frontières couleur-vif/sombre, s'ajoutant aux hairlines.

**Would confirm:** Liseré persistant même après suppression TOTALE des hairlines de bord (Finding 2 réglé) et même
épaisseur quelle que soit l'échelle d'affichage.

**Would refute:** Liseré disparaît entièrement une fois les hairlines de bord neutralisés → 100% hairlines, 0% AA.

**Resolution:** Non tranché — nécessiterait un test isolant (fill sans aucune bordure de grille). Secondaire : la
direction de fix A (pas de flood) rend le point sans objet.

## Source Code Trace

| Element        | Detail                                                                                           |
| -------------- | ------------------------------------------------------------------------------------------------ |
| Séparateur     | `.info-table__player-row border-top` `InfoTable.uss:137-138` (frame-soft, pleine largeur)         |
| Bordures case  | name `:159-163`, cell `:188`, camp `:92-95` (frame-soft / bevel-hi)                               |
| Fill inline    | `InfoTableUitkController.cs` `PaintFaction`/`PaintFactionTile` (bg + 4 border-colors = fill)       |
| Langage "résolu" existant | `.info-table__name-cell--locked` `InfoTable.uss:170-177` — bg transparent + **left-accent 3px** + bold : PATTERN à réutiliser |

## Conclusion

**Confidence:** High (mécanique Confirmed ; design Deduced avec forte évidence). L'inspection pixel runtime manque
mais n'est pas nécessaire — la géométrie des bordures suffit.

Le liseré résiduel = le séparateur de ligne pleine largeur (`.player-row border-top`, frame-soft) qui ressort contre
un fill vif et qu'aucune neutralisation par-case ne peut atteindre. Mais le vrai grief ("ça ne va pas du tout") est
que **flooder toute la grande case en aplat** est le mauvais traitement pour ce board à tuiles. Continuer à patcher
les bordures ne sortira pas de l'ornière.

## Recommended Next Steps

### Fix direction

**Direction A — RECOMMANDÉE : réutiliser le langage "ligne résolue" existant, pas de flood.**
Le board a déjà un visuel "ligne résolue" accepté : `.name-cell--locked` = fond transparent + **barre d'accent
gauche 3px** + texte bold (`InfoTable.uss:170-177`). Décliner ça pour le "trouvé", en couleur de faction :
- barre d'accent gauche faction (3-4px) sur la case pseudo (+ éventuellement camp),
- **texte** pseudo en couleur de faction (ou clair) au lieu d'un fond plein,
- garder la case Camp = puce icône de faction (petite, OK),
- au besoin un tint de fond TRÈS subtil (alpha bas, proche du sombre → aucun cadre visible).
→ Plus d'aplat plein = plus de liseré, et cohérent avec le look reveal-lock déjà validé. Décision visuelle exacte =
Sally ; valider le glissement d'intention vs "case du pseudo EN VERT" (Wouh) — l'accent+texte faction rend
l'identité de camp aussi lisible sans l'aplat.

**Direction B — si l'aplat plein-case est une exigence dure (Wouh) :** il faut alors changer le MODÈLE de séparation
de la grille — retirer le `.player-row border-top` pleine largeur et séparer les lignes autrement (zébrage de fond
sans bordure, ou bordures portées par chaque case et neutralisables), pour qu'une case remplie n'ait aucun hairline
sur ses bords. Refactor plus lourd de la grille.

### Diagnostic (si on veut trancher l'Hypothesis 1 avant de choisir)

Test isolant : une ligne trouvée avec fill mais TOUTES les bordures de grille de ses bords à 0/fill (y compris le
séparateur de ligne) — si un liseré subsiste, c'est l'AA du scaling RT (Direction A le contourne quand même).

## Status

Active — root cause Confirmed, direction de fix proposée, en attente d'arbitrage visuel (Sally/Wouh) avant implé.
