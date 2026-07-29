---
title: 'Copie de pouvoir : cloner le prefab de base (état frais), éligibilité isPassive sur la base'
type: 'bugfix'
created: '2026-07-17'
status: 'done'
baseline_commit: '9a7ddd37'
context: ['{project-root}/_bmad-output/implementation-artifacts/investigations/power-copy-clones-live-state-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `CharacterManager.GivePowerToCharacter` fait `Instantiate(_power)` sur l'**instance vivante** passée (`CharacterManager.cs:491`). Unity copie les champs sérialisés dans leur **état courant** → un pouvoir muté au runtime fuit dans la copie. Concret : `Power.isPassive` (sérialisé) passe à `true` sur une Réincarnation utilisée ; la copier donne une **copie passive/inutilisable**. Latent aujourd'hui (les copieurs ne copient que des sources non-utilisées) mais réel.

**Approach:** Faire cloner le **prefab de base** au lieu de l'instance. On mémorise le prefab source sur chaque `Power` (`basePrefab`) au moment du grant, et `GivePowerToCharacter` instancie `basePrefab ?? _power`. En plus, l'**éligibilité `isPassive` des copieurs se lit sur le prefab de base** (ratifié Poyo) : on peut ainsi copier un pouvoir devenu passif au runtime (Réincarnation utilisée) et le récupérer **à neuf**. `isCopiedPower` reste lu sur le **vivant** (marqueur de provenance runtime).

## Boundaries & Constraints

**Always:**
- `GivePowerToCharacter` reste le **choke point unique** (server-only). Clone `_source = _power.basePrefab != null ? _power.basePrefab : _power` ; après `Instantiate`, pose `_newPower.basePrefab = _source`.
- Éligibilité copieur : `isPassive` lu via un helper `Power.BaseIsPassive` (`basePrefab != null ? basePrefab.isPassive : isPassive`). Ugues et Luma alimentent le candidat avec `BaseIsPassive`. Le skip-passif d'une Réincarnation copiée utilise aussi `BaseIsPassive`.
- `isCopiedPower` **reste lu sur l'instance vivante** (`_p.isCopiedPower.Value`). NE PAS le baser sur le prefab : le prefab n'est jamais une copie → une copie redeviendrait copiable (casse le comportement de PR #90).
- `basePrefab` = `[NonSerialized] public Power` (référence prefab, non répliquée). Renseignée côté serveur ; seuls les chemins serveur la lisent (les copieurs sont server-authoritative).

**Ask First:**
- Toute lecture d'un autre champ que `isPassive` sur la base (ex. baser `maxPowerUse`/compteurs d'éligibilité sur la base) → renégocier.

**Never:**
- Ne pas changer l'attribution initiale (`RoleAttributionState`, passe déjà des prefabs → `_source == _power`, comportement identique) ni `PLegacy` (passe un prefab inspector → inchangé).
- Pas de reset par-pouvoir manuel : le clone-from-prefab remet tout à l'état de base d'un coup.
- Pas de `NetworkVariable` owner-write ; pas de nouveau `PowerId`/`IPowerDecision`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Copie d'un pouvoir muté | source vivante (`isPassive=true`, compteurs entamés, listes remplies) | Copie clonée du **prefab** → état de base (active, compteurs pleins, listes vides) | `basePrefab` null → fallback sur `_power` |
| Attribution initiale | `_power` = prefab (`basePrefab==null`) | `_source == _power` → clone du prefab, comme avant | N/A |
| Éligibilité d'un pouvoir base-actif devenu passif | Réincarnation utilisée (`isPassive` vivant=true, base=false) | **Copiable** (filtre lit `BaseIsPassive`=false) → copie fraîche active | N/A |
| Éligibilité d'un vrai passif | pouvoir dont le **prefab** est passif | Non copiable (`BaseIsPassive`=true) | N/A |
| Copie d'une copie | instance `isCopiedPower.Value==true` | Toujours exclue (lu sur le vivant) | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Powers/Power.cs` -- ajouter `[NonSerialized] public Power basePrefab;` + `public bool BaseIsPassive => basePrefab != null ? basePrefab.isPassive : isPassive;`.
- `Assets/Scripts/Characters/CharacterManager.cs` (`GivePowerToCharacter`, l.485-508) -- cloner `basePrefab ?? _power` ; poser `_newPower.basePrefab = _source` après `Instantiate`.
- `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` (l.95) -- candidat avec `_p.BaseIsPassive` (au lieu de `_p.isPassive`).
- `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` (l.152) -- `_isPassive = _p == null || _p.BaseIsPassive`.
- `Assets/Scripts/Characters/Powers/PReincarnation.cs` (l.45) -- skip-passif de la Réincarnation copiée via `_rolePower.BaseIsPassive`.
- `Assets/Scripts/Characters/Powers/PLegacy.cs` (l.53) -- vérifié : passe un prefab inspector → inchangé, aucune modif.
- `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs` (l.151-153) -- vérifié : passe des prefabs → inchangé, aucune modif.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Characters/Powers/Power.cs` -- `[NonSerialized] public Power basePrefab;` + `public bool BaseIsPassive => basePrefab != null ? basePrefab.isPassive : isPassive;` (commentaires : basePrefab = prefab source, server-set ; BaseIsPassive = état passif de base pour l'éligibilité copie).
- [x] `Assets/Scripts/Characters/CharacterManager.cs` -- `GivePowerToCharacter` : `Power _source = _power != null && _power.basePrefab != null ? _power.basePrefab : _power;` ; `Power _newPower = Instantiate(_source, null);` ; juste après, `_newPower.basePrefab = _source;`. Reste inchangé (idHolderServer, Spawn, reparent, onReady).
- [x] `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` -- `new PowerCandidate(_ownerIsChosen, _ownerIsUgues, _p.BaseIsPassive, _p.isCopiedPower.Value)`.
- [x] `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` -- `bool _isPassive = _p == null || _p.BaseIsPassive;` (garder `_isCopied = _p != null && _p.isCopiedPower.Value`).
- [x] `Assets/Scripts/Characters/Powers/PReincarnation.cs` -- `if (_isCopiedReincarnation && _rolePower.BaseIsPassive) continue;` (au lieu de `_rolePower.isPassive`).

**Acceptance Criteria:**
- Given un pouvoir dont l'instance vivante est mutée (passive, compteurs entamés, listes remplies), when un copieur le copie, then la copie naît à l'**état de base** (active, compteurs pleins, listes vides).
- Given une Réincarnation utilisée (passive au runtime, base active), when un copieur évalue l'éligibilité, then elle est **copiable** (lecture `BaseIsPassive`) et la copie est fraîche/active.
- Given un pouvoir dont le prefab est réellement passif, when un copieur évalue, then il est **non copiable**.
- Given une instance déjà copie (`isCopiedPower` vivant), when un copieur évalue, then elle reste **exclue** (lecture vivante).
- Given l'attribution initiale et `PLegacy` (sources prefab), when ils donnent un pouvoir, then le comportement est **identique** à avant.

## Design Notes

`GivePowerToCharacter` a 5 appelants : l'attribution (`RoleAttributionState`, prefab) et `PLegacy` (prefab inspector) passent déjà des prefabs → `_source == _power`, aucun changement. Seuls les 3 copieurs (Ugues/Luma/Réincarnation) passent des **instances vivantes** dont `basePrefab` pointe le prefab → ils clonent désormais frais. `basePrefab` est `[NonSerialized]` et posé côté serveur : suffisant car toute la logique de copie/éligibilité est server-authoritative ; les clients gardent `isPassive` vivant pour l'affichage (inchangé). `BaseIsPassive` fait un fallback `isPassive` si `basePrefab` est null (robuste hors serveur / powers legacy).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected : 0 erreur de compilation.
- `mcp__UnityMCP__run_tests` (EditMode) -- expected : suite verte (aucune régression ; kernel `StolenPowerSelector` inchangé).

**Manual checks (Poyo, playtest — règle no-playtest-by-Claude) :**
- Copier un rôle dont un pouvoir a été utilisé (Réincarnation passée passive) → la copie est **active/utilisable**, compteurs pleins.
- Copier un pouvoir à `powerUseLeft` entamé → la copie repart à `maxPowerUse`.
- Attribution normale + Héritage (Legacy) → inchangés.

## Spec Change Log

- **2026-07-17, revue itér. 1 (aucun changement code).** 3 relecteurs adverses : change jugé sain, aucun défaut bloquant. `isCopiedPower` confirmé lu sur le vivant partout (PR #90 non régressé). Findings LOW/nit : (1) le fallback `basePrefab==null → live` (clone + `BaseIsPassive`) est **délibéré** et **load-bearing** — `EntrapmentPowerTests` ajoute un power sans basePrefab et compte dessus ; le durcir en assert casserait le harnais → **on garde le fallback**. (2) fallback client-side `BaseIsPassive` sûr car l'éligibilité est server-only. (3) guard `_power != null` inoffensif. KEEP : cloner `basePrefab ?? _power`, `isCopiedPower` vivant.

## Suggested Review Order

**Cœur : la source du clone**

- Cloner le PREFAB de base (`basePrefab ?? _power`) au lieu de l'instance vivante + stamp du prefab sur la copie.
  [`CharacterManager.cs:494`](../../Assets/Scripts/Characters/CharacterManager.cs#L494)
- Champ `basePrefab` (server-set, `[NonSerialized]`) + helper `BaseIsPassive` (fallback live intentionnel).
  [`Power.cs:47`](../../Assets/Scripts/Characters/Powers/Power.cs#L47)

**Éligibilité isPassive sur la base (isCopiedPower reste vivant)**

- Ugues : candidat via `BaseIsPassive` + `isCopiedPower.Value` (vivant).
  [`PMarqueHurluberluges.cs:95`](../../Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs#L95)
- Luma : idem.
  [`PCardsShuffling.cs:152`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L152)
- Réincarnation : skip-passif d'une copie via `BaseIsPassive`.
  [`PReincarnation.cs:45`](../../Assets/Scripts/Characters/Powers/PReincarnation.cs#L45)
