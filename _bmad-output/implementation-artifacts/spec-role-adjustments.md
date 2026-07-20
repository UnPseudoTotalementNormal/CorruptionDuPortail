---
title: 'Ajustements des Rôles — Chasseuse de Prime, Abyss, Orpheline, Repenti'
type: 'feature'
created: '2026-07-20'
status: 'ready-for-dev'
baseline_commit: '509793e1'
branch: 'feat/role-adjustments'
source: 'Discord thread 1528057133575176242 « Ajustements des Rôles » (Wouh, 2026-07-18)'
context: ['{project-root}/_bmad-output/project-context.md', '{project-root}/_bmad-output/implementation-artifacts/spec-powers-poco-v2-architecture.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless Wouh/Poyo renegotiates">

## Intent

**Problem:** Le game designer (Wouh) a révisé quatre rôles. Trois sont des retouches ciblées (Abyss, Orpheline, Repenti) ; la quatrième (Chasseuse de Prime) est une **réécriture complète** d'*Observation Clandestine*, qui passe de passif à actif multi-cibles.

**Approach:** Quatre lots indépendants, dans l'ordre de risque croissant. Lots A–C sont des retouches de décisions POCO + recomposition des `powers:` sur les `RoleDataObject`. Lot D (Chasseuse) exige de la **nouvelle infra** (sélection multi-personnages, contexte de décision multi-cibles) : à livrer **en dernier, dans un commit isolé**, son design n'étant pas figé (décision D1).

**Design intent verbatim (Wouh) :**
> Abyss : « Au lieu de gagner le pouvoir du MO en cas d'enchaînement, c'est son pouvoir à lui qui s'améliore en pouvant s'activer une fois de plus. »
> Orpheline : « Son passif change, ce n'est plus de savoir si elle est corrompue […] elle apprend les rôles qui la ciblent pendant la nuit. »
> Repenti : « J'ai retiré qu'il se corrompait en utilisant son pouvoir […] j'ai donné l'ancien passif de l'Orpheline […] et j'ai mis qu'il commence Corrompu. »

## Boundaries & Constraints

**Always:**
- Autorité serveur stricte : toute mutation d'état passe par le serveur ; les décisions restent des POCO purs (`IPowerDecision.Decide` retourne des `EffectDescriptor`, n'applique rien).
- Réutiliser les briques d'effet existantes (`CorruptPlayer`, `RevealInfo`, `AddToChain`, `ChatBroadcast`, `NewTargeting`) — n'ajouter un `EffectDescriptor` que si aucune brique ne couvre l'intention.
- Toute nouvelle brique `EffectDescriptor` ⇒ un `IEffectExecutor` **à constructeur sans paramètre** ; `PowerDispatcherHost` ([:14](Assets/Scripts/Characters/Powers/Runtime/PowerDispatcherHost.cs#L14)) les découvre par réflexion — **aucun fichier partagé à éditer**. `EffectRegistryCompletenessTests` rougit si l'executor manque (allowlist vide, 100 % de couverture exigée).
- Tout nouveau passif doit être **idempotent** : `OnGameStartedServer` peut être rejoué au spawn (cf. §8 ci-dessous).
- `PowerId` est **append-only** — un nouveau pouvoir prend une nouvelle valeur en fin d'enum, jamais une réattribution.
- Un test EditMode par décision touchée (valeur des `EffectDescriptor` assertée), conformément à la règle « New power ⇒ Tests.Editor ».
- Tout nouveau RPC part avec `GetSafeRpcTarget` **et** un cas `MultiClientGameFixture`.

**Ask First:**
- Toute question de la section « Open Questions » — elles changent le comportement de jeu, elles sont design-owned.
- Lot D (Chasseuse) : ne pas commencer avant que le design soit figé (« sera à revoir du coup » = pas figé).

**Never:**
- Ne pas inventer de règle de gameplay non écrite ci-dessus (le design appartient à Wouh).
- Pas de `NetworkVariable` owner-write ; pas de nouveau `static instance`.
- Ne pas supprimer le pouvoir `Legacy` / `PLegacy.cs` du projet : le lot B le **retire d'Abyss**, il reste un pouvoir disponible pour une future attribution.
- Ne pas toucher `PCChainer` / `IFailablePower` (contrat de chaînage figé, cf. PR #94).

</frozen-after-approval>

---

## État actuel du code (vérifié, pas déduit)

| Rôle | SO | Pouvoirs attachés (GUID → prefab) |
|---|---|---|
| Traqueuse (`RoleID.ChasseuseDePrime`) | `Assets/ScriptableObjects/Characters/Traqueuse.asset` | `75e42c6b…` ClandestineObservation, `a7938e77…` HighPriorityBounty |
| Abyss | `Abyss.asset` | `e4f9bc64…` ChainedByTheShadows, `4be4aaf9…` CorruptionInsight, `ec614f26…` AutoCorruption, `8aded586…` EyeOfTheVoid, `9119f498…` **Legacy** |
| Orpheline | `Orpheline.asset` | `96fe74f3…` LackOfAffection, `487847a7…` **CorruptionParanoia** |
| Repenti | `Repenti.asset` | `f720e39e…` CursedVision (seul) |

Faits qui contredisent des hypothèses de la tâche :

1. **Le « pouvoir du MO » d'Abyss n'est pas un effet d'*Enchaîné par les Ombres***. C'est un **pouvoir séparé**, `Legacy` ([PLegacy.cs:24](Assets/Scripts/Characters/Powers/PLegacy.cs#L24)), qui s'abonne à `isChained` du rôle `roleForLegacy` et accorde `legacyPower` (prefab pointant sur `EmbraceOfShadows`). Le retirer = **retirer le prefab Legacy du SO Abyss**, pas éditer `ChainedByShadowsDecision`.
2. **`ChainedByShadowsDecision` ne corrompt pas.** ([ChainedByShadowsDecision.cs:17](Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs#L17)) — sur bonne devinette elle émet `RevealInfo` puis, si la cible est `chosen`, `AddToChain`. La corruption arrive **indirectement** : être enchaîné met `isCorrupted`. Le texte du designer (« il le corrompt et l'enchaine ») décrit donc le résultat observable, pas deux effets. **Aucun changement requis** sur ce point.
3. **Le passif actuel de l'Orpheline n'est pas « falsifié »** malgré la note du GDD. [CorruptionParanoiaDecision.cs:15](Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs#L15) : le dernier argument `true` de `RevealInfo` est `Broadcast`, pas un flag de falsification. Le pouvoir est donc **réutilisable tel quel** pour le Repenti — c'est bien « l'ancien passif de l'Orpheline ». La ligne 219 du GDD est à corriger.
4. **L'« icône envoyée aux anomalies » par *Manque d'affection* n'existe pas.** [PLackOfAffection.cs:80](Assets/Scripts/Characters/Powers/PLackOfAffection.cs#L80) ne joue qu'un **son FMOD** par faction. Le seul canal d'icône par joueur est `CardEffectID` ([CardEffectID.cs:3](Assets/Scripts/Board/CardEffects/CardEffectID.cs#L3)), qui ne contient que `TechnoBeacon / Blessing / CursedVision`. Le « même icône » du design est donc **à créer**, des deux côtés.
5. **Aucune infra multi-cibles n'existe.** `SelectionFlowService` n'expose que `StartRoleSelection` / `StartCharacterSelection` / `StartCharacterThenRoleSelection` ; `PowerContext` porte `TargetSlot` + `SecondaryTargetSlot`, deux slots maximum ([PowerContext.cs:12](Assets/Scripts/Domain/Powers/PowerContext.cs#L12)).
6. **`RoleTargetSystem` réplique à tout le monde.** `ReceiveTargetingDataRpc` est `SendTo.Everyone` ([RoleTargetSystem.cs:70](Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs#L70)) — `currentTargetingDataList` est présent sur chaque client. La liste est **remise à zéro à chaque `AwakeningState.onStateStartClient`** ([:45](Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs#L45)), ce qui donne exactement le périmètre « cette nuit » dont le passif Orpheline a besoin.
7. **La régénération d'usages est par réveil**, dans `Role.AwakenRole` ([Role.cs:52](Assets/Scripts/Characters/Role.cs#L52)) : `powerUseRegenPerAwakening == -1` ⇒ remise à `maxPowerUse`, **inconditionnelle**.
8. **L'ordre des passifs au démarrage n'est PAS piloté par la liste `powers:` du SO.** `PowerManager.OnGameStarted` ([PowerManager.cs:87](Assets/Scripts/GameLogic/PowerManager.cs#L87)) itère `role.powers`, que `Character.CheckForPowersRpc` ([Character.cs:118](Assets/Scripts/Characters/Character.cs#L118)) remplit depuis `GetComponentsInChildren<Power>()` — donc **l'ordre des enfants de transform**. Il coïncide avec la liste du SO uniquement parce que `RoleAttributionState.ApplyRole` ([:151](Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs#L151)) spawne dans cet ordre. Second chemin : `PowerManager.OnPowerSpawned` (:59-62) rejoue `OnGameStartedServer` au spawn si `hasGameStarted` — **double déclenchement documenté** ([PMarqueHurluberluges.cs:47](Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs#L47)). Conséquence pour ce chantier : ne jamais faire reposer une règle sur l'ordre de la liste `powers:`, et écrire tout nouveau passif de façon idempotente.

---

## Lot A — Repenti

**Cible :** retirer l'auto-corruption de *Vision Maudite* ; ajouter deux passifs existants.

### A.1 — Vision Maudite ne corrompt plus son lanceur

`CursedVisionDecision.Decide` ([CursedVisionDecision.cs:26](Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs#L26)) retourne 7 effets. Supprimer les **deux derniers** :

```csharp
new CorruptPlayer(ctx.OwnerSlot),
new RevealInfo(ctx.OwnerSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.OwnerSlot, false));
```

Les cinq premiers (targeting, corruption de la cible, révélation de sa corruption au lanceur, marqueur de carte, verdict élu/non-élu) restent inchangés, dans le même ordre.

### A.2 — Passifs du Repenti

`Repenti.asset`, liste `powers:` — ajouter les deux prefabs **existants** :
- `ec614f265e6f87da9b63730bf86cc449` — **AutoCorruption** (commence corrompu)
- `487847a75662496b09d32f94a06a1fd8` — **CorruptionParanoia** (connaît sa propre corruption)

**L'ordre des deux passifs n'a aucune importance** (vérifié). `RevealInfoExecutor.Execute` ([RevealInfoExecutor.cs:19](Assets/Scripts/Characters/Powers/Runtime/Executors/RevealInfoExecutor.cs#L19)) pose un **droit de visibilité** (`RevealLevel`), pas un instantané de `isCorrupted` ; la valeur de corruption est relue en direct sur le `Character` à l'affichage. Que la révélation précède ou suive la corruption, le Repenti voit le bon état.

⚠️ Ces deux prefabs sont aussi référencés dans `Assets/DefaultNetworkPrefabs.asset` (:113 et :138) — **ces entrées doivent rester**, c'est le registre de spawn NGO, pas une attribution de rôle.

---

## Lot B — Abyss

**Cible :** retirer *Héritage* ; sur réussite, si Abyss est la seule Anomalie, rendre *Enchaîné par les Ombres* réutilisable une fois de plus dans le tour.

### B.1 — Retirer Héritage

`Abyss.asset` : supprimer l'entrée `guid: 9119f49837d8ccfa2be2803dd7270f0a` de `powers:`. `PLegacy.cs`, `LegacyDecision.cs`, le prefab et la brique `GrantLegacyPower` **restent** dans le projet (non attribués).

### B.2 — Bonus d'usage sur réussite en solo-anomalie

`ChainedByShadowsDecision` calcule déjà `roleGuessed` et le renvoie en `PowerVerdict.Correct`. Ajouter une brique d'effet conditionnelle :

- **Condition :** `roleGuessed == true` **ET** l'owner est la seule Anomalie **encore en jeu** (décision B1 : état courant, pas composition de départ — comptage via `ctx.Roster.Slots` + `FactionOf`, en excluant les enchaînés/éliminés) **ET** le bonus n'a pas déjà été consommé cette nuit (décision B2 : un seul bonus par nuit, quel que soit le nombre de réussites).
- **Effet :** nouvelle brique `GrantExtraUse(int ownerSlot)` + son executor, qui **incrémente `powerUseLeft` directement**, sans passer par `maxPowerUse`.

⚠️ **Ne pas monter `maxPowerUse` à 2 sur le prefab.** `ChainedByTheShadows.prefab` a `maxPowerUse: 1` et `powerUseRegenPerAwakening: -1` ; avec `-1`, `Role.AwakenRole` ([Role.cs:52](Assets/Scripts/Characters/Role.cs#L52)) écrit `powerUseLeft = maxPowerUse` **inconditionnellement à chaque réveil**. Passer le prefab à 2 donnerait deux usages par nuit à **toute** Abyss, sans condition de solo-anomalie ni de réussite — ce qui viole directement les critères d'acceptation B3 et B4 et rend toute la machinerie `GrantExtraUse` décorative. Le prefab reste à 1 ; `powerUseLeft` dépasse temporairement `maxPowerUse` le temps du tour, et le réveil suivant le ramène à 1. Rien ne clampe `powerUseLeft` en dehors de `AwakenRole`.
- **État :** le drapeau « bonus déjà pris ce tour » est un état répliqué power-local ⇒ port d'état étroit (`IExtraUseState`) implémenté par `PChainedByTheShadows`, remis à zéro au réveil.

Le compte d'anomalies vit dans la décision (pur, testable). ⚠️ `IRosterView` ([IRosterView.cs:12](Assets/Scripts/Domain/Powers/IRosterView.cs#L12)) expose `Slots` / `FactionOf` / `IsCorrupted` / `IsHealed` / `IsRobot` mais **pas** `isChained` ni `isEliminated`. La décision B1 (« encore en jeu ») exige donc **d'étendre l'interface** — ajouter `IsChained(int slot)` et `IsEliminated(int slot)`, implémentés dans `CharacterManagerRoster` et dans le `FakeRoster` des tests EditMode.

---

## Lot C — Orpheline

**Cible :** remplacer *Paranoïa de la corruption* par un passif « quels rôles m'ont ciblée cette nuit ».

### C.1 — Retirer l'ancien passif

`Orpheline.asset` : supprimer `guid: 487847a75662496b09d32f94a06a1fd8` (CorruptionParanoia). Le prefab reste — le Lot A le réutilise sur le Repenti.

### C.2 — Nouveau passif « Ciblée »

Trois fichiers, sur le modèle de `PClandestineObservation` pour la **forme** (passif à rapport : décision pure + port de rapport implémenté par l'adaptateur) et de `PDroolyHealing` pour le **déclenchement fin de nuit** ([PDroolyHealing.cs:91](Assets/Scripts/Characters/Powers/PDroolyHealing.cs#L91) : `_awakeningState.onStateEndServer += …`) :

- `PowerId` : append `TargetedByReport = 25`.
- **Domaine** — `TargetedByReportDecision : IPowerDecision`, `IsPassive => true`. Lit un port de rapport (`ITargetedByReport { IReadOnlyList<string> TargeterRoleNames }`) et émet un `ChatBroadcast` adressé au seul owner (`PowerEffectAudience.Specific(ctx.OwnerSlot)`), exactement comme `ClandestineObservationDecision`.
- **Adaptateur** — `PTargetedByReport : Power, ITargetedByReport` : implémente le port via `roleTargetSystem.GetAllTargetingDataForTarget(ownerClientId.Value)` puis `characterManager.GetCharacter(targeterId).role.roleName`.
- **Prefab** — `Assets/Prefabs/Powers/TargetedByReport.prefab`, `isPassive = true`, `targetIncludeFlags = 0`, ajouté à `Orpheline.asset`.

⚠️ **Timing load-bearing.** L'Orpheline est **couche 8** de l'ordre de réveil ; Traqueuse (9), Robot (10) et Croupière (12) la ciblent **après**. Se brancher sur `onCharacterAwakened` (le hook de `PClandestineObservation`) raterait ces ciblages. Le rapport doit se déclencher sur `AwakeningState.onStateEndServer` ([GameState.cs:75](Assets/Scripts/GameLogic/GameState.cs#L75), invoqué :100). L'ordre est sûr : `RoleTargetSystem.ResetTargetingData` est branché sur `onStateStartClient` ([RoleTargetSystem.cs:45](Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs#L45)), donc la fin de la nuit N précède toujours le début de la nuit N+1, et il n'existe qu'un seul asset `AwakeningState`.

---

## Lot D — Chasseuse de Prime

**Cible :** *Observation Clandestine* devient un actif : cibler N joueurs, apprendre combien d'élus parmi eux.

⚠️ Design **non figé** (D1) — Poyo demande de le construire quand même, révision ultérieure assumée. Livrer ce lot **en dernier**, dans un commit isolé, pour qu'une révision de design ne force pas à défaire A/B/C.

Ce lot ne réutilise **rien** de l'existant : le pouvoir actuel est un passif auto-déclenché sans sélection, qui compte les cibleurs d'un rôle configuré (`targetRoleID = Robot` sur le prefab). Ce n'est pas une retouche, c'est un remplacement.

**Règles arrêtées :**
- **N = nombre de non-élus (Anomalies + Marginaux) de la composition de départ**, figé toute la partie (D2). À capturer une fois, à l'attribution des rôles — ne pas recalculer sur le roster vivant.
- **Une utilisation par nuit** (D4) — `maxPowerUse = 1`, `powerUseRegenPerAwakening = -1` (le défaut).
- **La Chasseuse ne peut pas se cibler** (C3) — règle du `targetValidator`.
- Cibler deux fois le même joueur est impossible (contrainte du flux de sélection, pas une règle de jeu).

Infra à créer avant d'écrire la décision :
1. `SelectionFlowService.StartMultiCharacterSelection(validator, count, onPicked)` — flux de sélection à N clics, sans doublon, avec annulation partielle.
2. `PowerContext.TargetSlots` (`IReadOnlyList<int>`) en plus des deux slots actuels, pour que la décision reste pure.
3. `ClandestineObservationDecision` réécrite : compte les `chosen` parmi `TargetSlots`, émet un `ChatBroadcast` owner-only. Le passif actuel, son port `IClandestineReport` et le champ `targetRoleID` disparaissent.
4. Prefab `ClandestineObservation.prefab` : `isPassive` false, `targetIncludeFlags` 0 → valeur de ciblage joueurs.

---

## I/O & Edge-Case Matrix

| # | Scénario | État / entrée | Comportement attendu |
|---|---|---|---|
| A1 | Repenti utilise Vision Maudite | cible quelconque | La cible est corrompue ; **le Repenti ne l'est pas par ce fait** |
| A2 | Début de partie, Repenti | — | `isCorrupted = true`, et il le voit sur sa propre carte |
| A3 | Repenti soigné puis reciblé | `isHealed` posé par un soin | Comportement standard de soin/recorruption, aucun cas spécial |
| A4 | Ordre des deux passifs du Repenti | quel qu'il soit | **Indifférent** — `RevealInfo` pose un droit de visibilité, la corruption est relue en direct |
| B1 | Abyss seule anomalie, devinette juste | 1 anomalie vivante | Rôle révélé + chaînage si élu + **1 usage rendu** |
| B2 | Abyss seule anomalie, 2ᵉ réussite le même tour | bonus déjà pris | **Aucun** usage supplémentaire |
| B3 | Abyss seule anomalie, devinette fausse | — | Aucun bonus |
| B4 | Abyss + une autre anomalie, devinette juste | ≥2 anomalies | Aucun bonus |
| B5 | Nuit suivante | — | `AwakenRole` remet `powerUseLeft` à `maxPowerUse` ; le drapeau bonus est remis à zéro |
| B6 | Rôle configuré en `roleForLegacy` enchaîné | — | **Rien** — Abyss n'a plus Héritage |
| C1 | Personne n'a ciblé l'Orpheline | liste de ciblage vide | Message « personne » (formulation à valider) |
| C2 | Deux joueurs du même rôle la ciblent | 2 entrées même rôle | ⚠️ Open Question C2 (dédoublonner ou non) |
| C3 | Un rôle la cible après sa couche de réveil | Traqueuse (couche 9) | **Doit apparaître** — d'où le déclenchement fin de nuit |
| C4 | L'Orpheline se cible elle-même | auto-ciblage | ⚠️ Open Question C3 |

---

## Code Map

| Fichier | Lot | Rôle dans le changement |
|---|---|---|
| [CursedVisionDecision.cs:32](Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs#L32) | A | Supprimer les 2 effets d'auto-corruption (lignes 32-33) |
| [PowerDecisionTests.cs:253](Assets/Scripts/Tests/Editor/PowerDecisionTests.cs#L253) | A | 7 → 5 effets + renommage du test |
| [OwnerLocalEffectBoundaryTests.cs:265](Assets/Scripts/Tests/PlayMode/OwnerLocalEffectBoundaryTests.cs#L265) | A | Nettoyer l'échafaudage devenu faux |
| `Assets/ScriptableObjects/Characters/Repenti.asset` | A | +AutoCorruption, +CorruptionParanoia (ordre libre) |
| `Assets/ScriptableObjects/Characters/Abyss.asset` | B | −Legacy |
| [ChainedByShadowsDecision.cs:17](Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs#L17) | B | +branche bonus solo-anomalie |
| [EffectDescriptor.cs](Assets/Scripts/Domain/EffectDescriptor.cs) | B | +`GrantExtraUse` |
| `Assets/Scripts/Characters/Powers/Runtime/Executors/` | B | +executor `GrantExtraUse` |
| [PChainedByTheShadows.cs:22](Assets/Scripts/Characters/Powers/PChainedByTheShadows.cs#L22) | B | +port d'état bonus, reset au réveil |
| [PDroolyHealing.cs:91](Assets/Scripts/Characters/Powers/PDroolyHealing.cs#L91) | C | Précédent de branchement `onStateEndServer` à copier |
| `Assets/ScriptableObjects/Characters/Orpheline.asset` | C | −CorruptionParanoia, +TargetedByReport |
| [PowerId.cs:34](Assets/Scripts/Domain/Powers/PowerId.cs#L34) | C | append `TargetedByReport = 25` |
| *nouveau* `Domain/Powers/Decisions/TargetedByReportDecision.cs` | C | décision pure |
| *nouveau* `Characters/Powers/PTargetedByReport.cs` | C | adaptateur + port de rapport |
| [RoleTargetSystem.cs:89](Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs#L89) | C | `GetAllTargetingDataForTarget` réutilisé tel quel |
| [PClandestineObservation.cs](Assets/Scripts/Characters/Powers/PClandestineObservation.cs) | C/D | modèle du passif à rapport ; réécrit en D |
| [SelectionFlowService.cs:53](Assets/Scripts/UI/BoardUI/Selection/SelectionFlowService.cs#L53) | D | +flux multi-sélection |
| [PowerContext.cs:12](Assets/Scripts/Domain/Powers/PowerContext.cs#L12) | D | +`TargetSlots` |

---

## Tasks & Acceptance

### Lot A — Repenti
- [ ] `CursedVisionDecision.cs` — supprimer `CorruptPlayer(ctx.OwnerSlot)` et le `RevealInfo` owner qui suit (lignes 32-33). Mettre à jour le commentaire de classe (il documente l'auto-corruption).
- [ ] `Repenti.asset` — ajouter AutoCorruption et CorruptionParanoia à `powers:` (ordre libre).
- [ ] [PowerDecisionTests.cs:253](Assets/Scripts/Tests/Editor/PowerDecisionTests.cs#L253) — `CursedVision_NonChosen_CorruptsRevealsCardsChatsBoth` asserte les **7** effets : ramener à 5 **et renommer** (le suffixe `Both` désigne la double corruption qui disparaît). `CursedVision_Chosen_FlipsCardAndVerdict` (:272, indices [3]/[4]) survit tel quel.
- [ ] [OwnerLocalEffectBoundaryTests.cs:265](Assets/Scripts/Tests/PlayMode/OwnerLocalEffectBoundaryTests.cs#L265) — le test **passe** toujours (il n'asserte que la révélation de `TargetSeat`), mais son échafaudage n'existe que pour l'auto-corruption : le `Character` `hostOwner` (:266), l'attente de réplication de `ownerClientId` (:288-291) et les commentaires de justification (:265, :285-287) deviennent faux. Nettoyer — sinon documentation trompeuse + surface de flake inutile.
- [ ] Test PlayMode — au démarrage, le Repenti est `isCorrupted` et sa corruption lui est révélée.

**Acceptance :**
- Given le Repenti utilise Vision Maudite, when la cible est traitée, then la cible est corrompue et le Repenti **ne** l'est **pas** du fait de ce lancer.
- Given la partie démarre, when les passifs se résolvent, then le Repenti est corrompu et voit sa propre corruption.
- Given le Repenti est soigné puis reciblé par un pouvoir de corruption, when le soin est consommé, then il peut être recorrompu normalement.

### Lot B — Abyss
- [ ] `Abyss.asset` — retirer l'entrée Legacy.
- [ ] `EffectDescriptor.cs` — ajouter `GrantExtraUse(int ownerSlot)` (immuable, value-equatable).
- [ ] Nouvel executor `GrantExtraUseExecutor` enregistré (sinon `EffectRegistryCompletenessTests` rougit).
- [ ] `ChainedByShadowsDecision` — sur `roleGuessed` + owner seule Anomalie + bonus non consommé, ajouter `GrantExtraUse(ctx.OwnerSlot)`.
- [ ] `PChainedByTheShadows` — port d'état du drapeau bonus, remis à zéro au réveil. **Le prefab n'est pas touché** (`maxPowerUse` reste à 1).
- [ ] Tests EditMode — les 4 combinaisons (seule/pas seule) × (juste/faux) + le second déclenchement dans le même tour.

**Acceptance :**
- Given Abyss est la seule Anomalie et devine juste, when la décision se résout, then un usage supplémentaire lui est rendu pour ce tour.
- Given elle réussit une seconde fois le même tour, when la décision se résout, then aucun usage supplémentaire n'est accordé.
- Given une autre Anomalie est en jeu, when Abyss devine juste, then aucun usage supplémentaire.
- Given un joueur portant l'ancien `roleForLegacy` est enchaîné, when le chaînage s'applique, then Abyss n'hérite d'aucun pouvoir.

### Lot C — Orpheline
- [ ] `PowerId` — append `TargetedByReport = 25`.
- [ ] `TargetedByReportDecision.cs` (Domain) + port `ITargetedByReport`.
- [ ] `PTargetedByReport.cs` (adaptateur) — lit `RoleTargetSystem`, se déclenche **en fin d'AwakeningState**.
- [ ] Prefab `TargetedByReport.prefab` (`isPassive`, `targetIncludeFlags = 0`) + attribution sur `Orpheline.asset` ; retirer CorruptionParanoia.
- [ ] Tests EditMode — zéro cibleur / un / plusieurs, formatage du message.
- [ ] Test PlayMode — un rôle réveillé **après** l'Orpheline la cible → il apparaît bien dans le rapport.

**Acceptance :**
- Given des joueurs ont ciblé l'Orpheline pendant la nuit, when la nuit se termine, then elle reçoit la liste de leurs rôles, elle seule.
- Given un rôle d'une couche de réveil postérieure la cible, when la nuit se termine, then ce rôle figure dans le rapport.
- Given personne ne l'a ciblée, when la nuit se termine, then elle reçoit un message le disant explicitement.
- Given une nouvelle nuit commence, when le rapport se déclenche, then il ne contient que les ciblages de cette nuit.

### Lot D — Chasseuse de Prime
- [ ] `SelectionFlowService` — `StartMultiCharacterSelection(validator, count, onPicked)` : N clics, pas de doublon, annulation partielle.
- [ ] `PowerContext` — ajouter `TargetSlots` (`IReadOnlyList<int>`), sans casser `TargetSlot` / `SecondaryTargetSlot`.
- [ ] Capture de N à l'attribution des rôles (composition de départ, figé) + exposition au pouvoir.
- [ ] `ClandestineObservationDecision` réécrite ; suppression de `IClandestineReport` et de `targetRoleID`.
- [ ] `PClandestineObservation` — passif → actif : `StartUse` lance le flux multi, `targetValidator` exclut l'owner.
- [ ] Prefab — `isPassive` false, `targetIncludeFlags` de ciblage joueurs, `maxPowerUse = 1`.
- [ ] Tests EditMode — 0 élu / tous élus / mixte parmi N ; N > joueurs ciblables ; auto-ciblage rejeté.
- [ ] Test PlayMode — le flux de sélection multi aboutit et le rapport n'atteint que la Chasseuse.

**Acceptance :**
- Given la Chasseuse utilise son pouvoir, when elle a désigné N joueurs, then elle seule reçoit le nombre d'élus parmi eux.
- Given elle tente de se désigner, when elle clique sur sa propre carte, then la sélection la refuse.
- Given des non-élus sont enchaînés en cours de partie, when elle rejoue une nuit suivante, then N est inchangé.
- Given elle a joué cette nuit, when elle retente, then le pouvoir est indisponible jusqu'au réveil suivant.

---

## Décisions design (tranchées par Poyo, 2026-07-20)

| # | Question | Décision |
|---|---|---|
| A1 | Le Repenti corrompu d'entrée compte-t-il dans `WAnomalyCorruption` ? | **Oui, il compte.** Aucune exception dans la règle de victoire — c'est le down-side voulu |
| B1 | « Seule Anomalie en jeu » | **État courant.** Abyss devient seule dès que les autres Anomalies sont enchaînées/éliminées |
| B2 | Réussites en chaîne la même nuit | **1 bonus maximum par nuit.** 2 utilisations plafond, le bonus ne se redéclenche pas |
| B3 | Quand l'usage bonus se dépense | **Immédiatement, même nuit** |
| C1 | Précision du rapport Orpheline | **Noms de rôles** (« le Mage Occulte t'a ciblée ») |
| C2 | Deux joueurs du même rôle la ciblent | Non-problème une fois les icônes livrées (une icône par carte dans la `CharactersBar`). **Intérim chat : une ligne par joueur**, l'info de volume est conservée |
| C3 | La Chasseuse peut-elle se cibler ? | **Non, elle s'exclut** |
| C4 | Canal d'icône entre joueurs | **Hors de ce chantier.** Lot C livre le rapport **par chat** ; le système d'icônes part en tâche Discord + branche dédiées |
| D1 | Design Chasseuse figé ? | **Pas figé, mais à construire quand même** — révision ultérieure assumée |
| D2 | Calcul de N | **Composition de départ, fixe toute la partie** |
| D4 | Fréquence d'*Observation Clandestine* | **Une fois par nuit** |

**Reste ouvert (non bloquant) :** *Prime Prioritaire*, le second pouvoir de la Traqueuse, garde sa logique d'élimination encore marquée TODO en code — hors périmètre de ce chantier.

## Suite — système d'icônes (chantier séparé)

Le passif de l'Orpheline est conçu par Wouh autour d'un **échange d'icônes** : icône envoyée à l'Anomalie qu'elle cible, même icône reçue des rôles qui la ciblent, affichée sur la carte du joueur dans la `CharactersBar`. Rien de tout ça n'existe (cf. §4 de l'état des lieux).

Ce chantier a sa **tâche Discord dédiée** (postée par Poyo le 2026-07-20). Le lot C livre entre-temps la règle de jeu par message chat — comportement final identique, présentation intérimaire.

**Stratégie de branche (décidée par Poyo) :** une branche `feat/targeting-icons` est créée depuis `Dev`, et la PR de `feat/role-adjustments` est mergée **dans** `feat/targeting-icons` — pas dans `Dev`. Le système d'icônes se développe donc par-dessus les ajustements de rôles, et une seule PR finale remonte vers `Dev`. La branche cible sera créée au moment d'ouvrir la PR.

---

## Verification

**Commandes (Poyo, côté checkout principal) :**
- `mcp__UnityMCP__read_console` — attendu : 0 erreur de compilation après chaque lot.
- `mcp__UnityMCP__run_tests` EditMode — attendu : suite verte, dont les nouveaux tests de décision et `EffectRegistryCompletenessTests` (allowlist toujours vide).
- `mcp__UnityMCP__run_tests` PlayMode — attendu : vert, dont l'ordre des passifs du Repenti et le rapport fin-de-nuit de l'Orpheline.
- Guards par catégorie avant push : `DiSeamGuard`, `SceneWiringGuard`, `StaticAbsenceGuard`.

**Contrôles manuels (playtest 2 builds, Poyo) :**
- Repenti : carte corrompue dès le début ; Vision Maudite ne le recorrompt pas.
- Abyss seule Anomalie : la barre de pouvoir remonte à un usage après une devinette juste.
- Orpheline : rapport reçu en fin de nuit, contenant un rôle qui se réveille après elle.

**Baseline avant travaux :** EditMode 462 / PlayMode 47+2 (commit `509793e1`).

## Suggested Review Order

**Le plus à risque — économie des usages**
- L'executor `GrantExtraUse` incrémente bien `powerUseLeft` et **non** `maxPowerUse` ; le prefab `ChainedByTheShadows` est resté à `maxPowerUse: 1`. Une erreur ici donne silencieusement 2 usages par nuit à toute Abyss.
- Le drapeau anti-répétition est bien remis à zéro au réveil, et une seule fois.

**Cœur des règles**
- Branche bonus solo-anomalie de `ChainedByShadowsDecision` (comptage d'anomalies via `IRosterView`).
- Suppression des deux effets d'auto-corruption de `CursedVisionDecision` + réalignement des tests.
- Déclenchement fin-de-nuit du rapport Orpheline (`onStateEndServer`, pas `onCharacterAwakened`).

**Périphérie**
- Recomposition des listes `powers:` sur les quatre SO.
- Nouvelle brique `GrantExtraUse` + son executor (couverture du guard).
