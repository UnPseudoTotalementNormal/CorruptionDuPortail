---
title: "Système d'icônes privées sur la CharactersBar"
type: 'feature'
created: '2026-07-20'
status: 'done'
branch: 'feat/targeting-icons'
baseline_commit: '509793e1'
source: 'Discord thread 1528819335815237633 (Poyo, 2026-07-20)'
context: ['{project-root}/_bmad-output/project-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Un pouvoir n'a aucun moyen de poser une marque visuelle sur un joueur. Le seul overlay existant sur une vignette de la `CharactersBar` est `corruptedOverlayImage`, codé en dur pour un cas unique — chaque nouvel effet visible devrait rajouter son champ et sa logique dans `CharactersBarObject`.

**Approach:** Un canal générique « icône de pouvoir posée sur un joueur, visible d'un seul spectateur ». Le pouvoir déclare une icône ; le serveur enregistre le marqueur (icône, joueur marqué, spectateur) et ne l'envoie **qu'au spectateur**. La vignette du joueur marqué affiche l'icône en bas-gauche, empilée si plusieurs, dépliée au survol.

L'icône identifie **le pouvoir qui a produit l'information**, pas l'état du joueur marqué : « mon pouvoir a marqué cette personne, dans ma vue à moi ».

Premier consommateur prévu : le nouveau passif de l'Orpheline — **hors périmètre**. On livre le système, pas le pouvoir.

## Boundaries & Constraints

**Always:**
- **Toutes les icônes sont privées** : un marqueur a exactement **un** spectateur. Le serveur seul détient la liste complète.
- Les clients ne reçoivent que leurs propres marqueurs, par RPC ciblé enveloppé dans `GetSafeRpcTarget`.
- Manager né **sans `static instance`** — registre `For(nm)`, sur le modèle `AvatarManager`.
- Logique de placement **pure** (POCO Domain, EditMode), affichage en adaptateur mince.
- Le dépliage réutilise le survol existant de `CharactersBarObject` : mêmes événements, même durée (0.35s), même easing (`Ease.OutQuint`).
- Aucun mécanisme purement Canvas pour l'empilement (les vignettes deviendront des objets 3D) : placement par offsets locaux explicites.
- Champ sérialisé ajouté en **append** seul, câblé sur chaque instance avant commit.

**Ask First:**
- Toute icône visible par plus d'un joueur — le modèle est mono-spectateur par décision explicite.
- Tout changement du geste de lecture (le survol est arbitré).

**Never:**
- **Jamais de `NetworkList` ni de RPC `SendTo.Everyone` pour les marqueurs** — ce serait la fuite d'information centrale (§Design Notes).
- Pas de nouveau `static instance` (le census-guard rougit).
- Ne pas toucher `RoleTargetSystem` : ce chantier ne consomme pas la donnée de ciblage.
- Ne pas livrer la suppression du tooltip et la pose des icônes dans le même commit.

## I/O & Edge-Case Matrix

| Scénario | Entrée / état | Comportement attendu | Gestion d'erreur |
|---|---|---|---|
| Pose d'une icône | marqueur (icône I, marqué M, spectateur V) | **Seul V** voit I sur la vignette de M | N/A |
| Spectateur = host | `V == ServerClientId` | Livraison locale, pas de RPC | N/A |
| Spectateur = bot simulé | `clientId >= 100` | `GetSafeRpcTarget` intercepte, le host traite | N/A |
| Deux pouvoirs, même joueur, même spectateur | 2 marqueurs distincts | 2 icônes empilées avec décalage | N/A |
| Marqueur en double exact | même (I, M, V) | **Un seul** marqueur conservé | Dédoublonnage silencieux |
| Débordement | plus d'icônes que la limite visible | Pile + compteur « +X » ; dépliage au survol | N/A |
| Début d'awakening | marqueurs mixtes | Seuls les `ClearAtAwakeningStart` sont purgés ; les `Persistent` survivent | N/A |
| Reconstruction de la barre | `ResetCharactersBar` | Les icônes du spectateur sont ré-appliquées | N/A |
| Client rejoint en cours | late-joiner | Reçoit sa propre tranche à la connexion | N/A |
| Pouvoir sans sprite | `barIcon` null | Aucune icône affichée | Aucun log d'erreur — cas normal |
| Sortie de survol pendant dépliage | hover exit | Repli dans le même easing, pas d'état bloqué | `DOKill` avant re-tween |

</frozen-after-approval>

## Code Map

- `Assets/Scripts/Avatars/AvatarManager.cs:29-57` -- **le modèle à copier** : registre `s_byNetworkManager` + `For(nm)`, reset `SubsystemRegistration` sous `#if UNITY_EDITOR`, désinscription par valeur (`:173-188`), triple teardown.
- `Assets/Scripts/Network/LobbyPlayerInfoHolder.cs:44-56` -- pattern de sync late-joiner : `OnClientConnectedCallback` côté serveur + appel inline pour le host.
- `Assets/Scripts/Characters/Powers/Power.cs:237-269` -- RPC ciblé mono-client : court-circuit host puis `[Rpc(SendTo.SpecifiedInParams)]` + `GetSafeRpcTarget`.
- `Assets/Scripts/Characters/Powers/Runtime/IEffectExecutor.cs:12-16` -- contrat des executors (interface directe, **pas** de base générique).
- `Assets/Scripts/Characters/Powers/Runtime/EffectRuntime.cs:11-22` -- porte le `NetworkManager` ; c'est par là qu'un executor atteint un manager.
- `Assets/Scripts/Characters/Powers/Runtime/Executors/NewTargetingExecutor.cs` -- executor complet le plus court, à calquer.
- `Assets/Scripts/Domain/CardLayout.cs:20-27` -- précédent exact de POCO géométrique pur : renvoie `CardPlacement` (floats nus), l'adaptateur fabrique le `Vector3`.
- `Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs:58-60` -- `onCharacterBarObjectHovered` / `onCharacterBarObjectUnhovered` **existent déjà** ; :80-112 le survol (0.35s, `Ease.OutQuint`) ; :203-212 le tooltip à supprimer ; :32 / :9 / :5 les membres qui en meurent.
- `Assets/Scripts/Board/UI/CharacterBar/CharacterAwakenTimer.cs:16` -- précédent de composant-enfant par vignette.
- `Assets/Prefabs/CharacterBarObject.prefab` -- accueille le nouvel enfant sous `hoverVisual`.
- `Assets/Scenes/GameScene.unity` -- les managers sont des enfants séparés de `---GameLogic---`, chacun avec son `NetworkObject`.

## Tasks & Acceptance

**Execution:**
- [x] `Assets/Scripts/Domain/PlayerIcons/IconStackLayout.cs` -- POCO pur : (nombre d'icônes, replié/déplié, pas de décalage, max visible) → liste de `LayoutPoint` (2 floats, calqué sur `CardPlacement`). Aucun type Unity — l'asmdef Domain l'interdit structurellement.
- [x] `Assets/Scripts/Domain/PlayerIcons/PlayerIconLifetime.cs` -- enum `Persistent` / `ClearAtAwakeningStart`.
- [x] `Assets/Scripts/Domain/EffectDescriptor.cs` -- ajouter `AddPlayerIcon(iconId, markedSlot, viewerSlot, lifetime)` et `RemovePlayerIcon(iconId, markedSlot, viewerSlot)`, immuables et value-equatable comme les briques voisines.
- [x] `Assets/Scripts/GameLogic/PlayerIconManager.cs` -- `NetworkBehaviour`, registre `For(nm)`, **sans static instance**. Détient `Dictionary<ulong, List<Marker>>` côté serveur, dédoublonne, purge à `AwakeningState.onStateStartServer` les seuls `ClearAtAwakeningStart`, pousse chaque tranche par RPC ciblé, et sert le late-joiner via `OnClientConnectedCallback` + balayage des clients déjà connectés.
- [x] `.../Powers/Runtime/Executors/AddPlayerIconExecutor.cs` + `RemovePlayerIconExecutor.cs` -- `IEffectExecutor` direct ; atteignent le manager par `PlayerIconManager.For(runtime.NetworkManager)`. Découverte par réflexion : aucun fichier partagé à éditer.
- [x] `Assets/Scripts/Characters/Powers/Power.cs` -- ajouter `[SerializeField] private Sprite barIcon` en **append**, plus un accesseur public. Laissé null partout (aucun pouvoir n'a de sprite aujourd'hui).
- [x] `Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs` -- composant enfant de `hoverVisual` : s'abonne aux deux événements de survol existants, instancie une icône par marqueur, place via `IconStackLayout`, anime le dépliage en 0.35s / `Ease.OutQuint`, `raycastTarget = false`, se ré-applique après `ResetCharactersBar`.
- [x] `Assets/Prefabs/CharacterBarObject.prefab` + `Assets/Scenes/GameScene.unity` -- ajouter l'enfant `CharacterBarIconStack` sous `hoverVisual` ; ajouter `---GameLogic---/PlayerIconManager` (GameObject dédié + `NetworkObject`). Vérifier chaque référence par relecture.
- [x] `Assets/Scripts/Tests/Editor/IconStackLayoutTests.cs` -- couvrir les lignes de la matrice qui sont pures : 0/1/N icônes, replié vs déplié, débordement au-delà du max visible, pas de décalage respecté.
- [x] `Assets/Scripts/Tests/PlayMode/PlayerIconPrivacyTests.cs` -- **le test critique**, 2 NetworkManagers (host + vrai client) : un marqueur destiné au host n'atteint jamais le client et réciproquement ; late-joiner reçoit bien sa tranche ; bot `clientId >= 100` passe par l'interception host.
- [x] `Assets/Scripts/Board/UI/CharacterBar/CharactersBarObject.cs` -- **dernière tâche, commit séparé** : supprimer le tooltip des rôles devenu obsolète (bloc `:203-212`), plus ce qui en meurt — le champ `hoverTooltipComponent` `:32`, le `using TooltipSystem;` `:9`, le `using Characters.Powers;` `:5`. Déscâbler aussi la référence dans le prefab, sinon Unity journalise un avertissement de sérialisation. La RoleCard au clic remplace ce tooltip.

**Acceptance Criteria:**
- Given deux clients réels en partie, when un marqueur est posé pour l'un d'eux, then l'autre client n'a jamais reçu ce marqueur, ni en RPC ni en état répliqué.
- Given plusieurs icônes sur une vignette, when le joueur la survole, then elles s'écartent dans le mouvement de survol existant, sans seconde animation concurrente ni conflit de tween.
- Given un nouvel awakening démarre, when la purge s'exécute, then les icônes `Persistent` sont toujours là et les `ClearAtAwakeningStart` ont disparu.
- Given la barre est reconstruite en cours de partie, when elle se réaffiche, then les icônes du spectateur sont restaurées à l'identique.
- Given un pouvoir sans sprite pose un marqueur, when le client le reçoit, then rien ne s'affiche et la console reste propre.

## Spec Change Log

### Itération 2 — 2026-07-20, après revue adverse à 3 couches (aveugle / cas limites / conformité)

**Baseline relue :** commit `2c8c30d7` (EditMode 510/510, PlayMode 251/251, console propre).

**Ce qui a déclenché l'amendement.** Quatre défauts structurels, dont trois signalés indépendamment par au moins deux relecteurs. Aucun n'était interdit par la spec — elle était muette dessus. D'où un amendement plutôt qu'un simple correctif.

**Ce qui est amendé** (sections non gelées seulement — Tâches, Notes de design) :

1. **Coexistence hôte / bot simulé.** La ligne « Spectateur = bot simulé » disait « `GetSafeRpcTarget` intercepte, le host traite » sans dire **comment l'hôte garde sa propre tranche**. Comme `IconEntry` ne porte pas le spectateur et qu'il n'existe qu'une seule liste locale, la tranche du bot écrase celle de l'hôte. → Nouvelle exigence : le payload porte le spectateur, et le pair conserve une tranche **par spectateur**, à la manière du `simulationsKnowledge` de `GameInfoRevealer`.
2. **Résolution paresseuse des dépendances.** La spec renvoyait au modèle `AvatarManager` sans exiger de re-tentative. `_characterManager` et `GameManager` résolus une seule fois dans `OnNetworkSpawn` meurent silencieusement sur une course d'ordre de spawn. → Nouvelle exigence : re-résoudre à l'usage, et journaliser l'échec au lieu de retourner en silence.
3. **Ordre d'initialisation de la vue.** `ResetCharactersBar` instancie la vignette **puis** lui assigne son personnage. Le premier `Rebuild` part donc sur un personnage null et rien ne le rappelle. → Nouvelle exigence : la reconstruction est déclenchée par l'assignation du personnage, pas par `OnEnable` seul.
4. **`NetworkManager.Singleton` interdit dans la vue.** Déjà couvert par les règles projet, jamais rappelé dans la spec ; la vue étant un `MonoBehaviour` sans `base.NetworkManager`, la source du NM doit être **désignée explicitement**. → Nouvelle exigence : résoudre via le `NetworkObject` du personnage de la vignette.

**État connu-mauvais que ça évite.** Un système qui passe 761 tests au vert tout en étant, en partie réelle : muet pour tous les clients distants si l'ordre de spawn tourne mal, jamais purgé entre deux nuits, invisible après chaque reconstruction de barre, et corrompant la vue de l'hôte dès qu'un bot est marqué. Les trois pannes sont **silencieuses** et **invisibles en playtest host-only**.

**KEEP — à préserver impérativement en re-dérivation :**
- Le chemin de confidentialité tel quel : un `PushSliceTo` par spectateur, court-circuit local si `viewer == ServerClientId`, `[Rpc(SendTo.SpecifiedInParams)]` + `GetSafeRpcTarget` sinon, `BuildSlice` ne lisant que `_byViewer[viewer]`. Vérifié conforme, et le test 2-NM échoue réellement sur une régression en broadcast.
- `IconStackLayout` et ses tests : POCO pur, bornes clampées, couverture complète. Ne pas y toucher.
- Le registre `For(nm)` : garde de doublon, désinscription **par valeur**, reset `SubsystemRegistration`. Conforme au patron `AvatarManager`, zéro entrée aux guards.
- Le câblage prefab et scène (`IconStack` sous `hoverVisual`, `---GameLogic---/PlayerIconManager` + `NetworkObject`), vérifié en YAML. **Ne pas refaire.**
- Le comportement « pouvoir sans sprite » : rien affiché, console propre.

**Rejeté.** Le cast `(ulong)slot` dans les executors : `NewTargetingExecutor` fait strictement pareil, les slots logiques mappent 1:1 sur les clientIds dans ce codebase. Le relecteur aveugle manquait ce contexte. En revanche, les commentaires contradictoires entre `EffectDescriptor` et l'executor sont à harmoniser.

**Différé** (vers `deferred-work.md`, non causé par ce chantier) : absence de plafond sur la taille de tranche RPC, `Destroy(gameObject)` sur un `NetworkObject` spawné dans la garde anti-doublon (partagé avec `AvatarManager`), marqueurs pointant un joueur déconnecté, et la question de savoir si `iconId = NetworkObjectId` d'un `Power` ouvre un chemin de lecture côté client.

## Tâches — itération 2 (correction ciblée)

Périmètre : **deux fichiers**. Le Domain, les assets, les tests de layout et le câblage sont conformes et hors périmètre.

- [x] `Assets/Scripts/GameLogic/PlayerIconManager.cs` -- (a) `IconEntry` porte le `ViewerClientId` ; le pair conserve une tranche **par spectateur** et la vue lit celle de son identité locale, pour que l'hôte et un bot simulé coexistent sans s'écraser. (b) `_characterManager` et `GameManager` re-résolus à l'usage si null, avec un log explicite en cas d'échec persistant — plus de `return` muet. (c) `Teardown` vide les tables serveur et locale. (d) Garde sur slot négatif / `ulong.MaxValue`.
- [x] `Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs` -- (a) résoudre le `NetworkManager` via le `NetworkObject` du personnage de la vignette, **jamais** `NetworkManager.Singleton`. (b) Reconstruire quand le personnage est assigné, pas seulement sur `OnEnable`. (c) Une seule boucle de résolution à la fois et un seul abonnement, quel que soit le cycle enable/disable. (d) `.Forget()` avec `this.GetCancellationTokenOnDestroy()`. (e) Lire la durée de tween sur `CharactersBarObject.hoverTweenDuration` au lieu d'un champ dupliqué. (f) `DOKill` des icônes clonées au teardown. (g) Le repli anime aussi les icônes qui sortent du champ visible. (h) Le compteur « +X » ne compte que les icônes réellement affichables.
- [x] `Assets/Scripts/Tests/PlayMode/PlayerIconPrivacyTests.cs` -- corriger le test bot : il doit prouver que la tranche propre de l'hôte **survit** au marquage d'un bot, au lieu de graver l'écrasement. Ajouter la couverture de la purge d'éveil **par son câblage réel** et non par appel direct.

**Acceptance Criteria — itération 2 :**
- Given l'hôte possède déjà un marqueur, when un marqueur est posé pour un bot simulé, then l'hôte conserve le sien et les deux tranches coexistent.
- Given une dépendance n'est pas encore enregistrée au spawn, when un marqueur est poussé plus tard, then la dépendance est re-résolue et la livraison aboutit ; si elle échoue durablement, la console le dit.
- Given la barre est reconstruite en cours de partie, when les vignettes reçoivent leur personnage, then les icônes réapparaissent sans attendre un nouveau push ni un survol.
- Given deux NetworkManagers en process, when une vue résout son manager, then elle obtient celui de son propre NetworkManager.

## Design Notes

**La fuite d'information est le vrai risque de ce chantier.** `RoleTargetSystem` réplique déjà sa liste à tous les clients (`ReceiveTargetingDataRpc`, `SendTo.Everyone`, `RoleTargetSystem.cs:70`). Personne ne l'affiche, donc rien ne fuit aujourd'hui. Reproduire ce schéma pour les marqueurs donnerait à chaque joueur la carte complète de qui est marqué par quoi — soit, une fois l'Orpheline branchée, son pouvoir entier offert à la table. **Et la fuite serait invisible en playtest host-only**, le host voyant tout légitimement. D'où le test 2-NM en critère d'acceptation n°1.

**Pourquoi `For(nm)` plutôt que `CompositionRoot`.** Le registre par NetworkManager (modèle `AvatarManager`, story 13.1) ne coûte ni `[SerializeField]` sur le root, ni entrée `SceneWiringGuard`, ni entrée au census-guard. Coût assumé : les deux executors résoudront `PlayerIconManager.For(runtime.NetworkManager)` au lieu de `CompositionRoot.For(...)` comme leurs voisins — seule inconsistance du chantier, documentée ici pour ne pas être « corrigée » par erreur.

**Pourquoi pas de `NetworkList`.** C'est le réflexe naturel pour un état répliqué, et c'est exactement ce qu'il ne faut pas ici : un `NetworkList` va à tout le monde. Le serveur garde un dictionnaire C# ordinaire et pousse des tranches. Corollaire : le piège du late-joiner NGO (liste pré-remplie livrée **sans** `OnListChanged`, cf. `CharacterManager.cs:249-256`) ne s'applique pas — mais il faut le remplacer par une poussée explicite à la connexion.

**Survie au passage 3D.** `IconStackLayout` rend des offsets en unités locales, jamais des `RectTransform`. Quand la vignette deviendra un objet 3D, seul le renderer d'icône change ; le placement et l'empilement sont déjà agnostiques.

## Verification

**Commands:**
- `mcp__UnityMCP__read_console` -- attendu : 0 erreur de compilation après chaque étape.
- `mcp__UnityMCP__run_tests` (EditMode) -- attendu : suite verte + `IconStackLayoutTests`.
- `mcp__UnityMCP__run_tests` (PlayMode) -- attendu : suite verte + `PlayerIconPrivacyTests`.
- `mcp__UnityMCP__run_tests` filtré `DiSeamGuard`, `SceneWiringGuard`, `StaticAbsenceGuard` -- attendu : verts, aucune nouvelle entrée de whitelist.

**Manual checks:**
- **Playtest 2 builds obligatoire** : la confidentialité ne peut pas se valider en host-only, le host voit légitimement tout.
- Lisibilité de la pile au survol et repli propre en sortie de survol.

**Baseline réelle mesurée :** EditMode 510 / PlayMode 251 au commit `509793e1`. (Le « 462 / 47+2 » annoncé initialement provenait d'une note périmée — corrigé après mesure.)

## Suggested Review Order

**La confidentialité — le cœur, et le plus à risque**

- Point d'entrée : un seul chemin d'envoi, une tranche par spectateur, jamais de diffusion.
  [`PlayerIconManager.cs:421`](../../Assets/Scripts/GameLogic/PlayerIconManager.cs#L421)

- La tranche ne lit que la ligne du spectateur — c'est là que la fuite serait née.
  [`PlayerIconManager.cs:428`](../../Assets/Scripts/GameLogic/PlayerIconManager.cs#L428)

- L'entrée porte son spectateur : sans ça, la tranche d'un bot écrasait celle de l'hôte.
  [`PlayerIconManager.cs:73`](../../Assets/Scripts/GameLogic/PlayerIconManager.cs#L73)

**Les pannes silencieuses fermées en itération 2**

- Re-résolution à l'usage : une course d'ordre de spawn ne condamne plus la partie.
  [`PlayerIconManager.cs:188`](../../Assets/Scripts/GameLogic/PlayerIconManager.cs#L188)

- L'abonnement à la purge se retente jusqu'à ce que le GameManager existe.
  [`PlayerIconManager.cs:226`](../../Assets/Scripts/GameLogic/PlayerIconManager.cs#L226)

- Le NetworkManager vient du personnage de la vignette, jamais du Singleton.
  [`CharacterBarIconStack.cs:199`](../../Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs#L199)

- La reconstruction suit l'assignation du personnage, pas `OnEnable` seul.
  [`CharacterBarIconStack.cs:139`](../../Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs#L139)

- Estampille de génération : deux cycles enable/disable ne peuvent plus double-abonner.
  [`CharacterBarIconStack.cs:97`](../../Assets/Scripts/Board/UI/CharacterBar/CharacterBarIconStack.cs#L97)

**Le noyau pur**

- Placement et débordement en floats nus — survit au passage des vignettes en 3D.
  [`IconStackLayout.cs:53`](../../Assets/Scripts/Domain/PlayerIcons/IconStackLayout.cs#L53)

**Les tests qui comptent**

- Prouve que l'hôte garde sa tranche quand un bot est marqué (ce test affirmait le bug avant).
  [`PlayerIconPrivacyTests.cs:298`](../../Assets/Scripts/Tests/PlayMode/PlayerIconPrivacyTests.cs#L298)

- La purge est exercée par son câblage réel, pas par appel direct de la méthode.
  [`PlayerIconPrivacyTests.cs:236`](../../Assets/Scripts/Tests/PlayMode/PlayerIconPrivacyTests.cs#L236)

- Les deux sens de la confidentialité, sur deux NetworkManagers distincts.
  [`PlayerIconPrivacyTests.cs:153`](../../Assets/Scripts/Tests/PlayMode/PlayerIconPrivacyTests.cs#L153)
