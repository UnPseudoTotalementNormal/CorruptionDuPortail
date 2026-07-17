---
title: 'Les copieurs ne recopient pas une copie + Réincarnation copiée = pouvoirs one-shot'
type: 'feature'
created: '2026-07-17'
status: 'done'
baseline_commit: '6fa16033'
context: ['{project-root}/_bmad-output/implementation-artifacts/investigations/copiers-no-copy-of-copies-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Réincarnation (pouvoir de l'Incomplet, chosen) copie **tout** un rôle **sans filtre ni marquage**, en permanent (`PReincarnation.cs:21-28`). Donc (a) un copieur peut recopier des pouvoirs déjà copiés — le filtre anti-copie n'exclut que `isStolenCopy`, or les grants de Réincarnation ne le portent pas — et (b) une Réincarnation elle-même volée (Ugues/Luma, one-shot) accorde quand même des pouvoirs **permanents** → déséquilibré (une copie temporaire ne doit jamais produire du permanent).

**Approach:** Découpler la double sémantique de `isStolenCopy`. Nouveau flag **`Power.isCopiedPower`** = « ce pouvoir est une copie » (⇒ non-recopiable), posé sur TOUTE copie (Ugues, Luma, grants de Réincarnation permanents comme one-shot). `isStolenCopy` garde son seul sens « one-shot → despawn ». Réincarnation skippe les pouvoirs copiés et accorde du **one-shot si elle est elle-même une copie**, sinon du permanent.

## Boundaries & Constraints

**Always:**
- `isCopiedPower` posé côté **serveur** sur chaque copie donnée. `ConfigureAsOneShotStolenCopy` (Ugues/Luma) le pose EN PLUS de la config one-shot ; nouveau `Power.MarkAsPermanentCopy` le pose seul (grant permanent de l'Incomplet).
- Filtre d'éligibilité copieur : `PowerCandidate.IsEligible` passe de `!IsStolenCopy` à `!IsCopiedPower`. Ugues et Luma alimentent le candidat avec `_p.isCopiedPower.Value`.
- Réincarnation `GrantRolePowers` : **skippe** tout `_rolePower.isCopiedPower.Value` (passif OU actif — seulement les copies) ; `onReady = this.isStolenCopy.Value ? Power.ConfigureAsOneShotStolenCopy : Power.MarkAsPermanentCopy`.
- L'Incomplet **réel** (Réincarnation non copiée) reste **permanent** : ses grants n'ont que `isCopiedPower`, pas le despawn one-shot.
- **Réincarnation copiée (one-shot) skippe aussi les PASSIFS** (option A, Poyo 2026-07-17) : un passif n'est jamais « dépensé » → une config one-shot ne le despawnerait jamais → il deviendrait permanent (interdit par Q1). Le vrai Incomplet, lui, donne les passifs normalement. Choix design tweakable plus tard.

**Ask First:**
- (résolu) Passifs d'une Réincarnation copiée → option A (skip). Toute autre extension du skip côté Incomplet réel (skip passifs normaux) → renégocier.

**Never:**
- Ne pas retirer/renommer `isStolenCopy` (il pilote le despawn one-shot déjà mergé sur la branche).
- Ne pas rendre les grants de l'Incomplet réel one-shot.
- Pas de `NetworkVariable` owner-write ; pas de nouveau `PowerId`/`IPowerDecision` (le grant reste engine-local).
- Ne pas toucher le flux de devinette de Luma ni la sélection de rôle de Réincarnation.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Ugues/Luma face à une copie | pouvoir `isCopiedPower==true` dans le pool | Non éligible (filtre `!IsCopiedPower`) — jamais recopié | N/A |
| Incomplet réel réincarne | Réincarnation `isStolenCopy==false` | Grants `isCopiedPower=true`, permanents, régénérants | skip les `isCopiedPower` de la cible |
| Réincarnation copiée (Ugues/Luma) utilisée | Réincarnation `isStolenCopy==true` | Grants one-shot (`ConfigureAsOneShotStolenCopy` → despawn à l'usage) | idem skip copies |
| Réincarnation cible un rôle portant des copies | cible `role.powers` contient des `isCopiedPower` | Ces pouvoirs sont **sautés**, le reste (dont passifs normaux) copié | N/A |
| Réincarnation **copiée** cible un rôle avec passifs | `isStolenCopy==true`, cible a des passifs | Les passifs sont **sautés** (option A) ; seuls les actifs non-copiés → grants one-shot | évite un passif « one-shot » qui ne despawn jamais |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Powers/Power.cs` -- ajouter `NetworkVariable<bool> isCopiedPower` ; `ConfigureAsOneShotStolenCopy` pose aussi `isCopiedPower=true` ; ajouter `public static void MarkAsPermanentCopy(Power)` (server-only, `isCopiedPower=true` seul).
- `Assets/Scripts/Domain/StolenPowerSelector.cs` -- `PowerCandidate` : renommer `IsStolenCopy`→`IsCopiedPower` (champ + param ctor) ; `IsEligible` = `OwnerIsChosen && !OwnerIsUgues && !IsPassive && !IsCopiedPower`.
- `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` (l.95) -- candidat avec `_p.isCopiedPower.Value`.
- `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` (l.152-154) -- candidat avec `_p.isCopiedPower.Value`.
- `Assets/Scripts/Characters/Powers/PReincarnation.cs` (`GrantRolePowers`, l.21-28) -- skip `isCopiedPower`, `onReady` selon `isStolenCopy` ; `using System` (Action).
- `Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs` -- renommer helper `AlreadyStolen`→`AlreadyCopied` + commentaire (ctor positionnel ⇒ compile-safe).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Characters/Powers/Power.cs` -- (1) `public NetworkVariable<bool> isCopiedPower = new();` (commentaire : provenance copie, non-recopiable ; distinct de `isStolenCopy` = one-shot/despawn). (2) dans `ConfigureAsOneShotStolenCopy`, ajouter `_copy.isCopiedPower.Value = true;`. (3) `public static void MarkAsPermanentCopy(Power _copy)` : guard `_copy == null || !_copy.IsServer`, `_copy.isCopiedPower.Value = true;`.
- [x] `Assets/Scripts/Domain/StolenPowerSelector.cs` -- renommer `PowerCandidate.IsStolenCopy`→`IsCopiedPower` (champ readonly + param ctor `isCopiedPower`) ; `IsEligible` utilise `!IsCopiedPower`.
- [x] `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` -- `new PowerCandidate(_ownerIsChosen, _ownerIsUgues, _p.isPassive, _p.isCopiedPower.Value)`.
- [x] `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` -- `bool _isCopied = _p != null && _p.isCopiedPower.Value;` → `new PowerCandidate(true, false, _isPassive, _isCopied)`.
- [x] `Assets/Scripts/Characters/Powers/PReincarnation.cs` -- `GrantRolePowers` : `bool _oneShot = isStolenCopy.Value; Action<Power> _onReady = _oneShot ? Power.ConfigureAsOneShotStolenCopy : Power.MarkAsPermanentCopy;` puis `foreach` : `if (_rolePower == null || _rolePower.isCopiedPower.Value) continue; GivePowerToCharacter((ulong)_ownerSlot, _rolePower, _onReady);`. Ajouter `using System;`.
- [x] `Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs` -- renommer le helper `AlreadyStolen()`→`AlreadyCopied()` + MAJ commentaire « isStolenCopy »→« isCopiedPower » (clarté ; le 4e param positionnel = maintenant la copie).

**Acceptance Criteria:**
- Given un pouvoir marqué `isCopiedPower`, when Ugues (game-start) ou Luma (fausse carte) construit son pool, then ce pouvoir est exclu (jamais recopié).
- Given l'Incomplet réel utilise Réincarnation, when il copie un rôle, then il obtient des pouvoirs permanents marqués `isCopiedPower`, et les pouvoirs déjà copiés du rôle cible sont sautés.
- Given une Réincarnation copiée (Ugues/Luma) est utilisée, when elle copie un rôle, then les pouvoirs accordés sont **one-shot** (despawn à l'usage), jamais permanents.
- Given un rôle cible contient un mix (normaux + copies), when Réincarnation copie, then seuls les non-copiés sont accordés (passifs normaux inclus).

## Spec Change Log

- **2026-07-17, revue itér. 1 (patch, pas de loopback).** Edge Hunter + Blind Hunter → une Réincarnation **copiée** (one-shot) réincarnant dans un rôle à passifs accordait ces passifs avec `maxPowerUse=1`, mais un passif ne passe jamais par `OnUsed` → jamais despawn → **permanent** → viole Q1 (temporaire ≠ permanent). **Fix (Poyo, option A) :** dans `GrantRolePowers`, quand la Réincarnation est elle-même une copie, **skip les passifs**. Le vrai Incomplet garde ses passifs. Acceptance auditor : conforme, aucune autre violation. KEEP : 2 flags orthogonaux, filtre `!IsCopiedPower`.

## Design Notes

`isStolenCopy` (lifetime one-shot → despawn, chantier précédent) et `isCopiedPower` (provenance → non-recopiable) sont **orthogonaux**. Table de vérité : copie Ugues/Luma = (copied oui, one-shot oui) ; grant Incomplet réel = (copied oui, one-shot NON → permanent) ; grant d'une Réincarnation copiée = (copied oui, one-shot oui). Le filtre copieur bascule sur `isCopiedPower` car c'est le concept juste (« est-ce une copie ? »), qui couvre désormais aussi les grants de Réincarnation — que `isStolenCopy` seul ratait. `MarkAsPermanentCopy` et `ConfigureAsOneShotStolenCopy` sont deux `Action<Power>` passées en `onReady` à `GivePowerToCharacter` (même pattern que le reste).

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- expected : 0 erreur de compilation.
- `mcp__UnityMCP__run_tests` (EditMode, filtre `StolenPowerSelector`) -- expected : verts (rename compile-safe, exclusion 4e param inchangée sémantiquement).

**Manual checks (Poyo, playtest — règle no-playtest-by-Claude):**
- Ugues/Luma volent Réincarnation → l'utilisent → pouvoirs accordés utilisables **une seule fois** puis disparus (pas permanents).
- Incomplet réel réincarne → pouvoirs permanents comme avant.
- Réincarnation sur un rôle portant une copie one-shot → la copie n'est pas reprise.
- Réincarnation **copiée** sur un rôle à passifs → les passifs ne sont pas donnés (option A).

## Suggested Review Order

**Cœur : sémantique du grant Réincarnation (le plus à risque)**

- Choix one-shot vs permanent selon `this.isStolenCopy` + skip des copies + skip passifs si copie (option A).
  [`PReincarnation.cs:29`](../../Assets/Scripts/Characters/Powers/PReincarnation.cs#L29)

**Les 2 flags orthogonaux**

- Nouveau marqueur provenance `isCopiedPower` (distinct de `isStolenCopy` = lifetime one-shot).
  [`Power.cs:110`](../../Assets/Scripts/Characters/Powers/Power.cs#L110)
- `ConfigureAsOneShotStolenCopy` pose AUSSI `isCopiedPower` (une copie one-shot est une copie).
  [`Power.cs:318`](../../Assets/Scripts/Characters/Powers/Power.cs#L318)
- `MarkAsPermanentCopy` : copie permanente non-recopiable (grants de l'Incomplet réel).
  [`Power.cs:328`](../../Assets/Scripts/Characters/Powers/Power.cs#L328)

**Filtre copieur bascule sur la provenance**

- `PowerCandidate.IsEligible` = `… && !IsCopiedPower` (couvre désormais les grants de Réincarnation).
  [`StolenPowerSelector.cs:96`](../../Assets/Scripts/Domain/StolenPowerSelector.cs#L96)
- Ugues + Luma alimentent le candidat via `isCopiedPower`.
  [`PMarqueHurluberluges.cs:95`](../../Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs#L95) · [`PCardsShuffling.cs:153`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L153)

**Tests (périphérie)**

- Rename `AlreadyStolen`→`AlreadyCopied` (ctor positionnel, compile-safe).
  [`StolenPowerSelectorTests.cs:81`](../../Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs#L81)
