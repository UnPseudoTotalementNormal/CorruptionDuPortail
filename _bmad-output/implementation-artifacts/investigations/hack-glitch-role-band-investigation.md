# Investigation: Le glitch ne rend pas sur le bandeau/texte de rôle de la carte piratée

## Hand-off Brief

1. **What happened.** Le glitch du hack Robot ne corrompt visuellement PAS le bandeau nom-de-rôle ni le texte de rôle, alors qu'il déchire le portrait et le cadre-pseudo — **Confirmed** : deux causes distinctes combinées (texte TMP exclu par code ; bandeau = sprite UNI + teinte noire, sur lequel un glitch texture-space ne produit rien).
2. **Where the case stands.** Concluded, confiance **High**. Hypothèse "parenté" du user **Refuted** : le bandeau EST bien sous FrontCanvas et reçoit le matériau glitch (prouvé par l'edit `_TintBypass` qui l'a viré au blanc).
3. **What's needed next.** Choisir le fix : (A) terme de static génératif écran dans le shader (répare les bandes unies, pas le texte), ou (B) rendu carte→RenderTexture (glitch fidèle de TOUT, texte inclus). Reco : B si "carte entière + texte" est l'exigence.

## Case Info

| Field            | Value                                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Ticket           | N/A (feature PR #78, branche feat/robot-hack-card-glitch)             |
| Date opened      | 2026-07-13                                                            |
| Status           | Concluded                                                             |
| System           | Unity 6000.2.6f2, uGUI world-space card, URP UI shader                |
| Evidence sources | Code (shader, UIGlitchGroup), prefab Card via Unity MCP, sprite asset, feedback user + capture |

## Problem Statement

User (capture d'une carte piratée) : le portrait et le cadre du pseudo glitchent fort, mais « le nom du rôle et son background ne sont pas inclus dans l'effet ». Hypothèse user : problème de parenté. Un premier fix shader (`_TintBypass`) a seulement rendu le fond du bandeau **blanc** au lieu de noir, sans vrai glitch → reverté (commit 62f11f91).

## Evidence Inventory

| Source | Status | Notes |
| --- | --- | --- |
| `UIGlitchGroup.cs` | Available | Logique de swap matériau + exclusion TMP |
| `UI_GlitchVideo.shader` | Available | Math du glitch (distorsion texture-space) |
| Prefab `Card.prefab` (Unity MCP) | Available | Parenté + couleurs + sprites des Graphic |
| `Square.png` | Available | Sprite du bandeau — lu, carré blanc uni |
| Runtime réel (2 builds) | Missing | Non rejouable ici (pas de playtest) — non nécessaire, preuves statiques suffisent |

## Confirmed Findings

### Finding 1 : Le texte de rôle (TMP) est exclu du glitch par code
**Evidence :** `Assets/Scripts/UI/UIGlitchGroup.cs:84` — `if (graphic is TMP_Text) continue;`
**Detail :** `RoleName` (comme `PseudoText`) est un `TextMeshProUGUI : TMP_Text`. Il n'est jamais material-swappé. Le shader sprite ne peut pas remplacer le shader SDF de TMP. Le "pseudo qui glitche" perçu = son **holder** Image derrière, pas les glyphes.

### Finding 2 : Le bandeau de rôle EST couvert par le glitch (pas un souci de parenté)
**Evidence :** path prefab `.../FrontCanvas/RoleNameHolder` (Unity MCP) ; UIGlitchGroup posé sur FrontCanvas swappe tout `Graphic` descendant (`UIGlitchGroup.cs:82`) ; l'edit `_TintBypass` (df38758a) a visiblement fait passer ce fond de noir à blanc.
**Detail :** Si le matériau glitch n'était pas appliqué à ce Graphic, l'edit shader n'aurait rien pu changer. Donc il reçoit bien le matériau. → **Réfute la parenté.**

### Finding 3 : Le sprite du bandeau est uni
**Evidence :** `Assets/Art/Sprites/Square.png` (lu) = carré blanc plein ; `RoleNameHolder.Image.sprite = Square.png` (Unity MCP).
**Detail :** Texture sans détail.

### Finding 4 : Le bandeau a une teinte de vertex noire
**Evidence :** `RoleNameHolder.Image.color = (0,0,0, 0.96)` (Unity MCP). À comparer à `PlayerPseudoHolder.Image.color = (1,1,1,1)` + sprite texturé `Premier_Plan_de_carte_Nom.png`.

### Finding 5 : Le glitch est une distorsion TEXTURE-SPACE
**Evidence :** `UI_GlitchVideo.shader:148-162` — déplacement de blocs sur `uv.x`, RGB split par re-échantillonnage `uv ± split`. `shader:177` — `col *= IN.color` en sortie.
**Detail :** Toute la corruption vient du ré-échantillonnage de `_MainTex`. Sur un sprite UNI, déplacer/splitter les UV renvoie la MÊME valeur uniforme → aucune déchirure visible ; seuls scanline+snow ajoutent une variation faible.

## Deduced Conclusions

### Deduction 1 : Le bandeau ne peut pas glitcher visuellement en l'état
**Based on :** Findings 3 + 4 + 5.
**Reasoning :** (a) sprite uni → la distorsion texture-space ne produit rien ; le résultat est ~blanc uniforme. (b) `col *= IN.color` avec color noire → multiplié par 0 → **noir plat**. Le `_TintBypass` a retiré (b) → blanc plat, mais (a) demeure → toujours pas de glitch.
**Conclusion :** Racine = le glitch actuel corrompt uniquement le DÉTAIL de texture existant ; un Graphic plat (uni + teinté sombre) ne peut pas être corrompu par ce shader, quelle que soit la parenté.

### Deduction 2 : Deux mécanismes distincts, aucun lié à la parenté
**Based on :** Findings 1, 2.
**Reasoning :** Texte de rôle = exclu TMP (Finding 1). Fond de rôle = couvert mais plat (Deduction 1). Les deux sont sous FrontCanvas.
**Conclusion :** L'hypothèse "parenté" est réfutée ; la cause est double (exclusion TMP + shader inopérant sur surface unie).

## Hypothesized Paths

### Hypothesis 1 : Parenté / le bandeau échappe au swap
**Status :** Refuted
**Theory :** Le bandeau serait hors de la hiérarchie couverte par UIGlitchGroup.
**Would refute :** l'edit shader modifie visuellement le bandeau.
**Resolution :** `_TintBypass` (df38758a) a viré le fond au blanc → le matériau glitch lui est bien appliqué. Réfuté par Finding 2.

## Source Code Trace

| Element | Detail |
| --- | --- |
| Origine 1 (texte) | `Assets/Scripts/UI/UIGlitchGroup.cs:84` — skip `TMP_Text` |
| Origine 2 (bandeau) | `Assets/Shaders/UI_GlitchVideo.shader:148-162` (distorsion texture-space) + `:177` (`col *= IN.color`) |
| Trigger | `CardHackGlitch.Refresh` → `UIGlitchGroup.Apply()` sur carte piratée (client Robot) |
| Condition | Graphic = TMP (texte) OU Graphic = sprite uni + teinte sombre (bandeau) |
| Related files | `CardVisualComponents.cs` (cardRoleText/Holder), `Card.prefab` |

## Conclusion

**Confidence : High.** Cause racine Confirmée et déterministe, indépendante du runtime.

Le glitch actuel = **distorsion du détail de texture existant**. Il ne peut donc PAS corrompre :
1. le **texte de rôle** — un TMP, explicitement sauté par UIGlitchGroup (par conception, shader SDF) ;
2. le **fond du bandeau** — un sprite UNI (Square) à teinte NOIRE : la distorsion texture-space ne produit rien et `col *= IN.color` l'écrase en noir.

« Parenté » réfuté : le bandeau reçoit bien le matériau (prouvé par l'edit reverté).

## Recommended Next Steps

### Fix direction (par mécanisme)

- **Bandes/surfaces unies** → ajouter au shader un terme de **static GÉNÉRATIF en espace écran** (barres RGB + neige + scanlines) composé PAR-DESSUS, piloté par l'intensité, **additif/post-teinte** (donc non tué par une teinte noire, et indépendant du détail de `_MainTex`). Répare le fond du bandeau (et toute Image plate), sans toucher au portrait déjà correct. Ne règle PAS le texte.
- **Texte de rôle + rendu "carte entière" fidèle** → **rendu de la FrontCanvas dans une RenderTexture**, affichée via un `RawImage` portant le matériau glitch. Le glitch opère alors sur des pixels COMPOSITÉS (qui ont du détail) → tout glitche uniformément, **texte TMP inclus**. Plus lourd, mais seul moyen de corrompre le texte. Précédent projet : InfoTable RT→RawImage.

Reco : si l'exigence est « toute la carte, texte compris » → **option RT**. Si « juste que les bandeaux ne restent pas nets » → **static génératif** (peu coûteux, 80%).

### Diagnostic
Aucun requis — racine Confirmée statiquement.

## Reproduction Plan
1. Carte révélée (rôle visible) avec fond de bandeau `RoleNameHolder` (sprite Square, color noire).
2. Appliquer UIGlitchGroup → le bandeau reste noir plat ; le portrait se déchire.
3. Preuve du swap : forcer `_TintBypass=1` → bandeau devient blanc plat (pas de glitch) — confirme couverture + surface unie.

## Side Findings
- Le "pseudo TMP glitche" perçu par le user est en réalité son **holder** Image (sprite texturé, color blanche) — les glyphes TMP restent nets (cohérent avec Finding 1).

## Follow-up: 2026-07-13

### Updated Conclusion — RESOLVED (bandeau)
Racine confirmée et précisée par Poyo : ce n'est pas le sprite mais la **couleur de l'Image** (vertex tint). Un sprite noir marche (la neige le recouvre) ; une **couleur d'Image noire** tue l'effet car `col *= IN.color` s'applique EN DERNIER. Fix = réordonner : appliquer `IN.color.rgb` sur la partie texture d'ABORD, composer scanlines+neige PAR-DESSUS, ne garder que `col.a *= IN.color.a` à la fin.
- Fix appliqué : `Assets/Shaders/UI_GlitchVideo.shader:164-185`. Compile OK.
- Sans régression sur les Images claires (color blanche = ×1, ordre indifférent).
- Réglage : sur une surface plate à couleur noire, seule la **neige** est visible (défaut `_NoiseIntensity` 0.2 → ~12% via `*gi*0.6`). Monter `_NoiseIntensity`/`_ScanlineIntensity` sur le matériau si trop discret.
- Reste hors périmètre : le **texte de rôle TMP** (Finding 1) ne glitchera toujours pas par ce swap ; option RT si un jour le texte doit se corrompre.
### Status : Concluded
