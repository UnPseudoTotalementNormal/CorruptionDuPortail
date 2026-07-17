---
title: 'Copie one-shot dépensée → despawn réel (Ugues / Luma) + dédup config'
type: 'bugfix'
created: '2026-07-17'
status: 'done'
baseline_commit: '154c52ba'
context: ['{project-root}/_bmad-output/implementation-artifacts/investigations/power-copy-roles-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Une copie de pouvoir « temporaire » one-shot (vol d'Ugues `PMarqueHurluberluges`, repli fausse-carte de Luma `PCardsShuffling`) devient inutilisable une fois dépensée (`powerUseLeft==0`) mais **reste affichée** dans la power bar : le filtre « cache la copie dépensée » (`PowersBar.cs:156`) ne tourne qu'au rebuild complet, or dépenser une copie ne change pas le *set* de `role.powers` → aucun rebuild → objet grisé persistant. La spec Luma exigeait déjà « puis disparue de la barre » — intention jamais tenue. De plus la config one-shot est **dupliquée à l'identique** dans Ugues et Luma.

**Approach:** Quand un pouvoir marqué `isStolenCopy` atteint `powerUseLeft<=0` à l'usage, le serveur le **despawn réellement** (retire de `role.powers` + `NetworkObject.Despawn`) — « temporaire = perdu » au sens propre. Le despawn est **déféré d'une frame serveur** (pas synchrone dans `OnUsed`). Visuellement, l'objet de la power bar **rétrécit en DOTween scale→0** avant destruction (pas de disparition instantanée). Factoriser la config one-shot en un helper partagé unique.

## Boundaries & Constraints

**Always:**
- Déclenchement **serveur uniquement**, à la fin de la branche serveur de `Power.OnUsed`, mais **déféré ~1 frame** (`UniTask.NextFrame`) — JAMAIS synchrone : un pouvoir « agir-puis-RPC » (ex. Vision de l'Impossible, Manque d'Affection) appelle `OnUsed()` **puis** un 2e ServerRpc sur `this` ; despawner synchro tuerait ce 2e RPC (effet perdu). Le report laisse aussi le client capter `powerUseLeft==0` pour lancer l'animation avant destruction.
- Retrait **order-safe / tolérant au despawn** : `RemovePowerFromCharacterPowerListRpc` doit gérer une référence déjà despawnée (cas hôte : son propre Everyone-RPC est traité APRÈS le despawn synchrone) en nettoyant les entrées mortes de `role.powers` (`RemoveAll(p => !p)`), puis **notifier la bar** (`onPowersUpdated`), au lieu d'`Assert` sur une ref nulle. Sinon husk persistant chez l'hôte.
- `CharacterManager.RemovePowerFromCharacter` null-guard le character (owner déconnecté sur la même frame → no-op silencieux).
- Disparition visuelle = **DOTween `transform.DOScale(0, ~0.25s)`** sur le `PowersBarObject`, `OnComplete → Destroy` ; l'objet est détaché de `powersBarObjects` au déclenchement pour qu'un rebuild ne le tue pas instantanément.
- Condition de despawn stricte : `isStolenCopy.Value && powerUseLeft.Value <= 0`. Un pouvoir normal (Incomplet/Réincarnation, pouvoirs de base) n'est JAMAIS despawné.

**Ask First:**
- Ajout d'un test **PlayMode** réseau pour la disparition (risque de flake port-7777, cf. mémoire) — proposé mais à valider.

**Never:**
- Ne pas toucher `PReincarnation` (l'Incomplet) : sa copie permanente reste inchangée.
- Ne pas changer le comportement de Réincarnation ni des pouvoirs standards.
- Pas de `NetworkVariable` owner-write ; pas de nouveau `PowerId` / `IPowerDecision`.
- Ne pas retirer le marqueur `isStolenCopy` (sert désormais de déclencheur de despawn + filtre d'éligibilité du kernel).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Copie one-shot dépensée | `isStolenCopy && powerUseLeft==0` après `OnUsed` | Despawn déféré ~1 frame ; retirée de `role.powers` ; bar : objet rétréci en DOTween scale→0 puis détruit | Ref despawnée dans le RPC → `RemoveAll(p=>!p)` + notif (pas d'assert) |
| Copie « agir-puis-RPC » (Vision, Manque d'Affection) | copie volée dont l'usage fait `OnUsed()` puis un 2e ServerRpc | Le report d'1 frame laisse passer le 2e RPC (effet exécuté) avant le despawn | N/A |
| Thief = hôte | l'hôte dépense sa copie | Husk nettoyé côté hôte via le RPC tolérant (RemoveAll dead) + notif | N/A |
| Copie one-shot non encore dépensée | `isStolenCopy && powerUseLeft>0` | Reste dans la barre, utilisable normalement | N/A |
| Pouvoir normal dépensé | `!isStolenCopy`, `powerUseLeft==0` | Aucun despawn (comportement actuel préservé) | N/A |
| Copie permanente (Incomplet) | pouvoir donné par Réincarnation | Jamais `isStolenCopy` → jamais despawné | N/A |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Powers/Power.cs` -- helper statique `ConfigureAsOneShotStolenCopy(Power)` ; `isStolenCopy` commentaire ; `OnUsed` déclenche un **despawn déféré** (`UniTask.NextFrame`) au lieu du despawn synchrone ; `using Cysharp.Threading.Tasks`.
- `Assets/Scripts/Characters/CharacterManager.cs` -- `RemovePowerFromCharacter` (l.510) : null-guard character.
- `Assets/Scripts/GameLogic/PowerManager.cs` -- `RemovePowerFromCharacterPowerListRpc` (l.144) : handler **tolérant au despawn** (TryGet-ou-`RemoveAll(p=>!p)`) + `InvokeOnPowersUpdated`.
- `Assets/Scripts/Characters/Character.cs` -- `InvokeOnPowersUpdated` (l.134) réutilisé.
- `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` / `PCardsShuffling.cs` -- config one-shot → helper partagé.
- `Assets/Scripts/Board/UI/PowerBar/PowersBar.cs` -- filtre d'affichage `isStolenCopy && useLeft<=0` **conservé** (ceinture-bretelles) ; `Update` détecte une copie épuisée → détache + `AnimateOutThenDestroy` ; `ArePowerSetsEqual` ignore les copies épuisées (évite le churn de rebuild).
- `Assets/Scripts/Board/UI/PowerBar/PowersBarObject.cs` -- ajoute `AnimateOutThenDestroy()` (DOTween scale→0 puis Destroy).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Characters/Powers/Power.cs` -- helper statique `ConfigureAsOneShotStolenCopy` (fait) ; commentaire `isStolenCopy` (fait) ; **REMPLACER** le despawn synchrone par un despawn **déféré** : `if (isStolenCopy.Value && powerUseLeft.Value <= 0) DespawnSpentCopyNextFrameServer().Forget();` avec `private async UniTaskVoid DespawnSpentCopyNextFrameServer()` : capture `ownerClientId.Value`, `await UniTask.NextFrame()`, guard `this == null || !IsSpawned`, `characterManager.RemovePowerFromCharacter(owner, this)`, guard `_despawnScheduled` idempotent. Ajouter `using Cysharp.Threading.Tasks`.
- [x] `Assets/Scripts/GameLogic/PowerManager.cs` -- `RemovePowerFromCharacterPowerListRpc` tolérant : si `TryGet` résout → `role.powers.Remove(p)` ; sinon `role.powers.RemoveAll(_p => !_p)` (nettoie la ref morte, cas hôte) ; puis `InvokeOnPowersUpdated()`. Remplacer les `Assert` durs par des guards.
- [x] `Assets/Scripts/Characters/CharacterManager.cs` -- `RemovePowerFromCharacter` : `if (_character == null) return;` (owner déconnecté). Ordre RPC-puis-despawn conservé (sûr grâce au handler tolérant).
- [x] `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` -- helper partagé (fait).
- [x] `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` -- helper partagé (fait).
- [x] `Assets/Scripts/Board/UI/PowerBar/PowersBar.cs` -- **restaurer** le filtre `isStolenCopy && useLeft<=0` dans `CreatePowerBar` ; `Update` : détecter une copie épuisée dans `powersBarObjects` → `powersBarObjects.RemoveAt` + `_pbo.AnimateOutThenDestroy()` ; `ArePowerSetsEqual` : exclure les copies épuisées côté `_powers`.
- [x] `Assets/Scripts/Board/UI/PowerBar/PowersBarObject.cs` -- `AnimateOutThenDestroy()` : idempotent, `SetInteractable(false)`, `transform.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack).OnComplete(() => Destroy(gameObject))` ; `OnDestroy → transform.DOKill()`.

**Acceptance Criteria:**
- Given une copie one-shot dans la barre d'Ugues/Luma, when elle est utilisée (`powerUseLeft`→0), then après ~1 frame elle est despawnée, quitte `role.powers`, et son objet de barre rétrécit en scale→0 puis disparaît.
- Given une copie volée d'un pouvoir « agir-puis-RPC » (Vision, Manque d'Affection), when elle est utilisée, then son effet (2e ServerRpc) s'exécute AVANT le despawn (report d'1 frame) — l'effet n'est pas perdu.
- Given l'hôte est le voleur, when il dépense sa copie, then l'objet disparaît aussi chez lui (pas de husk) via le RPC tolérant.
- Given un pouvoir normal non volé qui atteint 0 usage, when il est utilisé, then il n'est PAS despawné.
- Given l'Incomplet (Réincarnation), when il utilise ses pouvoirs copiés, then rien n'est despawné.
- Given Ugues et Luma configurent une copie, when donnée, then les deux passent par `Power.ConfigureAsOneShotStolenCopy` (zéro duplication).

## Spec Change Log

- **2026-07-17, itér. 2 (loopback bad_spec, revue adverse 3 couches).**
  - **Déclencheur :** Blind Hunter → use-after-despawn (le 2e ServerRpc des pouvoirs « agir-puis-RPC » droppé si `OnUsed` despawn synchro, effet perdu — Vision, Manque d'Affection confirmés). Edge Hunter → husk persistant chez l'hôte (Everyone-RPC de retrait traité après le `Despawn()` synchrone → ref non résolue → `role.powers.Remove` sauté). Edge Hunter → owner déconnecté même frame (assert). Acceptance auditor : conforme, aucune violation.
  - **Amendé (frozen, renégocié avec Poyo) :** despawn synchrone → **déféré 1 frame** (`UniTask.NextFrame`) ; retrait **tolérant au despawn** (`RemoveAll(p=>!p)` au lieu d'assert) ; null-guard character ; **NOUVEAU besoin design Poyo** : disparition visuelle animée **DOTween scale→0**, pas instantanée.
  - **Known-bad évité :** copie volée d'un pouvoir actif qui « ne fait rien » ; copie qui reste affichée chez l'hôte (le bug d'origine, mais host-only) — exactement la classe host-only que les mémoires projet signalent.
  - **KEEP :** helper partagé `Power.ConfigureAsOneShotStolenCopy` (dédup Ugues/Luma) ; réutilisation de `RemovePowerFromCharacter` ; l'Incomplet/Réincarnation intouché ; condition stricte `isStolenCopy && useLeft<=0`.
  - **Patches post-implémentation (revue itér. 2, sans re-loopback) :** Blind+Edge → l'animation pilotée par le poll `powerUseLeft==0` rate si le despawn (frame N+1) arrive dans le même network-update client que `useLeft→0` : l'objet devient fake-null avant détection → orphelin qui fait throw `IsTheSamePower` (`powerName` sur Power détruit) chaque frame. Fix : `PowersBarObject.wasStolenCopy` caché au build → `Update` anime aussi les orphelins fake-null (`!power && wasStolenCopy`), détruit instantanément tout autre power fake-null (reparent), et le `foreach` skippe les `power` null → plus de deref. `RemovePowerFromCharacterPowerListRpc` ne notifie que si la liste change (trim churn). Report d'1 frame confirmé suffisant pour Vision + Manque d'Affection (RPC d'effet même frame, ordering fiable).

## Design Notes

**Pourquoi déférer le despawn (itér. 2).** Un despawn synchrone en fin de `OnUsed` casse deux choses : (a) les pouvoirs « agir-puis-RPC » — `OnUsed()` puis un 2e ServerRpc sur `this` (Vision `PVisionOfTheImpossible.cs:119-123`, Manque d'Affection `PLackOfAffection.cs:50-51`) — dont le 2e RPC serait droppé sur un objet despawné, effet perdu ; (b) rien à voir avec le report mais lié : chez l'hôte, l'Everyone-RPC de retrait est traité APRÈS le `Despawn()` synchrone → sa `NetworkBehaviourReference` ne résout plus → husk. Le report d'1 frame (`UniTask.NextFrame`) règle (a) : les RPC entrants d'une frame sont traités avant le despawn de la frame suivante, et le client a une frame pour capter `powerUseLeft==0` et lancer le scale→0. Le handler tolérant (`RemoveAll(p=>!p)`) règle (b) pour tous les pairs, y compris l'hôte.

**Animation.** Signal client = `powerUseLeft`→0 (NetworkVariable répliquée), capté par le poll `Update` de `PowersBar`. L'objet est détaché de `powersBarObjects` (donc un rebuild ne le détruit pas) puis rétréci en DOTween indépendamment de son `Power` (qui sera despawné ~à la même frame) — le tween vit sur le transform de l'UI, pas sur le NetworkObject. Le filtre d'affichage restauré + l'exclusion dans `ArePowerSetsEqual` empêchent un rebuild de recréer la copie épuisée pendant l'animation.

## Verification

**Contexte :** ce checkout n'a pas Unity MCP dans une worktree, mais ici on est sur la branche `fix/oneshot-stolen-power-despawn` du checkout principal → MCP dispo. Compile + tests à lancer.

**Commands:**
- `mcp__UnityMCP__read_console` -- expected : 0 erreur de compilation après les modifs.
- `mcp__UnityMCP__run_tests` (EditMode) -- expected : suite verte (aucune régression ; `StolenPowerSelector` toujours OK).

**Manual checks (Poyo, playtest — règle no-playtest-by-Claude):**
- Ugues : copie one-shot utilisée → rétrécit (scale→0) puis disparaît de la barre (plus de husk grisé).
- Luma : fausse carte élue → copie one-shot utilisée une fois → animée puis disparue.
- Pouvoir « agir-puis-RPC » volé (si un rôle élu en a un) : l'effet se produit bien AVANT la disparition.
- Hôte = voleur : la copie disparaît aussi chez lui (host-only était le point faible).
- Pouvoir standard épuisé → reste géré comme avant.

## Suggested Review Order

**Cœur : quand & comment despawner (le plus à risque)**

- Déclencheur : copie épuisée → despawn DÉFÉRÉ 1 frame (pas synchrone) pour laisser passer un 2e RPC d'effet.
  [`Power.cs:270`](../../Assets/Scripts/Characters/Powers/Power.cs#L270)
- La coroutine UniTask : capture owner pré-await, guard `this==null||!IsSpawned||!IsServer` post-await, idempotent.
  [`Power.cs:278`](../../Assets/Scripts/Characters/Powers/Power.cs#L278)
- Retrait tolérant au despawn : `TryGet`-ou-`RemoveAll(p=>!p)`, notif seulement si changement — fixe le husk hôte.
  [`PowerManager.cs:144`](../../Assets/Scripts/GameLogic/PowerManager.cs#L144)
- Null-guard owner déconnecté (assert → no-op silencieux).
  [`CharacterManager.cs:515`](../../Assets/Scripts/Characters/CharacterManager.cs#L515)

**UI : disparition animée**

- `Update` anime la copie épuisée OU l'orphelin fake-null (`wasStolenCopy`), détruit tout autre power null (anti-crash).
  [`PowersBar.cs:98`](../../Assets/Scripts/Board/UI/PowerBar/PowersBar.cs#L98)
- Prédicat partagé « copie one-shot épuisée » (filtre CreatePowerBar + exclusion ArePowerSetsEqual).
  [`PowersBar.cs:129`](../../Assets/Scripts/Board/UI/PowerBar/PowersBar.cs#L129)
- Animation DOTween scale→0 puis Destroy, idempotente ; cache `wasStolenCopy` au build.
  [`PowersBarObject.cs:88`](../../Assets/Scripts/Board/UI/PowerBar/PowersBarObject.cs#L88)
- `OnDestroy` override + base (DOKill), conséquence du virtual ajouté à la base.
  [`PowerBarObject3D.cs:105`](../../Assets/Scripts/Board/UI/PowerBar/PowerBarObject3D.cs#L105)

**Dédup config one-shot (périphérie)**

- Helper partagé unique remplaçant les 2 `Configure*Copy` dupliqués.
  [`Power.cs:307`](../../Assets/Scripts/Characters/Powers/Power.cs#L307)
- Ugues + Luma le passent en `onReady`.
  [`PMarqueHurluberluges.cs:110`](../../Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs#L110) · [`PCardsShuffling.cs:168`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L168)
