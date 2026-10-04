# R&D — Parties automatisées : bots, 8 clients, buts précis, piloté par Claude

- **Date :** 2026-10-04
- **Statut :** R&D conclue → recommandation + roadmap. Rien n'est codé.
- **Question (Poyo) :** "faire + de tests avec Unity CLI / MCP / scripts, automatiser au maximum, jusqu'à des
  parties à 8 clients qui se jouent toutes seules, rapidement, sans trop de coût, avec des buts précis à chaque fois."

## Avancement du spike (2026-10-04, même jour)

Décisions Poyo : spike T1 GO · builds T3 autorisés (bornés) · changements prod dev-only acceptés · tests visuels
(captures au bon moment + export de variables) · framework à terme sous forme de package réutilisable.

**Fait (worktree, non committé) :**
- **Instance Unity perso sans dialogues** : `Unity.exe -batchmode -automated -projectPath <worktree>` piloté par le CLI
  (`--project-path`). Preuve : `DisplayDialog` → false / `DisplayDialogComplex` → 1 instantanément en batchmode → aucun
  popup (FMOD, NGO, save…) ne peut bloquer l'automatisation. Rien n'apparaît sur l'écran de Poyo.
- **Seam `ISelectionAutopilot`** sur `SelectionFlowService` (+61 lignes, inerte quand null).
- **`Assets/Scripts/Autoplay/`** (`UNITY_EDITOR || DEVELOPMENT_BUILD`) : `AutoplayDriver` (cerveau : pouvoirs via
  `CanUse/StartUse`, vote, sommeil, portail du Mage, possession de l'acteur), `AutoplaySelectionAutopilot`,
  `AutoplayPolicy` (graine), `AutoplayJournal` (`report.json` + `events.ndjson`), `AutoplayComposition` (preset classique
  du GD, comme la tablette), `AutoplaySession` (déroulé partagé test/build, port libre auto 7850–7899),
  `AutoplayBootstrap` (build dev : `Game.exe -autoplay …`).
- **Export d'état à chaque capture** : `NNN-label.json` (état, jour, identité vue, joueurs + flags + pouvoirs en cours,
  sélecteur ouvert / cartes cliquables / cartes levées / alpha du voile flou, sondes `AddProbe(name, fn)`) — écrit même
  quand le PNG est impossible.
- **Mode visuel du sélecteur** (`visualPicker`) : le vrai picker s'affiche, rafale de captures à t+0/0.1/0.25/0.5/1 s,
  puis survol + clic d'une carte valide via `IPointerEnter/Click`. Vue lecture seule ajoutée à `CardPickerManager`.
- **Test** `AutoplaySoloHostTests` (Explicit) + **script** `tools/autoplay/unityctl.sh`
  (`compile | playmode | editmode | build | play-build | last-run`).

**Résultats :** EditMode 548/548 vert. La partie autoplay boote, héberge, charge GameScene, 8 joueurs, preset classique,
force-start, joue une nuit complète (9–13 pouvoirs, verdicts Correct/Incorrect, 8 votes) en ~30 s réelles.

**Pièges levés en route (consignés en mémoire) :**
- Library copiée depuis le checkout principal ⇒ mapping script↔classe périmé ⇒ prefab `Card` sans
  `TransformCompositorComponent` ⇒ NRE `ShowAllPlayerCards` ⇒ partie figée en `VoteRecapState`. **Artefact
  d'environnement, pas un bug du jeu** (prouvé par instrumentation `[CARDNRE]`, pas par théorie). Réparé par
  recompilation `CleanBuildCache` + réimport des prefabs (129, 0 script manquant).
- Recompiler pendant un run PlayMode = run tué sans rapport, et socket UDP qui fuit ⇒ garde dans le script + port libre auto.
- En batchmode, `WaitForEndOfFrame` ne reprend jamais ⇒ pas de PNG en batchmode ; les captures visuelles se font en
  **build dev fenêtré** (pas de dialogues d'éditeur possibles dans un player).

**Limite d'environnement majeure :** en batchmode, la fin de frame n'arrive jamais (`WaitForEndOfFrame`,
`Awaitable.EndOfFrameAsync`). Le jeu en dépend (`WaitAFrameAndNextGameState` → partie figée en
`TakeDownThePortalState` non activé). ⇒ **Répartition :** éditeur headless = compile / tests unitaires & réseau / builds ;
**parties complètes + captures = build dev fenêtré** (`unityctl.sh build` puis `play-build`).

**Plan "package" (demande Poyo) :** extraire en package embarqué `Packages/com.<org>.autoplay` :
- *Runtime générique* : `AutoplayJournal` (rapport/ndjson), captures + export d'état + sondes, `IAutoplayPolicy`,
  squelette de session (boot → host loopback → attente → détection de blocage → rapport), bootstrap d'arguments de
  build, moteur de rafales de captures, port libre auto.
- *Interface d'adaptation* `IAutoplayGameAdapter` : joueurs, phase courante, fin de partie, démarrage, composition,
  état exporté, tour d'un bot.
- *Outils* : `unityctl.sh` (CLI Unity), lancement/kill de builds, recette d'éditeur headless.
- *Reste dans le jeu* : l'adaptateur CDP (pouvoirs, vote, portail, `SelectionFlowService` autopilot, presets).
Extraction après stabilisation du spike (API connue), pas avant.

**Trouvaille réelle à remonter (code, pas design) :** `VoteRecapState` fait avancer la boucle **serveur**
(`Loop.NextGameState()`) à l'intérieur de son animation **cliente** async ; si l'animation lève, le `catch` logue et la
partie reste bloquée pour toujours. Une erreur de présentation peut donc figer une partie entière.

## TL;DR

**Faisable, et moins cher que prévu : la moitié des briques existe déjà.** La pièce manquante est un **cerveau de bot**
(`BotBrain`) qui joue à la place d'un humain en passant par **les mêmes chemins de code que l'UI**. Une fois écrit, il
sert à trois échelles :

| Tier | Quoi | 8 joueurs ? | Durée / partie | Lancement par Claude |
|---|---|---|---|---|
| **T1 — Solo-host autoplay** ⭐ | 1 process (test PlayMode), vraie `GameScene`, host + 7 bots simulés (id ≥ 100), `timeScale` ×10 | Oui (logique) | ~30–90 s | `unity command run_tests --filter Autoplay` |
| **T2 — N-NM in-process** | `MultiClientGameFixture` étendu de 1 à 7 vrais clients loopback | Oui (réplication) | ~5–20 s | idem |
| **T3 — Multi-process headless** | 1 host + 7 clients = 8 builds dev `-batchmode -nographics`, vrai réseau UTP loopback | Oui (réel) | ~1–3 min | script `tools/autoplay/run.ps1` + `unity command --runtime-path` |
| T0 — Simulateur Domain (option) | POCO pur (EditMode) : des milliers de parties/s | Oui (abstrait) | ms | `run_tests --mode EditMode` |

**Recommandation : commencer par T1** (le plus gros gain pour le moins d'effort, et ça reste un *test* → compatible avec
la règle "Claude ne lance pas de play mode"). T3 = l'objectif "8 vrais clients", à faire ensuite car il demande une
**exception explicite à la règle "pas de build par Claude"** (voir § Décisions).

**Rejeté :** MPPM (Multiplayer Play Mode) pour l'automatisation, et le pilotage pixel (`simulate_pointer`, computer-use).

---

## 1. Ce qui existe déjà (inventaire vérifié)

| Brique | Où | Pourquoi c'est utile |
|---|---|---|
| **Bots simulés** (clientId ≥ 100) | `CharacterManager.SpawnSimulatedPlayer()` `CharacterManager.cs:402`, F1 dans `DevIdentityController` | Le host joue déjà 8 identités dans **un seul process**. `GetSafeRpcTarget` / `IsLocalOrSimulated` interceptent les RPC des bots. |
| **Possession d'identité** | `CharacterManager.SetPossessedIdentity` (F2/F3) | `GetLocalClientId()` suit la possession → le vote (`VoteState.OnPlayerVoted`, qui envoie `GetLocalClientId()`) marche déjà pour un bot. |
| **Seam de sélection des cibles** | `ISelectionFlowService` (`UI/BoardUI/Selection/`) — tous les pouvoirs passent par `selectionFlowService.Start*Selection(validator, callback)` | **Le point d'accroche clé.** Un `AutoSelectionFlowService` choisit une cible valide (via le *même* `validator`) et appelle le callback → le pouvoir suit exactement le chemin humain, sans clic. |
| **Validation des cibles** | `Power.GetValidTargets()`, `CheckIsTargetValid`, `CanUse()` (`Power.cs:294-329`) | Le bot ne joue que des coups légaux, sans dupliquer les règles. |
| **Snapshot d'état lossless** | `GameSnapshotBuilder.FromLiveState` (`GameLogic/Snapshot/`) | Hash d'état par client à chaque transition → **détecteur de désync** host vs clients. |
| **Harnais 2-NM** | `MultiClientGameFixture` (host + vrai client loopback UTP, hook `BuildExtraNetworkPrefabs`) | Base de T2 : passer de 1 à N clients. |
| **Domain POCO** | `GameLoopMachine`, `VictoryEvaluator`, `VoteTally`, `ChainingResolver`, `RoleDistributor`, 22 `IPowerDecision`, `SeededRandomProvider` | Base de T0 ; et graine déterministe pour rejouer une partie. |
| **Timers en temps scalé** | `AwakeningState`, `VoteState`… utilisent `Time.deltaTime` ; `UniTask.Delay` est scalé par défaut | `Time.timeScale = 10` accélère la partie sans toucher au code. Le tick NGO reste en temps réel → le réseau n'est pas faussé. |
| **Unity CLI → builds en cours d'exécution** | `com.unity.pipeline` : serveur runtime dans les **builds dev** (`enableInBuilds`, ports 7900–7949, token), `unity command --runtime-path <portfile>` | Claude peut **inspecter / piloter chaque process de T3 en direct** (`runtime_status`, `console`, `set_timescale`, `quit`) + **commandes custom** `[CliCommand]` (ex. `autoplay_snapshot`, `autoplay_act`). |
| **Network simulator** | `com.unity.multiplayer.tools` 2.2.8 installé ; UTP debug simulator | Tests sous lag / perte de paquets. |
| **Catalogue de 300 scénarios** | `_bmad-output/implementation-artifacts/test-scenarios-catalog.md` | Réservoir de "buts précis" à convertir en scénarios. |
| **Machine** | Ryzen 7 8845HS 8c/16t, 31 GB RAM | 8 process headless (~0,3–0,7 GB chacun, à mesurer) tiennent largement. |

## 2. Options évaluées

| Option | Verdict | Raison |
|---|---|---|
| **T1 Solo-host autoplay** | ✅ **Priorité 1** | Vraie scène, vraies `GameState`, vrais pouvoirs ; 1 process ; aucun build ; lancé comme un test. Ne teste pas le fil réseau des bots (interceptés). |
| **T2 N-NM in-process** | ✅ Complément | Teste la *réplication* à 8 sur le fil, mais sur prefabs minimaux (pas la `GameScene`). Extension directe d'un harnais déjà vert. |
| **T3 Multi-process headless** | ✅ **Objectif "8 vrais clients"** | Seul tier qui teste la vraie `GameScene` × 8 sur le vrai réseau. Coût : un build dev + contournement Lobby/Relay. |
| T0 Simulateur Domain | 🟡 Optionnel | Ultra-rapide (fuzz, invariants), mais il faut un interpréteur `EffectDescriptor` → état ; les effets restent partiellement engine-coupled. Utile plus tard pour des stats (taux de victoire = info pour le GD, **jamais** une décision de design). |
| MPPM 2.0.2 (Play Mode Scenarios) | ❌ Pour l'auto | Max **4 éditeurs + 4 builds**, clones d'éditeur à plusieurs GB chacun, piloté par GUI, exige le play mode. Garder pour les playtests manuels de Poyo. |
| Pilotage pixel (`simulate_pointer`, computer-use, screenshots) | ❌ | Lent, fragile, cher en tokens. Seul usage gardé : captures d'écran ponctuelles d'un client non headless pour vérifier l'UI (T4 optionnel). |
| NGO `NetcodeIntegrationTest` (framework officiel) | 🟡 Alternative T2 | Gère N clients + scènes in-process, mais le projet a délibérément choisi le harnais maison (story 5.0). Ne pas mélanger sans raison. |

## 3. Architecture cible : une seule pièce commune, trois échelles

```
            Scenario (JSON)  ──►  BotBrain × N  ──►  chemins de code "humains"  ──►  EventLog (NDJSON [AUTOPLAY])
            (but + rôles +        (policy :           (StartUse → ISelectionFlowService,     │
             graine + attentes)    scripted / random    OnPlayerVoted, SleepCharacterServerRpc,│
                                   / passive / chaos)   isReady)                               ▼
                                                                                    Oracles → report.json (compact)
                                                                                             │
                                                                                     Claude lit, trie, corrige
```

### 3.1 `BotBrain` (par identité)
- S'abonne aux transitions de `GameState` de son NM.
- **Lobby** → passe `isReady`. **Awakening** (si réveillé) → choisit un pouvoir `CanUse()`, `StartUse()`, puis
  `SleepCharacterServerRpc()`. **Vote** → `OnPlayerVoted(cible)` sous possession. **Fin** → log du résultat.
- Une **policy** décide des choix : `Scripted` (étapes du scénario), `RandomValid` (graine), `Passive` (ne fait rien →
  teste les timers), `Chaos` (actions + déconnexions).

### 3.2 `AutoSelectionFlowService`
- Implémente `ISelectionFlowService`. Reçoit le `validator` du pouvoir → la policy choisit parmi les cibles valides →
  appelle le callback (`Action<Character>`, `Action<Character, Role>`, multi-sélection…).
- **Changement prod minimal :** aujourd'hui `CompositionRoot` et `Power.selectionFlowService` exposent le type concret
  `SelectionFlowService` (`CompositionRoot.cs:116`, `Power.cs:109`). Deux options : (a) un *autopilot* enregistrable
  dans `SelectionFlowService` qui court-circuite l'UI quand il est présent (1 fichier) ; (b) typer en
  `ISelectionFlowService` (déjà anticipé "Epic 11 may narrow"). Recommandé : (a), sous `UNITY_EDITOR || DEVELOPMENT_BUILD`.

### 3.3 Scénario = "le but précis"
```json
{
  "id": "etreinte-chaine-cible-visible-partout",
  "goal": "Une Étreinte la nuit 1 enchaîne bien la cible, et TOUS les clients le voient",
  "players": 8, "seed": 4242, "timeScale": 10,
  "roles": { "seat0": "Mage", "seat3": "Elu", "...": "random-valid" },
  "defaultPolicy": "RandomValid",
  "script": [ { "night": 1, "seat": 0, "power": "Etreinte", "target": 3 } ],
  "expect": [
    { "after": "night:1", "on": "all-clients", "assert": "seat3.isChained == true" },
    { "game": "endsWithinDays", "value": 6 },
    { "logs": "noErrors" }
  ]
}
```

### 3.4 Oracles (toujours actifs) + attentes du scénario
- **Universels :** aucune `Exception`/`LogError` ; la partie atteint `GameEndingState` en ≤ N jours ; aucun état
  bloqué > T s ; règles de composition respectées ; `VictoryEvaluator` cohérent avec le snapshot.
- **Désync (T2/T3) :** chaque process logue le hash de `GameSnapshot` à chaque transition ; l'orchestrateur compare
  host vs chaque client. **C'est le détecteur le plus précieux** : il attrape les bugs "marche en host-only, casse
  en vrai client" (régressions CursedVision/Embrace, NetworkList late-joiner…).
- **Spécifiques :** les `expect` du scénario.

### 3.5 Rapport compact (économie de tokens)
`report.json` = résumé + uniquement les échecs (étape, attendu/obtenu, 20 lignes de log autour). Les logs complets
restent sur disque ; Claude les `grep` sur le tag `[AUTOPLAY]` seulement si besoin.

## 4. Détail par tier

### T1 — Solo-host autoplay (priorité 1)
- **Forme :** un test PlayMode `AutoplayTests` paramétré par scénario. Charge la vraie `GameScene` (BootScene + NM),
  `StartHost` en UTP loopback sur un **port dédié UDP 7850–7899** (jamais 7777/7788), spawn 7 bots simulés, attache un
  `BotBrain` par identité, `Time.timeScale = 10`, joue jusqu'à la fin, évalue les oracles.
- **Lancé par Claude :** `unity command run_tests --mode PlayMode --filter Autoplay --timeout 600` → compatible avec
  `feedback_no_playtest_by_claude` (c'est un test, pas un playtest).
- **Attrape :** blocages de boucle de jeu, NRE dans les états / pouvoirs, enchaînements de pouvoirs, conditions de
  victoire en contexte réel, régressions d'ordre de réveil, timers.
- **N'attrape pas :** la réplication vers de vrais clients (bots interceptés).
- **Risque n°1 (à lever au spike) :** aucun test du repo ne charge encore la vraie `GameScene` → dépendances
  probables (UGS init, `LobbyManager`, FMOD, `StatesCanvas`, panels UITK/RT). Mitiger par un flag `Autoplay` qui
  no-op les services cloud.
- **Effort :** spike 1–2 j ; version scénarios + oracles ~3–5 j.

### T2 — N-NM in-process
- Généraliser `MultiClientGameFixture` : `ClientNm` → `ClientNms[ ]` (1..7), ports éphémères, résolution des répliques
  via `ClientNms[i].SpawnManager.SpawnedObjects[netId]` (cf. `reference_char_replica_resolution_multi_nm`).
- Rejouer les scénarios de réplication existants à 8 clients + late-joiner + déconnexions en cascade.
- **Effort :** ~2–3 j. **Limite connue :** prefabs minimaux, pas la `GameScene` ; singletons non dé-singletonisés à
  spawner dans chaque NM.

### T3 — 8 vrais process headless
- **Build :** Windows dev (Development Build + `enableInBuilds` pipeline) dans `Builds/Autoplay/` (gitignored).
- **Lancement :** `Game.exe -batchmode -nographics -autoplay host|client -port 7860 -scenario X.json -seed N -logFile …`.
- **Contournement Lobby/Relay (dev only) :** `-autoplay` → `UnityTransport.SetConnectionData("127.0.0.1", port)` +
  `StartHost`/`StartClient` directs, sans UGS. `LobbyState.SetCloudLobbyLocked` doit no-op sans lobby. Garde la règle
  "tests toujours sur UTP" (`project_test_transport_always_utp`).
- **Orchestrateur** `tools/autoplay/run.ps1` : vérifie que les ports sont libres → lance 8 process → attend la fin ou
  un timeout → collecte les NDJSON → oracles + désync → `report.json` → **tue tous les process** (Job Object / tag),
  même en cas d'échec (évite le flake de port 7777 déjà vécu).
- **Pilotage live par Claude :** chaque process écrit son port pipeline ; `unity command --runtime-path <portfile>
  autoplay_snapshot` / `autoplay_act` / `set_timescale` / `console --level error` → Claude peut inspecter un client
  précis en pleine partie, ou injecter une action ciblée.
- **Bonus :** lag/perte via le simulateur UTP (`-netsim delay=120,jitter=30,drop=2`) ; chaos = tuer un client en
  pleine partie → vérifie la politique "quit = CHAIN" + liveness.
- **Risques :** FMOD sans périphérique audio (forcer la sortie NOSOUND en autoplay), UITK/RenderTexture en
  `-nographics`, mémoire réelle par process (à mesurer), durée du build (~5–10 min à froid, incrémental ensuite).
- **Effort :** ~4–6 j après T1 (le `BotBrain` est réutilisé tel quel).

### T0 — Simulateur Domain (plus tard)
Boucle POCO : `RoleDistributor` → nuits (`IPowerDecision` + état fake) → `VoteTally` → `ChainingResolver` →
`VictoryEvaluator`. Des milliers de parties par graine en EditMode : fuzz d'invariants, crash-free, *stats
informatives* pour le GD. Prérequis : un interpréteur `EffectDescriptor` sur un état pur.

## 5. Rôle de Claude : la boucle de travail

1. **Poyo donne un but** ("vérifie qu'un Ugues qui vole l'Étreinte peut chaîner quelqu'un, vu par tous").
2. **Claude écrit le scénario** (JSON) et choisit **le tier le moins cher** qui prouve le but (T1 si c'est de la
   logique, T2/T3 si c'est de la réplication).
3. **Claude lance** (`run_tests` ou `run.ps1`), **lit `report.json`**, et en cas d'échec : inspection live
   (`--runtime-path`), lecture des logs `[AUTOPLAY]`, investigation, correctif + test de non-régression.
4. **Le scénario est versionné** (`Assets/Tests/Autoplay/Scenarios/`) → il devient un test de régression permanent.
5. **Nightly (plus tard) :** balayage de N graines `RandomValid` + `Chaos` → rapport (post Discord seulement avec ton OK).

Skill proposé : `/autoplay <but>` (EN+FR, auto-trigger) qui encapsule 2→4.

## 6. Coûts

| Poste | T1 | T2 | T3 |
|---|---|---|---|
| Temps machine / partie | 30–90 s | 5–20 s | 1–3 min (+ build) |
| RAM | 1 éditeur | 1 éditeur | ~3–6 GB (8 process, à mesurer) |
| Tokens Claude / run | faible (rapport compact) | faible | faible ; plus si inspection live |
| Effort dev initial | 1–2 j spike, 3–5 j complet | 2–3 j | 4–6 j |
| Coût cloud | 0 | 0 | 0 (pas de Relay/UGS en autoplay) |

## 7. Décisions à prendre (Poyo)

1. **Exception à "pas de build par Claude"** pour T3, bornée : builds dans `Builds/Autoplay/`, ports UDP 7850–7899 (le TCP 7800 est pris par le serveur pipeline de l'éditeur)
   uniquement, orchestrateur qui tue tous ses process en fin de run. Sans ce OK, on s'arrête à T1 + T2.
2. **Changements prod dev-only** acceptés : autopilot dans `SelectionFlowService`, arg `-autoplay` (connexion directe),
   no-op des services cloud en autoplay — le tout sous `UNITY_EDITOR || DEVELOPMENT_BUILD`.
3. **Les policies de bots ne sont pas du game design.** `RandomValid` = coups légaux au hasard ; un éventuel taux de
   victoire par composition est une *donnée* pour l'ami GD, jamais une recommandation.

## 8. Roadmap proposée

| Phase | Livrable | Critère de sortie |
|---|---|---|
| **0 — Spike T1** | `AutoSelectionFlowService` + `BotBrain` minimal + 1 test qui joue 1 partie 8 joueurs jusqu'à `GameEndingState` | Test vert, < 2 min, sans erreur de log |
| **1 — Scénarios + oracles** | Format JSON, oracles universels, `report.json`, 5 scénarios tirés du catalogue | Un bug injecté exprès est détecté |
| **2 — T2 à 8 clients** | Fixture N-NM + hash de désync | Réplication 8 clients verte |
| **3 — T3 headless** | Arg `-autoplay`, build dev, `run.ps1`, commandes `[CliCommand]` autoplay | 8 process jouent une partie, rapport sans désync |
| **4 — Industrialisation** | Skill `/autoplay`, nightly multi-graines, lag + chaos, réactivation du CI test-runner (`unity-tests.yml:20 if:false`) | Run nightly autonome |

## 9. Risques et inconnues (à lever dans l'ordre)

1. Charger la vraie `GameScene` dans un test PlayMode (jamais fait ici) → **Phase 0 le prouve ou l'infirme.**
2. Animations DOTween / UI qui conditionnent la boucle de jeu (si en temps non scalé, `timeScale` n'accélère pas tout).
3. Singletons non dé-singletonisés (`RoleTargetSystem`, `ChainingManager`, `ChatManager`…) en T2 → pattern connu
   (`reference_spawn_prod_netbehaviour_in_2nm_fixture`).
4. FMOD + UITK en `-nographics` (T3).
5. Pouvoirs à flux spéciaux (Vision de l'Impossible = devinette, Cartes mélangées = `AskForGuessRoleRpc`, Observation
   = multi-sélection) → chaque type de callback doit être couvert par `AutoSelectionFlowService` ou un petit adaptateur.
