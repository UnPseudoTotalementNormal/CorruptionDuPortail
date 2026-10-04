---
title: 'Luma la tricheuse — copie de pouvoir sur carte élu absente'
type: 'feature'
created: '2026-07-15'
status: 'done'
baseline_commit: '114f52b3'
context: ['{project-root}/_bmad-output/project-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Le pouvoir « Mélange des cartes » de Luma (`PCardsShuffling`) se termine en cul-de-sac quand elle pioche une fausse carte : un simple message « c'était une fausse carte » puis consommation. Le design a été remanié (tâche Discord « Remodifier Luma la tricheuse ») : piocher un rôle **élu absent de la partie** doit désormais récompenser Luma.

**Approach:** Restreindre la pioche aux rôles élus (faction `chosen`). Si le rôle piochée est tenu par un vrai joueur → flux de devinette actuel, inchangé. Si c'est une fausse carte (`isFake` = rôle non joué) → donner à Luma une **copie one-shot d'UN pouvoir actif aléatoire** de ce rôle, en réutilisant le pattern de vol d'Ugues.

## Boundaries & Constraints

**Always:**
- Autorité serveur : le grant/spawn se fait uniquement côté serveur (`GivePowerToCharacter` assert `IsServer`).
- Réutiliser `CharacterManager.GivePowerToCharacter` + configurer la copie comme `PMarqueHurluberluges.ConfigureStolenCopy` : `isStolenCopy=true`, `maxPowerUse=1`, `powerUseRegenPerAwakening=0`, `powerUseLeft=1`.
- Tirage aléatoire via le kernel pur déjà testé `StolenPowerSelector.SelectStealable(candidates, 1, new UnityRandomProvider())`.
- Conserver la sémantique actuelle de la branche fausse carte : `discoveredClientIds.Add(clicked)` + `OnUsed()` (consomme l'usage de Mélange).
- RPC via `GetSafeRpcTarget` (déjà en place). Filtre élu = règle ajoutée au `targetValidator`.

**Ask First:**
- Tout élargissement du design figé (copier plusieurs actifs, changer la durée en one-turn/permanent, laisser Luma choisir le pouvoir) → renégocier avant de coder.

**Never:**
- Ne pas modifier le flux de devinette (branche vraie carte : `AskForGuessRoleRpc` → `GuessRoleRpc` → `CardsShufflingDecision`).
- Ne pas dé-spawn ni retirer le pouvoir du rôle source — c'est une COPIE, pas un vol de l'instance.
- Pas de nouveau `PowerId` ni de nouvelle `IPowerDecision` : le grant reste engine-local serveur (symétrique au vol d'Ugues, déjà hors Domain).
- Pas de `NetworkVariable` owner-write.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rôle élu, vrai joueur | carte non-`isFake`, `factionType==chosen` | Flux de devinette actuel (`AskForGuessRoleRpc`) inchangé | N/A |
| Rôle élu absent, ≥1 actif | `isFake`, `role.powers` contient un non-passif | Copie one-shot d'un actif aléatoire donnée à Luma + message ; `discoveredClientIds.Add` + `OnUsed()` | N/A |
| Rôle élu absent, que du passif | `isFake`, aucun actif copiable | Aucun pouvoir donné, message « aucun pouvoir actif à copier », `discoveredClientIds.Add` + `OnUsed()` | Log + no-op sur le grant |
| Rôle non-élu | `factionType != chosen` | Non sélectionnable (validator rejette dans le picker) | Picker n'accepte pas la carte |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` -- adapter du pouvoir ; ajouter la règle validator élu, réécrire la branche `isFake`, ajouter `ConfigureCopy`.
- `Assets/Scripts/Characters/CharacterManager.cs` (`GivePowerToCharacter`, l.485) -- réutilisé tel quel (spawn + reparent + `onReady`).
- `Assets/Scripts/Domain/StolenPowerSelector.cs` (`SelectStealable` / `PowerCandidate`) -- kernel pur réutilisé pour le tirage d'un actif.
- `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs` (`ConfigureStolenCopy`, l.115) -- modèle exact de config one-shot à répliquer.
- `Assets/Scripts/Characters/Role.cs` (`factionType`, `powers`) -- source des candidats + filtre faction.
- `Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs` -- ajouter le test du tirage Luma (pickCount=1).

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Characters/Powers/PCardsShuffling.cs` --
  (1) `OnNetworkSpawn` : ajouter une règle `targetValidator.AddRule(ctx => characterManager.GetCharacter(ctx.targetId).role.factionType == FactionType.chosen)` (garder les règles existantes).
  (2) Branche `isFake` de `OnCharacterBarObjectClickedRpc` : construire une `List<PowerCandidate>` parallèle à `_character.role.powers` avec `new PowerCandidate(ownerIsChosen:true, ownerIsUgues:false, isPassive:_p.isPassive, isStolenCopy:_p.isStolenCopy.Value)` ; `SelectStealable(candidates, 1, new UnityRandomProvider())` ; si un index → `characterManager.GivePowerToCharacter(ownerClientId.Value, role.powers[idx], ConfigureCopy)` + message « copie obtenue » ; sinon message « aucun pouvoir actif à copier ». Conserver `discoveredClientIds.Add(clicked)` + `OnUsed()` dans les deux cas.
  (3) Ajouter `private void ConfigureCopy(Power _copy)` calqué sur `ConfigureStolenCopy` (server-only, `isStolenCopy=true`, `maxPowerUse=1`, `powerUseRegenPerAwakening=0`, `powerUseLeft=1`).
  (4) Ajouter les `[SerializeField] private string` pour les deux messages (copie obtenue / aucun actif) — chaîne éditable, pas de littéral en dur.
- [x] `Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs` -- ajouter un test « Luma copie » : sur un jeu de `PowerCandidate` (chosen, non-Ugues, mix passif/actif), `SelectStealable(..., 1, StubRandomProvider)` renvoie exactement un index d'actif ; renvoie vide quand tout est passif.

**Acceptance Criteria:**
- Given Luma pioche un rôle élu tenu par un vrai joueur, when elle valide, then le flux de devinette actuel démarre sans changement de comportement.
- Given Luma pioche une fausse carte élu ayant ≥1 pouvoir actif, when elle valide, then une copie one-shot (`maxPowerUse=1`, non régénérée, `isStolenCopy`) d'un pouvoir actif aléatoire apparaît dans sa barre, et son usage de Mélange est consommé.
- Given la fausse carte n'a que des pouvoirs passifs, when elle valide, then aucun pouvoir n'est donné, un message l'en informe, et l'usage est consommé.
- Given un rôle non-élu, when le role-picker s'affiche, then ce rôle n'est pas sélectionnable.
- Given le serveur donne la copie, when elle est reparentée, then elle appartient à Luma sans retirer le pouvoir du rôle source.

## Review Findings

### gds-code-review (2026-07-15) — post-patch, 3 couches adverses

- [x] [Review][Patch] `Debug.Log` diagnostic manquant sur la branche passif-seul [`PCardsShuffling.cs:159`] — appliqué (I/O row 3 « Log + no-op », aligné sur Ugues).
- [x] [Review][Defer] Copie orpheline si le reparent de `GivePowerToCharacter` échoue [`CharacterManager.cs:503`] — déféré, pré-existant, partagé avec Ugues (déjà dans `deferred-work.md`).
- Dismiss : consommation de l'usage sur fausse carte sans actif copiable = comportement spécifié (I/O row 3) + décision design confirmée.
- Blind Hunter : aucun défaut. Acceptance Auditor : conforme (3 décisions design, 5 ACs, 4 I/O rows, patterns critiques, tests requis présents).

## Spec Change Log

- **2026-07-15, revue itér. 1 (patches, pas de loopback).** 3 relecteurs adverses (blind / edge-case / acceptance).
  - **Patch — autorité serveur :** `OnCharacterBarObjectClickedRpc` re-valide la cible côté serveur (`CheckIsTargetValid`) avant d'accorder la copie. La branche fake, jadis inoffensive (message), accorde maintenant un pouvoir ; sans re-validation, un client trafiqué pouvait copier depuis un fake non-élu ou déjà découvert (copies illimitées) ou passer un rôle null (NRE). Ferme aussi le null-deref (rôle null rejeté par la règle faction) et la race RPC-double (2e rejetée par `!discoveredClientIds.Contains`).
  - **Patch — mineur :** règle validator faction utilise `GetCharacter(id, false)` pour éviter l'event `onCharactersListUpdated` parasite pendant la validation.
  - **Rejetés :** consommation de l'usage sur rôle passif-seul (= comportement spécifié, I/O row 3) ; `maxPowerUse`/`regen` en champ simple (miroir exact du pattern Ugues sanctionné).
  - **Déféré :** copie orpheline si le reparent de `GivePowerToCharacter` échoue (pré-existant, partagé avec Ugues) → `deferred-work.md`.
  - **KEEP :** réutilisation du kernel `StolenPowerSelector` (pickCount 1) + config one-shot calquée sur `ConfigureStolenCopy` — à préserver en cas de re-dérivation.

## Design Notes

Le kernel `PowerCandidate.IsEligible == OwnerIsChosen && !OwnerIsUgues && !IsPassive && !IsStolenCopy`. En fixant `ownerIsChosen:true, ownerIsUgues:false`, il se réduit à `!isPassive && !isStolenCopy` = exactement « pouvoir actif copiable ». `pickCount=1` → un seul actif aléatoire, aucun nouveau code Domain. Le grant reste engine-local serveur (pas de Decision POCO) car il est symétrique au vol d'Ugues, déjà hors Domain (`IStolenPowerGrant`). La fausse carte porte bien un `role.powers` peuplé : `GetCharacters(false)` inclut les factices et Ugues lit déjà leurs powers.

## Verification

**Contexte worktree :** pas de Unity MCP dans ce worktree (mémoire `feedback_no_unity_mcp_in_worktree`) — compile + tests DIFFÉRÉS au checkout principal ouvert par Poyo. Édition Edit/Write seulement ici.

**Commands (à lancer par Poyo côté main) :**
- `mcp__UnityMCP__read_console` -- expected : 0 erreur de compilation après la modif.
- `mcp__UnityMCP__run_tests` (EditMode, filtre `StolenPowerSelector`) -- expected : tous verts, dont le nouveau test Luma.

**Manual checks :**
- Playtest 2 builds : Luma pioche une fausse carte élu → copie one-shot utilisable une seule fois puis disparue de la barre ; Luma pioche une vraie carte élu → flux de devinette identique à avant ; rôle non-élu → non piochable.

## Suggested Review Order

**Autorité serveur (le plus à risque)**

- Re-validation serveur avant d'accorder la copie — ferme le contournement client, le null-deref et la race RPC-double.
  [`PCardsShuffling.cs:81`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L81)

- Nouvelle règle validator : pioche restreinte aux rôles élus (faction chosen), null-guardée.
  [`PCardsShuffling.cs:54`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L54)

**Mécanique de copie (le cœur)**

- Branche fausse carte : construit les candidats, tire UN actif, accorde la copie + message.
  [`PCardsShuffling.cs:146`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L146)

- Tirage d'un seul actif via le kernel pur réutilisé (pickCount 1).
  [`PCardsShuffling.cs:157`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L157)

- Config one-shot de la copie — miroir de `ConfigureStolenCopy` d'Ugues.
  [`PCardsShuffling.cs:182`](../../Assets/Scripts/Characters/Powers/PCardsShuffling.cs#L182)

**Tests (périphérie)**

- Deux tests EditMode : un actif tiré en sautant les passifs / vide si tout passif.
  [`StolenPowerSelectorTests.cs:141`](../../Assets/Scripts/Tests/Editor/StolenPowerSelectorTests.cs#L141)
