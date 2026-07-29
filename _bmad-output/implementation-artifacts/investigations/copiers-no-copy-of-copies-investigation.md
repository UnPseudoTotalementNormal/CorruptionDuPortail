# Investigation: Les copieurs ne doivent pas copier des pouvoirs déjà copiés + Réincarnation copiée = pouvoirs temporaires

## Hand-off Brief

1. **What happened.** Réincarnation (le pouvoir de l'Incomplet) copie **tous** les pouvoirs d'un rôle **sans filtre ni marquage**, en permanents — donc (a) elle peut recopier des pouvoirs déjà copiés, et (b) une Réincarnation elle-même copiée (Ugues/Luma la volent, one-shot) accorde quand même des pouvoirs **permanents**. *Confirmé.*
2. **Where the case stands.** Mécanisme identifié ; le nœud de design est un choix de **marquage** : le flag actuel `isStolenCopy` mélange deux propriétés (« est une copie » ⇒ non-recopiables, ET « one-shot ⇒ despawn »). Réincarnation d'Incomplet doit rester permanente mais non-recopiable ; une Réincarnation copiée doit accorder du one-shot.
3. **What's needed next.** Discussion design avec Poyo sur le modèle de marquage (1 flag vs 2), la portée (Réincarnation filtre-t-elle aussi passifs ?), puis `gds-quick-dev`.

## Case Info

| Field            | Value                                                     |
| ---------------- | -------------------------------------------------------- |
| Ticket           | N/A (demande Poyo, suite de la session despawn one-shot)  |
| Date opened      | 2026-07-17                                                |
| Status           | Active — mécanisme confirmé, design à discuter            |
| System           | Unity 6000.2.6f2, branche `fix/oneshot-stolen-power-despawn` |
| Evidence sources | Code (PReincarnation, ReincarnationDecision, StolenPowerSelector, PCardsShuffling, PMarqueHurluberluges), SO Incomplet |

## Problem Statement

Poyo : « En tant que Luma je copie l'Incomplet, et ça peut copier les pouvoirs que l'Incomplet a copié. Je préférerais que les copieurs ne puissent pas copier les pouvoirs copiés. Et la Luma copie le pouvoir de l'Incomplet (= prendre les pouvoirs d'un rôle) — ce pouvoir doit maintenant créer des pouvoirs **temporaires** (1 utilisation). Discutons du fonctionnement. »

## Confirmed Findings

### Finding 1 : L'Incomplet est chosen ; son unique pouvoir (Réincarnation) est donc copiable par Ugues/Luma

**Evidence :** `Incomplet.asset` → `factionType: 1` (= `chosen`, `FactionType.cs:5-7` anomaly=0/chosen=1/marginal=2), `roleID: 9154`, un seul `powers` (guid `eeb80e99…` = Réincarnation).

**Detail :** Réincarnation est active au départ (elle ne devient passive qu'après usage via `ChangeIsPassiveRpc`, `PReincarnation.cs:69-72`). Donc `PowerCandidate.IsEligible` (chosen ∧ non-Ugues ∧ non-passif ∧ non-copie) l'accepte : **Ugues peut la voler au game-start**, **Luma peut la copier depuis une fausse carte Incomplet**.

### Finding 2 : Réincarnation copie SANS filtre, SANS marquage, en permanent

**Evidence :** `PReincarnation.cs:21-28` — `IGrantRolePowers.GrantRolePowers` fait `foreach (_rolePower in _fromRoleCharacter.role.powers) GivePowerToCharacter(owner, _rolePower)` : **aucun** `onReady`/config, **aucun** filtre. `ReincarnationDecision.cs:12-16` émet juste `GrantRolePowers` sans critère.

**Detail :** Conséquences :
- Les pouvoirs accordés par Réincarnation sont des **pouvoirs normaux** : permanents, régénérants, `isStolenCopy=false`.
- Elle copie **tout** `role.powers` du rôle ciblé — y compris passifs ET copies déjà présentes (une copie one-shot d'un autre copieur, un grant Réincarnation antérieur).

### Finding 3 : Le filtre anti-copie n'attrape que `isStolenCopy` — pas les grants de Réincarnation

**Evidence :** `StolenPowerSelector.cs:96` — `IsEligible => OwnerIsChosen && !OwnerIsUgues && !IsPassive && !IsStolenCopy`. Ugues (`PMarqueHurluberluges.cs:95`) et Luma (`PCardsShuffling.cs:153`) construisent le candidat avec `_p.isStolenCopy.Value`.

**Detail :** Comme les grants de Réincarnation ne sont **pas** `isStolenCopy`, ils **passent** ce filtre → un copieur peut les recopier. Le flag `isStolenCopy` porte deux sens fusionnés depuis le chantier précédent : « est une copie » ET « one-shot → despawn à l'usage » ([[project_ugues_marque_hurluberluges]], spec-oneshot-stolen-power-despawn).

## Deduced Conclusions

### Deduction 1 : Réincarnation est le seul copieur « non-filtré/non-marqué » — d'où les deux symptômes

**Based on :** Findings 1-3.

**Reasoning :** Ugues et Luma filtrent déjà `isStolenCopy` et marquent leurs copies. Réincarnation ne fait ni l'un ni l'autre. Donc c'est via Réincarnation (celle d'Incomplet OU une Réincarnation copiée) que (a) des pouvoirs déjà copiés se font recopier et (b) une copie produit du permanent.

**Conclusion :** Les deux règles voulues par Poyo pointent le même endroit : **le comportement de grant de Réincarnation** + **la sémantique du flag de copie**.

## Hypothesized Paths

### Hypothesis 1 : « copier les pouvoirs que l'Incomplet a copié » n'est directement atteignable aujourd'hui que via Réincarnation elle-même

**Status :** Open (à valider avec Poyo — challenge de la formulation)

**Theory :** Luma ne copie que des **fausses cartes** (rôles absents) ; une fausse Incomplet n'a jamais réincarné → son `role.powers` = juste [Réincarnation]. Ugues vole au **game-start** → l'Incomplet n'a pas encore réincarné. Donc « recopier les sous-pouvoirs déjà copiés par un vrai Incomplet » n'est pas un chemin courant ; le chemin réel = **copier Réincarnation**, puis (copie one-shot) l'utiliser → elle accorde du permanent (symptôme (b)). Le cas « Réincarnation recopie des copies » se produit surtout quand **Réincarnation cible un rôle qui porte déjà des copies** (un autre copieur), car elle ne filtre rien.

**Would confirm :** Repro : Luma copie une fausse Incomplet → obtient Réincarnation one-shot → l'utilise sur un rôle → obtient des pouvoirs permanents (pas one-shot).

**Would refute :** Un chemin où Luma/Ugues copie directement un sous-pouvoir déjà accordé par un vrai Incomplet.

## Source Code Trace

| Element       | Detail                                                        |
| ------------- | ------------------------------------------------------------ |
| Origine       | `PReincarnation.cs:21-28` (grant sans filtre ni config) + `ReincarnationDecision.cs:12-16` |
| Filtre copie  | `StolenPowerSelector.cs:96` (n'exclut que `isStolenCopy`)     |
| Marquage copie| `Power.ConfigureAsOneShotStolenCopy` (Ugues/Luma) vs RIEN pour Réincarnation |
| Faction gate  | `PCardsShuffling.cs:54-58` (Luma pioche chosen), Incomplet=chosen |
| Related       | `Power.isStolenCopy` (flag fusionné copie+one-shot), spec-oneshot-stolen-power-despawn |

## Conclusion

**Confidence :** High (mécanisme confirmé par lecture de code). La portée exacte du symptôme « recopier des sous-copies » reste à préciser avec Poyo (Hypothesis 1).

Prémisse **confirmée dans son esprit** : Réincarnation, seul copieur sans filtre ni marquage, permet (a) de recopier des pouvoirs déjà copiés et (b) de produire du permanent même quand elle est elle-même une copie. Le vrai point de design = **découpler « est une copie » (⇒ non-recopiable) de « one-shot » (⇒ despawn)**, puis faire filtrer/marquer Réincarnation.

## Design decisions (ratifiées Poyo, 2026-07-17)

- **Q1 — copie temporaire ≠ copie permanente.** Une Réincarnation elle-même copiée (one-shot Ugues/Luma) doit accorder des pouvoirs **one-shot** (1 usage, despawn), jamais permanents (équilibrage). La vraie Réincarnation de l'Incomplet reste **permanente** (Q3).
- **Q2 — Réincarnation skippe UNIQUEMENT les pouvoirs copiés** (passifs OU actifs). Elle continue de copier les pouvoirs normaux, y compris les passifs normaux. Seuls les pouvoirs marqués « copie » sont exclus.
- **Q3 — la vraie Réincarnation reste permanente.** Ses grants sont juste « non-recopiables », pas one-shot.

### Modèle retenu : 2 flags orthogonaux

Découpler la double sémantique actuelle de `isStolenCopy` :
- **`isStolenCopy`** (existant) = **lifetime one-shot** → despawn à l'usage (chantier précédent). Inchangé.
- **`isCopiedPower`** (NOUVEAU `NetworkVariable<bool>`) = **provenance copie** → non-recopiable. Posé sur TOUTE copie : Ugues, Luma, ET les grants de Réincarnation (permanents comme one-shot).

## Fix direction (pour `gds-quick-dev`)

1. **`Power`** : ajouter `NetworkVariable<bool> isCopiedPower`. `ConfigureAsOneShotStolenCopy` pose AUSSI `isCopiedPower=true` (une copie one-shot est une copie). Ajouter `MarkAsPermanentCopy(Power)` (server-only : `isCopiedPower=true` seul).
2. **Filtre Ugues/Luma** : `PowerCandidate.IsEligible` passe de `!IsStolenCopy` à `!IsCopiedPower` (renommer/rebrancher le champ + tests EditMode `StolenPowerSelectorTests`). Ugues (`PMarqueHurluberluges.cs:95`) et Luma (`PCardsShuffling.cs:153`) alimentent le candidat avec `_p.isCopiedPower.Value`.
3. **`PReincarnation.GrantRolePowers`** :
   - **skip** `_rolePower.isCopiedPower.Value` (règle #1, passif ou actif).
   - `_oneShot = this.isStolenCopy.Value` (la Réincarnation est-elle elle-même une copie ?).
   - `onReady` = `_oneShot ? Power.ConfigureAsOneShotStolenCopy : Power.MarkAsPermanentCopy` — passer le hook à `GivePowerToCharacter`.
4. **Tests** : cas kernel « copie exclue » ; (idéalement) un test grant Réincarnation one-shot vs permanent.

Portée : l'Incomplet reste permanent ; se compose proprement avec le despawn one-shot déjà mergé sur la branche.

## Recommended Next Steps

Design ratifié → `gds-quick-dev` (implémentation sur la même branche `fix/oneshot-stolen-power-despawn` ou une nouvelle, au choix).
