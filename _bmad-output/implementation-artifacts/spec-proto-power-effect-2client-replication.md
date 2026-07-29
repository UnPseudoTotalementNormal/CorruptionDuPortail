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

### Review Findings (code review PR #91 — 2026-07-18)

Triage 3 couches (Blind Hunter / Edge Case Hunter / Acceptance Auditor) sur le diff `origin/Dev...test/2nm-power-effect-replication-proto`.

**Décisions requises**

- [ ] [Review][Decision] Seam `IRandomProvider` livré sans consommateur — les 2 seuls changements prod (`PCardsShuffling.cs:34`, `PMarqueHurluberluges.cs:44`) commentent « tests seed it via reflection » mais aucun test du diff ni du repo ne touche `_randomProvider` ; scénarios 248/249/252 du catalogue toujours « bloqué — story d'archi requise » alors que la story est livrée ; le seam est une prise réflexion-only alors que le catalogue R4 exige une couture prod settable. Options : (a) implémenter 248/249/252 dans cette PR, (b) corriger commentaires + re-pin catalogue et livrer les tests plus tard, (c) retirer le seam de la PR.
- [ ] [Review][Decision] Ordonnancement dur R8 du catalogue violé — fan-out tier H livré avant les 2 tests ROUGE (R5) et avant le smoke CI (`unity-tests.yml:20` toujours `if: false`) ; le « bug connu » Awaken→SleepClientRpc est pinné VERT comme comportement actuel sans trace de la confirmation Poyo que le catalogue exige (lignes 74-76, 122). Options : (a) ratifier le re-séquencement (re-pin R8 dans le catalogue + acter la confirmation Awaken), (b) bloquer le merge jusqu'aux ROUGE + smoke CI.
- [ ] [Review][Decision] Lot 1 (test-infra-foundation) incomplet malgré le fan-out — §1c (assert registres Composition/Avatar dans la fixture), §3 (port éphémère : port fixe 7788 conservé ET nouveau port fixe 7794 ajouté par `LateJoinerCharacterListTests`), §6 (matrice test→statiques) non livrés. Options : (a) livrer dans cette PR, (b) re-pin la spec d'infra et différer.

**Patches**

- [ ] [Review][Patch] Fenêtre négative 6 frames structurellement incapable d'échouer à haut fps (RPC part au tick ~33 ms > 6 frames) — remplacer par le pattern marqueur-sur-champ-différent que la même PR établit dans `RevealAsymmetryReplicationTests` ; clarifier le commentaire « replica » (l'objet écrit = objet host via cache Singleton, c'est aussi celui que la décision client lit) [Assets/Scripts/Tests/PlayMode/OwnerLocalEffectBoundaryTests.cs:420]
- [ ] [Review][Patch] Résolution de la réplique client sans attente bornée (KeyNotFoundException / null flake selon framerate) — utiliser le pattern `ResolveClientReplica` du même lot [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterMiscStateReplicationTests.cs:56 ; ClientToServerHealTests.cs:25 ; GoldenSequenceReplicationTests.cs:41]
- [ ] [Review][Patch] `WaitUntilStableOrTimeout` : `Time.deltaTime` scaled → boucle infinie à timeScale=0 (utiliser unscaled) + garde `stableFrames > 0` (0 = succès immédiat sans évaluer la condition) [Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs:120]
- [ ] [Review][Patch] `WaitForTicks` : jamais appelé + nom mensonger (draine des frames, pas des ticks réseau) — supprimer ou implémenter en vrais ticks [Assets/Scripts/Tests/PlayMode/NetworkTestHelper.cs:139]
- [ ] [Review][Patch] `TestStaticReset.ResolveType` : résolution par nom simple `FirstOrDefault` inter-assemblies — collision homonyme = mauvais type nullé en silence, invisible du gate (même résolveur) ; détecter les doublons et fail loud [Assets/Scripts/Tests/PlayMode/Infra/TestStaticReset.cs:62]
- [ ] [Review][Patch] Inventaire `SingletonTypeNames` divergent de la liste du census — ajouter un test de synchronisation avec `StaticSingletonCensusGuardTests.RecordedSurvivors` (spec infra §1a exige la liste partagée) [Assets/Scripts/Tests/PlayMode/Infra/TestStaticReset.cs:30]
- [ ] [Review][Patch] Gate `ResetAll_NullsEveryResolvableSingleton` : skips silencieux (`catch { continue; }`, non-Component) + plancher `dirtied > 0` seulement — exiger la comptabilité complète (dirtied + skippés == inventaire, lister les skippés) [Assets/Scripts/Tests/PlayMode/Infra/FixtureResetTests.cs:66]
- [ ] [Review][Patch] Doc contradictoire sur `ResetAll` : XML doc « every PlayMode fixture teardown should call ResetAll » vs décision opt-in délibérée (commit 46bee1de) — aligner le XML doc + `test-infra-foundation.md` §2/§5 sur l'opt-in [Assets/Scripts/Tests/PlayMode/Infra/TestStaticReset.cs:12]
- [ ] [Review][Patch] Messages de timeout interpolés à l'appel (`$"got {count}"` évalué avant l'attente → diagnostic toujours « got 0 » au timeout) — surcharge `Func<string>` ou message statique [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterStateReplicationTests.cs:204 ; GameLoopClientLifecycleTests.cs:73]
- [ ] [Review][Patch] `GameLoopClientLifecycleTests` : tolère un double-dispatch (pas d'assert `count == 2` post-attente) + commentaire « 42 chars » faux (41) [Assets/Scripts/Tests/PlayMode/Desingleton/GameLoopClientLifecycleTests.cs:60]
- [ ] [Review][Patch] Test 7 heal one-shot : 4 asserts synchrones après les ServerRpc sans yield, contrairement au reste du fichier — ajouter les yields [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterStateReplicationTests.cs:236]
- [ ] [Review][Patch] Tests message-per-turn court-circuitent le mutateur prod (écriture directe des NV au lieu d'invoquer `OnMessageSentRpc` — catalogue 171/172) ; reset 173 via `AwakenCharacterServerRpc` non couvert [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterMiscStateReplicationTests.cs:30]
- [ ] [Review][Patch] R7 violé : assertions lisent la donnée asservie par réflexion (NetworkList privé `networkedCharacters`, champ privé `Power.characterManager`) — passer par InternalsVisibleTo / API publique [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterStateReplicationTests.cs:190 ; LateJoinerCharacterListTests.cs:150 ; PowerObjectReplicationProtoTests.cs:120]
- [ ] [Review][Patch] Deux sémantiques de quiescence coexistent — router les assertions « état-client après quiescence » (dont le scénario d'atomicité 259) par `WaitUntilStableOrTimeout` comme la spec infra §4 l'exige [Assets/Scripts/Tests/PlayMode/Desingleton/CharacterStateReplicationTests.cs:120]
- [ ] [Review][Patch] Catalogue désynchronisé : 179/189/038/048/058/064/073/083/084/159/164 couverts par le diff mais toujours « à implémenter » ; frontmatter « 245 scénarios » pour 300 entrées ; zone fragile #7 (« aucun test late-join ») devenue fausse ; statuts re-pinnés sans qualificatif COVERED-exact/adjacent (061 = adjacent survendu) [_bmad-output/implementation-artifacts/test-scenarios-catalog.md]
- [ ] [Review][Patch] Commentaires périmés livrés : header Proto A contredit Proto B (« the wall that wasn't ») ; GoldenSequence prétend « resets its own statics » (aucun reset) ; label « Catalog I (golden games) » survendu [Assets/Scripts/Tests/PlayMode/Desingleton/PowerEffectReplicationProtoTests.cs:14 ; GoldenSequenceReplicationTests.cs:12]
- [ ] [Review][Patch] `WinningConditionFieldIndifferenceTests` : « EditMode-pure » mais rangé sous Tests/PlayMode + `[Category("PowerDecision")]` erroné — déplacer vers Tests/Editor, corriger la catégorie [Assets/Scripts/Tests/PlayMode/WinningConditionFieldIndifferenceTests.cs:1]
- [ ] [Review][Patch] `LateJoiner_ReceivesEachSeatExactlyOnce` : le nom promet une unicité que l'assertion (HashSet dédupliquant) ne vérifie pas — renommer (CharlistGuardHolds) ou asserter `rawList.Count` [Assets/Scripts/Tests/PlayMode/Desingleton/LateJoinerCharacterListTests.cs:110]
- [ ] [Review][Patch] Assertion chat réduite au `chatId` — n'importe quel message vers la fenêtre Server rend le test vert ; vérifier le contenu [Assets/Scripts/Tests/PlayMode/Desingleton/PowerPipelineClientReplicationTests.cs:200]
- [ ] [Review][Patch] `BuildExtraNetworkPrefabs` : aucune validation d'unicité/non-nullité des `GlobalObjectIdHash` — garde HashSet + refus du hash 0 au moment de l'enregistrement [Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs:203]
- [ ] [Review][Patch] `test-infra-foundation.md` §État périmé (« reste à coder : FixtureResetTests + helper quiescence » — livrés par cette PR) [_bmad-output/implementation-artifacts/test-infra-foundation.md]

**Déférés**

- [x] [Review][Defer] Teardowns des nouvelles fixtures nullent `RoleTargetSystem`/`ChatManager`/`ChainingManager` — risque latent de couplage d'ordre avec les tests leak-dépendants que la NOTE de la fixture de base protège ; suite verte aujourd'hui [Assets/Scripts/Tests/PlayMode/Desingleton/PowerPipelineClientReplicationTests.cs:40] — deferred, risque d'ordre latent, non manifesté
