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

### C.2 — Nouveau passif « Ciblée » (via le canal d'icônes privées)

**Décision Poyo (2026-07-20) : livraison par ICÔNE SEULE, plus de chat.** Le canal d'icônes privées existe désormais (branche `feat/targeting-icons`, mergée ici). Le passif s'appuie dessus : quand un joueur cible l'Orpheline, l'icône du pouvoir de l'Orpheline se pose sur **la vignette du cibleur**, visible **d'elle seule**. C'est exactement le premier consommateur prévu par [[spec-charactersbar-icon-system]].

Forme calquée sur `PClandestineObservation` (passif à rapport : décision pure + port implémenté par l'adaptateur) ; déclenchement fin de nuit calqué sur `PDroolyHealing` ([PDroolyHealing.cs:91](Assets/Scripts/Characters/Powers/PDroolyHealing.cs#L91) : `_awakeningState.onStateEndServer += …`) ; id d'icône fourni par l'adaptateur comme `CursedVision` fournit son `CardEffectId` ([PCursedVision.cs:31](Assets/Scripts/Characters/Powers/PCursedVision.cs#L31)).

- `PowerId` : append `TargetedByReport = 25`.
- **Domaine** — `TargetedByReportDecision : IPowerDecision`, `IsPassive => true`, champ `public ulong IconId` (posé par l'adaptateur). Lit un port `ITargetedByReport { IReadOnlyList<int> TargeterSlots }` et émet **un `AddPlayerIcon` par cibleur** : `new AddPlayerIcon(IconId, markedSlot: targeter, viewerSlot: ctx.OwnerSlot, PlayerIconLifetime.ClearAtAwakeningStart)`. Pur, testable par valeur (liste d'effets = un par cibleur).
- **Trigger — PER-TARGETING, pas fin de nuit (décision Poyo 2026-07-20, après playtest).** Poyo veut que l'icône apparaisse **à l'instant du ciblage**, pas batchée en fin de nuit. `RoleTargetSystem` expose désormais `onTargetingAddedServer` ([RoleTargetSystem.cs:70](Assets/Scripts/RoleTargetSystem/RoleTargetSystem.cs#L70)), levé côté serveur dès qu'un ciblage est enregistré. Le passif s'y abonne ; quand `data.targetId == owner`, il rejoue la décision immédiatement. **Bonus :** ça capture inhéremment les cibleurs des couches de réveil tardives (Traqueuse/Robot/Croupière), puisque chaque ciblage se déclenche quand il arrive, quel que soit l'ordre — le batch fin-de-nuit n'est plus nécessaire.
- **Adaptateur** — `PTargetedByReport : Power, ITargetedByReport` : `TargeterSlots` lit `roleTargetSystem.GetAllTargetersForTarget(ownerClientId.Value)` (déjà un `HashSet<ulong>`, donc **pas de doublon** — répond à C2 sans code). Pose `_decision.IconId = NetworkObjectId` en `OnNetworkSpawn`. S'abonne à `roleTargetSystem.onTargetingAddedServer` en `OnGameStartedServer` (idempotent via un drapeau `_subscribedToTargeting`), **se désabonne en `OnNetworkDespawn`** (symétrie — `RoleTargetSystem` est un objet partagé longue-vie, ne pas fuir). Handler : filtre `targetId == owner`, puis `RunDecisionEffects` — la décision relit tous les cibleurs et émet une `AddPlayerIcon` par cibleur ; `PlayerIconManager.AddIcon` dédoublonne, donc re-jouer à chaque ciblage n'ajoute que la nouvelle icône.
- **Prefab** — `Assets/Prefabs/Powers/TargetedByReport.prefab`, `isPassive = true`, `hasToBeAwakened = false` (passif), `targetIncludeFlags = 0`, `powerDescription = "Chaque nuit, elle repère les rôles qui l'ont ciblée."`, `barIcon` = sprite placeholder `MeIcon` (`guid 543077bfedecd36eca0aa48a0a0b575b`) — moche mais visible, à remplacer par le vrai visuel plus tard. Ajouté à `Orpheline.asset`.

  ⚠️ **Câblage SO — piège rencontré.** La liste `powers:` d'un `RoleDataObject` attend le fileID du **composant `Power`**, pas du GameObject racine. Le MCP a d'abord mis le fileID du GameObject → résolution `null` → NRE à l'attribution des rôles (`GivePowerToCharacter(..., null)`). Corrigé : fileID composant `2751029845932013476`. Toujours relire qu'un slot affiche `(PTargetedByReport)`, pas le nom du GameObject.

  ⚠️ **Description obligatoire pour l'affichage carte.** `RoleCardPowerVisibility.Classify` ([RoleCardPowerVisibility.cs:52](Assets/Scripts/Domain/RoleCardPowerVisibility.cs#L52)) cache tout passif dont `powerDescription` est vide (`hasDescription ? PassiveRow : Hidden`). Un passif sans description ne s'affiche pas — par design. D'où la description ci-dessus (placeholder, à raffiner par Wouh).
- **Nom** : placeholder clair (`powerName = "Paranoïa [WIP]"`), à trancher par Wouh — se change en une ligne dans le prefab.

⚠️ **Durée de vie de l'icône.** `ClearAtAwakeningStart` : l'icône posée pendant la nuit N survit au jour et au vote, et est purgée au **début de la nuit N+1** par `PlayerIconManager` — comportement voulu par Poyo (« en début d'awakening ça enlève toutes les icônes pour paranoïa »).

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
| C1 | Personne n'a ciblé l'Orpheline | liste de cibleurs vide | Aucune icône posée, aucun effet |
| C2 | Deux cibleurs du même rôle | 2 clientIds distincts | 2 icônes, sur 2 vignettes — pas de collision (marqueurs à `markedSlot` différents) |
| C3 | Un rôle la cible après sa couche de réveil | Traqueuse (couche 9) | **Icône posée** — déclenchement fin de nuit |
| C4 | L'Orpheline se cible elle-même via un autre pouvoir | targeter==owner | Icône sur sa propre vignette, vue d'elle seule — inoffensif, non filtré |
| C5 | Le sprite n'est pas encore le vrai | `barIcon` = placeholder | Icône visible (placeholder), pouvoir testable de bout en bout |

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

### Lot A — Repenti ✅ (livré, tests verts, revue adverse RAS — commit en attente)
- [x] `CursedVisionDecision.cs` — supprimé `CorruptPlayer(ctx.OwnerSlot)` + le `RevealInfo` owner. Commentaire de classe mis à jour.
- [x] `Repenti.asset` — ajouté AutoCorruption (`fileID 2782998364119609300`) et CorruptionParanoia (`fileID 4186362527338676593`) — fileID **composant** vérifiés (piège du fileID-GameObject évité).
- [x] `PowerDecisionTests.cs` — renommé `CursedVision_NonChosen_CorruptsTargetRevealsCardsChats`, ramené à 5 effets. `CursedVision_Chosen_FlipsCardAndVerdict` inchangé (indices [3]/[4] survivent, effets retirés en fin).
- [x] `OwnerLocalEffectBoundaryTests.cs` — commentaires corrigés (le siège owner reste requis comme **viewer** du reveal + source du NewTargeting, pas comme cible d'auto-corruption). Test toujours vert.
- [x] Test PlayMode dédié **non créé** (décision) — `AutoCorruptionDecision`/`CorruptionParanoiaDecision` sont déjà couverts EditMode (`AutoCorruption_CorruptsOwner`, `CorruptionParanoia_RevealsOwnCorruptionToSelf_Broadcast`) + `CorruptionTests` PlayMode ; un test Repenti dédié re-testerait de la machinerie de boot partagée. A2 satisfait par la couverture existante + le câblage.

**Acceptance :**
- Given le Repenti utilise Vision Maudite, when la cible est traitée, then la cible est corrompue et le Repenti **ne** l'est **pas** du fait de ce lancer.
- Given la partie démarre, when les passifs se résolvent, then le Repenti est corrompu et voit sa propre corruption.
- Given le Repenti est soigné puis reciblé par un pouvoir de corruption, when le soin est consommé, then il peut être recorrompu normalement.

### Lot B — Abyss ✅ (livré, EM 519/PM 251, revue adverse 1 HIGH résolu — commit en attente)
- [x] `Abyss.asset` — entrée Legacy retirée (Legacy.prefab/PLegacy conservés, détachés).
- [x] `EffectDescriptor.cs` — `GrantExtraUse(int ownerSlot)` ajouté.
- [x] `GrantExtraUseExecutor` (créé via `create_script` — piège d'exclusion silencieuse évité) + carrier `IExtraUseGrant` + read port `IExtraUseState`.
- [x] `ChainedByShadowsDecision` — branche bonus : `roleGuessed` + `OwnerIsSoleAnomalyInPlay` (anomalies non-chained/non-eliminated == 1) + `IExtraUseState.BonusConsumedThisNight == false` → `GrantExtraUse`. `IRosterView` étendu (`IsChained`/`IsEliminated`, 4 impls).
- [x] `PChainedByTheShadows` — carrier (`powerUseLeft += 1` + drapeau) + read port ; **prefab intouché** (`maxPowerUse` reste 1). Reset du drapeau via `isAwakened.OnValueChanged` (PAS `onCharacterAwakened` — event mort projet-wide, cf. `deferred-work.md`).
- [x] Tests EditMode — 5 cas : seule+juste+bonus frais→grant ; seule+juste+déjà consommé→pas de grant ; 2 anomalies→pas de grant ; autre anomalie chained→compte comme seule→grant ; seule+faux→rien.

**Décision de scope (autonome) :** le bug préexistant `onCharacterAwakened` (jamais levé — copier-coller `Character.cs:145`) a été **contourné** (reset sur `isAwakened`), **pas corrigé** — le fix réveillerait 5 handlers dormants dont `PowerManager`/`AwakeningState`, trop risqué à empaqueter ici. Documenté dans `deferred-work.md` pour une tâche + playtest dédiés.

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

### Lot D — Chasseuse de Prime ✅ (livré, EM 519/PM 251, revue adverse 0 CRITICAL/HIGH — commit en attente)
- [x] `SelectionFlowService.StartMultiCharacterSelection` — N clics par récursion sur le picker, validateur par étape excluant les déjà-pickés (distinct garanti), annulation partielle → tout le flux annulé, aucun RPC. Interface `ISelectionFlowService` étendue.
- [x] `PowerContext.TargetSlots` (`IReadOnlyList<int>`, défaut vide) — ajouté sans casser les slots existants.
- [x] N capturé à `OnGameStartedServer` (`_nonEluCount` NetworkVariable = nb de non-`chosen`, figé, répliqué au client owner).
- [x] `ClandestineObservationDecision` réécrite (compte les `chosen` parmi `TargetSlots`, `ChatBroadcast` owner-only) ; `IClandestineReport` + `targetRoleID` supprimés (aucune ref restante).
- [x] `PClandestineObservation` passif → actif : `StartUse` lance le flux multi, `targetValidator` exclut l'owner (C3) + flags 158 ; `ObserveServerRpc(ulong[])` → décision serveur.
- [x] Prefab — `isPassive` 0, `hasToBeAwakened` 1, `targetIncludeFlags` 158, `maxWaitTime` 60, `maxPowerUse` 1.
- [x] Tests EditMode — 2 cibles mixtes → 1 élu ; 2 non-élus → 0. PlayMode réécrit — `ObserveServerRpc` sur 2 cibles (1 chosen) → rapport « 1 sont des élus » à l'owner via serveur.

**⚠️ Point de design à réviser (non figé, D1) — dead-end mid-game.** N est figé à la composition de départ, mais le pool de cibles valides rétrécit quand des joueurs sont enchaînés. Sans garde, le picker réclamerait plus de cibles distinctes qu'il n'en reste → sélection impossible, usage gâché. **Interim livré : `StartUse` clampe le nombre à cibler au pool réellement disponible** (`min(N, GetValidTargets().Count)`) — la feature reste utilisable, l'intention « N de départ » tient tôt (cas courant). À trancher par Wouh à la révision : clamp (actuel) / gate `CanUse` sur ≥N / autre. Noté `deferred-work.md`.

**Non couvert (acknowledgé) :** le flux multi-select UI (`StartMultiCharacterSelection`) et la capture de N n'ont pas de test automatisé (pas de `CardPickerManager` en EditMode/2-NM) → **playtest 2 clients requis** pour valider la sélection à N clics, l'exclusion des doublons, l'exclusion de soi, et le clamp mid-game.

**Acceptance :**
- Given la Chasseuse désigne ses cibles, when la sélection aboutit, then elle seule reçoit le nombre d'élus parmi elles.
- Given elle tente de se désigner, when elle clique sur sa propre carte, then la sélection la refuse (règle validator + flags).
- Given elle a joué cette nuit, when elle retente, then le pouvoir est indisponible jusqu'au réveil (`maxPowerUse 1`, regen -1).

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
| C4 | Canal d'icône entre joueurs | **RENÉGOCIÉ 2026-07-20 : le canal d'icônes a été construit ([[spec-charactersbar-icon-system]], mergé) et le lot C livre désormais par ICÔNE SEULE, plus par chat.** Voir `### C.2`. L'ancienne décision (chat provisoire + icône plus tard) est caduque |
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
