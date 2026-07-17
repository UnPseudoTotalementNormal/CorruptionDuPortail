# Investigation: La copie de pouvoir clone l'instance VIVANTE (état runtime) au lieu du prefab de base

## Hand-off Brief

1. **What happened.** `CharacterManager.GivePowerToCharacter` fait `Instantiate(_power)` sur l'**instance vivante** du pouvoir source ; Unity copie les champs sérialisés dans leur **état courant** — donc un pouvoir déjà muté au runtime (ex. Réincarnation devenue `isPassive=true` après usage) produit une copie **elle aussi mutée** (passive → inutilisable). *Confirmé.*
2. **Where the case stands.** Le prefab de base existe (`RoleDataObject.powers` = prefabs, source de l'attribution initiale). Le fix = faire cloner le **prefab de base** au lieu de l'instance vivante. Portée : 3 copieurs (Ugues/Luma/Réincarnation). Latent aujourd'hui car ils ne copient que des sources non-utilisées.
3. **What's needed next.** Décider la route (champ `basePrefab` sur `Power` vs lookup RoleDatabase) + la nuance « éligibilité lue sur l'état vivant vs base », puis `gds-quick-dev`.

## Case Info

| Field            | Value                                                          |
| ---------------- | ------------------------------------------------------------- |
| Ticket           | N/A (question Poyo, suite du chantier copieurs PR #90)         |
| Date opened      | 2026-07-17                                                     |
| Status           | Active — mécanisme confirmé, fix à cadrer                      |
| System           | Unity 6000.2.6f2, branche `fix/oneshot-stolen-power-despawn`   |
| Evidence sources | Code (CharacterManager, Power, PReincarnation, RoleDataObject, RoleAttributionState, Role) |

## Problem Statement

Poyo : « Un rôle qui copie temporairement le rôle d'une personne n'importe quel tour. Au tour 3, l'Incomplet a déjà réincarné ; ce rôle copie l'Incomplet. Sa Réincarnation, après usage, se change pour ne plus être utilisable. Je voudrais que le pouvoir copié **revienne à l'état de base** (utilisable), pour n'importe quel pouvoir. Est-ce déjà le cas, ou ça crée une copie parfaite qui traîne les états/listes en cours qui ne devraient pas être répliqués ? »

## Confirmed Findings

### Finding 1 : La copie clone l'instance VIVANTE, pas le prefab

**Evidence :** `CharacterManager.cs:491` — `Power _newPower = Instantiate(_power, null);` où `_power` est l'objet passé par l'appelant.

**Detail :** Les 3 copieurs passent des **instances vivantes** issues de `role.powers` (runtime) :
- Réincarnation : `foreach (_rolePower in _fromRoleCharacter.role.powers) GivePowerToCharacter(owner, _rolePower)` (`PReincarnation.cs`).
- Ugues : les `Power` des chars vivants (`PMarqueHurluberluges.cs`).
- Luma : les `Power` de la fausse carte (`PCardsShuffling.cs`).

`Object.Instantiate` copie les **champs sérialisés** → l'état courant est cloné.

### Finding 2 : `isPassive` est un champ sérialisé muté au runtime → il fuit dans la copie

**Evidence :** `Power.cs:42` — `public bool isPassive;` (sérialisé). `PReincarnation.ChangeIsPassiveRpc` (`PReincarnation.cs:69-72`) fait `isPassive = _isPassive` (mis à `true` après usage, sur tous les clients).

**Detail :** Cloner une Réincarnation déjà utilisée → `copy.isPassive = true` → **copie passive/inutilisable**. C'est exactement le symptôme décrit par Poyo. Idem pour tout autre champ sérialisé muté (`maxPowerUse`, `powerUseRegenPerAwakening`, …).

### Finding 3 : Le prefab de base existe et est déjà la source de l'attribution initiale

**Evidence :** `RoleDataObject.cs:17` — `public List<Power> powers;` (prefabs du rôle, SO). `RoleAttributionState.cs:151-153` — `foreach (_powerDataObject in _randomRole.powers) Command.GivePowerToCharacter(_character.ownerClientId.Value, _powerDataObject);` où `_randomRole` est un `RoleDataObject`.

**Detail :** À l'attribution, `GivePowerToCharacter` reçoit un **prefab** (état de base) → `Instantiate(prefab)` = frais. La divergence vient uniquement des copieurs qui passent des **instances vivantes**. `GivePowerToCharacter` est le **choke point unique** (4 appelants : l'attribution + les 3 copieurs).

## Deduced Conclusions

### Deduction 1 : « État de base » = cloner le prefab, pas l'instance

**Based on :** Findings 1-3.

**Reasoning :** L'attribution initiale produit déjà des pouvoirs frais parce qu'elle clone des prefabs. Les copieurs produisent des pouvoirs mutés parce qu'ils clonent des instances vivantes. Même méthode, source différente.

**Conclusion :** Faire cloner le **prefab de base** aux copieurs remet automatiquement `isPassive`, compteurs, listes et tout champ à l'état de base, **pour n'importe quel pouvoir**, sans reset par-pouvoir.

## Hypothesized Paths

### Hypothesis 1 : Les listes/NetworkVariable runtime fuient aussi

**Status :** Open (confirmation runtime requise — pas de playtest par Claude)

**Theory :** Des états runtime comme `PCardsShuffling.discoveredClientIds` (`NetworkList`) ou `powerUseLeft` (`NetworkVariable`) pourraient se traîner dans la copie. `Instantiate` + `Spawn` **ré-initialise généralement** les NetworkVariable/NetworkList à leur défaut, donc ce n'est peut-être PAS un leak — mais non vérifié en run.

**Would confirm :** Log taggé sur une copie fraîchement donnée : lire `discoveredClientIds.Count` / `powerUseLeft` juste après le grant.

**Would refute :** Copie avec listes vides + compteurs par défaut.

**Résolution anticipée :** Moot sous le fix (cloner le prefab garantit l'état de base quelle que soit la réponse).

### Hypothesis 2 : L'éligibilité devrait se lire sur l'état de BASE, pas l'état vivant

**Status :** **Confirmé — ratifié Poyo 2026-07-17.** `isPassive` (éligibilité structurelle) se lit sur le **prefab de base** → un pouvoir base-actif devenu passif au runtime (Réincarnation utilisée) reste copiable et revient à neuf. **Nuance conservée :** `isCopiedPower` reste lu sur le **vivant** (marqueur de provenance runtime ; le prefab de base n'est jamais une copie → le lire sur la base rendrait toute copie re-copiable et casserait PR #90).

**Theory :** Le filtre copieur lit l'état **vivant** : une Réincarnation utilisée est `isPassive=true` → exclue par `!IsPassive`. Or Poyo veut pouvoir la copier (et obtenir une fraîche active). Donc « est-ce un passif ? » devrait se juger sur le **prefab de base**, pas l'instance mutée. `isCopiedPower`, lui, DOIT rester lu sur le vivant (marqueur runtime).

**Would confirm/refute :** Décision de Poyo sur le comportement voulu (copier un pouvoir actuellement « épuisé/passif » → autorisé et remis à neuf ?).

## Source Code Trace

| Element       | Detail                                                       |
| ------------- | ----------------------------------------------------------- |
| Origine       | `CharacterManager.cs:491` (`Instantiate(_power)` sur instance vivante) |
| Champ fuité   | `Power.cs:42` `isPassive` (sérialisé) ; muté par `PReincarnation.cs:69-72` |
| Base dispo    | `RoleDataObject.cs:17` (prefabs) ; source init `RoleAttributionState.cs:151-153` |
| Choke point   | `GivePowerToCharacter` (4 appelants : attribution + Ugues/Luma/Réincarnation) |
| Related       | `Role.powers` (runtime, vivant) vs `RoleDataObject.powers` (prefabs) |

## Conclusion

**Confidence :** High sur le mécanisme (`isPassive` sérialisé cloné = confirmé). Medium sur l'ampleur des fuites runtime (Hypothesis 1, à confirmer en run mais rendu moot par le fix).

Réponse à Poyo : **NON, ce n'est pas frais** — `GivePowerToCharacter` clone l'instance **vivante**, traînant l'état runtime sérialisé (au minimum `isPassive`, d'où la Réincarnation copiée passive). Le comportement voulu (retour à l'état de base) **n'est pas** le comportement actuel ; il est masqué aujourd'hui car les copieurs ne copient que des sources non-utilisées.

## Recommended Next Steps

### Fix direction

1. **Cloner le prefab de base** dans `GivePowerToCharacter` plutôt que l'instance passée. Route recommandée : champ `[NonSerialized] Power basePrefab` sur `Power`, posé côté serveur au grant (`_new.basePrefab = _power.basePrefab != null ? _power.basePrefab : _power`) et cloner `Instantiate(_power.basePrefab ?? _power)`. L'attribution (prefab, basePrefab null) reste identique ; les copieurs (instance vivante → basePrefab = prefab) clonent désormais **frais**. Alternative : lookup roleID→`RoleDataObject`→prefab via une RoleDatabase (plus de plomberie).
2. **Trancher Hypothesis 2** (éligibilité sur base vs vivant) : si Poyo veut copier un pouvoir « épuisé/passif au runtime » et le remettre à neuf, le filtre `!IsPassive` doit se lire sur le prefab de base (`isCopiedPower` reste lu sur le vivant).

### Diagnostic (si besoin de lever Hypothesis 1)

- Log taggé `[COPYSTATE]` sur une copie juste après grant : `isPassive`, `powerUseLeft`, `discoveredClientIds.Count` (Poyo, playtest).

## Reproduction Plan

1. Rôle copieur runtime (ou Réincarnation volée) copie un rôle dont un pouvoir a été **utilisé** (Réincarnation passée passive, ou un pouvoir à `powerUseLeft` entamé).
2. **Attendu bug :** la copie naît passive/entamée (état vivant), pas à l'état de base.
3. **Attendu après fix :** la copie naît à l'état de base (active, compteurs pleins, listes vides).
