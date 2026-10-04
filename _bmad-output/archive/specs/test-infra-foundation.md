---
title: Fondation infra de test (Lot 1) — inventaire statiques + fixture partagée
status: in-progress
date: 2026-07-17
depends_on: test-scenarios-catalog.md (R8)
---

# Lot 1 — Fondation infra (spec R8 d'Amelia, instanciée)

Prérequis dur avant tout fan-out PlayMode du catalogue. « Verte » = son propre test de reset passe, pas « compile ».

## 1. Inventaire NOMMÉ et exhaustif des statiques mutables (la gate)

### 1a. Singletons `.instance` de PRODUCTION (21) — à remettre à `null` en teardown PlayMode
Domain-reload OFF → chacun survit entre tests s'il n'est pas nullé. Un `StaticSingletonCensusGuardTests` EXISTE déjà
(`Assets/Scripts/Tests/Editor/StaticSingletonCensusGuardTests.cs`, dict `RecordedSurvivors`) — la fixture doit
consommer cette même liste (ou la partager) pour rester synchronisée, PAS en refaire une divergente.

| Singleton | Fichier:ligne | Reset |
|---|---|---|
| GameManager | `GameLogic/GameManager.cs:33` | prop `instance` → null (réflexion) |
| CharacterManager | `Characters/CharacterManager.cs:38` | field → null |
| RoleTargetSystem | `RoleTargetSystem/RoleTargetSystem.cs:16` | field → null |
| ChainingManager | `GameLogic/ChainingManager.cs:22` | field → null |
| ChatManager | `ChatSystem/ChatManager.cs:28` | field → null |
| PowerManager | `GameLogic/PowerManager.cs:16` | field → null |
| BoardManager | `Board/BoardManager.cs:30` | field → null |
| CardEffectManager | `Board/CardEffects/CardEffectManager.cs:13` | field → null |
| GameAudioManager | `AudioSystem/GameAudioManager.cs:16` | field → null |
| LobbyPlayerInfoHolder | `Network/LobbyPlayerInfoHolder.cs:21` | prop → null |
| FocusManager | `FocusSystem/FocusManager.cs:24` | field → null (census survivor) |
| SelectionFlowService | `UI/BoardUI/Selection/SelectionFlowService.cs:21` | `_instance` → null (census survivor) |
| MessageManager | `MessageSystem/MessageManager.cs:20` | field → null (census survivor) |
| ArrowManager | `ArrowSystem/ArrowManager.cs:14` | field → null |
| CardPickerManager | `UI/BoardUI/CardPickerManager.cs:21` | field → null |
| TooltipManager | `TooltipSystem/TooltipManager.cs:15` | field → null |
| NoteManager | `NoteSystem/NoteManager.cs:10` | field → null |
| BoardCameraManager | `Board/BoardCameraSystem/BoardCameraManager.cs:15` | field → null |
| InputManager | `Inputs/InputManager.cs:11` | field → null |
| LobbyManager | `Network/Services/LobbyManager.cs:14` | prop → null |
| GameAssetHolder | `GameAssetHolder.cs:8` | prop → null |

### 1b. Collections statiques mutables qui FUITENT (les plus dangereuses)
| Statique | Fichier:ligne | Note |
|---|---|---|
| `PBoundByInk.usedBoundByInkIds` | `Characters/Powers/PBoundByInk.cs:33` | `List<int>` grandit entre tests → NE JAMAIS asserter un id de chat littéral ; clear en teardown |
| `DontDestroyOnLoadComponent.existingIds` | `DontDestroyOnLoadComponent.cs:9` | `HashSet<int>` ; clear |
| `LivenessNetworkBridge.s_byNetworkManager` + `s_sinks` | `Network/Liveness/LivenessNetworkBridge.cs:30,34` | reset via `ResetSessionStatics()` (déjà appelé par MultiClientGameFixture) |

### 1c. Registres per-NetworkManager (`s_byNetworkManager`) — self-clean sur despawn, à VÉRIFIER
`GameManager:39`, `CharacterManager:44`, `CompositionRoot:62`, `AvatarManager:33`, `LivenessNetworkBridge:30`. Se
nettoient via OnNetworkDespawn/OnDestroy ; la fixture doit ASSERTER qu'ils sont vides en fin de teardown (le
MultiClientGameFixture le fait déjà pour GM/CM — étendre à Composition/Avatar).

## 2. Ordre de teardown load-bearing
`despawn NM → drain callbacks (yield) → reset singletons 1a → clear statiques 1b → libération port → assert registres 1c vides`.
(Reset un singleton avant un `OnNetworkDespawn` qui le lit = NRE qui masque le résultat.)

## 3. Preuve de port libre
Port ÉPHÉMÈRE par test (incrémenter/randomiser dans une plage) OU poll borné « socket fermé » avant le test suivant.
Ne PAS réutiliser un port fixe avec un simple `yield` (flake bind-7777 documenté).

## 4. Helper de quiescence UNIQUE
`WaitUntilQuiescent(nm, timeout)` partagé : draine N ticks / attend une condition explicite (pas « yield return null » au pif).
Toutes les assertions « état-client-après-quiescence » (R1) passent par LUI. Une seule définition dans tout le repo.

## 5. Test-de-la-fixture = la GATE
`FixtureResetTests` : test A salit chaque statique 1a/1b (set instance, add à usedBoundByInkIds…), test B asserte
tout propre. L'infra n'est ACCEPTÉE que si ce test passe. C'est la preuve que le teardown couvre bien l'inventaire.

## 6. Matrice test→statiques-touchés
Colonne par scénario : quels singletons/statiques il touche → clé de partitionnement du dispatch (deux tests qui
touchent le même statique ne peuvent pas être écrits en parallèle, Unity MCP série + reload OFF).

## Ordonnancement (Murat + Amelia)
1. Cette fondation (1-6), `FixtureResetTests` VERT.
2. Les 2 bugs connus en ROUGE **sur cette infra** (Awaken/Sleep, WOmniscience NRE) — sonde qui prouve que la fixture
   voit l'état CLIENT (pas l'objet host).
3. Réactivation runner CI (`unity-tests.yml:20 if:false`) + smoke 2-NM vert (story bloquante, owner).
4. Refactor-à-blanc + 1ᵉ contrat paramétré pilote (3-4 pouvoirs) → audit forme `EffectDescriptor`.
5. Fan-out tier H.

## État
Inventaire (§1) produit par scan. Seam seed (catalogue 246-252) DÉJÀ en place (IRandomProvider injectable sur
PMarqueHurluberluges + PCardsShuffling, EditMode 455/455 vert). Reste à coder : la fixture partagée + `FixtureResetTests`
+ helper quiescence + les 2 ROUGE. Gros chantier Unity séquentiel — prochaine étape.
