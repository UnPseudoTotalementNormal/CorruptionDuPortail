---
status: done
slug: proto-power-effect-2client-replication
date: 2026-07-17
owner: Poyo
---

## Résultat (2026-07-17)

**VERT.** `PowerEffectReplicationProtoTests.CorruptionEffect_OnTarget_IsObservedOnRemoteClientReplica` passe
(1/1, 0.9 s) et coexiste avec les 3 tests `MultiClientGameFixtureTests` (4/4, pas de fuite port/singleton — AC4).
Fichier : `Assets/Scripts/Tests/PlayMode/Desingleton/PowerEffectReplicationProtoTests.cs`. Aucun code de prod touché.

**Découverte (pré-existante, capturée en mémoire `reference_char_replica_resolution_multi_nm`) :** la 1ʳᵉ passe a
échoué sur `AreNotSame(host, client)` — `ClientCm.GetCharacter` rend l'objet DU HOST car
`CharacterManager.RebuildCharactersCache` fait `NetworkBehaviourReference.TryGet` sans NM → résout via
`Singleton`=host. NON reproductible en prod (1 NM/process). Réplique client correcte résolue via
`ClientNm.SpawnManager.SpawnedObjects[netId]`. C'est l'assertion AC3 qui a attrapé le piège — le proto a prouvé sa
valeur dès l'écriture.

**Commit :** en attente OK explicite Poyo (folder spec + test ensemble si validé).

# Proto vérifiable — effet de pouvoir observé sur un VRAI second client (2-NM)

## But (single goal)

Prouver, par un test PlayMode vert, que le harnais `MultiClientGameFixture` peut porter une **interaction de
pouvoir entre deux joueurs** jusqu'à **observation sur la réplique d'un vrai client distant** — pas seulement sur le
serveur/host. C'est la preuve de faisabilité du chantier "tests d'intégration réseau réels"
(cf. `investigations/build-integration-tests-feasibility.md`).

## Pourquoi ce scénario

`CorruptionTests` (existant) tourne en `StartHost` seul → host==server, RTT=0 : il prouve la logique serveur
(`target.isCorrupted.Value == true`) mais **jamais** que l'effet réplique vers un client. C'est l'angle mort qui a
caché les régressions CursedVision/Embrace (mémoire `project_powers_poco_v2_complete`). Le proto comble exactement
cet écart pour la corruption.

**Fidélité :** l'effet `CorruptPlayer` de `CorruptingMarkDecision` est réalisé par `CorruptPlayerExecutor`
(`Assets/Scripts/Characters/Powers/Runtime/Executors/CorruptPlayerExecutor.cs:17-19`) qui appelle
`characterManager.GetCharacter(slot).CorruptPlayerServerRpc()`. Piloter `Character.CorruptPlayerServerRpc()`
(`Assets/Scripts/Characters/Character.cs:181-185`) côté host = piloter la **méthode terminale exacte** de la chaîne
décision→dispatch→executor du pouvoir. La couche décision/dispatch au-dessus est déjà couverte host-only par
`CorruptionTests.PCorruptingMark_CorruptsTarget` — le proto n'ajoute QUE la traversée du fil.

## Scope

**IN :** un fichier de test, une classe dérivant de `MultiClientGameFixture`, un `[UnityTest]`. Aucune modification
de code de prod. Aucune modification du fixture.

**OUT de CE proto (Proto A) — Proto B fait séparément, voir ci-dessous :**
- Proto B — spawn d'un vrai `Power` NetworkObject dans le fixture.
- Effets owner-local (LackOfAffection/CursedVision/Embrace via `RunClientDecisionEffects`) — classe de bug la plus
  intéressante à couvrir en 2-NM. Toujours ouverte après Proto B (nécessite `RunClientDecisionEffects` côté client).

## Proto B — RÉSULTAT (2026-07-17, agent Fable 5) — VERT

`PowerObjectReplicationProtoTests.SpawnedPCorruptingMark_FullPipeline_CorruptsTargetOnRemoteClientReplica` : spawn
d'un **vrai `PCorruptingMark`** dans le fixture 2-NM, pipeline complet (OnCardClickedRpc → `CorruptingMarkDecision` →
dispatcher → executors), corruption observée sur la réplique client du target. **5/5 verts** avec Proto A + les
self-tests fixture (vérif indépendante) ; 32/32 sur tout le namespace Desingleton (agent).

**CORRECTION de mon hypothèse (Proto A scoping) :** le "mur CompositionRoot-par-NM" **n'existait pas**.
`CompositionRoot.For(nm)` est une **struct sans état** qui délègue aux registres per-NM
(`CharacterManager.For(nm)` / `GameManager.For(nm)`) — aucune instance root nécessaire ; c'est documenté dans le doc
de classe de `CompositionRoot.cs`. HostCm/ClientCm déjà enregistrés par le fixture → l'Assert de
`Power.OnNetworkSpawn:158` passe sur les 2 répliques sans rien ajouter. Le vrai blocage était `RoleTargetSystem.instance`
(singleton non-dé-singletonisé) déréférencé par `NewTargetingExecutor` → résolu en spawnant un vrai RTS dans les 2 NM.

**Fichiers Proto B :** `Assets/Scripts/Tests/PlayMode/Desingleton/PowerObjectReplicationProtoTests.cs` (nouveau) +
extension **additive** de `MultiClientGameFixture.cs` (hook `virtual BuildExtraNetworkPrefabs`, défaut vide → zéro
impact fixtures existants ; 2 helpers private→protected). Pattern réutilisable pour spawner n'importe quel
NetworkBehaviour de prod dans le 2-NM. Non committé (décision Poyo).

## Fichier

`Assets/Scripts/Tests/PlayMode/Desingleton/PowerEffectReplicationProtoTests.cs`
(même dossier + asmdef que `MultiClientGameFixtureTests`, donc dépendances déjà résolues).

Créer via Unity `create_script` (mémoire `reference_unity_silent_compile_exclusion` : PRÉFÉRER create_script pour un
nouveau .cs, sinon exclusion silencieuse de l'assembly possible).

## Tâche (ordonnée)

1. Créer la classe `PowerEffectReplicationProtoTests : MultiClientGameFixture` (namespace
   `Tests.PlayMode.Desingleton`).
2. Ajouter `[UnityTest] IEnumerator CorruptionEffect_OnTarget_IsObservedOnRemoteClientReplica()` :
   - `yield return SpawnRealCharacterForClient(ClientNm.LocalClientId);` — cible possédée par le VRAI client.
   - `ulong _targetId = ClientNm.LocalClientId;`
   - **Given (host) :** `Character _hostTarget = HostCm.GetCharacter(_targetId, false);` → `Assert.IsNotNull`,
     `Assert.IsFalse(_hostTarget.isCorrupted.Value)`.
   - **Given (client) :** attendre la réplique —
     `yield return NetworkTestHelper.WaitUntilOrTimeout(() => ClientCm.GetCharacter(_targetId, false) != null, 5f, "...");`
     puis `Character _clientTarget = ClientCm.GetCharacter(_targetId, false);` → `Assert.IsNotNull`,
     `Assert.IsFalse(_clientTarget.isCorrupted.Value, "baseline client")`.
   - **When (host, effet réel du pouvoir) :** `_hostTarget.CorruptPlayerServerRpc();`
   - **Then (le fil) :** `yield return NetworkTestHelper.WaitUntilOrTimeout(() => _clientTarget.isCorrupted.Value, 5f, "corruption n'a pas traversé le fil vers la réplique client");`
   - **Sanity :** `Assert.IsTrue(_hostTarget.isCorrupted.Value, "host");` + confirmer que l'assertion charnière est
     bien lue sur `_clientTarget` (réplique distincte, pas l'objet host — `Assert.AreNotSame(_hostTarget, _clientTarget)`).
3. Compiler : `read_console` (filtrer Error) jusqu'à 0 erreur.
4. `run_tests` (PlayMode, filtrer sur `PowerEffectReplicationProtoTests`) → vert.

## Acceptance Criteria

- **AC1 — baseline :** *Given* une cible fraîchement spawnée possédée par le client, *When* aucun effet n'est
  appliqué, *Then* `isCorrupted.Value == false` sur la réplique host ET sur la réplique client.
- **AC2 — traversée du fil :** *Given* la cible répliquée sur le client, *When* le host appelle
  `CorruptPlayerServerRpc()`, *Then* dans les 5 s `ClientCm.GetCharacter(targetId).isCorrupted.Value == true`
  (observé sur la réplique CLIENT, distincte de l'objet host).
- **AC3 — répliques distinctes :** `_hostTarget` et `_clientTarget` ne sont pas le même objet
  (`AreNotSame`) — la preuve porte bien sur une traversée réseau, pas une lecture host déguisée.
- **AC4 — vert isolé :** le test passe seul ET dans la suite PlayMode (pas de fuite de port/singleton — le fixture
  gère déjà teardown).

## Validation

- `run_tests` PlayMode filtré = vert (preuve auto).
- Anti-régression : la suite PlayMode complète reste verte (fixture teardown déjà éprouvé).
- Contre-preuve (mutation, manuelle/optionnelle) : remplacer `CorruptPlayerServerRpc()` par une écriture host-locale
  non répliquée doit faire ÉCHOUER AC2 mais passer un équivalent host-only — confirme que le test discrimine bien la
  traversée du fil (ne pas committer la mutation).

## Notes

- Pas de commit sans OK explicite de Poyo (mémoire `feedback_no_commit_without_authorization`). Si commit validé :
  folder ce spec + le test dans le même commit (mémoire `feedback_commit_generated_md_artifacts`).
- Piloter les assertions depuis le host / lire la réplique côté client (mémoire
  `reference_isowner_unreliable_multi_nm_tests`).
