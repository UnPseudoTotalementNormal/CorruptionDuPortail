# Investigation: Les 3 rôles copieurs de pouvoirs (Incomplet / Ugues / Luma) — unification & feedback « disparition »

## Hand-off Brief

1. **What happened.** Le codebase a bien **2 mécaniques** de copie (permanente vs one-shot), pas 3 — mais la one-shot est **dupliquée à l'identique** dans Ugues et Luma, et la copie one-shot **dépensée reste affichée** dans la power bar (grisée mais visible) au lieu de disparaître. *Confirmé.*
2. **Where the case stands.** Cause racine du feedback trouvée et confirmée : le filtre « cache la copie dépensée » n'existe que dans le rebuild complet `CreatePowerBar` (PowersBar.cs:156), or dépenser une copie ne change pas le *set* de pouvoirs → aucun rebuild déclenché → l'objet persiste jusqu'au prochain rebuild réel.
3. **What's needed next.** Décision design de Poyo : « disparaît vraiment » = **despawn serveur de la copie** (aligne « temporaire = perdu » + déclenche le rebuild naturellement) OU simple rafraîchissement bar sur `powerUseLeft==0`. Puis dédup des deux `Configure*Copy` en un helper partagé.

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A (demande directe Poyo)                                                  |
| Date opened      | 2026-07-17                                                                  |
| Status           | Active — diagnostic complet, décision design en attente                    |
| System           | Unity 6000.2.6f2, branche Dev, NGO                                          |
| Evidence sources | Code source (Power/PowerBar/3 pouvoirs + kernel Domain), mémoire projet    |

## Problem Statement

Poyo : « 3 rôles copient des pouvoirs — Luma, Ugues, l'Incomplet. Je suppose qu'ils fonctionnent tous les 3 différemment alors que ça devrait pas. Il n'y a que 2 fonctionnements possibles : **permanent** (les pouvoirs copiés sont utilisables comme n'importe quel pouvoir) et **temporaire** (dès qu'on l'utilise, le pouvoir disparaît). Actuellement je crois qu'il est seulement inutilisable mais il ne disparaît pas → feedback bizarre. But : que le code soit nickel. »

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| PMarqueHurluberluges.cs (Ugues) | Available | One-shot, 3 vols au game-start |
| PCardsShuffling.cs (Luma) | Available | One-shot mais uniquement sur branche « fausse carte » d'un pouvoir de devinette |
| PReincarnation.cs (Incomplet) | Available | Copie permanente de TOUS les pouvoirs d'UN rôle |
| StolenPowerSelector.cs (Domain) | Available | Kernel pur partagé (pick N distincts) — déjà mutualisé |
| Power.cs / PowersBar.cs | Available | Marqueur `isStolenCopy` + filtre d'affichage |
| Playtest visuel « ça reste affiché » | Missing | Confirmé par lecture de code, pas observé en run (no-playtest-by-Claude) |

## Confirmed Findings

### Finding 1 : Il n'y a que 2 mécaniques, mais réparties sur 3 pouvoirs très hétérogènes

**Evidence :**
- Permanent → `PReincarnation` (Incomplet) : `IGrantRolePowers.GrantRolePowers` appelle `characterManager.GivePowerToCharacter(owner, rolePower)` **sans config** pour *chaque* pouvoir du rôle ciblé → copies pleines, régénérantes, permanentes. (`PReincarnation.cs:21-28`)
- One-shot → `PMarqueHurluberluges` (Ugues) : `ConfigureStolenCopy` met `maxPowerUse=1`, `powerUseRegenPerAwakening=0`, `powerUseLeft=1`, `isStolenCopy=true`. (`PMarqueHurluberluges.cs:115-125`)
- One-shot → `PCardsShuffling` (Luma) : `ConfigureCopy` — **byte-identique** à celui d'Ugues. (`PCardsShuffling.cs:183-193`)

**Detail :** Le modèle mental de Poyo (2 fonctionnements) est **correct**. Mais :
- **Incomplet** copie *tous* les pouvoirs d'*un* rôle, en permanence — intention distincte.
- **Ugues** copie 3 pouvoirs actifs aléatoires de la faction élue au démarrage.
- **Luma** n'est PAS un copieur au sens propre : son pouvoir est une **devinette/découverte** ; la copie n'est qu'une **branche de repli** (`GrantCopyFromFakeRole`, `PCardsShuffling.cs:146-179`) déclenchée seulement quand la carte piochée est un rôle élu **absent** (fausse carte, `_character.isFake`). Donc « 3 rôles qui copient » est imprécis — Luma copie par accident de branche.

### Finding 2 : La config one-shot est dupliquée (Ugues ≡ Luma)

**Evidence :** `PMarqueHurluberluges.cs:115-125` vs `PCardsShuffling.cs:183-193` — deux méthodes privées, corps identique. Le commentaire de Luma l'admet : « mirrors PMarqueHurluberluges.ConfigureStolenCopy ».

**Detail :** Seul le *kernel de tirage* (`StolenPowerSelector`) est mutualisé. La *transformation en one-shot* ne l'est pas → dette. C'est le vrai « ils fonctionnent différemment » ressenti : même comportement, deux implémentations.

### Finding 3 (cause racine feedback) : la copie dépensée n'est cachée qu'au rebuild complet, jamais déclenché par la dépense

**Evidence :**
- Le filtre « une copie dépensée quitte la barre » vit **uniquement** dans `CreatePowerBar` : `if (_currentPower.isStolenCopy.Value && _currentPower.powerUseLeft.Value <= 0) continue;` (`PowersBar.cs:156-159`).
- `CreatePowerBar` (rebuild total) n'est appelé que si le *set* de pouvoirs change : `RefreshCharacterPowerBar` ne rebuild que si `powersBarObjects.Count == 0` ou `!ArePowerSetsEqual(...)` (`PowersBar.cs:111-119`) ; `Update()` ne rebuild que si un objet de barre pointe un pouvoir **absent** de `role.powers` (`PowersBar.cs:90-95`).
- Dépenser une copie ne fait que `powerUseLeft.Value -= 1` (`Power.cs:265`). L'objet `Power` **reste** dans `role.powers`. Donc `ArePowerSetsEqual` reste `true` → **pas de rebuild** → le filtre 156 ne tourne pas.

**Detail :** Résultat exactement conforme à la plainte : après usage, `Update()` fait `SetInteractable(CanUse=false)` → la copie devient **grisée/inutilisable** mais l'objet **persiste** jusqu'à un vrai rebuild (prochain réveil qui change le set, changement de rôle…). Le commentaire de `Power.cs:101` (« so the owner's power bar can hide it once spent ») décrit une intention **non tenue** dans le flux réel.

## Deduced Conclusions

### Deduction 1 : « nickel » = 2 axes de nettoyage, pas 3 réécritures

**Based on :** Findings 1, 2, 3.

**Reasoning :** Le comportement runtime est déjà réduit à 2 modèles ; le désordre est (a) **duplication** de la config one-shot et (b) un **bug de rafraîchissement UI** qui rend le one-shot « inutilisable mais visible » au lieu de « disparu ».

**Conclusion :** Cible propre = un **helper one-shot partagé** (Ugues+Luma) + un mécanisme de **disparition réelle** de la copie dépensée. Incomplet reste tel quel (mécanique permanente légitime, séparée).

## Hypothesized Paths

### Hypothesis 1 : Le despawn serveur de la copie est le vrai « disparaît »

**Status :** **Confirmé — direction retenue (Poyo, 2026-07-17)**. Rouvre et remplace le choix passé « spent+hidden not despawn » de [[project_ugues_marque_hurluberluges]].

**Faisabilité (Confirmed) :** Le chemin de despawn **existe déjà** — `CharacterManager.RemovePowerFromCharacter(characterId, power)` (`CharacterManager.cs:510-528`) : retire de `role.powers` via `PowerManager.RemovePowerFromCharacterPowerListRpc` (`PowerManager.cs:144-155`) **+** `NetworkObject.Despawn()`. Réutilisable tel quel pour la copie one-shot dépensée.

**⚠️ Gotchas pour l'implémentation :**
1. **`RemovePowerFromCharacterPowerListRpc` ne déclenche PAS `onPowersUpdated`** (seul le chemin *add* le fait, `Character.cs:128-131`). La bar se rafraîchira quand même via le poll `Update()` (`PowersBar.cs:90-95`, power absente → rebuild), mais pour un refresh **immédiat/événementiel** appeler `Character.InvokeOnPowersUpdated()` (`Character.cs:134`) après la suppression.
2. **Use-after-despawn** : ne pas despawn `this` au milieu de `Power.OnUsed` — la suite (`onPowerUsed?.Invoke()`, `OnUsedClientRpc`, `Power.cs:256-260`) touche encore l'instance. Différer la suppression en fin de flux (ou hook `onPowerUsedServer` après retour).
3. Point de déclenchement : après `powerUseLeft.Value -= 1` (`Power.cs:265`), si `isStolenCopy.Value && powerUseLeft.Value <= 0` → `RemovePowerFromCharacter(ownerClientId.Value, this)` (côté serveur uniquement).

*(section originale conservée ci-dessous pour l'historique)*

**Status initial :** Open (décision design)

**Theory :** Retirer la copie de `role.powers` + `NetworkObject.Despawn` côté serveur à la dépense change le *set* → `ArePowerSetsEqual` faux → rebuild → objet disparu naturellement, et aligne « temporaire = perdu » (mémoire projet Ugues : « consume = spent+hidden not despawn » était un choix antérieur — à réviser si Poyo veut la vraie disparition).

**Would confirm :** Un playtest où la copie dépensée quitte la barre immédiatement.

**Would refute :** Le despawn casse la référence dans `role.powers`/InfoTable/late-joiner → il faudrait garder le marqueur `isStolenCopy` et se contenter d'un refresh UI (Hypothesis 2).

### Hypothesis 2 : Un simple refresh UI sur `powerUseLeft==0` suffit (moins invasif)

**Status :** Open

**Theory :** S'abonner à `powerUseLeft.OnValueChanged` (ou étendre `Update()`) pour appeler `CreatePowerBar` quand une copie visible atteint 0. Pas de despawn réseau, garde l'objet vivant (utile si l'InfoTable/journal le lit).

**Would confirm :** Playtest OK sans toucher au réseau.

**Would refute :** Poyo veut que « le pouvoir n'existe plus » sémantiquement (pas juste caché) → alors Hypothesis 1.

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Origine feedback | `PowersBar.cs:156` (filtre présent) + `PowersBar.cs:111-119` / `90-95` (rebuild non déclenché) |
| Trigger       | Dépense d'une copie one-shot → `Power.OnUsedServer` `powerUseLeft-=1` (`Power.cs:265`) → `AskForUpdateAllCharactersRpc` → `OnPowersUpdated` → `RefreshCharacterPowerBar` (pas de rebuild car set inchangé) |
| Condition     | `isStolenCopy==true && powerUseLeft==0` mais `role.powers` inchangé |
| Duplication   | `PMarqueHurluberluges.cs:115-125` ≡ `PCardsShuffling.cs:183-193` |
| Related files | `Power.cs`, `PowersBar.cs`, `StolenPowerSelector.cs`, `PReincarnation.cs`, `CharacterManager.GivePowerToCharacter` |

## Conclusion

**Confidence :** High (cause racine Confirmée par lecture de code de bout en bout ; seul le rendu visuel exact n'a pas été observé en run — cohérent avec la règle no-playtest).

Prémisse de Poyo **majoritairement confirmée**, avec une nuance : le codebase a **déjà** 2 mécaniques (pas 3), donc le vrai travail « nickel » n'est pas d'unifier 3 comportements mais de **(1) dédupliquer** la config one-shot Ugues/Luma en un helper partagé, et **(2) corriger** la disparition de la copie dépensée. Incomplet est une 3ᵉ mécanique **légitimement distincte** (copie permanente de tous les pouvoirs d'un rôle) — à ne pas fusionner. Luma est un pouvoir de **devinette** dont la copie n'est qu'une branche de repli — à garder en tête pour ne pas sur-généraliser.

## Recommended Next Steps

### Fix direction

1. **Dédup one-shot (dette morte-née).** Extraire `Configure*Copy` en un point unique — ex. méthode statique `Power.MakeOneShotStolenCopy(Power copy)` ou paramètre de `GivePowerToCharacter`. Ugues + Luma l'appellent. Supprime la divergence potentielle.
2. **Disparition réelle (feedback).** Choisir Hypothesis 1 (despawn serveur — aligne « perdu ») ou Hypothesis 2 (refresh UI sur `powerUseLeft==0` — moins invasif). Décision **design-owned** (Poyo).
3. **Optionnel — clarté.** Renommer `isStolenCopy` / le helper en « one-shot copy » neutre (Luma n'est pas un « vol »), puisqu'il couvre Ugues *et* Luma.

### Diagnostic

- Log taggé `[COPYFEEDBACK]` sur `PowersBar.RefreshCharacterPowerBar` (rebuild vs skip) + sur `ConfigureCopy`, puis dépenser une copie en playtest → confirmer que le rebuild ne se déclenche pas (Poyo, la règle interdit que je playtest).

## Reproduction Plan

1. Partie avec Ugues (ou Luma sur fausse carte élue) → obtenir une copie one-shot dans la barre.
2. Utiliser la copie.
3. **Attendu bug :** la copie reste visible, grisée, non cliquable, jusqu'au prochain réveil/changement de set.
4. **Attendu après fix :** la copie quitte la barre à l'instant de la dépense.

## Side Findings

- `PReincarnation` copie *tous* les pouvoirs d'un rôle **sans dédoublonnage** ni cap : si un jour un rôle a beaucoup de pouvoirs, la barre de l'Incomplet peut déborder (non lié au ticket). *Deduced, `PReincarnation.cs:24-27`.*
- Mémoire projet [[project_ugues_marque_hurluberluges]] indiquait « consume = spent+hidden not despawn » comme choix délibéré — le présent ticket **rouvre** ce choix. À reconfirmer avec Poyo avant de coder le despawn.
