# Investigation: InfoTable UITK — cadrage du feedback UI (Wouh)

## Hand-off Brief

1. **What happened.** Feedback design (Wouh, validé Poyo) sur l'InfoTable UITK (PR #72, mergé Dev) : 4 évolutions
   — colonne statut de faction, grise-ligne/colonne + pseudo vert au "trouvé", couleurs spéciales Mage
   Occulte/Robot (vert = camp Élus, contre-intuitif), légende "Je pense" → "peut être".
2. **Where the case stands.** Exploration terminée : architecture UITK entièrement tracée (Confirmed). Le blocage
   structurel unique = `IInfoTableDataSource.GetRoles()` / `InfoTableRole` n'exposent que `RoleName`+`Capacity`,
   pas `FactionType`/`RoleID` — nécessaires aux items 1 et 3. 3 des 4 items sont additifs et non-conflictuels.
3. **What's needed next.** Trancher les décisions design-owned listées (voir « Décisions à cadrer »), puis
   `gds-create-story` (ou `gds-quick-dev` pour l'item 4 seul, trivial). Ne rien coder avant arbitrage Poyo.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A (feedback Discord Wouh, 11:38 ; confirmé UPTNButButter 12:12)          |
| Date opened      | 2026-07-10                                                                 |
| Status           | Active — exploration/cadrage (pas un défaut)                               |
| System           | Unity 6000.2.6f2 · UITK · RT→RawImage · branche worktree infopanel-ui-feedback |
| Evidence sources | Code source Assets/Scripts/UI/InfoTable/*, InfoTable.uxml/uss, variables.uss, Role.cs, FactionType.cs, RoleID.cs |

## Problem Statement

Feedback Discord verbatim (Wouh) — 4 demandes :

1. **Colonne statut à gauche.** Liste déroulante tout à gauche : assigner "Élu" / "Marginal" / "Anomalie" à un
   joueur *sans* connaître son rôle exact ("si on safe quelqu'un, mais sans avoir son rôle, tu lui mets ça").
2. **Grise-out au vert.** Quand on met du Vert (joueur=rôle confirmé), au lieu d'une contradiction, **griser le
   reste de la colonne ET de la ligne** ("libère visuellement ce qui ne sert plus") + mettre **en vert la case du
   pseudo** (et peut-être du rôle) pour confirmer trouvé.
3. **Clash de couleur Mage Occulte / Robot.** Le vert = couleur de validation MAIS aussi couleur des Élus →
   contre-intuitif de mettre du vert pour un Mage occulte (méchant caché). Proposition : case emoji 😈
   exceptionnelle pour Mage Occulte + Robot qui débloque du **violet** (= suspecté méchant). Bouton ✅ pour
   Mage Occulte → **tout en Rouge Écarlate** ; ✅ pour Robot → **tout en Orange Foncé**.
4. **Légende.** Remplacer "Je pense" par "peut être".

Note owner (UPTNButButter/Poyo, 12:12) : le "1/1" (compteur capacité) est retirable pour les rôles uniques ; "ok
pour le reste".

## Evidence Inventory

| Source                                   | Status    | Notes                                                                 |
| ---------------------------------------- | --------- | --------------------------------------------------------------------- |
| InfoTableUitkController.cs (393 l.)      | Available | Vue : build grille, segments, footer, reveal-lock, tiers responsive   |
| InfoTableModel.cs (163 l.)               | Available | POCO : CellState, conflits, lock. **Pas** de faction ni "found"       |
| IInfoTableDataSource.cs + InfoTableRole  | Available | **Gap** : role = `RoleName`+`Capacity` seulement                      |
| GameInfoTableDataSource.cs               | Available | Adapte NGO → DTO ; a accès à `Role` (faction/RoleID) mais ne l'expose pas |
| InfoTable.uss + variables.uss (l.77-100) | Available | Tokens `--cdp-color-info-*` ; sure=vert, maybe=amber, not=rouge       |
| FactionType.cs / RoleID.cs               | Available | Confirme le mapping domaine (voir Findings 4-5)                       |
| InfoTableModelTests.cs (176 l.)          | Partial   | 14 tests EditMode existants — surface à étendre, pas encore lue en détail |
| Screenshot mockup (joint au feedback)    | Available | Image Discord ; référence visuelle, pas de fichier repo               |

## Confirmed Findings

### Finding 1: La grille est 100 % construite au runtime depuis un data source ; l'UXML est un canevas vide

**Evidence:** `Assets/UI/Screens/InfoTable/InfoTable.uxml:10` (racine `info-table` seule) ;
`InfoTableUitkController.cs:152` `BuildGrid()`.

**Detail:** Aucune structure de cellule en UXML. Tout ajout de colonne (statut faction) ou de segment se fait en
C# dans `BuildGrid()` / `AddSegment()`. Avantage : pas de rewiring de prefab pour l'item 1 ; tout est code.

### Finding 2: Une cellule = 3 segments cliquables (Sûr ✓ / "Je pense" ? / Pas lui ✗) ; l'état vit dans le POCO

**Evidence:** `InfoTableUitkController.cs:218-227` (3 `AddSegment`), `:258-270` (segment → `_model.SetCell`) ;
`InfoTableModel.cs:7-13` (`enum CellState { None, Sure, Maybe, SurelyNot }`).

**Detail:** Le clic d'un segment appelle `SetCell(pi, ri, state)` ; re-clic sur l'état actif → `None`
(`InfoTableModel.cs:92`). Le rendu lit le modèle dans `Render()` (`:347`) via `EnableInClassList`. Toute nouvelle
règle visuelle (item 2, 3) se branche dans `Render()` + une classe USS, sans toucher la logique de clic.

### Finding 3: Le "reveal-lock" est déjà la mécanique « ligne verrouillée » — proche de l'item 2 mais déclenché serveur, pas utilisateur

**Evidence:** `InfoTableModel.cs:97-120` `LockRowToRole()` : colonne correcte = `Sure`, autres = `SurelyNot`,
`_locked[pi]=true`, non-interactif. Rendu : `InfoTableUitkController.cs:353-363` (classes `--locked` sur row/name/cell),
USS `InfoTable.uss:101-103,120-126,155-157`. Source du reveal : `GameInfoTableDataSource.cs:69-79`
(`GetRevealedRoleName` via `GameInfoRevealer`).

**Detail:** L'item 2 (« mettre du vert → grise le reste ») EST conceptuellement un lock déclenché par l'utilisateur
au lieu du serveur. Différence clé : le reveal-lock grise TOUTE la ligne ; l'item 2 demande de griser la ligne **et
la colonne**, et de « verdir le pseudo ». Le grisé-colonne n'existe nulle part aujourd'hui (le modèle n'a pas de
notion de colonne verrouillée). C'est le vrai travail neuf de l'item 2.

### Finding 4: FactionType existe et mappe exactement le vocabulaire du feedback

**Evidence:** `Assets/Scripts/Domain/FactionType.cs:3-9` → `{ anomaly, chosen, marginal, unknown }`.

**Detail:** "Élu" = `chosen`, "Marginal" = `marginal`, "Anomalie" = `anomaly`, + `unknown`. Le dropdown de l'item 1
est un choix parmi ces valeurs. Couleurs de faction : `FactionData.color` dans
`Assets/Scripts/Characters/FactionDatabase.cs:16-25` (SO design-owned) — source réutilisable pour teinter la colonne.

### Finding 5: Mage Occulte et Robot sont des RoleID stables et identifiables

**Evidence:** `Assets/Scripts/Characters/RoleID.cs:11` `MageOcculte = 3307`, `:17` `Robot = 3917`.
`Role` porte `roleID` + `factionType` (`Role.cs:24-26`).

**Detail:** L'item 3 (traitement spécial 😈/violet, ✅ rouge écarlate / orange foncé) se cible proprement par
`RoleID` plutôt que par nom string. **Mais** `InfoTableRole` (`IInfoTableDataSource.cs:23-33`) n'expose ni `roleID`
ni `factionType` — c'est le blocage structurel (voir Missing Evidence / Hypothesis 1).

### Finding 6: L'item 4 est une modification d'une string unique

**Evidence:** `InfoTableUitkController.cs:308` `legend.Add(BuildLegendItem(SegMaybeClass, GlyphMaybe, "Je pense"));`.

**Detail:** Le libellé "Je pense" n'apparaît QUE dans la légende (le glyphe de la cellule est "?", pas de texte).
Remplacer par "peut être" = un littéral. Commentaires de doc (`:8` uss, `:14` controller) mentionnent aussi
"Je pense" — cosmétique, à aligner. Aucun impact logique.

### Finding 7: Le compteur capacité affiche toujours `x/Cap`, y compris `/1`

**Evidence:** header `InfoTableUitkController.cs:176` n'affiche `×{Capacity}` QUE si `Capacity > 1` (déjà conforme
à la demande owner pour l'en-tête). MAIS le chip footer `:294` `$"0/{Capacity}"` et `:386` `$"{sure}/{Capacity}"`
affichent le `/1` pour les rôles uniques.

**Detail:** La demande owner (« retirer le 1/1 pour rôles uniques ») cible le **chip footer** `RenderFooter()`
`:379-388`. Pour `Capacity == 1`, afficher juste la présence (ex. `sure>0 ? "✓" : "–"`) au lieu de `1/1`.
Décision d'affichage design-owned.

## Deduced Conclusions

### Deduction 1: Les items 1 et 3 exigent d'élargir le contrat data source ; les items 2 et 4 non

**Based on:** Findings 4, 5, 6, plus `IInfoTableDataSource.cs:40-56` et `InfoTableRole` `:23-33`.

**Reasoning:** L'item 1 (statut faction par joueur) et l'item 3 (spécial Mage/Robot) ont besoin de données de
domaine — faction devinée (item 1, état utilisateur, PAS depuis le data source) et identité de rôle réelle (item 3,
depuis le data source). L'item 3 requiert `RoleID`/`FactionType` sur `InfoTableRole`, aujourd'hui absents.
L'item 2 (grise-out) est purement dérivé de `CellState.Sure` déjà présent. L'item 4 est une string.

**Conclusion:** Ordonner l'implémentation : **item 4** (trivial, isolé) → **item 2** (modèle+USS, pas de contrat) →
**item 7/owner** (footer) → **item 1** (nouvel état joueur + colonne + dropdown) → **item 3** (élargir contrat +
règles couleur). Items 1 et 3 partagent l'élargissement du data source ; les grouper.

### Deduction 2: L'item 2 entre en tension directe avec la détection de conflit existante

**Based on:** Findings 2, 3 ; `InfoTableModel.cs:122-161` `Recompute()` (conflit local = >1 Sure/ligne ; global =
Sure > capacité).

**Reasoning:** Aujourd'hui, deux `Sure` sur une même ligne = conflit (bannière rouge). Le feedback item 2 réinterprète
un `Sure` comme « trouvé » qui **grise le reste de la ligne** — donc un 2ᵉ Sure deviendrait impossible/inutile, pas
un conflit. Cela change la sémantique de `Sure`. Si un `Sure` utilisateur grise sa ligne+colonne, faut-il encore la
détection de conflit local ? Probablement redondante pour les lignes « trouvées » mais toujours utile ailleurs.

**Conclusion:** L'item 2 n'est pas une simple couche visuelle : il touche la sémantique du modèle (un `Sure` devient
un soft-lock utilisateur). À cadrer avec Wouh : le grisé est-il réversible (re-clic) ? Le grisé-colonne est-il un
vrai lock (empêche de cliquer) ou juste une atténuation visuelle ? Voir Hypothesis 2.

## Hypothesized Paths

### Hypothesis 1: Élargir `InfoTableRole` (+ `IInfoTableDataSource`) avec `RoleID`/`FactionType` suffit à débloquer l'item 3

**Status:** Open

**Theory:** Ajouter `RoleID` (et/ou `FactionType`) à `InfoTableRole`, peuplé par `GameInfoTableDataSource.GetRoles()`
(qui a déjà `Role` en main, `:59-66`) et un stub dans `DemoInfoTableDataSource` (`:101-109`). Le controller cible
alors Mage/Robot dans le rendu de cellule et applique les couleurs spéciales.

**Would confirm:** Un prototype où `Render()` lit `role.RoleID == MageOcculte` et applique une classe USS
`--evil-scarlet` peint bien la ligne en rouge écarlate au lieu de vert.

**Would refute:** Si le mapping RoleID n'est pas disponible côté Demo/harness (Demo n'a pas de vrais RoleID),
il faudra un chemin de test alternatif ; ou si le clash concerne d'autres rôles « méchants cachés » non listés,
un flag `isHiddenEvil` sur le rôle serait plus robuste qu'un check RoleID en dur.

### Hypothesis 2: L'item 2 se modélise comme un `foundRole[playerIndex]` dérivé + un grisé-colonne calculé

**Status:** Open

**Theory:** Ajouter au modèle un état « trouvé » par ligne (déclenché quand l'utilisateur pose un `Sure`), qui : (a)
grise les autres cellules de la ligne, (b) grise la même colonne pour les autres joueurs (car ce rôle est pris), (c)
marque la case pseudo « verte ». Réutilise le pattern `_locked` mais séparé du reveal-lock serveur (réversible).

**Would confirm:** La logique reprend `LockRowToRole` mais réversible + ajoute un `IsColumnClaimed(roleIndex)` que
`Render()` lit pour atténuer les cellules de la colonne.

**Would refute:** Si Wouh veut que le grisé-colonne reste cliquable (juste visuel, pas verrouillé), alors ce n'est
pas un lock mais une classe cosmétique — plus simple, pas de changement de `Recompute()`.

## Missing Evidence

| Gap                                                          | Impact                                                            | How to Obtain                                              |
| ----------------------------------------------------------- | ---------------------------------------------------------------- | --------------------------------------------------------- |
| `InfoTableRole` n'a ni `RoleID` ni `FactionType`            | Bloque item 3 (ciblage Mage/Robot) et teinte faction item 1      | Élargir la struct + les 2 implémenteurs data source       |
| Réversibilité + portée exacte du grisé (item 2)             | Détermine si le modèle change (soft-lock) ou juste USS           | Question design à Wouh (voir Décisions)                    |
| Couleurs exactes "violet / rouge écarlate / orange foncé"   | Nouveaux tokens `--cdp-color-info-*` à définir                   | Valeurs design-owned (Wouh) → variables.uss               |
| Le dropdown item 1 : natif UITK (`DropdownField`/`PopupField`) sur RT | Popup UITK dans un panel RenderTexture peut mal se positionner | Prototype à Play-tester (Poyo) — les popups RT sont un piège connu |
| "😈 case exceptionnelle" — mécanique exacte (case en plus ? remplace ✅ ?) | Change le nombre de segments pour ces 2 rôles              | Clarification Wouh                                          |

## Source Code Trace

| Element        | Detail                                                                                          |
| -------------- | ----------------------------------------------------------------------------------------------- |
| Point d'entrée | `InfoTableUitkController.BuildGrid()` `:152` (structure) + `Render()` `:347` (états visuels)     |
| Modèle d'état  | `InfoTableModel` `:25` — étendre ici pour item 2 (found/column-claim) et item 1 (faction guess) |
| Contrat data   | `IInfoTableDataSource` + `InfoTableRole` `:23` — élargir pour items 1/3                          |
| Item 4 (string)| `InfoTableUitkController.cs:308`                                                                 |
| Item 7 (footer)| `InfoTableUitkController.cs:294,386` `RenderFooter()`                                            |
| Tokens couleur | `Assets/UI/Styles/variables.uss:77-100` (`--cdp-color-info-*`)                                   |
| Tests          | `Assets/Scripts/Tests/Editor/InfoTableModelTests.cs` (14 EditMode) — étendre pour nouveaux états |

## Conclusion

**Confidence:** High (architecture entièrement Confirmed ; le seul inconnu est design-owned, pas technique).

L'InfoTable UITK est proprement séparée (POCO modèle testable + controller de rendu + data source seam). Les 4
items du feedback se cadrent nettement :

- **Item 4** ("Je pense" → "peut être") : trivial, 1 string (`:308`). Prêt à coder.
- **Item 7/owner** (retirer "1/1" rôles uniques) : petit, footer `RenderFooter()`. Décision d'affichage à confirmer.
- **Item 2** (grise ligne+colonne au vert + pseudo vert) : touche la **sémantique du modèle** (un `Sure` devient un
  soft-lock utilisateur, réutilise le pattern reveal-lock mais réversible + nouveau grisé-colonne). Pas juste du USS.
- **Item 1** (colonne statut faction) : nouvel état joueur + dropdown UITK + colonne. `FactionType` mappe déjà le
  vocabulaire. Risque : popup UITK sur RenderTexture (à Play-tester).
- **Item 3** (spécial Mage Occulte/Robot) : **exige d'élargir `InfoTableRole` avec `RoleID`/`FactionType`** (blocage
  structurel unique). Ensuite règles couleur + nouveaux tokens. À grouper avec l'item 1 (même élargissement).

Aucun défaut : c'est un cadrage d'évolution. Le travail est bien découpé et à faible risque, hors les 2 pièges à
Play-tester (popup dropdown sur RT) et à trancher avec le design (sémantique du grisé + couleurs exactes).

## Recommended Next Steps

### Décisions — arbitrage Poyo 2026-07-10

- **#3 (mécanique 😈) — RÉSOLU.** Pas de segment séparé : garder le MÊME emoji ✅/Sûr, mais le recolorer par la
  couleur de faction du rôle au lieu du vert fixe.
- **#4 (ciblage) — RÉSOLU.** Générique via `factionType`, PAS de `RoleID` en dur ni de flag `isHiddenEvil`. Preuve :
  `MageOcculte.asset` factionType=0 (**anomaly**), `Robot.asset` factionType=2 (**marginal**) — factions distinctes,
  donc la couleur de faction discrimine à elle seule (anomaly→rouge, marginal→orange, chosen→vert).
- **#5 (chip 1/1) — RÉSOLU.** Chip footer **caché** pour les rôles à capacité 1.
- **Couleurs — DÉJÀ DÉFINIES (design-owned).** `Assets/ScriptableObjects/FactionDatabase.asset` :
  Anomalie `rgb(255,49,49)`, Marginal `rgb(255,189,89)`, Élu `rgb(126,217,87)`, Inconnu blanc. C'est le SoT.
  L'item 3 = lire ces couleurs, ne rien inventer. (Nuance : le token `--cdp-color-info-sure` = `rgb(78,168,92)`
  diffère du vert Élu `rgb(126,217,87)` — incohérence à trancher, voir Sally.)
- **#1 (grisé item 2) — TRANCHÉ Poyo.** Le grisé est **purement visuel** : les cellules grisées **RESTENT
  cliquables** (`opacity` seulement, PAS `pointer-events: none`, PAS de soft-lock). Un Sûr n'est qu'une déduction —
  le joueur reste libre de tout modifier n'importe quand. Le grisé « libère visuellement ce qui ne sert plus »,
  il n'empêche rien et ne prévient AUCUN conflit. La détection de conflit + bannière restent inchangées et
  atteignables (2 Sûr/ligne toujours possible). Retirer le hover-highlight des cellules grisées ; teinter la case
  pseudo par la couleur de faction du rôle trouvé (voir #2). "Verdir la case rôle" : accent (bord/soulignement)
  faction quand colonne pleine, pas remplissage (garde le gold).
- **Cohérence + dropdown — délégué à Sally.** Aligner Sûr↔Élu ? style + placement du `DropdownField` faction (RT).

### Décisions visuelles — arbitrage Sally + Poyo 2026-07-10

- **Couleur du ✓ "trouvé" (2b) — TRANCHÉ : Option A.** Le segment ✓ posé prend la **couleur de faction du rôle**
  (anomaly→rouge écarlate, chosen→vert, marginal→orange), en plus du pseudo. « Tout en rouge écarlate » (Wouh).
  Vigilance Play-test (non bloquante) : ✓ rouge anomaly `rgb(255,49,49)` vs ✗ "Pas lui" `rgb(190,82,74)` — reds
  distincts + glyphes ≠ + grisé/pseudo = contexte suffisant. Implique : `InfoTableRole` doit porter la faction
  (ou la couleur) pour que `Render()` peigne le ✓ sélectionné inline.
- **Source des couleurs de faction — TRANCHÉ : `FactionDatabase.asset` (SO), source unique globale.** L'InfoTable
  réutilise le MÊME asset que la RoleCard et résout `FactionType → Color` **inline en C#** (pattern
  `RoleCardController.ApplyFactionTint`, `RoleCardController.cs:301-331` — cf. commentaire `:53` « Shades are
  computed from that one source »). **NE PAS** créer de tokens USS `--cdp-color-faction-*` : ce serait une 2ᵉ source
  qui doublonne le SO → dérive. « Partout la même » garanti par construction (un seul asset), pas par discipline.
  - Faction (✓ trouvé, pseudo, accents, colonne camp) → `FactionDatabase`, inline C#.
  - Non-faction (amber « peut être », rouge « pas lui », gold, grisé, hairlines) → tokens USS `--cdp-color-info-*`
    dans `variables.uss` (déjà globaux, restent).
- **Aligner les 2 verts (2c) — DISSOUT.** Plus besoin d'aligner : le ✓ d'un Élu est peint depuis
  `FactionDatabase.chosen` rgb(126,217,87) inline → identique à la RoleCard automatiquement. `--cdp-color-info-sure`
  rgb(78,168,92) ne sert plus qu'à la légende + au harness Demo.
- **Seam technique.** `FactionType` = enum pur (`Assets/Scripts/Domain/FactionType.cs`, zéro dépendance engine) →
  `InfoTableRole` porte un champ `FactionType` sans polluer le POCO. Le controller (MonoBehaviour) tient la ref
  `FactionDatabase` (`[SerializeField]`, à wirer comme la RoleCard) et résout la couleur. Modèle testable intact.
- **Grisé (1a/1b) — visuel pur, cliquable** (voir bloc précédent). `opacity` ~0.3–0.4 (Play-test sur le dark) +
  retrait du hover sur cellules grisées. Pas de `pointer-events: none`.
- **En-tête rôle (1c) — accent faction (bord/soulignement) quand colonne pleine**, pas de remplissage (garde le gold).
- **Rôle ×2 (1d) — grisé-colonne seulement à `sureCount == capacity`.**
- **Colonne camp (3a) — contrôle par CYCLE** (Inconnu→Élu→Marginal→Anomalie), PAS de `DropdownField` (popup
  mal placé sur panel RenderTexture). Placement tout à gauche, en-tête "Camp".
- **Teinte camp (3b) — chip de statut seul** (couleur + icône/lettre de faction), jamais toute la ligne.
  Bonus optionnel : ligne "found" → auto-remplir + verrouiller le chip camp sur la faction du rôle trouvé.

### Fix direction (une fois les décisions prises)

- **Vague A (isolée, sans risque) :** item 4 (string) + item 7 (footer). → `gds-quick-dev`.
- **Vague B (modèle+USS) :** item 2 — étendre `InfoTableModel` (état found/column-claim, réversible), `Render()`,
  nouvelles classes USS. Étendre `InfoTableModelTests`.
- **Vague C (contrat) :** élargir `InfoTableRole`/`IInfoTableDataSource` avec `RoleID`/`FactionType`, brancher les 2
  data sources, puis items 1 (colonne faction + dropdown) et 3 (couleurs Mage/Robot). Play-test popup RT requis.

### Diagnostic

- Lire `InfoTableModelTests.cs` en détail avant d'étendre le modèle (vague B/C) pour préserver le contrat testé.
- Prototyper le `DropdownField` UITK dans le panel RT (`TabletOnlySpike`) AVANT d'investir dans l'item 1 —
  vérifier le placement du popup (piège connu des panels RenderTexture).

## Reproduction Plan

N/A — cas d'exploration/cadrage, pas de défaut à reproduire. Vérification = revue design des décisions ci-dessus,
puis tests EditMode étendus (`InfoTableModelTests`) pour les vagues B/C et un Play-test harness pour le dropdown RT.

## Follow-up: 2026-07-10 — Implémentation (worktree, vérif Unity différée)

Toutes les décisions ratifiées → code écrit dans le worktree `infopanel-ui-feedback-c9cfc4`. L'Editor Unity pointe
sur le checkout principal (Dev) → tests + wiring scène NON vérifiés (choix owner : « vérif à la fin »). Diff : 8
fichiers, +406/-9.

**Fait (A+B+C) :**
- **A** — "Je pense"→"peut être" (légende + commentaires) ; chip footer caché si `capacity<=1`.
- **B** — grisé dérivé : `InfoTableModel.IsRowFound/IsColumnClaimed/IsCellDimmed` (aucun état neuf, visuel pur,
  cellules cliquables) ; `.info-table__cell--dimmed` (`opacity:0.3` + hover neutralisé) ; 6 tests.
- **C** — `InfoTableRole += FactionType` (+ ctor 2-arg rétro-compat) ; `GameInfoTableDataSource` peuple depuis
  `role.factionType` ; `DemoInfoTableDataSource.GuessFaction` (harness). ✓ + pseudo peints couleur faction inline
  (Option A) via `FactionColor()` lisant `FactionDatabase` (source unique, jamais d'USS faction). Accent en-tête
  `--claimed` quand colonne pleine. Colonne "Camp" (cycle Inconnu→Élu→Marginal→Anomalie, pas de dropdown) :
  `InfoTableModel._campGuess/GetCampGuess/CycleCampGuess` (fill unknown explicite — piège enum default=anomaly) ;
  4 tests. Nouveau token `--cdp-info-campcol`.

**TODO vérif finale (quand Unity ouvert sur le worktree) :**
1. **WIRING OBLIGATOIRE** — `[SerializeField] _factionDatabase` sur le composant `InfoTableUitkController` (dans
   `InfoTable.prefab` et/ou GameScene) → assigner l'asset `FactionDatabase.asset` (via MCP, comme la RoleCard).
   Sans ça : `FactionColor` tombe sur le fallback vert pour toutes les factions (pas de rouge/orange). Cf.
   [[feedback_serialized_field_rewiring]].
2. `run_tests` EditMode catégorie InfoTable (14 existants + 10 neufs = 24).
3. `read_console` → 0 erreur compile.
4. Play-test harness `TabletOnlySpike` (perceptuel) : couleurs faction, grisé lisible sur dark (`opacity` 0.3 à
   ajuster), ✓ rouge anomaly vs ✗ "pas lui", cycle camp, placement colonne.

## Side Findings

- Le reveal-lock (`LockRowToRole`) et l'item 2 sont quasi le même mécanisme sous deux déclencheurs (serveur vs
  utilisateur) — factoriser plutôt que dupliquer (`InfoTableModel.cs:97`).
- `DemoInfoTableDataSource` retourne `Capacity = 1` en dur (`:106`) et n'a pas de vrais RoleID — pour tester l'item 3
  dans le harness il faudra enrichir le Demo avec des identités de rôle factices mais typées.
- Le header masque déjà `×1` (`:176`) — cohérent avec la demande owner ; seule l'incohérence footer reste (Finding 7).
