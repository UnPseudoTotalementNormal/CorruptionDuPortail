---
title: Catalogue de scénarios de test — Corruption du Portail
status: catalog
date: 2026-07-17
total: 245
generated_by: 9 agents (inventaire ancré dans le code de prod)
---

# Catalogue de scénarios de test — Corruption du Portail

245 scénarios numérotés (001→245, contigus) couvrant la logique des pouvoirs, la corruption/soin,
les états du Character, le système de reveal, les game states, les conditions de victoire et la
réplication réseau. **Chaque scénario est ancré dans du vrai code** (règle : documenter l'existant,
jamais inventer une règle) — voir la citation `chemin:ligne` dans chaque entrée.

But : donner au jeu une **vraie stabilité** — une fois implémentés, ces tests attrapent tôt les
régressions silencieuses (celles qu'un playtest host-only ne voit pas). ~169 sont `à implémenter`,
77 sont `déjà couvert` (gardés pour la traçabilité, à ne PAS réimplémenter).

## Les 3 couches de test

- **EditMode-pure** — teste une décision pure `IPowerDecision.Decide(ctx)` ou une fonction POCO
  (validator, win-condition oracle, tally) sans NGO. Rapide, déterministe. Pattern : `PowerContext`
  + `FakeRoster`/`FakeState` → `Decide` → asserter les `EffectDescriptor`. **Couche à privilégier.**
- **PlayMode-StartHost** — spawn du vrai Power/manager en `StartHost` (host==server, RTT=0),
  invocation du vrai point d'entrée RPC, assertion de l'état serveur réel. Réf : `CorruptionTests.cs`.
- **PlayMode-2NM** — comportement observé sur la réplique d'un VRAI second client à travers le fil
  (réplication NetworkVariable/NetworkList, RPC serveur→client ET client→serveur, asymétrie de reveal
  owner/target-local). Réf : `Assets/Scripts/Tests/PlayMode/Desingleton/`. Réservée au réellement
  cross-client.

## Catégories & plages

| Plage | Cat. | Zone | Couche dominante |
|---|---|---|---|
| 001–045 | A1 | Décisions corruption / soin (9 pouvoirs) | EditMode-pure |
| 046–090 | A2 | Décisions vision / reveal / chaîne / ciblage (15 pouvoirs) | EditMode-pure |
| 091–115 | B | Mécaniques d'état Character (corrupt/soin/chaîne/bless/élim/éveil/fake) | Pure / StartHost |
| 116–135 | C | Système de reveal (GameInfoRevealer) | Pure / StartHost / 2-NM |
| 136–155 | D | Game states & boucle de jeu | StartHost / 2-NM |
| 156–170 | E | Conditions de victoire (4) | EditMode-pure |
| 171–190 | F | Réplication / RPC 2-NM (le fil) | PlayMode-2NM |
| 191–215 | G1 | Chaque pouvoir en PlayMode StartHost — corruption/soin (RPC + wiring réels) | PlayMode-StartHost |
| 216–245 | G2 | Chaque pouvoir en PlayMode StartHost — vision/chaîne/cible/divers | PlayMode-StartHost |
| 246–265 | H | Seams : déterminisme/seed, atomicité de réplication, frontière serveur/client négative | Pure / 2-NM |
| 266–3xx | I | Interactions & golden games — ordonnancement même-tour, pouvoir volé, info-dans-le-temps, victoire simultanée, parties scriptées *(round b — Samus)* | StartHost / 2-NM |

## Top zones fragiles (prioriser à l'implémentation)

Remontées par les agents pendant l'inventaire — points où une régression passerait silencieuse :

1. **Asymétrie corruption/soin/chaîne** (091-099) — `CorruptPlayerServerRpc` inconditionnel vs
   `HealPlayerServerRpc` one-shot vs `ChainCharacterServer` re-corrompt. Interactions non testées.
2. **Filtres de ciblage reveal-gated** (`TargetUtils`, 112-113) — `corrupted`/`faction` ne filtrent que
   si l'info est révélée ; `blessed`/`chained`/`healed` inconditionnels. Anti-fuite subtil, 0 test.
3. **Scoping reveal réel-vs-bot** (C, 125-128) — store réel NON scopé par observateur (asymétrie = ciblage
   RPC) vs bots ≥100 scopés. Une inversion fuite des rôles, invisible en StartHost.
4. **`VoteState.CanVote` / auto-clôture** (D, 147-151) — dénominateur d'électeurs (parti/éliminé/déjà-voté)
   déjà perdu une fois (fakify→chaining). Riche logique serveur non testée.
5. **Transition de `VictoryConditionCheckState`** (D, 154-155) — l'évaluateur pur est golden-testé, la
   transition qu'il pilote (fin de partie vs boucle) ne l'est pas.
6. **`WOmniscienceHackedCharacter`** (E, 168-170) — NRE owner-absent, modèle auto-hack design-flagué.
7. **Late-joiner NetworkList #3280** (F, 179) — la garde `[CHARLIST]` n'a pas de filet 2-NM (aucun test ne
   connecte un client APRÈS un spawn). Le trou le plus dangereux.
8. **Reveals `broadcast=true` par-pouvoir** (F, 181-184) — CorruptionInsight/Omniscience/ChainedByShadows/
   Blessing : chemin ciblé `SendRevealLevelRpc` jamais testé via le pipeline réel jusqu'à un vrai client.
9. **`MarqueHurluberluges` (vol de pouvoir)** (G2, 222-226) — spawn/reparent async + source aléatoire, cœur
   du rôle Uges, 0 test réel.
10. **`PDroolyHealing`** (G1, 214) — reveal+`healedCharactersThisNight` découplés du soin réel ; fusionner les
    gardes = régression invisible. (Seul pouvoir sans décision pure extraite.)

## Bug possible remonté (à vérifier, non tranché)

`Character.AwakenCharacterServerRpc` appelle `SleepCharacterClientRpc()` et non `AwakenCharacterClientRpc()`
(`Character.cs:145`) → `onCharacterAwakened` n'est jamais déclenché par cette voie. Comportement **pinné tel
quel** (scénario 105 = `onCharacterSleep`), pas qualifié de bug — à confirmer avec Poyo.

## Révision post-party-review (Murat / Cloud Dragonborn / Link Freeman) — 2026-07-17

Le catalogue brut optimisait la *complétude* ; la review a imposé de l'orienter *risque + contrat + infra*.
Décisions ratifiées, à appliquer AVANT et PENDANT l'implémentation :

### R1 — Assertion sur le CONTRAT, jamais sur la ligne
Le `chemin:ligne` de chaque entrée est de la **provenance pour l'auteur du test**, PAS une cible d'assertion.
Chaque test assert sur la **valeur de sortie du contrat** : les `EffectDescriptor` retournés par `Decide` (égalité
structurelle), l'état-client-après-quiescence, un `RevealLevel`, une NetworkVariable/NetworkList répliquée. Jamais
« la ligne 142 a été empruntée », jamais un champ privé ou un ordre d'opérations interne. Test de survie :
*« si je réécris l'implémentation en préservant le contrat, ce test reste-t-il vert ? »* — si non, le scénario est
à retravailler. Interdit : recopier la réponse depuis l'implémentation (test tautologique qui ne peut pas échouer).

### R2 — Priorité d'implémentation par RISQUE, pas par numéro
On n'implémente PAS 001→245 dans l'ordre. Tier **H (d'abord)** = les zones fragiles + la couche 2-NM :
091-099, 112-113, 125-128, 147-151, 154-155, 168-170, 171-190, 179 (late-joiner #3280), 214, 222-226, + les
familles 246-265. Tier **M** = 116-135, 136-146, 156-170, StartHost non-triviaux. Tier **L** = décisions-pures
triviales 001-090 (du beurre, à faire en dernier / en masse).

### R3 — Effondrer 191-245 (StartHost par-pouvoir) en CONTRATS PARAMÉTRÉS
La décision pure étant déjà couverte en 001-090, la couche StartHost ne teste que wiring-RPC + mutation-serveur +
broadcast. Ne PAS écrire 55 tests bespoke (55 spawns = 55 sources de flake port-7777 + suite lente que personne ne
lance). Écrire ~5 tests de **contrat** paramétrés par une table `[TestCase]` (un contrat corrupt, un heal, un
reveal, un chain, un steal). Les entrées 191-245 deviennent les LIGNES de ces tables, pas des méthodes.

### R4 — 3 familles de seams manquantes ajoutées (246-265)
- **Déterminisme / seed — RÉSOLU (agent famille)** : le kernel `StolenPowerSelector.SelectDistinct/SelectStealable`
  prend un `IRandomProvider` (`Assets/Scripts/Domain/StolenPowerSelector.cs:22,52`) — port injectable, avec
  `SeededRandomProvider` déjà testé seedé (`StolenPowerSelectorTests.IsDeterministic_ForAGivenSeed`). MAIS le
  pouvoir câble `new UnityRandomProvider()` EN DUR (`PMarqueHurluberluges.cs:101` + `PCardsShuffling.cs:157`) →
  déterminisme **bloqué au niveau intégration**. Scénarios 248/249/252 = `bloqué — story d'archi requise`. Story
  (petite) : exposer un `IRandomProvider` injectable sur le carrier des 2 pouvoirs (défaut `UnityRandomProvider`,
  override seedé en test). **Durcissement Winston** : le seed doit être une couture de PROD *server-authoritative*
  (pas une prise de test) — vérifier que l'aléa est déjà server-only avant d'injecter des 2 côtés, sinon flake
  déterministe déguisé en bug. (Note : l'ordre d'itération du pool candidat EST déjà déterministe — le RNG est la
  seule variable à maîtriser.)
- **Atomicité de réplication** : quand un pouvoir mute N NetworkVariables + 1 NetworkList au même tick serveur,
  asserter la **cohérence de l'état client après quiescence** (tous messages drainés), pas une valeur isolée.
- **Frontière serveur/client négative** : un ClientRpc de mutation d'autorité / un `CanVote` franchi côté client
  DOIT être rejeté/ignoré. « server authority strict » n'est un invariant que s'il est pinné par un test négatif.

### R5 — Écrire ROUGE d'abord les bugs connus
Ne pas figer le présent en oracle. Écrire en tête de file, ROUGES, reproduisant le bug avant tout fix :
`AwakenCharacterServerRpc`→`SleepClientRpc` (`Character.cs:145`) et le NRE owner-absent de
`WOmniscienceHackedCharacter`. À confirmer avec Poyo si ce sont des bugs (design-owned).

### R6 — Comptes séparés + granularité de couverture
Ne PAS présenter « 245 tests ». Trois compteurs distincts : **pure** (~110, coût CI négligeable), **StartHost**
(~55, moyen, à paramétrer en R3), **2-NM** (~20, cher + flake-prone). Le statut `déjà couvert` doit distinguer
**COVERED-exact** (ce Given/When/Then précis est asserté) vs **COVERED-adjacent** (un test touche la mécanique) —
ne pas sauter un cas COVERED-adjacent en croyant qu'il est couvert.

### R7 — Rendre R1 mécanique + auditer la forme du contrat (round b — Winston)
- R1 n'est pas une intention. Un test ne peut asserter QUE sur : (a) la valeur de retour de `Decide`
  (`EffectDescriptor`, égalité structurelle), (b) une `NetworkVariable`/`NetworkList` publique répliquée, (c) un
  état observable via l'API publique du game state. Toute autre cible (champ privé, ordre d'appel interne, log) =
  test disqualifié. La réflexion n'est tolérée que pour NAVIGUER le graphe réseau (localiser une réplique via
  `SpawnManager`), JAMAIS pour lire la donnée asservie.
- **Test-du-test obligatoire** : après la 1ʳᵉ vague, passer une PR de refactor *behaviour-preserving* et EXIGER la
  suite verte. Tout test qui rougit là pinne l'implémentation → à retravailler. On ne débat pas la survie-au-refactor,
  on la démontre.
- **AUDIT PRÉ-R3 (bloquant)** : avant de paramétrer 191-245, auditer si `EffectDescriptor` a une forme UNIFIÉE ou 23
  dialectes par-pouvoir. R3 présuppose une forme stable ; si elle varie par instance, R3 ne peut pas marcher. ~0,5j
  d'audit avant la 1ʳᵉ famille paramétrée.

### R8 — « Infra d'abord » = spec exécutable, pas slogan (round b — Amelia)
La fixture de base n'est pas « verte parce qu'elle compile » — verte quand SON PROPRE test de reset passe. Avant tout
fan-out PlayMode, produire :
1. **Inventaire NOMMÉ et exhaustif des statiques mutables** (grep `.instance` + `static` mutable, domaine + net), avec
   pour chacun : qui le reset et comment (null / re-new / Dispose). Liste FERMÉE dans le fichier — sinon teardown
   faux-par-omission → leak ordre-dépendant non reproductible.
2. **Ordre de teardown load-bearing** : despawn NM → drain callbacks → reset statiques → libération port.
3. **Preuve de port libre** : port éphémère par test OU poll « socket fermé » borné (pas un `yield` au pif → flake
   bind-7777).
4. **Définition UNIQUE de « quiescence »** : une helper partagée (`WaitUntilQuiescent` = N ticks drainés / condition
   explicite). Sans elle, 40 définitions maison = flake n°1.
5. **Test-de-la-fixture** = la gate : salir un statique en test A, asserter propre en B. Infra acceptée seulement si
   ce test passe.
6. **Matrice test→statiques-touchés** : prérequis pour dispatcher par état statique partagé.

Ordonnancement dur : infra squelette → **les 2 ROUGE (R5) sur le 2-NM** (sonde qui prouve que la fixture voit l'état
CLIENT, pas l'objet host) → **réactivation runner CI + smoke 2-NM vert** (story bloquante, owner nommé) → fan-out
tier H. Les ROUGE et le smoke CI sont des bloqueurs ORDONNANCÉS, pas des parallèles.

**Précision R3** (Amelia) : 191-245 = **~5 contrats paramétrés** (`[TestCaseSource]`, **une invocation NUnit = un
pouvoir**, jamais un `foreach` interne qui masque 54 échecs et casse le COVERED-exact de R6) **+ 8-12 tests DÉDIÉS
nommés** pour les branches non-uniformes (CursedVision/Embrace owner-local, Orpheline non-choisie, Mage auto-skip).
AC : partagé par ≥2 pouvoirs → paramétré ; unique → dédié. La liste des dédiés est énumérée, pas laissée à l'auteur.

### R9 — Corrections owner (Poyo) sur 2 prémisses du panel — 2026-07-17
Le panel party-mode a soulevé 2 « trous design-owned » qui reposaient sur une compréhension incomplète des règles.
Poyo (owner du design) tranche = les deux sont des NON-problèmes, à ne pas transformer en tests ni en stories :
- **Ordre soin↔corruption même nuit (scénario 267)** : il n'y a PAS de simultanéité. Les rôles se réveillent
  séquentiellement (`awakeningOrder`) précisément pour l'éviter ; un corrompu est soignable, point. Pas de règle à
  créer, pas de décision design. 267 rétrogradé en pin d'ordre bas-priorité.
- **Victoire simultanée multi-camps (scénario 287)** : INVALIDE — les anomalies sont corrompues de base, l'exemple
  double-win est faux. 287 retiré. **Le GATE « tie-break VictoryEvaluator » proposé par le panel est ANNULÉ** — il
  n'y a qu'un seul seam d'archi réel en tête de file : l'injection `IRandomProvider` (seed), pas deux.
Leçon : le panel adverse est utile pour la rigueur de test, mais la vérité de gameplay = Poyo. Ne pas enshriner une
sur-théorisation du panel comme une règle de jeu.

## Protocole de dispatch (phase d'implémentation) — RÉVISÉ

- **Infra d'abord (non négociable)** : coder UNE fois une fixture/teardown de base partagée (reset de TOUS les
  singletons statiques + despawn NM + libération du port), validée verte, AVANT tout fan-out PlayMode. Domain
  reload OFF → toute liste statique fuit sinon (RoleTargetSystem/ChatManager/ChainingManager.instance,
  `usedBoundByInkIds`, etc.).
- **Dispatch par COUCHE et par état statique partagé**, PAS par 10 numéros contigus (sinon 2 agents avec des
  StartHost qui se marchent dessus sur le reset singleton / le port 7777). **Un seul owner pour tout le 2-NM**
  (harnais loopback fragile — pas 3 personnes dessus).
- **Smoke 2-NM en CI réelle** avant d'en écrire 20 : le test-runner CI est OFF (`unity-tests.yml:20 if:false`) —
  savoir maintenant si ces tests ne tournent qu'en local.
- N'implémenter QUE les `Statut: à implémenter` (~169), par tier de risque R2 (H→M→L). Ceux `déjà couvert` traçés.
- Gotchas connus : réplique client via `ClientNm.SpawnManager.SpawnedObjects` (JAMAIS un registre TryGet-Singleton
  = faux vert qui lit l'objet host) ; `create_script` pour tout nouveau .cs ; CursedVision StartHost exige
  `LogAssert.Expect("No card effect found for ID")` ; ne pas asserter un id de chat littéral (`usedBoundByInkIds`
  statique). Unity MCP séquentiel : `run_tests` est un goulot série, le fan-out d'écriture n'y change rien.

---


### 001 — AutoCorruption corrompt son propre owner
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** AutoCorruptionDecision.Decide émet un unique `CorruptPlayer(ctx.OwnerSlot)` — `Assets/Scripts/Domain/Powers/Decisions/AutoCorruptionDecision.cs:12`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 4)` sans target ni roster
- **When:** appeler `new AutoCorruptionDecision().Decide(ctx)`
- **Then:** `outcome.Accepted == true` et `outcome.Effects` égale exactement `[ CorruptPlayer(4) ]`
- **Stabilité:** empêche que le passif d'auto-corruption cesse de corrompre son porteur au démarrage.
- **Statut:** déjà couvert (PowerDecisionTests.AutoCorruption_CorruptsOwner)

### 002 — AutoCorruption n'émet qu'un seul effet
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** AutoCorruptionDecision.Decide — un seul brick, sans target — `Assets/Scripts/Domain/Powers/Decisions/AutoCorruptionDecision.cs:11-12`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 0)`
- **When:** appeler `Decide(ctx)`
- **Then:** `outcome.Effects.Count == 1` et `outcome.UsesConsumed == 1`
- **Stabilité:** attrape l'ajout accidentel d'un effet parasite (reveal, chat) à un passif censé n'être qu'une corruption.
- **Statut:** à implémenter

### 003 — AutoCorruption est passif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** AutoCorruptionDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/AutoCorruptionDecision.cs:8-9`
- **Couche:** EditMode-pure
- **Given:** une instance `new AutoCorruptionDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == true` et `Id == PowerId.AutoCorruption`
- **Stabilité:** garantit que le power reste déclenché en passif (OnGameStartedServer) et non exposé comme actif.
- **Statut:** à implémenter

### 004 — PAutoCorruption corrompt réellement l'owner au démarrage
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PAutoCorruption.OnGameStartedServer → RunDecisionEffects → CorruptPlayer → Character.CorruptPlayerServerRpc — `Assets/Scripts/Characters/Powers/PAutoCorruption.cs:20` + `Assets/Scripts/Characters/Character.cs:182`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, un `Character` owner spawné (isCorrupted == false) et un `PAutoCorruption` spawné avec `ownerClientId = LocalClientId`
- **When:** appeler `power.OnGameStartedServer()`
- **Then:** `owner.isCorrupted.Value == true`
- **Stabilité:** verrouille le câblage décision→exécuteur→NetworkVariable pour le passif d'auto-corruption.
- **Statut:** déjà couvert (CorruptionTests.PAutoCorruption_CorruptsOwnerAtStart)

### 005 — CorruptingMark émet les 4 effets ciblés dans l'ordre
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptingMarkDecision.Decide — NewTargeting, StoreLastCorrupted, CorruptionSucceeded, CorruptPlayer — `Assets/Scripts/Domain/Powers/Decisions/CorruptingMarkDecision.cs:13-17`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 0, targetSlot: 3)`
- **When:** appeler `new CorruptingMarkDecision().Decide(ctx)`
- **Then:** `Effects` égale exactement `[ NewTargeting(0,3), StoreLastCorrupted(3), CorruptionSucceeded(3), CorruptPlayer(3) ]`
- **Stabilité:** fige l'ordre (ciblage → mémorisation last-corrupted → event succès → corruption) contre tout réordonnancement.
- **Statut:** déjà couvert (PowerDecisionTests.CorruptingMark_TargetsStoresRaisesCorrupts)

### 006 — CorruptingMark est actif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptingMarkDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/CorruptingMarkDecision.cs:9-10`
- **Couche:** EditMode-pure
- **Given:** une instance `new CorruptingMarkDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == false` et `Id == PowerId.CorruptingMark`
- **Stabilité:** garantit que la marque corruptrice reste un pouvoir actif (déclenché par pick), pas un passif.
- **Statut:** à implémenter

### 007 — CorruptingMark route owner et target sur les bons slots
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptingMarkDecision.Decide — `ctx.OwnerSlot` vs `ctx.TargetSlot` — `Assets/Scripts/Domain/Powers/Decisions/CorruptingMarkDecision.cs:13-17`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 2, targetSlot: 6)` (slots distincts)
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects[0] == NewTargeting(2,6)` et `StoreLastCorrupted`/`CorruptionSucceeded`/`CorruptPlayer` portent tous le slot 6 (jamais 2)
- **Stabilité:** empêche une inversion owner/target qui corromprait le lanceur au lieu de la cible.
- **Statut:** à implémenter

### 008 — CorruptingMark sur soi-même émet quand même la liste complète
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptingMarkDecision.Decide — aucune garde de validité dans la décision (précondition adaptateur) — `Assets/Scripts/Domain/Powers/Decisions/CorruptingMarkDecision.cs:12-17`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 5, targetSlot: 5)` (owner == target)
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(5,5), StoreLastCorrupted(5), CorruptionSucceeded(5), CorruptPlayer(5) ]`
- **Stabilité:** documente que la décision est pure (pas de filtre de cible) — le filtrage reste la responsabilité du `targetValidator` côté adaptateur.
- **Statut:** à implémenter

### 009 — PCorruptingMark corrompt la cible cliquée
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PCorruptingMark.OnCardClickedRpc → RunDecisionEffects → CorruptPlayer → Character.CorruptPlayerServerRpc — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:73` + `Assets/Scripts/Characters/Character.cs:182`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, un owner et un target (clientId 999) spawnés, un `PCorruptingMark` spawné avec `ownerClientId = LocalClientId`
- **When:** invoquer `OnCardClickedRpc(target.ownerClientId.Value)`
- **Then:** `target.isCorrupted.Value == true`
- **Stabilité:** verrouille le chemin pick→décision→corruption serveur de la marque corruptrice.
- **Statut:** déjà couvert (CorruptionTests.PCorruptingMark_CorruptsTarget)

### 010 — CorruptionInsight révèle la corruption de chaque personnage dans l'ordre du roster
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.Decide — un `RevealInfo(slot, CorruptRevealed, Personal, owner, true)` par slot — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:16-21`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [0,1,5] }` et `PowerContext(ownerSlot: 0, roster: roster)`
- **When:** appeler `new CorruptionInsightDecision().Decide(ctx)`
- **Then:** `Effects` égale `[ RevealInfo(0,CorruptRevealed,Personal,0,true), RevealInfo(1,...), RevealInfo(5,...) ]` dans cet ordre
- **Stabilité:** garantit que le passif de clairvoyance révèle bien TOUTES les corruptions à l'owner.
- **Statut:** déjà couvert (PowerDecisionTests.CorruptionInsight_RevealsEveryCharacterCorruptionToOwner_InOrder)

### 011 — CorruptionInsight sur roster vide accepte sans effet
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.Decide — boucle sur `ctx.Roster.Slots` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:16-21`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [] }` et `PowerContext(ownerSlot: 0, roster: roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `outcome.Accepted == true` et `outcome.Effects.Count == 0`
- **Stabilité:** évite un crash/exception ou un effet fantôme quand aucun personnage n'est en jeu.
- **Statut:** à implémenter

### 012 — CorruptionInsight avec l'owner seul dans le roster émet un unique reveal sur lui-même
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.Decide — viewer == `ctx.OwnerSlot` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:19`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [3] }` et `PowerContext(ownerSlot: 3, roster: roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale `[ RevealInfo(3, CorruptRevealed, Personal, 3, true) ]`
- **Stabilité:** confirme que l'owner apparaît dans sa propre révélation broadcast (pas d'auto-exclusion).
- **Statut:** à implémenter

### 013 — CorruptionInsight utilise CorruptRevealed / Personal / broadcast pour le bon viewer
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.Decide — champ/niveau/broadcast/viewer du RevealInfo — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:19`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [1] }` et `PowerContext(ownerSlot: 8, roster: roster)`
- **When:** appeler `Decide(ctx)` et lire `Effects[0]` casté en `RevealInfo`
- **Then:** `Field == RevealField.CorruptRevealed`, `Level == RevealVisibility.Personal`, `Broadcast == true`, `ViewerSlot == 8`, `TargetSlot == 1`
- **Stabilité:** empêche une dérive du niveau (Public vs Personal) ou du viewer qui fuiterait l'info au mauvais joueur.
- **Statut:** à implémenter

### 014 — CorruptionInsight est passif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:9-12`
- **Couche:** EditMode-pure
- **Given:** une instance `new CorruptionInsightDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == true` et `Id == PowerId.CorruptionInsight`
- **Stabilité:** garantit que la clairvoyance reste déclenchée au game start en passif.
- **Statut:** à implémenter

### 015 — CorruptionInsight préserve l'ordre exact des slots (non trié)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionInsightDecision.Decide — itère `Slots` tel quel — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:17`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [5,0,2] }` (ordre volontairement non croissant) et `PowerContext(ownerSlot: 5, roster: roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** les `RevealInfo.TargetSlot` sortent dans l'ordre `5, 0, 2`
- **Stabilité:** verrouille que la décision ne réordonne/trie pas les slots (l'ordre du roster est l'ordre canonique).
- **Statut:** à implémenter

### 016 — CorruptionKnowledge révèle forceCorruptOnRoleRevealed de chaque personnage
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionKnowledgeDecision.Decide — `RevealInfo(slot, ForceCorruptOnRoleRevealed, Personal, owner, true)` par slot — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:16-21`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [2,7] }` et `PowerContext(ownerSlot: 2, roster: roster)`
- **When:** appeler `new CorruptionKnowledgeDecision().Decide(ctx)`
- **Then:** `Effects` égale `[ RevealInfo(2,ForceCorruptOnRoleRevealed,Personal,2,true), RevealInfo(7,ForceCorruptOnRoleRevealed,Personal,2,true) ]`
- **Stabilité:** garantit la révélation du flag force-corrupt à l'owner pour chaque personnage.
- **Statut:** déjà couvert (PowerDecisionTests.CorruptionKnowledge_RevealsForceCorruptPerCharacter)

### 017 — CorruptionKnowledge révèle le champ ForceCorrupt, PAS CorruptRevealed
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionKnowledgeDecision.Decide — `RevealField.ForceCorruptOnRoleRevealed` (distinct de CorruptionInsight) — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:19`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [4] }` et `PowerContext(ownerSlot: 4, roster: roster)`
- **When:** appeler `Decide(ctx)` et lire `((RevealInfo)Effects[0]).Field`
- **Then:** `Field == RevealField.ForceCorruptOnRoleRevealed` (et non `CorruptRevealed`)
- **Stabilité:** attrape un copier-coller depuis CorruptionInsight qui révélerait le mauvais champ (les deux passifs ont la même forme).
- **Statut:** à implémenter

### 018 — CorruptionKnowledge sur roster vide accepte sans effet
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionKnowledgeDecision.Decide — boucle sur `ctx.Roster.Slots` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:16-21`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots = [] }` et `PowerContext(ownerSlot: 0, roster: roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `outcome.Accepted == true` et `outcome.Effects.Count == 0`
- **Stabilité:** évite un effet fantôme quand aucun personnage n'est présent.
- **Statut:** à implémenter

### 019 — CorruptionKnowledge est passif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionKnowledgeDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:9-12`
- **Couche:** EditMode-pure
- **Given:** une instance `new CorruptionKnowledgeDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == true` et `Id == PowerId.CorruptionKnowledge`
- **Stabilité:** garantit le déclenchement passif au game start.
- **Statut:** à implémenter

### 020 — CorruptionParanoia révèle sa propre corruption à soi (broadcast)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionParanoiaDecision.Decide — `RevealInfo(owner, CorruptRevealed, Personal, owner, true)` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs:15-16`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 3)` sans roster
- **When:** appeler `new CorruptionParanoiaDecision().Decide(ctx)`
- **Then:** `Effects` égale `[ RevealInfo(3, CorruptRevealed, Personal, 3, true) ]` et `UsesConsumed == 1`
- **Stabilité:** garantit que le joueur voit sa propre corruption au démarrage.
- **Statut:** déjà couvert (PowerDecisionTests.CorruptionParanoia_RevealsOwnCorruptionToSelf_Broadcast)

### 021 — CorruptionParanoia est passif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionParanoiaDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs:10-11`
- **Couche:** EditMode-pure
- **Given:** une instance `new CorruptionParanoiaDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == true` et `Id == PowerId.CorruptionParanoia`
- **Stabilité:** garantit le déclenchement passif.
- **Statut:** déjà couvert (PowerDecisionTests.CorruptionParanoia_IsPassive)

### 022 — CorruptionParanoia n'émet qu'un reveal auto-ciblé (viewer == target == owner)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CorruptionParanoiaDecision.Decide — target et viewer tous deux `ctx.OwnerSlot` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs:16`
- **Couche:** EditMode-pure
- **Given:** un `PowerContext(ownerSlot: 9)`
- **When:** appeler `Decide(ctx)` et lire `Effects[0]` casté en `RevealInfo`
- **Then:** `Effects.Count == 1`, `TargetSlot == 9`, `ViewerSlot == 9`, `Level == Personal`, `Broadcast == true`
- **Stabilité:** empêche que la paranoïa révèle la corruption d'autrui ou à autrui (fuite d'info).
- **Statut:** à implémenter

### 023 — PCorruptionParanoia révèle la corruption de l'owner au démarrage
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PCorruptionParanoia.OnGameStartedServer → RunDecisionEffects → RevealInfo → GameInfoRevealer — `Assets/Scripts/Characters/Powers/PCorruptionParanoia.cs:16`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, un owner spawné dont `isCorruptRevealed == RevealLevel.False`, un `PCorruptionParanoia` spawné avec `ownerClientId = LocalClientId`
- **When:** appeler `power.OnGameStartedServer()`
- **Then:** `revealer.GetCharacterInfo(owner).isCorruptRevealed == RevealLevel.Personal`
- **Stabilité:** verrouille le chemin décision→RevealInfoExecutor pour l'auto-révélation de corruption.
- **Statut:** déjà couvert (CorruptionTests.PCorruptionParanoia_RevealsOwnCorruptionAtStart)

### 024 — EmbraceOfShadows rôle correspondant : corrompt, event succès, double reveal owner-local
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** EmbraceOfShadowsDecision.Decide branche `SameRole` — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:15-22`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=7 }` et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `new EmbraceOfShadowsDecision().Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(0,1), CorruptPlayer(1), CorruptionSucceeded(1), RevealInfo(1,CorruptRevealed,Personal,0,false), RevealInfo(1,RoleRevealed,Personal,0,false) ]`
- **Stabilité:** fige la séquence succès de l'étreinte quand le rôle deviné est bon.
- **Statut:** déjà couvert (PowerDecisionTests.EmbraceOfShadows_RoleMatch_CorruptsRaisesRevealsBoth)

### 025 — EmbraceOfShadows rôle non correspondant : cible et échoue
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** EmbraceOfShadowsDecision.Decide branche else — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:24-26`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=9 }` (rôles différents) et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(0,1), CorruptionFailed(1) ]` (aucune corruption, aucun reveal)
- **Stabilité:** garantit qu'un mauvais rôle deviné ne corrompt pas et ne révèle rien.
- **Statut:** déjà couvert (PowerDecisionTests.EmbraceOfShadows_RoleMismatch_TargetsFails)

### 026 — EmbraceOfShadows cible inconditionnellement (NewTargeting dans les deux branches)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** EmbraceOfShadowsDecision.Decide — `NewTargeting(ctx.OwnerSlot, ctx.TargetSlot)` présent branche succès ET échec — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:18,25`
- **Couche:** EditMode-pure
- **Given:** deux contextes, l'un rôles égaux, l'autre rôles différents (mêmes slots owner:0 target:1)
- **When:** appeler `Decide` sur chacun
- **Then:** dans les deux cas `Effects[0] == NewTargeting(0,1)`
- **Stabilité:** garantit que la flèche de ciblage apparaît même sur un échec de corruption.
- **Statut:** à implémenter

### 027 — EmbraceOfShadows révèle en owner-local (broadcast == false)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** EmbraceOfShadowsDecision.Decide — RevealInfo avec `broadcast: false` (SetRevealLevel serveur-local) — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:21-22`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=7 }` et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `Decide(ctx)` et inspecter les deux `RevealInfo`
- **Then:** les deux ont `Broadcast == false` (contraste avec les reveals broadcast=true de Blessing)
- **Stabilité:** attrape une régression qui broadcasterait la révélation au lieu de la garder locale au lanceur (bug historique v2 owner-local).
- **Statut:** à implémenter

### 028 — EmbraceOfShadows est actif et porte le bon PowerId
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** EmbraceOfShadowsDecision.IsPassive / Id — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:10-11`
- **Couche:** EditMode-pure
- **Given:** une instance `new EmbraceOfShadowsDecision()`
- **When:** lire `.IsPassive` et `.Id`
- **Then:** `IsPassive == false` et `Id == PowerId.EmbraceOfShadows`
- **Stabilité:** garantit que l'étreinte reste un actif à pick char+role.
- **Statut:** à implémenter

### 029 — EmbraceOfShadows lancé par un client : reveal sur le revealer du CLIENT, pas de l'hôte
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PEmbraceOfShadows.OnCharacterAndRolePicked → RunClientDecisionEffects (owner-local) — `Assets/Scripts/Characters/Powers/PEmbraceOfShadows.cs:60-64`
- **Couche:** PlayMode-2NM
- **Given:** hôte + client réel ; le client possède un PEmbraceOfShadows et pick une cible de rôle correspondant
- **When:** exécuter la décision côté client (callback de sélection)
- **Then:** la révélation de corruption/rôle de la cible apparaît dans le `GameInfoRevealer` du CLIENT lanceur, pas dans celui de l'hôte
- **Stabilité:** verrouille la correction du bug v2 où un reveal owner-local d'un lanceur non-hôte atterrissait sur l'hôte (invisible au caster).
- **Statut:** déjà couvert (OwnerLocalEffectBoundaryTests.EmbraceCastByClient_RevealsOnClientRevealer_NotHost)

### 030 — CursedVision cible non-élu : corrompt cible, révèle, carte cachée, verdict, corrompt owner
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CursedVisionDecision.Decide branche non-chosen — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:20-33`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1], Factions[1]=anomaly, Pseudos[1]="Bob" }` et `PowerContext(ownerSlot:0, targetSlot:1, roster)` avec `CardEffectId = 2`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(0,1), CorruptPlayer(1), RevealInfo(1,CorruptRevealed,Personal,0,false), AddCardEffect(2,1,true), ChatLocal("Bob n'est pas un élu.",-1), CorruptPlayer(0), RevealInfo(0,CorruptRevealed,Personal,0,false) ]`
- **Stabilité:** fige la séquence complète de la vision maudite sur une cible non-élu.
- **Statut:** déjà couvert (PowerDecisionTests.CursedVision_NonChosen_CorruptsRevealsCardsChatsBoth)

### 031 — CursedVision cible élu : inverse le flag carte et le verdict
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CursedVisionDecision.Decide — `targetIsChosen` → `cardHidden=false`, verdict "est un élu" — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:20-24`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1], Factions[1]=chosen, Pseudos[1]="Alice" }` et `PowerContext(ownerSlot:0, targetSlot:1, roster)` avec `CardEffectId = 2`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects[3] == AddCardEffect(2,1,false)` et `Effects[4] == ChatLocal("Alice est un élu.",-1)`
- **Stabilité:** garantit que la carte est révélée (non cachée) et le verdict positif quand la cible est un élu.
- **Statut:** déjà couvert (PowerDecisionTests.CursedVision_Chosen_FlipsCardAndVerdict)

### 032 — CursedVision cible marginal traitée comme non-élu
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CursedVisionDecision.Decide — `targetIsChosen = FactionOf == chosen` (marginal n'est pas chosen) — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:20-21`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1], Factions[1]=marginal, Pseudos[1]="Zoé" }` et `PowerContext(ownerSlot:0, targetSlot:1, roster)` avec `CardEffectId = 2`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects[3] == AddCardEffect(2,1,true)` (carte cachée) et `Effects[4] == ChatLocal("Zoé n'est pas un élu.",-1)`
- **Stabilité:** confirme que la branche verdict repose sur `== chosen` strict et que marginal ne passe pas pour un élu.
- **Statut:** à implémenter

### 033 — CursedVision corrompt et révèle toujours l'owner, quelle que soit la faction cible
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CursedVisionDecision.Decide — `CorruptPlayer(ctx.OwnerSlot)` + `RevealInfo(owner,...)` hors branche verdict — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:31-33`
- **Couche:** EditMode-pure
- **Given:** deux contextes (owner:0 target:1), l'un `Factions[1]=chosen`, l'autre `Factions[1]=anomaly`
- **When:** appeler `Decide` sur chacun et lire les deux derniers effets
- **Then:** dans les deux cas les deux derniers effets sont `CorruptPlayer(0)` puis `RevealInfo(0,CorruptRevealed,Personal,0,false)`
- **Stabilité:** garantit le coût auto-infligé de la vision (l'owner se corrompt) indépendant du verdict.
- **Statut:** à implémenter

### 034 — CursedVision propage CardEffectId depuis le champ vers AddCardEffect
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** CursedVisionDecision.CardEffectId → `AddCardEffect(CardEffectId, target, cardHidden)` — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:12,30`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1], Factions[1]=anomaly }` et une décision avec `CardEffectId = 77`, `PowerContext(ownerSlot:0, targetSlot:1, roster)`
- **When:** appeler `Decide(ctx)` et lire l'`AddCardEffect`
- **Then:** `((AddCardEffect)Effects[3]).CardEffectId == 77`
- **Stabilité:** attrape une valeur d'id de carte codée en dur ou perdue lors du câblage prefab (CardEffectID.CursedVision).
- **Statut:** à implémenter

### 035 — CursedVision lancé par un client : reveal sur le revealer du CLIENT, pas de l'hôte
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PCursedVision.OnCharacterPicked → RunClientDecisionEffects (owner-local) — `Assets/Scripts/Characters/Powers/PCursedVision.cs:43-45`
- **Couche:** PlayMode-2NM
- **Given:** hôte + client réel ; le client possède un PCursedVision, l'owner et la cible ont des seats réels ; le client pick la cible
- **When:** exécuter la décision côté client
- **Then:** la révélation de corruption de la cible ET de l'owner apparaît dans le `GameInfoRevealer` du CLIENT lanceur, pas dans celui de l'hôte
- **Stabilité:** verrouille la correction du bug v2 (effets owner-local d'un lanceur non-hôte atterrissant sur l'hôte).
- **Statut:** déjà couvert (OwnerLocalEffectBoundaryTests.CursedVisionCastByClient_RevealsOnClientRevealer_NotHost)

### 036 — Blessing rôle correspondant non soigné : soigne, révèle, bénit, annonce
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** BlessingDecision.Decide branche SameRole + non healed — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:18-28`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=7, Pseudos[1]="Bob" }` (cible non Healed) et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `new BlessingDecision().Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(0,1), HealPlayer(1), RevealInfo(1,RoleRevealed,Personal,0,true), SetBlessed(1), ChatBroadcast("Bob est maintenant béni.",-1,Specific(0)) ]`
- **Stabilité:** fige la séquence complète de bénédiction sur cible non soignée.
- **Statut:** déjà couvert (PowerDecisionTests.Blessing_RoleMatch_NotHealed_HealsRevealsBlessesAnnounces)

### 037 — Blessing cible déjà soignée : saute le HealPlayer
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** BlessingDecision.Decide — `if (!IsHealed(target))` — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:20-23`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=7, Pseudos[1]="Bob", Healed={1} }` et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale `[ NewTargeting(0,1), RevealInfo(1,RoleRevealed,Personal,0,true), SetBlessed(1), ChatBroadcast("Bob est maintenant béni.",-1,Specific(0)) ]` (pas de `HealPlayer`)
- **Stabilité:** garantit qu'on ne re-soigne pas une cible déjà soignée (idempotence du soin).
- **Statut:** déjà couvert (PowerDecisionTests.Blessing_AlreadyHealed_SkipsHeal)

### 038 — Blessing rôle non correspondant : cible seulement, aucun soin/bénédiction
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** BlessingDecision.Decide — `NewTargeting` inconditionnel puis bloc SameRole non exécuté — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:17-28`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=9, Pseudos[1]="Bob" }` (rôles différents) et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `Decide(ctx)`
- **Then:** `Effects` égale exactement `[ NewTargeting(0,1) ]`
- **Stabilité:** attrape l'unique branche non testée de Blessing — un mauvais rôle deviné ne doit ni soigner ni bénir ni révéler.
- **Statut:** à implémenter

### 039 — Blessing annonce à l'owner seul avec pseudo et révèle en broadcast
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** BlessingDecision.Decide — `ChatBroadcast(..., Specific(owner))` + `RevealInfo(..., broadcast:true)` — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:24,26-27`
- **Couche:** EditMode-pure
- **Given:** un `FakeRoster { Slots=[0,1,2], Roles[1]=7, Roles[2]=7, Pseudos[1]="Bob" }` et `PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster)`
- **When:** appeler `Decide(ctx)` et inspecter le `ChatBroadcast` et le `RevealInfo`
- **Then:** le `ChatBroadcast.Audience == PowerEffectAudience.Specific(0)` et le message contient "Bob" ; le `RevealInfo.Broadcast == true` (contraste avec Embrace en owner-local)
- **Stabilité:** garantit que l'annonce de bénédiction reste privée à l'owner et que le reveal de rôle est bien broadcast.
- **Statut:** à implémenter

### 040 — PBlessing soigne, bénit et révèle une cible de rôle correspondant
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PBlessing.TryBlessCharacterServerRpc → RunDecisionEffects → HealPlayer/SetBlessed/RevealInfo — `Assets/Scripts/Characters/Powers/PBlessing.cs:45`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, un owner et un target (clientId 1357) spawnés, `target.role.roleName="Bless-Test"`, un `compareRole` de même roleName+ownerClientId, un `PBlessing` spawné
- **When:** invoquer `TryBlessCharacterServerRpc(target.ownerClientId.Value, compareRole)`
- **Then:** `target.isBlessed.Value == true`, `target.isHealed.Value == true`, et `isRoleRevealed(target, owner) == RevealLevel.Personal`
- **Stabilité:** verrouille le chemin décision→exécuteurs (heal+bless+reveal) de la bénédiction.
- **Statut:** déjà couvert (CorruptionTests.PBlessing_HealsBlessesAndRevealsMatchingTarget)

### 041 — Blessing observée sur la réplique d'un client distant (bless + heal)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** SetBlessed + HealPlayer executors → NetworkVariable isBlessed/isHealed répliquées — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:20-25` + `Assets/Scripts/Characters/Character.cs:188`
- **Couche:** PlayMode-2NM
- **Given:** hôte + client réel ; l'hôte lance PBlessing sur une cible de rôle correspondant
- **When:** exécuter la décision côté serveur puis attendre la réplication
- **Then:** sur la réplique du client distant, `target.isBlessed.Value == true` et `target.isHealed.Value == true`
- **Stabilité:** garantit que l'état bénédiction/soin se propage aux clients (pas seulement côté hôte).
- **Statut:** déjà couvert (PowerPipelineClientReplicationTests.Blessing_BlessesAndHealsMatchingTarget_ObservedOnRemoteClientReplica)

### 042 — DroolyHealing rôle correspondant + cible corrompue : soigne (décorrompt)
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PDroolyHealing.TryHealServerRpc — `IsTheSameRole` + `isCorrupted` → `HealPlayerServerRpc` — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:55-60` + `Assets/Scripts/Characters/Character.cs:188`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, owner + target (clientId 2468) spawnés ; `target.role.roleName="Drool-Test"`, `target.isCorrupted.Value=true`, `target.isHealed.Value=false` ; un `compareRole` de même roleName+ownerClientId ; un `PDroolyHealing` spawné
- **When:** invoquer `TryHealServerRpc(target.ownerClientId.Value, compareRole)`
- **Then:** `target.isCorrupted.Value == false`, `target.isHealed.Value == true`, et `target.ownerClientId.Value` est dans `power.healedCharactersThisNight`
- **Stabilité:** verrouille le soin baveux qui décorrompt une cible corrompue de rôle deviné correct.
- **Statut:** à implémenter

### 043 — DroolyHealing rôle correspondant + cible déjà soignée : reste soignée, rôle révélé
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PDroolyHealing.TryHealServerRpc — branche `isHealed` (HealPlayerServerRpc early-return) + OnHealSuccessfulRpc — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:57-64,73` + `Assets/Scripts/Characters/Character.cs:190-192`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, owner + target (clientId 2469) spawnés ; `target.role.roleName="Drool-Test"`, `target.isHealed.Value=true`, `target.isCorrupted.Value=false` ; `compareRole` correspondant ; un `PDroolyHealing` spawné
- **When:** invoquer `TryHealServerRpc(target.ownerClientId.Value, compareRole)`
- **Then:** `target.isHealed.Value == true` (inchangé), et `isRoleRevealed(target, owner) == RevealLevel.Personal`
- **Stabilité:** garantit qu'une cible déjà soignée n'est pas re-mutée (early-return) tout en révélant son rôle à l'owner.
- **Statut:** à implémenter

### 044 — DroolyHealing rôle correspondant + cible ni corrompue ni soignée : pas de soin mais rôle révélé
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PDroolyHealing.TryHealServerRpc — `_healSuccess` reste false, mais `healedCharactersThisNight.Add` + `OnHealSuccessfulRpc` s'exécutent sur match de rôle — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:55-64`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, owner + target (clientId 2470) spawnés ; `target.role.roleName="Drool-Test"`, `target.isCorrupted.Value=false`, `target.isHealed.Value=false` ; `compareRole` correspondant ; un `PDroolyHealing` spawné
- **When:** invoquer `TryHealServerRpc(target.ownerClientId.Value, compareRole)`
- **Then:** `target.isHealed.Value == false` (aucun soin appliqué), mais `isRoleRevealed(target, owner) == RevealLevel.Personal` et la cible est dans `power.healedCharactersThisNight`
- **Stabilité:** documente la subtilité — sur bon rôle, la révélation de rôle et l'ajout à la liste de nuit se font MÊME sans soin effectif.
- **Statut:** à implémenter

### 045 — DroolyHealing rôle non correspondant : aucun soin, aucune révélation, cible non listée
- **Catégorie:** A1 — Corruption & Soin
- **Mécanique:** PDroolyHealing.TryHealServerRpc — `IsTheSameRole` false → bloc entier sauté (seul `NewTargeting` en amont) — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:51,55`
- **Couche:** PlayMode-StartHost
- **Given:** en StartHost, owner + target (clientId 2471) spawnés ; `target.role.roleName="RealRole"`, `target.isCorrupted.Value=true` ; un `compareRole` avec un roleName DIFFÉRENT ("WrongGuess") + ownerClientId de la cible ; un `PDroolyHealing` spawné
- **When:** invoquer `TryHealServerRpc(target.ownerClientId.Value, compareRole)`
- **Then:** `target.isCorrupted.Value == true` (inchangé), `target.isHealed.Value == false`, `isRoleRevealed(target, owner) == RevealLevel.False`, et `power.healedCharactersThisNight` est vide
- **Stabilité:** garantit qu'un mauvais rôle deviné ne soigne pas, ne révèle rien et ne pollue pas la liste des soignés de la nuit.
- **Statut:** à implémenter

### 046 — EyeOfTheVoid ne discover le chat anomalie que pour les anomalies
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** EyeOfTheVoidDecision (passif, filtre faction==anomaly) — `Assets/Scripts/Domain/Powers/Decisions/EyeOfTheVoidDecision.cs:23-29`
- **Couche:** EditMode-pure
- **Given:** roster slots {0=chosen, 1=anomaly, 2=anomaly}, owner slot 0, `AnomalyChatId=1`
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, roster))`
- **Then:** effets == exactement [`DiscoverChat(1,"",Specific(1))`, `DiscoverChat(1,"",Specific(2))`] dans l'ordre du roster ; le slot chosen (0) n'a AUCUN DiscoverChat
- **Stabilité:** empêche une régression qui révélerait le chat anomalie partagé à un élu (fuite d'information critique).
- **Statut:** déjà couvert (PowerDecisionTests.EyeOfTheVoid_DiscoversAnomalyChat_AnomaliesOnly)

### 047 — EyeOfTheVoid sans aucune anomalie accepte sans effet
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** EyeOfTheVoidDecision branche `effects.Count == 0 → AcceptEmpty` — `Assets/Scripts/Domain/Powers/Decisions/EyeOfTheVoidDecision.cs:30`
- **Couche:** EditMode-pure
- **Given:** roster slots {0=chosen}, owner slot 0, `AnomalyChatId=1`
- **When:** appeler `Decide`
- **Then:** `outcome.Accepted == true` ET `outcome.Effects.Count == 0`
- **Stabilité:** empêche qu'une partie 100% élus fasse échouer/lever le passif au lieu d'un no-op propre.
- **Statut:** déjà couvert (PowerDecisionTests.EyeOfTheVoid_NoAnomalies_AcceptsEmpty)

### 048 — EyeOfTheVoid inclut l'owner anomalie dans les découvertes
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** EyeOfTheVoidDecision itère tout le roster sans exclure l'owner — `Assets/Scripts/Domain/Powers/Decisions/EyeOfTheVoidDecision.cs:23-29`
- **Couche:** EditMode-pure
- **Given:** roster slots {0=anomaly (owner), 1=anomaly}, owner slot 0, `AnomalyChatId=7`
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, roster))`
- **Then:** effets == [`DiscoverChat(7,"",Specific(0))`, `DiscoverChat(7,"",Specific(1))`] — l'owner reçoit lui-même le chat
- **Stabilité:** empêche une régression qui exclurait l'owner (le porteur anomalie perdrait son propre accès au chat partagé).
- **Statut:** à implémenter

### 049 — VisionOfTheImpossible s'arrête au premier match et annonce trouvé
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** VisionOfTheImpossibleDecision (boucle guesses, break au premier Matches) — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:20-31`
- **Couche:** EditMode-pure
- **Given:** IVisionGuesses = [ (1,false,"Alice"), (7,true,"Bob"), (9,false,"Carol") ], owner slot 0
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, state))`
- **Then:** effets == [`NewTargeting(0,1)`, `NewTargeting(0,7)`, `ChatBroadcast("Bob est l'un de ces personnages.", -1, Specific(0))`] ; PAS de `NewTargeting(0,9)`
- **Stabilité:** empêche la régression où la boucle continue après le match (cible en trop + message dupliqué).
- **Statut:** déjà couvert (PowerDecisionTests.Vision_FirstMatchStops_FoundMessage)

### 050 — VisionOfTheImpossible sans aucun match annonce non trouvé
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** VisionOfTheImpossibleDecision (message vide → "Aucun personnage n'a été trouvé.") — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:30`
- **Couche:** EditMode-pure
- **Given:** IVisionGuesses = [ (1,false,"A"), (7,false,"B") ], owner slot 0
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,1)`, `NewTargeting(0,7)`, `ChatBroadcast("Aucun personnage n'a été trouvé.", -1, Specific(0))`]
- **Stabilité:** empêche qu'un échec de devinette n'affiche rien ou un faux positif.
- **Statut:** déjà couvert (PowerDecisionTests.Vision_NoMatch_NotFound)

### 051 — VisionOfTheImpossible avec liste de guesses vide n'émet aucun ciblage
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** VisionOfTheImpossibleDecision (boucle 0-itération, message reste vide) — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:20-31`
- **Couche:** EditMode-pure
- **Given:** IVisionGuesses.Guesses == liste vide, owner slot 0
- **When:** appeler `Decide`
- **Then:** effets == exactement [`ChatBroadcast("Aucun personnage n'a été trouvé.", -1, Specific(0))`] ; AUCUN `NewTargeting`
- **Stabilité:** empêche un NRE ou un ciblage fantôme quand aucune devinette n'est fournie.
- **Statut:** à implémenter

### 052 — VisionOfTheImpossible match dès la première devinette
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** VisionOfTheImpossibleDecision (break dès index 0) — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:22-28`
- **Couche:** EditMode-pure
- **Given:** IVisionGuesses = [ (4,true,"Zoe"), (5,false,"X") ], owner slot 2
- **When:** appeler `Decide(new PowerContext(ownerSlot:2, state))`
- **Then:** effets == [`NewTargeting(2,4)`, `ChatBroadcast("Zoe est l'un de ces personnages.", -1, Specific(2))`] ; PAS de `NewTargeting(2,5)`
- **Stabilité:** empêche que le break au premier élément soit cassé (off-by-one avalant une 2e cible).
- **Statut:** à implémenter

### 053 — ClandestineObservation sans porteur du rôle affiche 0 avec point
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ClandestineObservationDecision (branche `!HasCharacters` → ": 0.") — `Assets/Scripts/Domain/Powers/Decisions/ClandestineObservationDecision.cs:18-20`
- **Couche:** EditMode-pure
- **Given:** IClandestineReport { HasCharacters=false, RoleLabel="Robot", DistinctTargetingCount=0 }, owner slot 0
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, state))`
- **Then:** effets == [`ChatBroadcast("Total de personne qui ont ciblé le rôle \"Robot\": 0.", -1, Specific(0))`] (avec le point final)
- **Stabilité:** empêche un flottement de wording (point/absence de point) qui trahirait la présence du rôle.
- **Statut:** déjà couvert (PowerDecisionTests.Clandestine_NoChars_ZeroWithPeriod)

### 054 — ClandestineObservation avec porteurs affiche le compte sans point
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ClandestineObservationDecision (branche `HasCharacters` → ": {count}") — `Assets/Scripts/Domain/Powers/Decisions/ClandestineObservationDecision.cs:18-19`
- **Couche:** EditMode-pure
- **Given:** IClandestineReport { HasCharacters=true, RoleLabel="Robot Mécanique", DistinctTargetingCount=3 }, owner slot 0
- **When:** appeler `Decide`
- **Then:** effets == [`ChatBroadcast("Total de personne qui ont ciblé le rôle \"Robot Mécanique\": 3", -1, Specific(0))`] (sans point final)
- **Stabilité:** empêche l'inversion des deux branches de format (point présent/absent) = fuite d'existence du rôle.
- **Statut:** déjà couvert (PowerDecisionTests.Clandestine_HasChars_CountNoPeriod)

### 055 — Omniscience sur cible élue cible, stocke le hack, révèle et hacke
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** OmniscienceDecision (branche `isChosen` complète) — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:19-31`
- **Couche:** EditMode-pure
- **Given:** roster {0, 5=chosen}, owner slot 0, target slot 5
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:5, roster))`
- **Then:** effets == [`NewTargeting(0,5)`, `StoreHackTarget(5)`, `RevealInfo(5,RoleRevealed,Personal,0,true)`, `RevealInfo(5,Hacked,Personal,0,true)`, `RequestCharacterRefresh.Instance`]
- **Stabilité:** empêche la perte du piratage (StoreHackTarget/Hacked) sur une cible élue.
- **Statut:** déjà couvert (PowerDecisionTests.Omniscience_ChosenTarget_TargetsStoresRevealsHacksRefreshes)

### 056 — Omniscience sur cible anomalie révèle le rôle sans hacker
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** OmniscienceDecision (branche `!isChosen` → reveal seul) — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:19-31`
- **Couche:** EditMode-pure
- **Given:** roster {0, 5=anomaly}, owner slot 0, target slot 5
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,5)`, `RevealInfo(5,RoleRevealed,Personal,0,true)`, `RequestCharacterRefresh.Instance`] ; PAS de `StoreHackTarget` ni `RevealInfo(...,Hacked,...)`
- **Stabilité:** empêche qu'un piratage s'applique sur une non-élue (le hack ne doit toucher que les élus).
- **Statut:** déjà couvert (PowerDecisionTests.Omniscience_NonChosenTarget_RevealsRoleOnly_NoHack)

### 057 — Omniscience sur cible marginale révèle le rôle sans hacker
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** OmniscienceDecision (marginal ∉ chosen → pas de piratage) — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:19`
- **Couche:** EditMode-pure
- **Given:** roster {0, 5=marginal}, owner slot 0, target slot 5
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,5)`, `RevealInfo(5,RoleRevealed,Personal,0,true)`, `RequestCharacterRefresh.Instance`] ; aucun effet Hacked
- **Stabilité:** verrouille que "marginal" soit traité comme non-élu (le commentaire du code le dit, aucun test ne l'ancre).
- **Statut:** à implémenter

### 058 — Omniscience avec roster null révèle le rôle sans hacker (garde)
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** OmniscienceDecision (`ctx.Roster != null && ...` → isChosen=false) — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:19`
- **Couche:** EditMode-pure
- **Given:** owner slot 0, target slot 5, `roster: null`
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:5))`
- **Then:** pas de NRE ; effets == [`NewTargeting(0,5)`, `RevealInfo(5,RoleRevealed,Personal,0,true)`, `RequestCharacterRefresh.Instance`]
- **Stabilité:** empêche un NRE si le roster n'est pas câblé (garde défensive du null-check).
- **Statut:** à implémenter

### 059 — Omniscience le hack expire si la cible n'est pas votée au tour suivant
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** POmniscience carrier (durée du hack liée au vote) — `Assets/Scripts/Characters/Powers/POmniscience.cs`
- **Couche:** PlayMode-StartHost
- **Given:** host, owner Omniscience + cible élue piratée au tour N
- **When:** avancer au tour N+1 sans que la cible soit votée
- **Then:** l'état `Hacked` de la cible pour l'owner est effacé
- **Stabilité:** empêche un piratage permanent (le glitch de carte doit se limiter à un tour).
- **Statut:** déjà couvert (VisionPowerTests.POmniscience_HackExpires_WhenTargetNotVotedNextTurn)

### 060 — Omniscience le hack persiste si la cible est votée au tour suivant
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** POmniscience carrier (rétention du hack sur vote) — `Assets/Scripts/Characters/Powers/POmniscience.cs`
- **Couche:** PlayMode-StartHost
- **Given:** host, owner Omniscience + cible élue piratée au tour N
- **When:** la cible est votée au tour N+1
- **Then:** l'état `Hacked` de la cible pour l'owner reste actif
- **Stabilité:** empêche la perte prématurée du piratage quand la cible reste en jeu par le vote.
- **Statut:** déjà couvert (VisionPowerTests.POmniscience_HackKept_WhenTargetVotedNextTurn)

### 061 — Omniscience reveal Hacked réplique et se nettoie sur observateur distant
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** RevealInfo(Hacked) asymétrie owner-local sur 2 clients — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:29`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, un observateur/révélateur client distant
- **When:** appliquer un reveal Hacked puis un clear ciblé
- **Then:** le store de reveal du client distant round-trip (set puis clear) correctement
- **Stabilité:** empêche que le piratage fuite chez le host ou reste bloqué chez le client distant.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.HackReveal_ThenTargetedClear_RoundTripsOnRemoteObserver)

### 062 — PersonalBeacons révèle forceCorrupt de chaque robot à l'owner
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** PersonalBeaconsDecision (reveal par robot) — `Assets/Scripts/Domain/Powers/Decisions/PersonalBeaconsDecision.cs:18-24`
- **Couche:** EditMode-pure
- **Given:** roster {0=owner, 1=robot, 2=robot}, owner slot 0
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, roster))`
- **Then:** effets == [`RevealInfo(1,ForceCorruptOnRoleRevealed,Personal,0,true)`, `RevealInfo(2,ForceCorruptOnRoleRevealed,Personal,0,true)`]
- **Stabilité:** empêche la régression du NRE historique de l'ancien Awake sur la révélation des robots.
- **Statut:** déjà couvert (PowerDecisionTests.PersonalBeacons_RevealsForceCorruptPerRobot)

### 063 — PersonalBeacons ne révèle que les robots dans un roster mixte
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** PersonalBeaconsDecision (filtre `IsRobot`) — `Assets/Scripts/Domain/Powers/Decisions/PersonalBeaconsDecision.cs:19-22`
- **Couche:** EditMode-pure
- **Given:** roster {0=owner non-robot, 1=robot, 2=non-robot, 3=robot}, owner slot 0
- **When:** appeler `Decide`
- **Then:** effets == exactement [`RevealInfo(1,ForceCorruptOnRoleRevealed,Personal,0,true)`, `RevealInfo(3,ForceCorruptOnRoleRevealed,Personal,0,true)`] ; slots 0 et 2 absents
- **Stabilité:** empêche une régression du filtre qui révélerait des non-robots (fuite d'info).
- **Statut:** à implémenter

### 064 — PersonalBeacons sans aucun robot accepte sans effet
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** PersonalBeaconsDecision (branche `effects.Count == 0 → AcceptEmpty`) — `Assets/Scripts/Domain/Powers/Decisions/PersonalBeaconsDecision.cs:25`
- **Couche:** EditMode-pure
- **Given:** roster {0=owner, 1, 2} sans aucun robot, owner slot 0
- **When:** appeler `Decide`
- **Then:** `outcome.Accepted == true` ET `outcome.Effects.Count == 0`
- **Stabilité:** empêche un échec/lever du passif quand aucun robot n'est en jeu.
- **Statut:** à implémenter

### 065 — CardsShuffling devinette correcte enregistre et révèle
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** CardsShufflingDecision (branche `IsCorrect`) — `Assets/Scripts/Domain/Powers/Decisions/CardsShufflingDecision.cs:19-28`
- **Couche:** EditMode-pure
- **Given:** ICardsShufflingGuess { IsCorrect=true, ClickedPseudo="Bob", GuessRoleName="Sorcier" }, owner 0, target 1
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, state))`
- **Then:** effets == [`NewTargeting(0,1)`, `DiscoveredAdd(1)`, `RevealInfo(1,RoleRevealed,Personal,0,true)`, `ChatBroadcast("Vous avez correctement deviné que Bob est Sorcier.", -1, Specific(0))`]
- **Stabilité:** empêche de perdre l'enregistrement/reveal sur une bonne devinette.
- **Statut:** déjà couvert (PowerDecisionTests.CardsShuffling_Correct_RecordsReveals)

### 066 — CardsShuffling devinette incorrecte liste les rôles ciblés
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** CardsShufflingDecision (branche incorrect avec `TargetedRoleNames`) — `Assets/Scripts/Domain/Powers/Decisions/CardsShufflingDecision.cs:33-40`
- **Couche:** EditMode-pure
- **Given:** ICardsShufflingGuess { IsCorrect=false, ClickedPseudo="Bob", GuessRoleName="Sorcier", TargetedRoleNames=["Robot","Élu"] }, owner 0, target 1
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,1)`, `ChatBroadcast("Votre supposition était incorrecte, Bob n'est pas Sorcier.\nLe role Sorcier a ciblé ces rôles:\n- Robot\n- Élu", -1, Specific(0))`] ; pas de DiscoveredAdd/reveal
- **Stabilité:** empêche une régression de composition du message multi-lignes de la liste ciblée.
- **Statut:** déjà couvert (PowerDecisionTests.CardsShuffling_Incorrect_WithTargets_AppendsList)

### 067 — CardsShuffling devinette incorrecte sans ciblage annonce aucun rôle
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** CardsShufflingDecision (branche `TargetedRoleNames.Count == 0`) — `Assets/Scripts/Domain/Powers/Decisions/CardsShufflingDecision.cs:29-32`
- **Couche:** EditMode-pure
- **Given:** ICardsShufflingGuess { IsCorrect=false, ClickedPseudo="Bob", GuessRoleName="Sorcier", TargetedRoleNames=[] }, owner 0, target 1
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,1)`, `ChatBroadcast("Votre supposition était incorrecte, Bob n'est pas Sorcier.\nLe role Sorcier n'a ciblé aucun rôle.", -1, Specific(0))`]
- **Stabilité:** empêche que la branche "aucun ciblage" produise une boucle vide ou un message tronqué.
- **Statut:** à implémenter

### 068 — ChainedByShadows role-match élu cible, révèle et enchaîne
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ChainedByShadowsDecision (SameRole + faction chosen → AddToChain) — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:20-26`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1(role=7,chosen), 2(role=7)}, owner 0, target 1, secondaryTarget 2
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, secondaryTargetSlot:2, roster))`
- **Then:** effets == [`NewTargeting(0,1)`, `RevealInfo(1,RoleRevealed,Personal,0,true)`, `AddToChain(1)`]
- **Stabilité:** empêche de perdre l'enchaînement d'un élu dont le rôle correspond.
- **Statut:** déjà couvert (PowerDecisionTests.ChainedByShadows_RoleMatchChosen_TargetsRevealsChains)

### 069 — ChainedByShadows role-match non-élu révèle mais n'enchaîne pas
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ChainedByShadowsDecision (SameRole mais faction != chosen) — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:23`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1(role=7,anomaly), 2(role=7)}, owner 0, target 1, secondaryTarget 2
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,1)`, `RevealInfo(1,RoleRevealed,Personal,0,true)`] ; PAS de `AddToChain`
- **Stabilité:** empêche l'enchaînement d'un non-élu (l'enchaînement doit être réservé aux élus).
- **Statut:** déjà couvert (PowerDecisionTests.ChainedByShadows_RoleMatchNotChosen_NoChain)

### 070 — ChainedByShadows role mismatch ne fait que cibler
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ChainedByShadowsDecision (SameRole false → NewTargeting seul) — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:19-20`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1(role=7), 2(role=9)}, owner 0, target 1, secondaryTarget 2
- **When:** appeler `Decide`
- **Then:** effets == exactement [`NewTargeting(0,1)`]
- **Stabilité:** empêche un reveal/chain accidentel quand les rôles ne correspondent pas.
- **Statut:** déjà couvert (PowerDecisionTests.ChainedByShadows_RoleMismatch_OnlyTargets)

### 071 — ChainedByShadows enchaîne l'élu observé sur la réplique distante
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** AddToChain propagé dans la NetworkList sur 2 clients — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:25`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, un owner ChainedByShadows + cible élue role-match
- **When:** l'owner utilise le pouvoir côté serveur
- **Then:** l'enchaînement de la cible est observé sur la réplique NetworkList du client distant
- **Stabilité:** empêche que l'enchaînement reste server-local et n'atteigne pas les clients.
- **Statut:** déjà couvert (PowerPipelineClientReplicationTests.ChainedByShadows_ChainsChosenTarget_NetworkListObservedOnRemoteReplica)

### 072 — BoundByInk cible, découvre le chat encre et enregistre la cible
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** BoundByInkDecision (chatId via IInkChatState) — `Assets/Scripts/Domain/Powers/Decisions/BoundByInkDecision.cs:16-21`
- **Couche:** EditMode-pure
- **Given:** IInkChatState { ChatId=515100 }, owner 0, target 1
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, state))`
- **Then:** effets == [`NewTargeting(0,1)`, `DiscoverChat(515100, "Lié par l'encre", Specific(1))`, `RegisterInkTarget(1)`]
- **Stabilité:** empêche de perdre l'enregistrement de la cible d'encre ou la découverte du chat privé.
- **Statut:** déjà couvert (PowerDecisionTests.BoundByInk_TargetsDiscoversRegisters)

### 073 — BoundByInk avec état d'encre absent retombe sur chatId -1
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** BoundByInkDecision (`ctx.State<IInkChatState>()?.ChatId ?? -1`) — `Assets/Scripts/Domain/Powers/Decisions/BoundByInkDecision.cs:16`
- **Couche:** EditMode-pure
- **Given:** aucun IInkChatState câblé (state resolver retourne null), owner 0, target 1
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, state: <resolver vide>))`
- **Then:** pas de NRE ; effets == [`NewTargeting(0,1)`, `DiscoverChat(-1, "Lié par l'encre", Specific(1))`, `RegisterInkTarget(1)`]
- **Stabilité:** empêche un NRE si le chat encre n'est pas encore assigné (garde du null-coalescing).
- **Statut:** à implémenter

### 074 — TruthChains sur cible anomalie enchaîne et annonce
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** TruthChainsDecision (branche faction anomaly) — `Assets/Scripts/Domain/Powers/Decisions/TruthChainsDecision.cs:18-23`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1=anomaly, pseudo "Bob"}, owner 0, target 1
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, roster))`
- **Then:** effets == [`NewTargeting(0,1)`, `AddToChain(1)`, `ChatSendServer("Bob sera lié par les chaînes de la vérité.", -1)`]
- **Stabilité:** empêche de perdre l'enchaînement + l'annonce serveur sur une anomalie.
- **Statut:** déjà couvert (PowerDecisionTests.TruthChains_Anomaly_ChainsAndAnnounces)

### 075 — TruthChains sur cible non-anomalie ne fait que cibler
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** TruthChainsDecision (faction != anomaly → NewTargeting seul) — `Assets/Scripts/Domain/Powers/Decisions/TruthChainsDecision.cs:17-18`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1=chosen}, owner 0, target 1
- **When:** appeler `Decide`
- **Then:** effets == exactement [`NewTargeting(0,1)`] ; pas de chain ni d'annonce
- **Stabilité:** empêche d'enchaîner un élu par erreur avec TruthChains.
- **Statut:** déjà couvert (PowerDecisionTests.TruthChains_NonAnomaly_OnlyTargets)

### 076 — TruthChains sur cible marginale ne fait que cibler
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** TruthChainsDecision (marginal != anomaly) — `Assets/Scripts/Domain/Powers/Decisions/TruthChainsDecision.cs:18`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1=marginal}, owner 0, target 1
- **When:** appeler `Decide`
- **Then:** effets == exactement [`NewTargeting(0,1)`]
- **Stabilité:** verrouille que seule la faction anomaly déclenche l'enchaînement (marginal exclu).
- **Statut:** à implémenter

### 077 — HighPriorityBounty sur le robot élimine, annonce et révèle publiquement
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** HighPriorityBountyDecision (branche `IsRobot`) — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:18-25`
- **Couche:** EditMode-pure
- **Given:** roster {0(roleName "Chasseur"), 1=robot pseudo "Bob"}, owner 0, target 1
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, roster))`
- **Then:** effets == [`NewTargeting(0,0)`, `SetEliminated(1)`, `ChatBroadcast("Bob était le robot et a été éliminé par Chasseur.", -1, All)`, `RevealPublic(1,RoleRevealed)`, `RequestCharacterRefresh.Instance`]
- **Stabilité:** empêche de casser la récompense (élimination + reveal public du robot).
- **Statut:** déjà couvert (PowerDecisionTests.HighPriorityBounty_Robot_EliminatesBroadcastsRevealsRefresh)

### 078 — HighPriorityBounty hors robot enchaîne l'owner et l'avertit
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** HighPriorityBountyDecision (branche `else` → AddToChain owner) — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:27-32`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1=non-robot}, owner 0, target 1
- **When:** appeler `Decide`
- **Then:** effets == [`NewTargeting(0,0)`, `AddToChain(0)`, `ChatBroadcast("Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.", -1, Specific(0))`, `RequestCharacterRefresh.Instance`]
- **Stabilité:** empêche que la punition (auto-enchaînement de l'owner) sur mauvaise cible disparaisse.
- **Statut:** déjà couvert (PowerDecisionTests.HighPriorityBounty_NotRobot_ChainsOwnerWarnsRefresh)

### 079 — HighPriorityBounty élimination + reveal public observés sur client distant
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** SetEliminated + RevealPublic répliqués sur 2 clients — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:20,24`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, owner HPB + cible robot
- **When:** l'owner utilise le pouvoir sur le robot
- **Then:** l'élimination et le reveal public du robot sont observés sur la réplique du client distant
- **Stabilité:** empêche que l'élimination/reveal restent server-local et ne soient pas vus des autres joueurs.
- **Statut:** déjà couvert (PowerPipelineClientReplicationTests.HighPriorityBounty_EliminatesRobot_AndPublicReveal_ObservedOnRemoteClient)

### 080 — HighPriorityBounty broadcast reçu par la réplique de chat distante
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ChatBroadcast(All) reçu par tous — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:21-23`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, owner HPB + cible robot
- **When:** l'owner élimine le robot
- **Then:** le message d'annonce est reçu par la réplique de chat du client distant
- **Stabilité:** empêche que l'annonce publique de l'élimination du robot ne parvienne pas aux clients.
- **Statut:** déjà couvert (PowerPipelineClientReplicationTests.HighPriorityBounty_BroadcastChat_IsReceivedByRemoteChatReplica)

### 081 — LackOfAffection cible élue en true-local révèle et poste le chat
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LackOfAffectionDecision (faction chosen + IsTrueLocalTarget) — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:19-26`
- **Couche:** EditMode-pure
- **Given:** roster {0(roleName "Marginal"), 1=chosen}, owner 0, target 1, isTrueLocalTarget=true
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:1, isTrueLocalTarget:true, roster))`
- **Then:** effets == [`RevealInfo(0,RoleRevealed,Personal,1,false)`, `ChatLocal("Marginal est venu(e) vous voir...", -1)`]
- **Stabilité:** empêche de perdre le reveal du sender OU le chat local sur la cible contactée.
- **Statut:** déjà couvert (PowerDecisionTests.LackOfAffection_ChosenTrueLocal_RevealsAndChats)

### 082 — LackOfAffection cible non-élue hors true-local n'émet rien
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LackOfAffectionDecision (aucune branche prise) — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:18-26`
- **Couche:** EditMode-pure
- **Given:** roster {0, 1=anomaly}, owner 0, target 1, isTrueLocalTarget=false
- **When:** appeler `Decide`
- **Then:** `outcome.Effects.Count == 0`
- **Stabilité:** empêche un reveal/chat parasite quand ni la faction ni le true-local ne s'appliquent.
- **Statut:** déjà couvert (PowerDecisionTests.LackOfAffection_NonChosen_NotTrueLocal_Empty)

### 083 — LackOfAffection cible élue hors true-local révèle sans chat
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LackOfAffectionDecision (branche faction seule, IsTrueLocalTarget=false) — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:19-23`
- **Couche:** EditMode-pure
- **Given:** roster {0(roleName "Marginal"), 1=chosen}, owner 0, target 1, isTrueLocalTarget=false
- **When:** appeler `Decide`
- **Then:** effets == exactement [`RevealInfo(0,RoleRevealed,Personal,1,false)`] ; PAS de `ChatLocal`
- **Stabilité:** verrouille l'indépendance des deux branches (le chat local ne doit se poster que sur le vrai target local).
- **Statut:** à implémenter

### 084 — LackOfAffection cible non-élue en true-local poste le chat sans reveal
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LackOfAffectionDecision (branche IsTrueLocalTarget seule, faction != chosen) — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:23-25`
- **Couche:** EditMode-pure
- **Given:** roster {0(roleName "Marginal"), 1=anomaly}, owner 0, target 1, isTrueLocalTarget=true
- **When:** appeler `Decide`
- **Then:** effets == exactement [`ChatLocal("Marginal est venu(e) vous voir...", -1)`] ; PAS de `RevealInfo`
- **Stabilité:** empêche qu'une cible non-élue voie le rôle du sender (le reveal doit être réservé aux élus).
- **Statut:** à implémenter

### 085 — LackOfAffection reveal du sender atterrit sur le révélateur client, pas sur le host
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** RevealInfo owner-local (broadcast=false) côté target-client — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:21`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, target élue contactée sur le client distant
- **When:** LackOfAffection s'exécute sur le client de la cible
- **Then:** le rôle du sender est révélé dans le store du révélateur client distant, PAS chez le host
- **Stabilité:** empêche la régression où l'effet owner-local tournerait côté serveur et serait invisible pour le contacté.
- **Statut:** déjà couvert (OwnerLocalEffectBoundaryTests.LackOfAffectionContactedOnClient_RevealsSenderRoleOnClientRevealer_NotHost)

### 086 — LackOfAffection cible non-élue contactée sur client ne révèle rien
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LackOfAffectionDecision (faction != chosen → pas de reveal) répliqué — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:19`
- **Couche:** PlayMode-2NM
- **Given:** 2 NetworkManagers, target non-élue contactée sur le client distant
- **When:** LackOfAffection s'exécute
- **Then:** aucun rôle de sender n'est révélé dans le store du client distant
- **Stabilité:** empêche une fuite de rôle du sender vers une cible non-élue à travers le fil.
- **Statut:** déjà couvert (OwnerLocalEffectBoundaryTests.LackOfAffectionContactedOnClient_NonChosenTarget_RevealsNothing)

### 087 — Legacy accorde le pouvoir légué à l'owner quand le rôle surveillé est enchaîné
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** LegacyDecision → GrantLegacyPower + trigger `OnCharacterChainChanged` — `Assets/Scripts/Domain/Powers/Decisions/LegacyDecision.cs:13-14` ; `Assets/Scripts/Characters/Powers/PLegacy.cs:40-53`
- **Couche:** PlayMode-StartHost
- **Given:** host, un owner PLegacy avec `roleForLegacy=R`, `legacyPower=P` ; un personnage de rôle R non enchaîné au départ
- **When:** le personnage de rôle R passe `isChained.Value` de false à true
- **Then:** l'owner reçoit le pouvoir P (GivePowerToCharacter) ET `isLegacyInherited == true`
- **Stabilité:** empêche que l'héritage ne se déclenche jamais (le watch de la NetworkVariable isChained doit rester câblé).
- **Statut:** à implémenter

### 088 — Legacy accorde immédiatement si le rôle surveillé est déjà enchaîné au démarrage
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** PLegacy.OnGameStartedServer (branche `isChained.Value` déjà vrai → grant immédiat) — `Assets/Scripts/Characters/Powers/PLegacy.cs:27-37`
- **Couche:** PlayMode-StartHost
- **Given:** host, owner PLegacy avec `roleForLegacy=R`, `legacyPower=P` ; un personnage de rôle R DÉJÀ `isChained.Value==true` au moment de OnGameStartedServer
- **When:** déclencher OnGameStartedServer sur le pouvoir
- **Then:** l'owner reçoit P immédiatement ET `isLegacyInherited == true`
- **Stabilité:** empêche de rater l'héritage quand le rôle est enchaîné avant que le passif ne s'abonne.
- **Statut:** à implémenter

### 089 — Reincarnation cible le rôle, broadcast passif puis accorde ses pouvoirs
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** ReincarnationDecision (NewTargeting → SetPassiveBroadcast(true) → GrantRolePowers) — `Assets/Scripts/Domain/Powers/Decisions/ReincarnationDecision.cs:12-16`
- **Couche:** EditMode-pure
- **Given:** owner 0, target 3
- **When:** appeler `Decide(new PowerContext(ownerSlot:0, targetSlot:3))`
- **Then:** effets == [`NewTargeting(0,3)`, `SetPassiveBroadcast(true)`, `GrantRolePowers(0,3)`] dans cet ordre exact
- **Stabilité:** empêche une inversion d'ordre (broadcast passif doit précéder le grant).
- **Statut:** déjà couvert (PowerDecisionTests.Reincarnation_TargetsPassiveThenGrants)

### 090 — Reincarnation copiée (Ugues/Luma) accorde des copies one-shot en sautant les passifs
- **Catégorie:** A2 — VISION / REVEAL / CHAÎNE / CIBLAGE / DIVERS
- **Mécanique:** PReincarnation.GrantRolePowers (branche `isStolenCopy` → ConfigureOneShotGrant + skip `BaseIsPassive`) — `Assets/Scripts/Characters/Powers/PReincarnation.cs:22-47`
- **Couche:** PlayMode-StartHost
- **Given:** host, owner PReincarnation avec `isStolenCopy.Value==true` ; rôle cible ayant 1 pouvoir actif non-copié + 1 pouvoir passif
- **When:** la réincarnation s'exécute (ReincarnatePlayerRpc)
- **Then:** l'owner reçoit UNE copie one-shot du pouvoir actif (configurée despawn-on-use, non-recopiable) et AUCUNE copie du passif
- **Stabilité:** empêche qu'une copie temporaire mine un pouvoir permanent (passif jamais dépensé → jamais despawn).
- **Statut:** à implémenter

### 091 — Un Character fraîchement spawné démarre à l'état neutre
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** valeurs par défaut des NetworkVariable d'état — `Assets/Scripts/Characters/Character.cs:18-28`
- **Couche:** PlayMode-StartHost
- **Given:** un host (StartHost) ; on spawne un Character via `CharacterManager.AddNewCharacter(host.LocalClientId)` (harness identique à `CorruptionTests.SetUp`).
- **When:** aucune action après le spawn (on lit l'état immédiatement).
- **Then:** `isChained.Value`, `isCorrupted.Value`, `isEliminated.Value`, `isBlessed.Value`, `isHealed.Value`, `isAwakened.Value`, `hasSentMessageThisTurn.Value` valent tous `false`, et `messageLeft.Value == 1`.
- **Stabilité:** empêche qu'un changement de valeur par défaut d'un NetworkVariable (ou une init parasite dans OnNetworkSpawn) fasse démarrer un joueur corrompu/éveillé/muet sans qu'aucun pouvoir n'ait agi.
- **Statut:** à implémenter

### 092 — Un ownerClientId à la sentinelle FAKE_CLIENT_ID est marqué fake
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `Character.isFake => ownerClientId.Value.IsFakeClientId()` — `Assets/Scripts/Characters/Character.cs:29` via `Assets/Scripts/Extensions/UlongExtensions.cs:8-15`
- **Couche:** EditMode-pure
- **Given:** rien (fonction pure) ; `GameValues.FAKE_CLIENT_ID == ulong.MaxValue`.
- **When:** on appelle `GameValues.FAKE_CLIENT_ID.IsFakeClientId()`.
- **Then:** retourne `true`.
- **Stabilité:** empêche qu'un slot fake (valeur par défaut d'`ownerClientId`, `Character.cs:18`) soit un jour compté comme un vrai joueur dans les rosters/ciblages.
- **Statut:** à implémenter

### 093 — La fenêtre fake couvre exactement MAX_PLAYERS ids sous la sentinelle
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** borne `_clientId >= FAKE_CLIENT_ID - MAX_PLAYERS` — `Assets/Scripts/Extensions/UlongExtensions.cs:10` (`GameValues.MAX_PLAYERS == 15`)
- **Couche:** EditMode-pure
- **Given:** rien (fonction pure).
- **When:** on évalue `IsFakeClientId()` sur `ulong.MaxValue - 15` (borne basse fake) puis sur `ulong.MaxValue - 16` (juste en dessous).
- **Then:** `ulong.MaxValue - 15` → `true` ; `ulong.MaxValue - 16` → `false` ; et un vrai id bas (ex `3`) → `false`.
- **Stabilité:** verrouille la largeur de la plage réservée aux fakes (liée à MAX_PLAYERS) — un off-by-one classerait un vrai client comme fake (ou l'inverse) dans `isFake`.
- **Statut:** à implémenter

### 094 — La corruption n'est PAS bloquée par un soin antérieur (heal ≠ immunité)
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `CorruptPlayerServerRpc` inconditionnel vs garde de `HealPlayerServerRpc` — `Assets/Scripts/Characters/Character.cs:182-185`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; un Character cible `t` spawné pour le clientId 500. On appelle `t.HealPlayerServerRpc()` → `t.isHealed.Value == true`, `t.isCorrupted.Value == false`.
- **When:** on appelle `t.CorruptPlayerServerRpc()`.
- **Then:** `t.isCorrupted.Value == true` (la corruption s'applique malgré `isHealed == true`).
- **Stabilité:** empêche qu'on ajoute par erreur une garde `if (isHealed) return;` dans `CorruptPlayerServerRpc` : le soin protège une seule fois (au moment du heal), il n'immunise pas contre les corruptions futures.
- **Statut:** à implémenter

### 095 — Le soin est one-shot : un 2e heal après re-corruption ne fait rien
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** garde `if (isHealed.Value) return;` — `Assets/Scripts/Characters/Character.cs:190-196`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 501). Séquence : `t.HealPlayerServerRpc()` (→ isHealed=true) puis `t.CorruptPlayerServerRpc()` (→ isCorrupted=true).
- **When:** on rappelle `t.HealPlayerServerRpc()`.
- **Then:** `t.isCorrupted.Value` reste `true` (le 2e heal court-circuite sur la garde `isHealed` et ne nettoie pas la corruption).
- **Stabilité:** protège le caractère consommable du soin : un régresseur qui retire la garde `isHealed` rendrait le soigneur ré-utilisable à l'infini.
- **Statut:** à implémenter

### 096 — Un heal sur une cible non-corrompue consomme quand même le one-shot
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `HealPlayerServerRpc` pose `isHealed = true` sans condition de corruption — `Assets/Scripts/Characters/Character.cs:194-195`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 502) jamais corrompue (`isCorrupted.Value == false`, `isHealed.Value == false`).
- **When:** on appelle `t.HealPlayerServerRpc()`.
- **Then:** `t.isHealed.Value == true` et `t.isCorrupted.Value == false` ; un heal suivant reste sans effet (one-shot déjà consommé).
- **Stabilité:** verrouille que soigner « à vide » brûle bien la charge — empêche une régression où le heal ne se marquerait que s'il y avait corruption à nettoyer.
- **Statut:** à implémenter

### 097 — Le heal propage la transition corrompu→sain + isHealed sur la réplique distante
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `HealPlayerServerRpc` (isCorrupted true→false, isHealed=true) — `Assets/Scripts/Characters/Character.cs:187-196`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2 NM ; Character de la cible spawné, corrompu côté serveur, réplique client résolue.
- **When:** le serveur appelle `HealPlayerServerRpc()`.
- **Then:** sur la réplique client, `isCorrupted.Value` passe à `false` ET `isHealed.Value` passe à `true`.
- **Stabilité:** attrape un bug de réplication one-way (sync valeur initiale seulement) invisible en StartHost.
- **Statut:** déjà couvert (CharacterStateReplicationTests.HealAfterCorruption_ClearsCorruptionAndSetsHealed_OnRemoteClientReplica)

### 098 — Le chaînage pose isChained ET isCorrupted (observé sur la réplique)
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `ChainCharacterServer` pose les deux flags — `Assets/Scripts/Characters/Character.cs:198-207`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2 NM ; cible spawnée, réplique client résolue, baseline non-chaînée/non-corrompue.
- **When:** le serveur appelle `hostTarget.ChainCharacterServer()`.
- **Then:** sur la réplique client, `isChained.Value == true` ET `isCorrupted.Value == true`.
- **Stabilité:** verrouille l'invariant « chaîner implique corrompre » à travers le fil (les conditions de victoire lisent isChained/isCorrupted).
- **Statut:** déjà couvert (CharacterStateReplicationTests.ChainCharacterServer_ChainsAndCorrupts_OnRemoteClientReplica)

### 099 — Chaîner une cible déjà soignée la re-corrompt
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** `ChainCharacterServer` pose `isCorrupted = true` inconditionnel — `Assets/Scripts/Characters/Character.cs:205-206`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 503) soignée : `t.HealPlayerServerRpc()` → `isHealed=true`, `isCorrupted=false`.
- **When:** on appelle `t.ChainCharacterServer()`.
- **Then:** `t.isChained.Value == true` ET `t.isCorrupted.Value == true` (le chaînage écrase l'état sain).
- **Stabilité:** empêche qu'une future garde de soin fasse échapper une cible chaînée à la corruption — le chaînage doit toujours corrompre.
- **Statut:** à implémenter

### 100 — ChainCharacterServer appelé hors-serveur ne mute rien
- **Catégorie:** B — mécaniques d'état du Character
- **Mécanique:** garde `if (!IsServer) { LogError; return; }` — `Assets/Scripts/Characters/Character.cs:200-204`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2 NM ; cible spawnée, réplique client résolue (baseline non-chaînée). On attend l'erreur via `LogAssert.Expect`.
- **When:** on appelle `ChainCharacterServer()` sur la RÉPLIQUE client (IsServer == false).
- **Then:** aucune mutation : `isChained.Value` et `isCorrupted.Value` restent `false` sur les deux répliques ; une `LogError("ChainCharacterServer can only be called on the server")` est émise.
- **Stabilité:** protège l'autorité serveur stricte — un client ne doit jamais pouvoir chaîner/corrompre localement en appelant directement le mutateur.
- **Statut:** à implémenter

### 101 — AddCharacterToChainingList appelé hors-serveur est refusé
- **Catégorie:** B — mécaniques d'état du Character (chaîne)
- **Mécanique:** garde `if (!IsServer) { LogError; return; }` — `Assets/Scripts/GameLogic/ChainingManager.cs:64-68`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2 NM avec un ChainingManager spawné ; réplique client résolue. On attend l'erreur via `LogAssert.Expect`.
- **When:** on appelle `AddCharacterToChainingList(123)` sur la réplique client du ChainingManager.
- **Then:** la `chainingPlayers` reste vide sur host et client ; une `LogError("AddCharacterToChainingList can only be called on the server")` est émise.
- **Stabilité:** verrouille que seule l'autorité serveur alimente la liste de chaînage (source qui pilote `Character.isChained`).
- **Statut:** à implémenter

### 102 — ChainCharacterRpc révèle publiquement le rôle du chaîné
- **Catégorie:** B — mécaniques d'état du Character (chaîne)
- **Mécanique:** `SetRevealLevelRpc(..., isRoleRevealed, RevealLevel.Public, ...)` — `Assets/Scripts/GameLogic/ChainingManager.cs:96`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet (façon `CorruptionTests.SetUp` : CharacterManager + GameInfoRevealer + ChainingManager câblés) ; cible `t` (clientId 610) avec `role = new Role()` (sans le pouvoir « portail »).
- **When:** on appelle `ChainingManager.instance.ChainCharacterRpc(t.ownerClientId.Value)`.
- **Then:** `_revealer.GetCharacterInfo(t.ownerClientId.Value).isRoleRevealed == RevealLevel.Public` et `t.isChained.Value == true`.
- **Stabilité:** empêche qu'un chaînage cesse de dévoiler la carte du joueur chaîné (le reveal Public est un effet attendu du chaînage).
- **Statut:** à implémenter

### 103 — Éveiller un Character pose isAwakened et réarme l'envoi de message
- **Catégorie:** B — mécaniques d'état du Character (éveil)
- **Mécanique:** `AwakenCharacterServerRpc` : `isAwakened=true` + `hasSentMessageThisTurn=false` — `Assets/Scripts/Characters/Character.cs:140-146`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 504) avec `t.role = new Role()`, et on force `t.hasSentMessageThisTurn.Value = true` avant l'action.
- **When:** on appelle `t.AwakenCharacterServerRpc()`.
- **Then:** `t.isAwakened.Value == true` ET `t.hasSentMessageThisTurn.Value == false`.
- **Stabilité:** verrouille le réarmement du quota de message à l'éveil — une régression ici bloquerait ou dégèlerait à tort la parole du joueur au tour où il s'éveille.
- **Statut:** à implémenter

### 104 — Endormir un Character remet isAwakened à false
- **Catégorie:** B — mécaniques d'état du Character (éveil)
- **Mécanique:** `SleepCharacterServerRpc` : `isAwakened=false` — `Assets/Scripts/Characters/Character.cs:154-160`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 505) avec `t.role = new Role()`, éveillée au préalable (`t.AwakenCharacterServerRpc()` → isAwakened=true).
- **When:** on appelle `t.SleepCharacterServerRpc()`.
- **Then:** `t.isAwakened.Value == false`.
- **Stabilité:** garantit la transition d'endormissement (symétrique de l'éveil), sur laquelle s'appuie l'AwakeningState pour savoir qui agit encore.
- **Statut:** à implémenter

### 105 — L'éveil pose isAwakened et diffuse la notification sur la réplique distante
- **Catégorie:** B — mécaniques d'état du Character (éveil)
- **Mécanique:** `AwakenCharacterServerRpc` (isAwakened) + broadcast `SleepCharacterClientRpc` → `onCharacterSleep` — `Assets/Scripts/Characters/Character.cs:140-167`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2 NM ; cible spawnée avec un `role`, réplique client résolue et abonnée à `onCharacterSleep`.
- **When:** le serveur appelle `AwakenCharacterServerRpc()`.
- **Then:** sur la réplique client, `isAwakened.Value == true` ET l'événement `onCharacterSleep` s'est déclenché (leg NetworkVariable + leg RPC SendTo.Everyone).
- **Stabilité:** attrape la perte d'une des deux jambes (variable OU broadcast) invisible en StartHost.
- **Statut:** déjà couvert (CharacterStateReplicationTests.AwakenServerRpc_SetsAwakenedAndFiresSleepBroadcast_OnRemoteClientReplica)

### 106 — Un spectateur anomaly voit l'effet de carte Bénédiction quand une cible est bénie
- **Catégorie:** B — mécaniques d'état du Character (bénédiction)
- **Mécanique:** `OnBlessed` → `CardEffectManager.AddCardEffect(Blessing, ...)` si le local est anomaly — `Assets/Scripts/Characters/Character.cs:77-97` (condition ligne 90)
- **Couche:** PlayMode-StartHost
- **Given:** harness complet + un `CardEffectManager.instance` ; le Character local possédé a `role.factionType = FactionType.anomaly` ; une autre cible `t` (clientId 620) démarre `isBlessed == false`.
- **When:** le serveur pose `t.isBlessed.Value = true` (déclenche `OnBlessed` via `OnValueChanged`).
- **Then:** `CardEffectManager.instance` a reçu un `AddCardEffect(CardEffectID.Blessing, t.ownerClientId.Value)`.
- **Stabilité:** protège le feedback visuel de bénédiction pour le camp anomaly — régression facile car conditionnée à la faction du SPECTATEUR local, pas de la cible.
- **Statut:** à implémenter

### 107 — Un spectateur Dryade voit aussi l'effet de carte Bénédiction
- **Catégorie:** B — mécaniques d'état du Character (bénédiction)
- **Mécanique:** `OnBlessed` : condition `roleID == RoleID.Dryade` — `Assets/Scripts/Characters/Character.cs:91`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet + `CardEffectManager.instance` ; le Character local possédé a `role.roleID = RoleID.Dryade` (et une faction non-anomaly, ex `chosen`) ; cible `t` (clientId 621) `isBlessed == false`.
- **When:** le serveur pose `t.isBlessed.Value = true`.
- **Then:** `CardEffectManager.instance` a reçu un `AddCardEffect(CardEffectID.Blessing, t.ownerClientId.Value)`.
- **Stabilité:** verrouille l'exception Dryade dans la règle de visibilité de la bénédiction (branche OR distincte de la faction anomaly).
- **Statut:** à implémenter

### 108 — Un spectateur ni anomaly ni Dryade ne voit PAS l'effet Bénédiction
- **Catégorie:** B — mécaniques d'état du Character (bénédiction)
- **Mécanique:** `OnBlessed` sort sans `AddCardEffect` si local n'est ni anomaly ni Dryade — `Assets/Scripts/Characters/Character.cs:90-97`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet + `CardEffectManager.instance` ; le Character local possédé a `role.factionType = FactionType.chosen` et `roleID != Dryade` ; cible `t` (clientId 622) `isBlessed == false`.
- **When:** le serveur pose `t.isBlessed.Value = true`.
- **Then:** aucun `AddCardEffect(Blessing, ...)` n'est enregistré dans `CardEffectManager.instance`.
- **Stabilité:** empêche une fuite d'information de camp — les élus « ordinaires » ne doivent pas percevoir la bénédiction comme un signal visuel.
- **Statut:** à implémenter

### 109 — Le ciblage exclut le lanceur quand le flag Self est absent
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** `GetTargetsForCharacters` retire le local si `!HasFlag(Self)` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:39-43`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; un local possédé (clientId L) et une autre cible `t` ; on interroge avec `flags = Anomaly|Marginal|Chosen` (Self absent), tous rôles non révélés.
- **When:** on appelle `TargetUtils.GetTargetsForCharacters(flags)`.
- **Then:** la liste ne contient PAS `L` mais contient `t`.
- **Stabilité:** empêche qu'un pouvoir sans Self se cible lui-même (règle de base du picker de cibles).
- **Statut:** à implémenter

### 110 — Une cible chaînée est retirée du ciblage quand le flag Chained est absent
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** `RemoveAll(_t => _t.isChained.Value)` si `!HasFlag(Chained)` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:84-87`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; cible `t` (clientId 630) avec `t.isChained.Value = true` ; flags incluant sa faction mais SANS `Chained`.
- **When:** on appelle `GetTargetsForCharacters(flags)`.
- **Then:** `t.ownerClientId.Value` n'apparaît PAS dans la liste retournée.
- **Stabilité:** verrouille l'inconditionnalité du filtre chaîné (pas de reveal-gate) — un chaîné reste intargetable pour tout pouvoir ne déclarant pas Chained.
- **Statut:** à implémenter

### 111 — Une cible soignée est retirée du ciblage quand le flag Healed est absent
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** `RemoveAll(_t => _t.isHealed.Value)` si `!HasFlag(Healed)` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:89-92`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; cible `t` (clientId 631) avec `t.isHealed.Value = true` ; flags incluant sa faction mais SANS `Healed`.
- **When:** on appelle `GetTargetsForCharacters(flags)`.
- **Then:** `t.ownerClientId.Value` n'apparaît PAS dans la liste retournée.
- **Stabilité:** garantit qu'un joueur ayant déjà consommé son soin devient intargetable (filtre inconditionnel, pas reveal-gated).
- **Statut:** à implémenter

### 112 — Le filtre « corrupted » est reveal-gated : un corrompu non révélé reste ciblable
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** retrait seulement si `isCorruptRevealed > False && isCorrupted` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:66-77`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; cible `t` (clientId 632) avec `t.isCorrupted.Value = true` MAIS `GetCharacterInfo(t).isCorruptRevealed == RevealLevel.False` ; flags SANS `Corrupted`.
- **When:** on appelle `GetTargetsForCharacters(flags)`.
- **Then:** `t.ownerClientId.Value` APPARAÎT quand même dans la liste (la corruption cachée ne suffit pas à l'exclure) ; en comparaison, si on force `isCorruptRevealed = Personal/Public`, `t` disparaît.
- **Stabilité:** empêche une fuite d'info — exclure un corrompu non révélé révélerait indirectement sa corruption au lanceur via l'absence de la cible.
- **Statut:** à implémenter

### 113 — Le filtre de faction est reveal-gated : une anomaly non révélée reste ciblable
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** filtre faction appliqué uniquement si `isRoleRevealed > False` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:45-64`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; cible `t` (clientId 633) avec `t.role.factionType = FactionType.anomaly` et `GetCharacterInfo(t).isRoleRevealed == RevealLevel.False` ; flags `Marginal|Chosen` (Anomaly absent).
- **When:** on appelle `GetTargetsForCharacters(flags)`.
- **Then:** `t.ownerClientId.Value` APPARAÎT (faction cachée non filtrée) ; si on force `isRoleRevealed = Public`, `t` disparaît.
- **Stabilité:** empêche que le picker trahisse la faction cachée d'un joueur en le retirant des cibles selon une info non encore révélée.
- **Statut:** à implémenter

### 114 — Une cible bénie est intargetable pour la corruption
- **Catégorie:** B — mécaniques d'état du Character (validité de cible selon état)
- **Mécanique:** `RemoveAll(_t => _t.isBlessed.Value)` si `!HasFlag(Blessed)` — `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:79-82`
- **Couche:** PlayMode-StartHost
- **Given:** harness complet ; un pouvoir de corruption dont les flags n'incluent pas `Blessed` ; une cible d'abord non bénie puis `isBlessed.Value = true`.
- **When:** on évalue `CheckIsTargetValid(target)` avant et après la bénédiction.
- **Then:** valide avant, INVALIDE après la bénédiction.
- **Stabilité:** verrouille la protection de la bénédiction contre le ciblage de corruption.
- **Statut:** déjà couvert (CorruptionTests.PBlessing_MakesTargetUntargetableForCorruption)

### 115 — L'élimination est irréversible : elle survit à un cycle corruption/soin
- **Catégorie:** B — mécaniques d'état du Character (élimination)
- **Mécanique:** `isEliminated` n'est écrit qu'à `true` (aucun reset) — `Assets/Scripts/Characters/Character.cs:23` + seul writer `Assets/Scripts/Characters/Powers/Runtime/Executors/SetEliminatedExecutor.cs:14`
- **Couche:** PlayMode-StartHost
- **Given:** un host ; cible `t` (clientId 640) dont on pose `t.isEliminated.Value = true` (état éliminé).
- **When:** on lui applique ensuite `t.CorruptPlayerServerRpc()` puis `t.HealPlayerServerRpc()`.
- **Then:** `t.isEliminated.Value` reste `true` tout du long (aucune de ces transitions d'état ne dé-élimine le joueur).
- **Stabilité:** verrouille l'absence de chemin de retour depuis l'élimination — un futur mutateur ne doit pas ressusciter un éliminé via un effet de corruption/soin.
- **Statut:** à implémenter

### 116 — RevealLevel garde son ordre monotone False<Personal<Public
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** enum `RevealLevel` (valeurs 0/10/20 sur lesquelles repose la gate monotone) — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:319-324`
- **Couche:** EditMode-pure
- **Given:** l'enum `RevealLevel` avec ses trois membres False, Personal, Public.
- **When:** on compare `(int)RevealLevel.False`, `(int)RevealLevel.Personal`, `(int)RevealLevel.Public`.
- **Then:** `(int)False == 0 < (int)Personal == 10 < (int)Public == 20` ; l'ordre relatif False<Personal<Public est strict.
- **Stabilité:** empêche qu'une réattribution des valeurs de l'enum casse silencieusement la gate `>=` de `SetRevealLevel` (un Public deviendrait rétrogradable).
- **Statut:** à implémenter

### 117 — CharacterInfoReveal démarre tous champs à False
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** valeurs initiales des 4 champs révélables — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:311-316`
- **Couche:** EditMode-pure
- **Given:** rien.
- **When:** on instancie `new CharacterInfoReveal()`.
- **Then:** `isRoleRevealed`, `isCorruptRevealed`, `forceCorruptOnRoleRevealed` et `isHacked` valent tous `RevealLevel.False`.
- **Stabilité:** garantit qu'un nouveau joueur/carte n'expose aucune info par défaut (fuite d'info à l'ajout d'un champ).
- **Statut:** à implémenter

### 118 — SetRevealLevel ignore un downgrade Public→Personal
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** gate monotone `if ((int)_currentRevealLevel >= (int)_revealLevel) return;` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:163-166`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné (host==server, localClientId 0), un subject seat 5 dont `isRoleRevealed` a été porté à `RevealLevel.Public` via `SetRevealLevel(5, "isRoleRevealed", Public, 0)`.
- **When:** on appelle `SetRevealLevel(5, "isRoleRevealed", RevealLevel.Personal, 0)`.
- **Then:** `GetCharacterInfo(5, 0).isRoleRevealed` vaut toujours `RevealLevel.Public` (le downgrade est ignoré).
- **Stabilité:** empêche qu'un reveal Personal tardif (mark de corruption) efface une révélation publique déjà acquise (chaînage/élimination).
- **Statut:** à implémenter

### 119 — SetRevealLevel à niveau égal retourne tôt sans notifier
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** early-return sur `>=` avant `onCharacterInfoRevealedChanged` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:163-185`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné (localClientId 0), subject seat 5 déjà à `isRoleRevealed = Personal`, un compteur abonné à `onCharacterInfoRevealedChanged` remis à 0.
- **When:** on rappelle `SetRevealLevel(5, "isRoleRevealed", RevealLevel.Personal, 0)` (même niveau).
- **Then:** le compteur reste à 0 (aucune invocation de `onCharacterInfoRevealedChanged`) et la valeur reste `Personal`.
- **Stabilité:** empêche un re-render/flip de carte redondant à chaque re-application d'un reveal déjà présent.
- **Statut:** à implémenter

### 120 — SetRevealLevel applique un upgrade False→Personal et notifie le viewer local
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** écriture par réflexion + notification local-viewer — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:168,171,182-185`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné (localClientId 0), subject seat 5 à `isRoleRevealed = False`, un compteur abonné à `onCharacterInfoRevealedChanged`.
- **When:** on appelle `SetRevealLevel(5, "isRoleRevealed", RevealLevel.Personal, 0)` (observer 0 == local).
- **Then:** `GetCharacterInfo(5, 0).isRoleRevealed == Personal` et le compteur `onCharacterInfoRevealedChanged` a incrémenté exactement une fois.
- **Stabilité:** garantit que la révélation d'un rôle rafraîchit l'UI du propriétaire local du reveal.
- **Statut:** à implémenter

### 121 — SetRevealLevel avec nom de champ inconnu déclenche l'assert
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** `Assert.IsNotNull(_field, ...)` sur le champ résolu par réflexion — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:157-158`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné (localClientId 0), subject seat 5.
- **When:** on appelle `SetRevealLevel(5, "notAField", RevealLevel.Personal, 0)`.
- **Then:** une `UnityEngine.Assertions.AssertionException` est levée (le champ introuvable interrompt l'écriture).
- **Stabilité:** attrape une faute de frappe/renommage d'un champ de `CharacterInfoReveal` côté appelant (RevealInfoExecutor/EffectExecutorHelpers) au lieu d'un no-op silencieux.
- **Statut:** à implémenter

### 122 — GetCharacterInfo révèle toujours le rôle du joueur pour lui-même
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** invariant `EnsureOwnRoleRevealed` (subject==self) appliqué à la lecture — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:138,147-153`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 0 (== le client local) dont on n'a JAMAIS appelé de reveal (store à False).
- **When:** on lit `GetCharacterInfo(0)` (observer par défaut → local id 0).
- **Then:** `.isRoleRevealed >= RevealLevel.Personal` (le joueur voit son propre rôle même sans reveal explicite).
- **Stabilité:** empêche la régression "role-reveal-wrong-card" où un hoquet de réplication à l'attribution laissait un joueur sans voir son propre rôle.
- **Statut:** à implémenter

### 123 — GetCharacterInfo ne révèle PAS le rôle d'un autre joueur
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** garde `if (_clientId == _selfId ...)` de `EnsureOwnRoleRevealed` (subject != self) — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:149`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 5 (!= local) sans aucun reveal.
- **When:** on lit `GetCharacterInfo(5)` (observer local 0).
- **Then:** `.isRoleRevealed == RevealLevel.False` (aucun auto-reveal pour un tiers).
- **Stabilité:** empêche que l'invariant "voir son propre rôle" fuite le rôle des autres joueurs.
- **Statut:** à implémenter

### 124 — EnsureOwnRoleRevealed ne rétrograde jamais un rôle déjà Public
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** garde `_info.isRoleRevealed < RevealLevel.Personal` avant réécriture — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:149-152`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 0 (== self) dont `isRoleRevealed` a été porté à `Public` (élimination publique).
- **When:** on relit `GetCharacterInfo(0)`.
- **Then:** `.isRoleRevealed == RevealLevel.Public` (l'invariant self ne le ramène pas à Personal).
- **Stabilité:** empêche que la lecture de sa propre carte rétrograde une révélation publique acquise.
- **Statut:** à implémenter

### 125 — GetCharacterInfo (client réel) ignore l'observerId passé
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** branche réelle <100 lit le dict unique `charactersInfoRevealed[_clientId]` sans scoper par observer — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:129-139`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 5 porté à `isCorruptRevealed = Personal` via `SetRevealLevel(5, "isCorruptRevealed", Personal, 0)`.
- **When:** on lit `GetCharacterInfo(5, _observerId: 3)` avec un observer réel DIFFÉRENT (3, <100).
- **Then:** `.isCorruptRevealed == Personal` — le même objet du dict unique est renvoyé (le scoping par-observer n'existe pas pour les ids réels ; l'asymétrie réelle vient du ciblage RPC par client, pas d'un sous-dict local).
- **Stabilité:** documente que le store réel est mono-observateur par process (une régression qui scoperait par observer casserait le partage public/host).
- **Statut:** à implémenter

### 126 — GetCharacterInfo crée paresseusement l'entrée d'un clientId réel inconnu
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** `if (!charactersInfoRevealed.ContainsKey(_clientId)) AddCharacterToInfoList(...)` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:129-133`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, un Character seat 7 existant dans le CharacterManager mais jamais ajouté au store de reveal.
- **When:** on lit `GetCharacterInfo(7)` (observer local 0).
- **Then:** l'appel ne lève pas d'exception et renvoie un `CharacterInfoReveal` neuf (`isRoleRevealed == False` pour un tiers) ; `charactersInfoRevealed` contient désormais la clé 7.
- **Stabilité:** empêche une KeyNotFoundException quand l'UI interroge un joueur pas encore stampé (late-join / ordre d'attribution).
- **Statut:** à implémenter

### 127 — GetCharacterInfo (bot simulé >=100) crée un cerveau par-observateur
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** branche `_observerId >= 100` → `simulationsKnowledge[observer]` créé à la demande — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:113-124,87-104`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, un subject seat 5, aucun cerveau simulé encore créé.
- **When:** on lit `GetCharacterInfo(5, _observerId: 101)` (bot simulé).
- **Then:** l'appel renvoie un `CharacterInfoReveal` et `simulationsKnowledge` contient la clé observateur 101 (un sous-dictionnaire propre au bot 101).
- **Stabilité:** garantit que le scoping de connaissance par-observateur existe pour les bots (sinon un bot lirait le store humain).
- **Statut:** à implémenter

### 128 — Deux bots simulés ont des connaissances isolées
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** un sous-dictionnaire distinct par observateur bot dans `simulationsKnowledge` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:87-104,113-126`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, subject seat 5, deux bots 101 et 102.
- **When:** on appelle `SetRevealLevel(5, "isRoleRevealed", Personal, 101)` (reveal au seul bot 101).
- **Then:** `GetCharacterInfo(5, 101).isRoleRevealed == Personal` mais `GetCharacterInfo(5, 102).isRoleRevealed == False` (le bot 102 ne voit rien).
- **Stabilité:** empêche une fuite d'info entre bots simulés — le scoping par-observateur n'existe QUE pour les ids >=100 et doit rester étanche.
- **Statut:** à implémenter

### 129 — Un bot simulé voit son propre rôle (self >=100)
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** `EnsureOwnRoleRevealed(_clientId, _observerId, _simInfo)` où self = l'id bot — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:125,147-153`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, un Character seat 101 (bot) présent, aucun reveal explicite.
- **When:** on lit `GetCharacterInfo(101, _observerId: 101)` (le bot regarde sa propre carte).
- **Then:** `.isRoleRevealed >= RevealLevel.Personal` (le bot connaît son rôle) alors que `GetCharacterInfo(5, 101).isRoleRevealed == False` pour un tiers.
- **Stabilité:** l'invariant "voir son propre rôle" doit valoir aussi pour les identités simulées, sans révéler les autres au bot.
- **Statut:** à implémenter

### 130 — SetRevealLevelRpc Public diffuse au viewer local ET à tous les cerveaux simulés
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** branche `_revealLevel == Public` qui itère `simulationsKnowledge.Keys` après le reveal local — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:212-219`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 5, deux cerveaux bots 101 et 102 déjà matérialisés (via une lecture préalable).
- **When:** on invoque `SetRevealLevelRpc(5, "isRoleRevealed", RevealLevel.Public, false)` (host==intended viewer).
- **Then:** `GetCharacterInfo(5, 0)`, `GetCharacterInfo(5, 101)` et `GetCharacterInfo(5, 102)` valent tous `RevealLevel.Public`.
- **Stabilité:** garantit qu'une révélation publique (chaînage/portail) est visible par l'humain local ET les bots co-hébergés, pas seulement le viewer réel.
- **Statut:** à implémenter

### 131 — ClearHacked est idempotent quand isHacked vaut déjà False
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** early-return `if (_info.isHacked == RevealLevel.False) return;` avant toute notification — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:279-282`
- **Couche:** PlayMode-StartHost
- **Given:** un revealer spawné, localClientId 0, subject seat 5 dont `isHacked == False`, un compteur abonné à `onCharacterInfoRevealedChanged`.
- **When:** on invoque `ClearHackedRpc(5)` (observer local 0, hack jamais posé).
- **Then:** `isHacked` reste `False` et le compteur `onCharacterInfoRevealedChanged` reste à 0 (aucune notification superflue).
- **Stabilité:** empêche un re-calcul/retrait de glitch (CardHackGlitch) inutile quand l'expiration de POmniscience s'applique à une carte jamais piratée.
- **Statut:** à implémenter

### 132 — Reveal Personal ciblé sur l'observateur distant n'atterrit que chez ce client
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** `SendRevealLevelRpc` → `RpcTarget.Single(observer)` (reveal routé, pas broadcast) — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:188-201`
- **Couche:** PlayMode-2NM
- **Given:** host + un vrai second client, un revealer répliqué, subject seat 999, observer = `ClientNm.LocalClientId`.
- **When:** le host appelle `SendRevealLevelRpc(999, "isRoleRevealed", Personal, observer, false)`.
- **Then:** le store du client atteint `Personal` pour l'observateur, mais le store du host reste `False`.
- **Stabilité:** attrape la classe de régression CursedVision/Embrace (reveal qui fuit à tout le monde), invisible en StartHost.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.PersonalReveal_ToRemoteObserver_LandsOnClientRevealerOnly)

### 133 — Reveal Personal ciblé sur le host ne fuit pas au client distant
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** ciblage `RpcTarget.Single` du miroir de reveal — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:188-201`
- **Couche:** PlayMode-2NM
- **Given:** host + un vrai second client, un revealer répliqué, subject seat 999.
- **When:** le host appelle `SendRevealLevelRpc(999, "isRoleRevealed", Personal, hostObserver, false)` puis un marqueur sur `isCorruptRevealed` vers le client pour prouver la livraison du fil.
- **Then:** après réception du marqueur, `isRoleRevealed` reste `False` dans le store du client distant.
- **Stabilité:** miroir de 132 — un reveal destiné au host ne doit pas révéler la carte chez les autres.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.PersonalReveal_ToHostObserver_DoesNotLeakToRemoteClientRevealer)

### 134 — Reveal Public se diffuse aux deux stores répliqués
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** `SetRevealLevelRpc` en `[Rpc(SendTo.Everyone)]` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:204-224`
- **Couche:** PlayMode-2NM
- **Given:** host + un vrai second client, un revealer répliqué, subject seat 999.
- **When:** le host invoque `SetRevealLevelRpc(999, "isRoleRevealed", RevealLevel.Public, false)`.
- **Then:** les stores host ET client convergent tous deux vers `RevealLevel.Public`.
- **Stabilité:** garantit qu'une révélation publique (fin de partie / portail) est bien vue par tous les vrais clients.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.PublicReveal_FansOutToBothRevealerStores)

### 135 — Hack Personal puis clear ciblé fait l'aller-retour sur l'observateur distant
- **Catégorie:** C — système de REVEAL (GameInfoRevealer)
- **Mécanique:** route de downgrade dédiée `SendClearHackedRpc` → `ClearHacked` (isHacked→False) hors chemin monotone — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:247-291`
- **Couche:** PlayMode-2NM
- **Given:** host + un vrai second client, un revealer répliqué, subject seat 999, observer = `ClientNm.LocalClientId`.
- **When:** le host pose `SendRevealLevelRpc(999, "isHacked", Personal, observer)` puis `SendClearHackedRpc(999, observer)`.
- **Then:** le store du client passe `isHacked` à `Personal` puis revient à `RevealLevel.False` (downgrade que la voie monotone ne peut pas faire).
- **Stabilité:** garantit l'expiration de POmniscience — le seul chemin capable de rétrograder un reveal, testé cross-client.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.HackReveal_ThenTargetedClear_RoundTripsOnRemoteObserver)

### 136 — Advance sur bord de boucle rejoue le premier state in-loop et signale un nouveau jour
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `GameLoopMachine.Advance` day-pass (wasInGameLoop && !isInGameLoop[newIndex]) — `Assets/Scripts/Domain/GameLoopMachine.cs:65-69`
- **Couche:** EditMode-pure
- **Given:** `isInGameLoop = [false(Lobby), false(RoleAttr), true(Awakening), true(Vote), false(VictoryCheck)]`, `currentIndex = 3` (Vote, dernier in-loop), `gameHasStartedFirstLoop = true`, `ignoreGameLoop = false`, `ignoreGameLoopThisCall = false`.
- **When:** appel `new GameLoopMachine().Advance(3, flags, true, false, false)`.
- **Then:** `NewIndex == 2` (retour au premier index in-loop = Awakening), `FireNewDayPassed == true`, `FireGameStarted == false`.
- **Stabilité:** empêche une régression de l'arithmétique de bouclage de nuit (mauvais index de retour ou jour non signalé).
- **Statut:** déjà couvert (GameLoopMachineTests.Advance_DayPass_JumpsBackToFirstInLoopIndex_AndFiresNewDayPassed)

### 137 — Franchir un bord de boucle incrémente gameLoopCount et currentDay
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `GameManager.NextGameState` → `onNewDayPassed.Invoke()` câblé à `gameLoopCount++` dans Awake ; `currentDay => gameLoopCount + 1` — `Assets/Scripts/GameLogic/GameManager.cs:105,138,330-333`
- **Couche:** PlayMode-StartHost
- **Given:** un `GameManager` spawné en StartHost avec un `gameStates` de test dont les flags `isInGameLoop` forment une seule zone in-loop de 2 états, `currentGameStateIndex.Value` positionné sur le dernier état in-loop, `gameLoopCount == 0`, `gameHasStartedFirstLoop == true`.
- **When:** appel serveur `NextGameState()` qui franchit le bord de boucle (day-pass).
- **Then:** `gameLoopCount == 1` et `currentDay == 2` après l'appel.
- **Stabilité:** garantit que le compteur de jour (affiché / consommé par les pouvoirs par-tour) avance exactement une fois par nuit.
- **Statut:** à implémenter

### 138 — SwitchGameState termine l'ancien état serveur et démarre le nouveau
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `GameManager.SwitchGameState` : `_oldGameState.OnEndStateServer()` puis `currentGameStateIndex.Value = newIndex` puis `_newGameState.OnStartStateServer()` — `Assets/Scripts/GameLogic/GameManager.cs:369-382`
- **Couche:** PlayMode-StartHost
- **Given:** un `GameManager` StartHost avec deux `DummyGameState` instrumentés (compteurs `onEndServerCount`/`onStartServerCount`) aux index 0 et 1, `currentGameStateIndex.Value == 0`.
- **When:** appel serveur `SetGameState` ciblant l'état d'index 1.
- **Then:** l'état 0 a reçu exactement un `OnEndStateServer`, l'état 1 exactement un `OnStartStateServer`, et `currentGameStateIndex.Value == 1`.
- **Stabilité:** empêche une régression de l'ordre fin-avant-début des hooks serveur lors d'une transition d'état.
- **Statut:** à implémenter

### 139 — Une vraie transition d'état exécute les RPC de cycle de vie client sur la réplique de chaque client
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `GameManager.SwitchGameState` émet `DoStateMethodRpc(..., OnEndStateClient/OnStartStateClient, clients)` — `Assets/Scripts/GameLogic/GameManager.cs:376,381`
- **Couche:** PlayMode-2NM
- **Given:** fixture 2-NetworkManager (host + 1 vrai client) avec un `LoopProbeState` enregistrant les hooks `OnStartStateClient`/`OnEndStateClient` sur le clone d'état de chaque NM.
- **When:** le serveur déclenche une transition depuis le `LoopProbeState` vers l'état suivant.
- **Then:** le clone d'état côté client reçoit `OnEndStateClient` sur le state quitté et `OnStartStateClient` sur le state entré (observé sur la réplique du second client, pas seulement l'hôte).
- **Stabilité:** empêche que la logique client d'un état (UI/anim) ne s'exécute jamais sur un vrai client distant après une transition.
- **Statut:** déjà couvert (GameLoopClientLifecycleTests.RealStateTransition_RunsClientLifecycleRpcs_OnClientOwnStateClones)

### 140 — IsInLobbyPhase vaut vrai au démarrage (LobbyState index 0) et faux une fois la partie lancée
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `GameManager.IsInLobbyPhase` : `GetGameState(currentIndex) is LobbyState` (out-of-range → true) — `Assets/Scripts/GameLogic/GameManager.cs:396-407`
- **Couche:** PlayMode-StartHost
- **Given:** un `GameManager` StartHost dont `gameStates` place un `LobbyState` à l'index 0 et un autre état (non-Lobby) à l'index 1, `currentGameStateIndex.Value == 0`.
- **When:** on lit `IsInLobbyPhase`, puis on passe `currentGameStateIndex.Value` à 1 et on relit.
- **Then:** `true` à l'index 0, `false` à l'index 1.
- **Stabilité:** protège la porte serveur de connexion (ConnectionApprovalGate) qui n'autorise les join que tant qu'on est en lobby.
- **Statut:** à implémenter

### 141 — Auto-start lobby : tous prêts + composition valide fait avancer la boucle
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `LobbyState.TryAutoStart` → `AllParticipantsReady() && TryResolveValidComposition()` → `Loop.NextGameState()` (latch `_started`) — `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:71-95`
- **Couche:** PlayMode-StartHost
- **Given:** un `LobbyState` courant en StartHost, un `LobbyPlayerInfoHolder` où chaque Character joueur a une entrée `isReady == true`, et une composition de rôles valide (Σmax ≥ joueurs, ≥1 anomaly, ≥1 chosen).
- **When:** un tick `StateUpdateServer` déclenche `TryAutoStart`.
- **Then:** `Loop.NextGameState()` est invoqué une seule fois (index quitte le Lobby) et `_started` est latché.
- **Stabilité:** garantit le démarrage automatique dès que tout le monde est prêt, cœur de la boucle lobby→partie.
- **Statut:** déjà couvert (LobbyReadyAutoStartTests.AutoStart_AllParticipantsReadyAndValidComposition_Advances)

### 142 — Start guard : moins de joueurs que de rôles forcés refuse le démarrage
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `LobbyState.OnStartGameButtonPressed` → `TryResolveValidComposition` (via `RoleAttributionState.ValidateComposition` / `CompositionValidator`) refuse et log — `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:55-65`
- **Couche:** PlayMode-StartHost
- **Given:** un `LobbyState` StartHost avec 2 Characters joueurs et une composition dont Σforced == 3 (plus de rôles garantis que de joueurs réels).
- **When:** appel `OnStartGameButtonPressed()`.
- **Then:** retourne `false`, la boucle n'avance pas (index inchangé), un warning est loggé.
- **Stabilité:** empêche de démarrer une partie impossible à distribuer (rôles forcés > joueurs).
- **Statut:** déjà couvert (LobbyStateStartGuardTests.OnStartGameButtonPressed_FewerPlayersThanMandatoryRoles_WarnsAndDoesNotAdvance)

### 143 — Latch _started : un ForceStart après un démarrage déjà latché est un no-op
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `LobbyState.ForceStart` : garde `if (_started || !IsServer) return;` — `Assets/Scripts/GameLogic/GameStates/LobbyState.cs:128-139`
- **Couche:** PlayMode-StartHost
- **Given:** un `LobbyState` StartHost avec une composition valide, sur lequel un premier `ForceStart()` a réussi (`_started == true`, boucle avancée d'un cran).
- **When:** un second appel `ForceStart()` (sans re-entrer dans le Lobby).
- **Then:** aucune nouvelle transition n'est déclenchée (index inchangé par rapport à l'état post-premier-start).
- **Stabilité:** empêche un double-démarrage (double NextGameState) qui sauterait un état de la séquence de partie.
- **Statut:** à implémenter

### 144 — Distribution des rôles déterministe sous seed fixe
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `RoleAttributionState.OnStartStateServer` → `RoleDistributor.Distribute(...)` avec provider seedé — `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:78-91`
- **Couche:** PlayMode-StartHost
- **Given:** un `RoleAttributionState` StartHost, un pool de rôles multi-factions (anomaly/chosen), N Characters réels, et un `UnityRandomProvider` initialisé avec un seed fixe connu.
- **When:** deux exécutions successives d'`OnStartStateServer` avec le même seed et le même roster.
- **Then:** la même séquence de rôles assignés (mêmes rôles, mêmes owners, même ordre fake-puis-réel) est produite aux deux runs.
- **Stabilité:** garantit la reproductibilité de l'attribution (golden master) — toute dérive RNG casse ce test.
- **Statut:** déjà couvert (RoleAssignmentGoldenMasterTests.RoleAssignment_MultiRole_Exhaustion_SeedA)

### 145 — Nombre de fakes = |joueurs − total des rôles à attribuer|
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `RoleAttributionState.OnStartStateServer` : `_fakeRoleAmountToRemove = Abs(GetCharacters().Count - _totalRolesToAttribute)` puis `CreateNewFakeCharacter()` par index fake — `Assets/Scripts/GameLogic/GameStates/RoleAttributionState.cs:66-85`
- **Couche:** PlayMode-StartHost
- **Given:** un `RoleAttributionState` StartHost avec 2 Characters réels et un pool dont Σmax == 5 rôles à attribuer (aucun rule de faction, harnais).
- **When:** exécution d'`OnStartStateServer`.
- **Then:** exactement 3 fake characters (5−2) sont créés et reçoivent un rôle avant les 2 réels.
- **Stabilité:** empêche un mauvais compte de sièges fantômes (pool sur/sous-dimensionné vs joueurs) qui décalerait le plateau.
- **Statut:** à implémenter

### 146 — Égalité au vote se résout en skip et ne chaîne personne
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.OnEndStateServer` → `VoteTally.Resolve` renvoie `skipVoteId` sur égalité ; pas d'`AddCharacterToChainingList` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:213-236` ; `Assets/Scripts/Domain/VoteTally.cs:42-47`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` StartHost avec deux candidats réels ayant chacun 1 vote (égalité au sommet) et le bucket `SKIP_VOTE_ID` présent.
- **When:** fin de l'état (`OnEndStateServer`).
- **Then:** `mostVotedPlayer == SKIP_VOTE_ID` et `chainingManager.chainingPlayers` reste vide.
- **Stabilité:** empêche qu'une égalité chaîne à tort un joueur (règle de tie-break → skip).
- **Statut:** déjà couvert (VoteTallyGoldenMasterTests.TieForTop_ResolvesToSkip_NobodyChained)

### 147 — CanVote rejette un second vote du même votant
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.CanVote` : `votesForPlayer.Values.Any(l => l.Contains(_playerId))` → false (sans `_ignoreAlreadyVoted`) — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:87-94`, appelé par `OnPlayerVotedRpc` `:55-69`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` StartHost, votant `A` réel non éliminé qui a déjà voté (son id figure dans un `votesForPlayer[...]`).
- **When:** `OnPlayerVotedRpc(senderId=A, votedPlayerId=B)` est traité une seconde fois.
- **Then:** le vote est refusé (aucun ajout dans `votesForPlayer[B]`), un warning est loggé.
- **Stabilité:** empêche le bourrage d'urnes (un joueur votant plusieurs fois).
- **Statut:** à implémenter

### 148 — CanVote exclut un personnage éliminé
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.CanVote` : `_character.isEliminated.Value` → false — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:96-100`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` StartHost, votant `A` réel dont le Character a `isEliminated.Value == true`, n'ayant pas encore voté.
- **When:** appel `CanVote(A)`.
- **Then:** retourne `false`.
- **Stabilité:** garantit qu'un joueur éliminé ne compte plus dans le dénominateur d'électeurs ni ne peut voter.
- **Statut:** à implémenter

### 149 — CanVote exclut un vrai client qui a quitté la partie
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.CanVote` : `_playerId < 100 && gameManager.HasClientLeft(_playerId)` → false ; departed-set peuplé par `HandlePlayerLeft` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:107-110` ; `Assets/Scripts/GameLogic/GameManager.cs:635,670`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` StartHost, un vrai client `A` (id < 100) non éliminé et non-fake dont le Character existe encore, mais dont l'id a été enregistré comme parti (`HasClientLeft(A) == true`).
- **When:** appel `CanVote(A)`.
- **Then:** retourne `false`, alors qu'un joueur simplement chaîné-mais-présent resterait éligible.
- **Stabilité:** re-exclut un déconnecté du dénominateur de vote (comportement perdu quand fakify a été remplacé par le chaining).
- **Statut:** à implémenter

### 150 — Vote auto-clôturé quand tous les électeurs éligibles ont voté
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.OnPlayerVotedRpc` : `sum(votes) >= GetCharacters().Count(c => CanVote(c, true))` → `voteTimer = Min(voteTimer, 5)` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:80-84`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` StartHost avec exactement 2 électeurs éligibles, `voteTimer == voteDuration` (grand, ex. 30), un premier vote déjà enregistré.
- **When:** le second (et dernier) électeur éligible vote via `OnPlayerVotedRpc`.
- **Then:** `voteTimer <= 5` après traitement (le timer est effondré pour clôturer bientôt).
- **Stabilité:** empêche d'attendre inutilement la fin du timer quand plus personne ne peut voter.
- **Statut:** à implémenter

### 151 — Expiration du voteTimer fait avancer la boucle
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VoteState.StateUpdateServer` : `voteTimer -= dt; if (voteTimer <= 0) Loop.NextGameState()` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:283-294`
- **Couche:** PlayMode-StartHost
- **Given:** un `VoteState` courant StartHost avec `voteTimer` positionné à une valeur très petite (ex. 0.001s).
- **When:** un ou deux ticks `StateUpdateServer` suffisent à faire passer `voteTimer` sous 0.
- **Then:** `Loop.NextGameState()` est invoqué exactement une fois (transition hors du VoteState).
- **Stabilité:** garantit que le vote se termine à l'expiration du timer même si personne ne vote.
- **Statut:** à implémenter

### 152 — AwakeLayer n'éveille pas les personnages chaînés ou éliminés
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `AwakeningState.AwakeLayer` : `if (isChained.Value || isEliminated.Value) continue;` — `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:91-99`
- **Couche:** PlayMode-StartHost
- **Given:** un `AwakeningState` StartHost dont le layer 0 cible un rôle donné ; deux Characters de ce rôle, l'un `isChained.Value == true`, l'autre libre.
- **When:** `AwakeLayer(0)` est exécuté (via `OnStartStateServer`).
- **Then:** seul le Character libre figure dans `currentlyAwakenedCharacters` (le chaîné n'est pas éveillé).
- **Stabilité:** empêche qu'un joueur chaîné/éliminé se réveille et bloque l'avancée du layer (drain impossible).
- **Statut:** à implémenter

### 153 — Chaîner le Mage active l'étape TakeDownThePortal et pointe le mage id
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `ChainingManager.ChainCharacterRpc` : si le rôle porte `takeDownThePortalPowerDataObject` → `portalState.shouldActivate = true` + `SetMageCharacterRpc(ownerId)` — `Assets/Scripts/GameLogic/ChainingManager.cs:89-106` ; `Assets/Scripts/GameLogic/GameStates/TakeDownThePortalState.cs:38-41`
- **Couche:** PlayMode-StartHost
- **Given:** un `ChainingManager` StartHost câblé, un `TakeDownThePortalState` dans `gameStates` avec `shouldActivate == false`, un Character « Mage » dont le rôle possède la power take-down-the-portal.
- **When:** `ChainCharacterRpc(mageOwnerId)` sur le serveur.
- **Then:** le Character est chaîné (`isChained.Value == true`), `TakeDownThePortalState.shouldActivate == true` et `mageCharacterOwnerId == mageOwnerId`.
- **Stabilité:** garantit que l'étape spéciale du portail ne s'active que quand le Mage est réellement chaîné.
- **Statut:** à implémenter

### 154 — VictoryConditionCheck avec une équipe gagnante saute à GameEndingState
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VictoryConditionCheckState.TryResolveVictoryNow` : si `VictoryEvaluator.Evaluate` renvoie ≥1 équipe → `GameEndingState.SetWinnersServer(...)` + `Loop.SetGameState(typeof(GameEndingState))` + return true — `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:44-83`
- **Couche:** PlayMode-StartHost
- **Given:** un `VictoryConditionCheckState` StartHost et un roster où une condition de victoire est remplie (ex. tous les anomaly chaînés → les chosen gagnent), avec un `GameEndingState` seedé dans `gameStates`.
- **When:** `OnStartStateServer()` (qui appelle `TryResolveVictoryNow`).
- **Then:** l'état courant devient `GameEndingState` (via `SetGameState`), et `SetWinnersServer` a reçu l'équipe gagnante ; aucun `NextGameState` linéaire n'est fait.
- **Stabilité:** garantit qu'une victoire détectée termine réellement la partie plutôt que de continuer la boucle.
- **Statut:** à implémenter

### 155 — VictoryConditionCheck sans gagnant fait avancer la boucle linéairement
- **Catégorie:** D — Game States & boucle de jeu
- **Mécanique:** `VictoryConditionCheckState.OnStartStateServer` : `if (TryResolveVictoryNow()) return; Loop.NextGameState();` (Evaluate renvoie 0 équipe → false) — `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:22-33,73-75`
- **Couche:** PlayMode-StartHost
- **Given:** un `VictoryConditionCheckState` StartHost et un roster où aucune condition de victoire n'est remplie (au moins un anomaly libre et au moins un chosen non gagnant), `GameEndingState` seedé.
- **When:** `OnStartStateServer()`.
- **Then:** `TryResolveVictoryNow` retourne false, `Loop.NextGameState()` est invoqué une fois, l'état courant n'est PAS `GameEndingState`.
- **Stabilité:** empêche une fin de partie prématurée (faux positif de victoire) ou un blocage (pas d'avancée) quand la partie doit continuer.
- **Statut:** à implémenter

### 156 — WAnomalyCorruption : un anomaly soigné (IsCorrupted=false) fait échouer la victoire anomaly
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — premier non-fake non-corrompu → false — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:43-45`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` (hand-built, cf. pattern `WinningConditionSnapshotOracleTests.cs:148-157`) de 3 `CharacterSnapshot` non-fake : owner 1 `IsCorrupted=true`, owner 2 `IsCorrupted=true`, owner 3 `IsCorrupted=false` (précédemment corrompu puis soigné — au niveau victoire ça se lit comme `IsCorrupted=false`), tous `FactionType.anomaly`.
- **When:** `new WAnomalyCorruption().CheckCondition(snapshot)`.
- **Then:** retourne `false` (le 3e non-corrompu déclenche le retour anticipé).
- **Stabilité:** empêche qu'un « heal » qui remet `IsCorrupted=false` soit ignoré et laisse déclarer une victoire anomaly à tort.
- **Statut:** à implémenter

### 157 — WAnomalyCorruption : population 100% fake est vacuously true
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — le filtre `if (IsFake) continue;` — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:38-39`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` tous `IsFake=true`, `IsCorrupted=false`, `FactionType.unknown`.
- **When:** `new WAnomalyCorruption().CheckCondition(snapshot)`.
- **Then:** retourne `true` (tous les éléments sont `continue`'d, aucun non-fake non-corrompu → boucle vacuously vraie).
- **Stabilité:** distingue le cas « liste non vide mais entièrement fake » du cas « liste vide » — empêche qu'un durcissement du filtre fake casse la sémantique vacuously-true.
- **Statut:** à implémenter

### 158 — WAnomalyCorruption : un anomaly corrompu ET chaîné compte comme corrompu
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — ne lit QUE `IsCorrupted`, jamais `IsChained` — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:43`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` non-fake : owner 1 `IsCorrupted=true, IsChained=true`, owner 2 `IsCorrupted=true, IsChained=false`, tous `FactionType.anomaly`.
- **When:** `new WAnomalyCorruption().CheckCondition(snapshot)`.
- **Then:** retourne `true` (l'état chaîné n'est jamais lu ; seul `IsCorrupted` compte).
- **Stabilité:** empêche qu'un futur couplage victoire↔chaînage fasse dépendre la victoire anomaly de `IsChained` (interaction chained×corrupted).
- **Statut:** à implémenter

### 159 — WAnomalyCorruption ignore la faction (mix chosen/anomaly/marginal tous corrompus → true)
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — la boucle ne lit jamais `FactionType` — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:36-48`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 3 `CharacterSnapshot` non-fake tous `IsCorrupted=true` : owner 1 `FactionType.chosen`, owner 2 `FactionType.anomaly`, owner 3 `FactionType.marginal`.
- **When:** `new WAnomalyCorruption().CheckCondition(snapshot)`.
- **Then:** retourne `true` (la faction n'est pas un critère : « tout le monde corrompu » suffit, quelle que soit la faction).
- **Stabilité:** pin que la victoire anomaly = tous corrompus indépendamment de la faction ; empêche un ajout accidentel de filtre faction.
- **Statut:** à implémenter

### 160 — WChosenChainedAllAnomaly : au seuil — un seul anomaly chaîné parmi des chosen → true
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — non-anomaly `continue`, anomaly non-chaîné → false — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:50-57`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 3 `CharacterSnapshot` non-fake : owner 1 & 2 `FactionType.chosen` (`IsChained=false`), owner 3 `FactionType.anomaly, IsChained=true`.
- **When:** `new WChosenChainedAllAnomaly().CheckCondition(snapshot)`.
- **Then:** retourne `true` (les chosen sont `continue`'d, l'unique anomaly est chaîné).
- **Stabilité:** cas victoire chosen minimal réel — empêche que l'état `IsChained` des chosen (non pertinent) soit lu à tort.
- **Statut:** à implémenter

### 161 — WChosenChainedAllAnomaly : juste sous le seuil — même population, l'anomaly non chaîné → false
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — `if (!IsChained) return false;` sur un anomaly — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:55-57`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` identique à 160 mais owner 3 `FactionType.anomaly, IsChained=false`.
- **When:** `new WChosenChainedAllAnomaly().CheckCondition(snapshot)`.
- **Then:** retourne `false` (l'unique anomaly libre déclenche le retour anticipé).
- **Stabilité:** paire « juste-sous-le-seuil » de 160 — empêche que la victoire chosen soit déclarée alors qu'un anomaly reste libre.
- **Statut:** à implémenter

### 162 — WChosenChainedAllAnomaly : un anomaly chaîné ET corrompu satisfait quand même la condition
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — ne lit que `IsChained` de l'anomaly, jamais `IsCorrupted` — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:55`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 1 `CharacterSnapshot` non-fake : owner 1 `FactionType.anomaly, IsChained=true, IsCorrupted=true`.
- **When:** `new WChosenChainedAllAnomaly().CheckCondition(snapshot)`.
- **Then:** retourne `true` (`IsCorrupted` n'est jamais lu par cette condition ; seul le chaînage des anomalies compte).
- **Stabilité:** empêche qu'un anomaly à la fois corrompu et chaîné (état plausible en fin de partie) fasse dévier la victoire chosen (interaction corrupted×chained).
- **Statut:** à implémenter

### 163 — WChosenChainedAllAnomaly : deux anomalies, un chaîné un libre → false quel que soit l'ordre
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — conjonction « TOUTES les anomalies chaînées » sur la boucle entière — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:43-60`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` non-fake `FactionType.anomaly` : owner 1 `IsChained=true`, owner 2 `IsChained=false` (l'anomaly libre est en DERNIÈRE position).
- **When:** `new WChosenChainedAllAnomaly().CheckCondition(snapshot)`.
- **Then:** retourne `false` (il suffit d'un seul anomaly libre, où qu'il soit dans la liste).
- **Stabilité:** verrouille la sémantique universelle (∀ anomaly chaîné) au niveau pur ; empêche une régression en « ∃ un anomaly chaîné ».
- **Statut:** à implémenter

### 164 — WMarginalIsChainedWin ignore la faction de l'owner (owner chosen chaîné → true)
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — sur match `OwnerClientId`, retourne `!IsFake && IsChained` sans jamais lire `FactionType` — `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:32-34`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 1 `CharacterSnapshot` : owner 1 `IsFake=false, IsChained=true, FactionType.chosen`. Condition `ownerClientId = 1`.
- **When:** `new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `true` (la faction de l'owner n'est pas un critère — seuls le match d'id, `!IsFake` et `IsChained`).
- **Stabilité:** pin explicite que WMarginal ne filtre PAS sur la faction ; empêche l'ajout silencieux d'un `FactionType == marginal` qui casserait la condition pour un owner reclassé.
- **Statut:** à implémenter

### 165 — WMarginalIsChainedWin : owner en double id, premier match gagne
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — `return` dès le PREMIER `OwnerClientId == ownerClientId` — `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:31-35`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` partageant `OwnerClientId=1` (situation possible via le doublon de réplication NetworkList connu, cf. dedup `[CHARLIST]`) : le premier `IsChained=true`, le second `IsChained=false`, tous `IsFake=false`. Condition `ownerClientId = 1`.
- **When:** `new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `true` (la boucle retourne sur la première entrée matchée, sans consulter la seconde).
- **Stabilité:** documente le comportement first-match face à un doublon d'`OwnerClientId` ; empêche une régression vers un « dernier gagne » ou un scan complet.
- **Statut:** à implémenter

### 166 — WMarginalIsChainedWin : owner corrompu mais NON chaîné → false
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — la victoire dépend de `IsChained`, pas de `IsCorrupted` — `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:34`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 1 `CharacterSnapshot` : owner 1 `IsFake=false, IsChained=false, IsCorrupted=true, FactionType.marginal`. Condition `ownerClientId = 1`.
- **When:** `new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `false` (la corruption ne se substitue pas au chaînage).
- **Stabilité:** interaction corrupted×chained — empêche qu'un owner corrompu mais libre soit déclaré vainqueur marginal.
- **Statut:** à implémenter

### 167 — WMarginalIsChainedWin : owner fake chaîné → false
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — le terme `!IsFake` — `Assets/Scripts/Characters/WinningConditions/WMarginalIsChainedWin.cs:33`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 1 `CharacterSnapshot` : owner 1 `IsFake=true, IsChained=true, FactionType.marginal`. Condition `ownerClientId = 1`.
- **When:** `new WMarginalIsChainedWin { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `false` (un owner fake ne peut pas gagner même chaîné).
- **Stabilité:** pin du terme `!IsFake` au niveau pur — empêche qu'un slot fake chaîné déclenche une fausse victoire marginale.
- **Statut:** déjà couvert (Oracle_WMarginal_FakeOwner_False — `WinningConditionSnapshotOracleTests.cs:148-157`, même snapshot hand-built pur)

### 168 — WOmniscienceHackedCharacter : cible chaînée+chosen ET corrompue → true
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — conjonction finale `IsChained && FactionType == chosen`, `IsCorrupted` non lu — `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:68-70`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` : owner 1 (`FactionType.marginal`) `HackedByOmniscienceTarget = 2` ; owner 2 `IsChained=true, FactionType.chosen, IsCorrupted=true`. Condition `ownerClientId = 1`.
- **When:** `new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `true` (la corruption de la cible n'entre pas dans la conjonction ; chaîné + chosen suffisent).
- **Stabilité:** empêche qu'une cible piratée corrompue casse la victoire robot (interaction corrupted×victoire omniscience).
- **Statut:** à implémenter

### 169 — WOmniscienceHackedCharacter : cible piratée FAKE (faction unknown) → false
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — `FactionType == chosen` échoue pour une cible fake (le builder mappe un fake en `FactionType.unknown`) — `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:70` + `Assets/Scripts/GameLogic/Snapshot/GameSnapshotBuilder.cs:59,62`
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 2 `CharacterSnapshot` : owner 1 (`FactionType.marginal`) `HackedByOmniscienceTarget = 2` ; owner 2 `IsFake=true, IsChained=true, FactionType.unknown` (comme le produit le builder pour un fake). Condition `ownerClientId = 1`.
- **When:** `new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `false` (`unknown != chosen` → le second conjunct échoue).
- **Stabilité:** interaction fake×cible — empêche qu'une cible piratée fake (faction non résolue) soit comptée comme chosen et déclenche une fausse victoire robot.
- **Statut:** à implémenter

### 170 — WOmniscienceHackedCharacter : auto-hack (owner cible lui-même) chaîné+chosen → true
- **Catégorie:** E — Conditions de victoire
- **Mécanique:** `CheckCondition(GameSnapshot)` — la cible est retrouvée par `OwnerClientId == hackedId` ; le modèle « robot auto-piraté » est explicitement design-flagged — `Assets/Scripts/Characters/WinningConditions/WOmniscienceHackedCharacter.cs:53,66-70` (TODO robot-victory :9-12)
- **Couche:** EditMode-pure
- **Given:** un `GameSnapshot` de 1 `CharacterSnapshot` : owner 1 `HackedByOmniscienceTarget = 1` (id de soi-même), `IsChained=true, FactionType.chosen`. Condition `ownerClientId = 1`.
- **When:** `new WOmniscienceHackedCharacter { ownerClientId = 1 }.CheckCondition(snapshot)`.
- **Then:** retourne `true` (l'owner se matche comme sa propre cible piratée, chaîné et chosen).
- **Stabilité:** verrouille le modèle « robot techniquement auto-piraté » (design-owned, cf. TODO) au niveau pur — toute refonte robot future qui change ce comportement rougira ce test et forcera une décision explicite avec Poyo.
- **Statut:** à implémenter

### 171 — Le décompte messageLeft d'un envoi de message traverse le fil vers la réplique cliente
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `OnMessageSentRpc` décrémente `messageLeft` côté serveur — `Assets/Scripts/MessageSystem/SendMessagePanel.cs:66-72` (write NetworkVariable `Character.messageLeft`, `Assets/Scripts/Characters/Character.cs:26`)
- **Couche:** PlayMode-2NM
- **Given:** host + un vrai second NM ; un Character possédé par `ClientNm.LocalClientId` spawné via `SpawnRealCharacterForClient`, `messageLeft.Value == 1` (valeur d'init) ; on résout la réplique cliente par `ClientNm.SpawnManager.SpawnedObjects[netId]` (jamais `ClientCm.GetCharacter`).
- **When:** on invoque `OnMessageSentRpc(targetSeat, "hi")` sur l'objet HÔTE (corps serveur : `messageLeft.Value -= 1`).
- **Then:** la réplique cliente observe `messageLeft.Value == 0` après un tick réel ; l'hôte lit aussi 0 (sanity).
- **Stabilité:** empêche une régression où le compteur de messages n'est pas répliqué (le client croirait pouvoir encore écrire alors que le serveur l'a épuisé).
- **Statut:** à implémenter

### 172 — hasSentMessageThisTurn passe true sur la réplique cliente après un envoi
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `OnMessageSentRpc` pose `hasSentMessageThisTurn.Value = true` — `Assets/Scripts/MessageSystem/SendMessagePanel.cs:71` (NetworkVariable `Character.hasSentMessageThisTurn`, `Assets/Scripts/Characters/Character.cs:27`)
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId`, `hasSentMessageThisTurn.Value == false` au départ ; réplique cliente résolue par NetworkObjectId.
- **When:** on invoque `OnMessageSentRpc(clientSeat, "hi")` sur l'objet HÔTE.
- **Then:** la réplique cliente observe `hasSentMessageThisTurn.Value == true` (transition false→true franchie par le fil).
- **Stabilité:** empêche que le verrou "un message par tour" reste invisible côté client (double-envoi silencieux si non répliqué).
- **Statut:** à implémenter

### 173 — Awaken remet hasSentMessageThisTurn à false sur la réplique cliente
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `AwakenCharacterServerRpc` remet `hasSentMessageThisTurn.Value = false` — `Assets/Scripts/Characters/Character.cs:140-146`
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId` avec un `Role` non-null ; d'abord `OnMessageSentRpc` sur l'hôte pour porter `hasSentMessageThisTurn.Value == true` (attendre la réplique cliente à true).
- **When:** on invoque `AwakenCharacterServerRpc()` sur l'objet HÔTE (corps serveur : `isAwakened=true`, `hasSentMessageThisTurn=false`).
- **Then:** la réplique cliente observe le retour `hasSentMessageThisTurn.Value == false` (transition true→false), et `isAwakened.Value == true`.
- **Stabilité:** empêche qu'un nouveau tour ne réautorise pas l'envoi côté client (reset non répliqué → joueur bloqué le tour suivant).
- **Statut:** à implémenter

### 174 — Sleep repasse isAwakened à false et diffuse onCharacterSleep sur la réplique
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `SleepCharacterServerRpc` → `isAwakened.Value=false` + `SleepCharacterClientRpc` (SendTo.Everyone) — `Assets/Scripts/Characters/Character.cs:154-167`
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId`, `Role` non-null, déjà réveillé (`AwakenCharacterServerRpc` sur l'hôte → attendre `isAwakened.Value==true` sur la réplique) ; on s'abonne à `onCharacterSleep` de la réplique cliente.
- **When:** on invoque `SleepCharacterServerRpc()` sur l'objet HÔTE.
- **Then:** la réplique cliente observe `isAwakened.Value == false` (transition true→false) ET l'événement `onCharacterSleep` fired via le `SendTo.Everyone` client-RPC.
- **Stabilité:** empêche une désync du cycle réveil→sommeil (couvre la branche « rendormissement » distincte du réveil déjà testé par AwakenServerRpc_SetsAwakenedAndFiresSleepBroadcast).
- **Statut:** à implémenter

### 175 — UpdateRoleRpc (SendTo.NotServer) atteint le client mais PAS le corps serveur
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `UpdateRoleRpc` est `[Rpc(SendTo.NotServer)]` — `Assets/Scripts/Characters/Character.cs:107-113` (exécute `role.UpdateRole` + `onRoleUpdated` UNIQUEMENT hors serveur)
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId` avec `role = new Role{ roleName="Base" }` posé sur hôte ET réplique ; on s'abonne à `onRoleUpdated` sur la réplique cliente ET sur l'objet hôte ; un flag serveur `hostFired=false`, client `clientFired=false`.
- **When:** on invoque `UpdateRoleRpc(new Role{ roleName="Updated" })` sur l'objet HÔTE (émetteur serveur → NGO ne livre qu'aux NotServer).
- **Then:** `clientFired == true` (le corps a tourné sur la réplique cliente) et `hostFired == false` (le corps n'a JAMAIS tourné côté serveur) — la directionnalité NotServer est respectée sur le fil.
- **Stabilité:** empêche une régression de cible RPC (Everyone/Server à la place de NotServer) qui ferait tourner l'update de rôle au mauvais endroit — invisible en StartHost (host==NotServer indiscernable).
- **Statut:** à implémenter

### 176 — AskForRoleUpdateRpc : aller-retour client→serveur→NotServer met à jour le rôle du client
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `AskForRoleUpdateRpc` `[Rpc(SendTo.Server, RequireOwnership=false)]` → `UpdateRoleRpc` `[Rpc(SendTo.NotServer)]` — `Assets/Scripts/Characters/Character.cs:100-113`
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId` ; `role` serveur = `new Role{ roleName="Server-Truth" }` ; la réplique cliente a un `role` distinct `new Role{ roleName="Stale" }`.
- **When:** on invoque `AskForRoleUpdateRpc()` sur la RÉPLIQUE CLIENTE (part vers le serveur, qui renvoie `UpdateRoleRpc(role)` aux NotServer).
- **Then:** la réplique cliente observe `role.roleName == "Server-Truth"` après l'aller-retour ; le rôle serveur reste `"Server-Truth"`.
- **Stabilité:** empêche la rupture du seul canal de resynchro de rôle à la demande d'un client (rôle figé côté client si l'aller-retour casse).
- **Statut:** à implémenter

### 177 — GiveRoleToCharacterRpc (SendTo.Everyone + spawn-promise async) livre le rôle à la réplique distante
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `GiveRoleToCharacterRpc` `[Rpc(SendTo.Everyone, RequireOwnership=true)]` → `GiveRoleToCharacterAsync` await `GetCharacterAsync` — `Assets/Scripts/Characters/CharacterManager.cs:348-362`
- **Couche:** PlayMode-2NM
- **Given:** host + client ; un Character possédé par `ClientNm.LocalClientId` spawné et projeté des deux côtés ; la réplique cliente a `role.roleName != "Attributed"`.
- **When:** on invoque `HostCm.GiveRoleToCharacterRpc(clientSeat, new Role{ roleName="Attributed" })` sur l'objet HÔTE (fan-out Everyone).
- **Then:** la réplique cliente observe `GetCharacter(clientSeat).role.roleName == "Attributed"` après résolution de la promesse de spawn (bounded wait).
- **Stabilité:** empêche que l'attribution de rôle échoue à atteindre un client dont le Character n'était pas encore résolu au moment du RPC (course promesse/spawn cross-process).
- **Statut:** à implémenter

### 178 — RemoveCharacter despawn la réplique cliente (porteur du départ-lobby)
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `RemoveCharacter` retire de `networkedCharacters` puis `Despawn()` — `Assets/Scripts/Characters/CharacterManager.cs:455-483` (branche lobby de `HandlePlayerLeft`, `Assets/Scripts/GameLogic/GameManager.cs:686`)
- **Couche:** PlayMode-2NM
- **Given:** host + client ; deux Characters spawnés aux seats `hostSeat=HostNm.LocalClientId` et `victimSeat=ClientNm.LocalClientId` ; répliques clientes résolues, `victimNetId` mémorisé.
- **When:** on appelle `HostCm.RemoveCharacter(victimSeat)` sur l'hôte.
- **Then:** `ClientNm.SpawnManager.SpawnedObjects` ne contient plus `victimNetId` (réplique despawnée) ET la réplique de `hostSeat` reste présente ; `ClientCm.GetCharacters(false).Count == 1`.
- **Stabilité:** empêche une réplique fantôme persistante côté client après un départ-lobby (siège vide non nettoyé sur les autres écrans).
- **Statut:** à implémenter

### 179 — Un client qui rejoint APRÈS les spawns reçoit chaque siège exactement une fois (garde [CHARLIST])
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** dédup self-healing de `RebuildCharactersCache` sur double-livraison NetworkList au late-joiner — `Assets/Scripts/Characters/CharacterManager.cs:150-184` (log `[CHARLIST]` ligne 169)
- **Couche:** PlayMode-2NM
- **Given:** un fixture DÉRIVÉ qui spawne 3 Characters sur l'hôte AVANT `StartClient` (l'ordre inverse du fixture de base, où le client est déjà connecté) — le client reçoit `networkedCharacters` déjà peuplé via l'initial-sync, chemin où le bug NGO #3280 livre une entrée en double.
- **When:** le client démarre et projette sa liste (`ClientCm.GetCharacters(false)`).
- **Then:** exactement 3 entrées côté client, seats uniques ; aucune levée d'assert ; le compteur reste 3 même après plusieurs lectures (garde persistante).
- **Stabilité:** empêche la réintroduction du doublon de carte au late-join (technomancer-duplicate-card) — testable UNIQUEMENT avec un second client qui reçoit l'initial-sync, jamais en StartHost. Note faisabilité : requiert un ordre de setup custom (spawn avant `StartClient`), le fixture de base connecte le client en `SetUp`.
- **Statut:** à implémenter

### 180 — InfiniteMessage (passif) pose messageLeft=MaxValue sur la réplique du propriétaire distant
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `SetMessageLeftExecutor` écrit `messageLeft.Value` via `CompositionRoot.For(nm).CharacterManager` — `Assets/Scripts/Characters/Powers/Runtime/Executors/SetMessageLeftExecutor.cs:10-16` (décision `InfiniteMessageDecision.Decide` → `SetMessageLeft(OwnerSlot, int.MaxValue)`, `Assets/Scripts/Domain/Powers/Decisions/InfiniteMessageDecision.cs:12-13`)
- **Couche:** PlayMode-2NM
- **Given:** propriétaire = `ClientNm.LocalClientId` (réplique distante), Character spawné ; un root lié à l'hôte (pattern OwnerLocalEffectBoundaryTests) pour que l'exécuteur résolve `HostCm` ; `messageLeft.Value == 1` au départ sur la réplique.
- **When:** on exécute l'effet `SetMessageLeft(ownerSlot=clientSeat, int.MaxValue)` via la pipeline exécuteur côté serveur.
- **Then:** la réplique cliente observe `messageLeft.Value == int.MaxValue`.
- **Stabilité:** empêche que le pouvoir « messages illimités » n'accorde rien côté client (write serveur non répliqué → joueur toujours plafonné).
- **Statut:** à implémenter

### 181 — CorruptionInsight : le reveal Personal de corruption n'atterrit QUE dans le store du propriétaire distant
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `CorruptionInsightDecision` émet `RevealInfo(slot, CorruptRevealed, Personal, OwnerSlot, broadcast=true)` par siège — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:14-21` (chemin réseau `GameInfoRevealer.SendRevealLevelRpc` ciblé `RpcTarget.Single(observer)`)
- **Couche:** PlayMode-2NM
- **Given:** propriétaire = `ClientNm.LocalClientId` ; un GameInfoRevealer répliqué (host + client) recâblé par NM (pattern RevealAsymmetryReplicationTests) ; un siège sujet `999UL` corrompu côté serveur.
- **When:** on émet le `RevealInfo(subject=999, CorruptRevealed, Personal, observer=clientSeat, broadcast=true)` via le chemin réseau du revealer.
- **Then:** le revealer CLIENT observe `GetCharacterInfo(999, clientSeat).isCorruptRevealed == Personal`, et le revealer HÔTE reste `False` pour son propre observer (reveal routé, pas diffusé).
- **Stabilité:** empêche une fuite du reveal de corruption d'un passif d'insight à un observateur non ciblé (classe de bug Cursed/Embrace, mais via le chemin networked broadcast=true, distinct de l'owner-local déjà couvert).
- **Statut:** à implémenter

### 182 — Omniscience : le marqueur hack Personal atteint le propriétaire distant sans fuir à l'hôte
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `OmniscienceDecision` émet `RevealInfo(TargetSlot, Hacked, Personal, OwnerSlot, broadcast=true)` — `Assets/Scripts/Domain/Powers/Decisions/OmniscienceDecision.cs:26-29`
- **Couche:** PlayMode-2NM
- **Given:** propriétaire (observer) = `ClientNm.LocalClientId` ; revealer répliqué recâblé par NM ; siège cible `999UL`.
- **When:** on émet `RevealInfo(target=999, Hacked, Personal, observer=clientSeat, broadcast=true)` via le revealer serveur.
- **Then:** le revealer CLIENT observe `GetCharacterInfo(999, clientSeat).isHacked == Personal` ; le revealer HÔTE reste `False` pour l'observer hôte.
- **Stabilité:** empêche que le marqueur « hacké » d'Omniscience soit visible d'un autre observateur que le lanceur ciblé (couvre la source Omniscience du hack, la garde `HackReveal_ThenTargetedClear` ne couvrant que le RPC brut).
- **Statut:** à implémenter

### 183 — ChainedByShadows : la jambe reveal-au-propriétaire atteint le store du propriétaire distant
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `ChainedByShadowsDecision` émet `RevealInfo(TargetSlot, RoleRevealed, Personal, OwnerSlot, broadcast=true)` en plus de l'ajout au chaining — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:20-22`
- **Couche:** PlayMode-2NM
- **Given:** propriétaire = `ClientNm.LocalClientId` ; cible faction `chosen` seat `999UL` ; revealer répliqué recâblé par NM + root lié hôte (pattern PowerPipelineClientReplicationTests).
- **When:** on lance PChainedByShadows sur la cible `chosen` correspondante (pipeline complet serveur).
- **Then:** le revealer CLIENT observe `GetCharacterInfo(999, clientSeat).isRoleRevealed == Personal` ; le revealer HÔTE reste `False` pour l'observer hôte.
- **Stabilité:** empêche que la révélation de rôle due au chaînage n'atteigne pas le lanceur distant (la jambe chaining-list est couverte par ChainedByShadows_ChainsChosenTarget, la jambe reveal ne l'est pas).
- **Statut:** à implémenter

### 184 — Blessing : la jambe reveal-de-rôle-au-propriétaire atteint le propriétaire distant
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `BlessingDecision` émet `RevealInfo(TargetSlot, RoleRevealed, Personal, OwnerSlot, broadcast=true)` en sus du bless+heal — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:24`
- **Couche:** PlayMode-2NM
- **Given:** propriétaire = `ClientNm.LocalClientId` ; cible de rôle correspondant seat `999UL` ; revealer répliqué recâblé par NM + root lié hôte.
- **When:** on lance PBlessing sur la cible correspondante (pipeline complet serveur).
- **Then:** le revealer CLIENT observe `GetCharacterInfo(999, clientSeat).isRoleRevealed == Personal` ; le revealer HÔTE reste `False` pour l'observer hôte.
- **Stabilité:** empêche que la révélation du rôle béni n'atteigne pas le lanceur distant (Blessing_BlessesAndHealsMatchingTarget couvre bless+heal, pas le reveal de rôle au propriétaire).
- **Statut:** à implémenter

### 185 — Une double-transition d'état (0→1→2) arrive dans l'ordre sur le client, cycles de vie appariés
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `SwitchGameState` écrit `currentGameStateIndex.Value` + `DoStateMethodRpc(End/Start)` par transition — `Assets/Scripts/GameLogic/GameManager.cs:369-382`
- **Couche:** PlayMode-2NM
- **Given:** GMs re-seedés avec des `LoopProbeState` (nom court, `gameManager` câblé) des deux côtés (pattern GameLoopClientLifecycleTests) ; on enregistre la trace d'index cliente et l'ordre `onStateEndClient`/`onStateStartClient`.
- **When:** on appelle `HostGm.NextGameState(true)` DEUX fois d'affilée (0→1 puis 1→2).
- **Then:** `RemoteIndexTrace` se termine par `[..,1,2]` dans l'ordre ; `ClientGm.currentGameStateIndex.Value == 2` ; la séquence de cycle de vie cliente est `end,start,end,start` (jamais entrelacée ni inversée).
- **Stabilité:** empêche un réordonnancement/coalescence des transitions rapides côté client (état affiché ≠ état serveur) — GameLoopClientLifecycle ne couvre qu'UNE transition.
- **Statut:** à implémenter

### 186 — DoStateMethodRpc ciblé (single) n'atteint QUE le client visé
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `CallStateMethodRpc` `[Rpc(SendTo.SpecifiedInParams)]` via `characterManager.GetSafeRpcTarget(clientId)` pour `RpcTargetType.single` — `Assets/Scripts/GameLogic/GameManager.cs:497-505` + `:519-520`
- **Couche:** PlayMode-2NM
- **Given:** GMs re-seedés `LoopProbeState` des deux côtés ; un `characterManager` câblé sur `HostGm` ; on s'abonne à un événement observable de la state cliente (ex. `onStateStartClient`) sur la réplique cliente ET on vérifie l'absence d'appel côté hôte via un second listener.
- **When:** on appelle `HostGm.DoStateMethodRpc(stateFullName, "OnStartStateClient", new CustomRpcParams(single, clientId=ClientNm.LocalClientId))`.
- **Then:** l'événement fire sur la state CLIENTE ciblée et NE fire PAS sur une seconde identité (ni sur l'hôte) — la cible unique via GetSafeRpcTarget est respectée sur le fil.
- **Stabilité:** empêche qu'un RPC d'état « privé » (destiné à un seul joueur) soit diffusé à tous — invisible en StartHost (un seul destinataire possible).
- **Statut:** à implémenter

### 187 — ShutOffGameRpc (fin gracieuse) atteint le client et supprime la notif de perte-hôte
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `ShutOffGameRpc` `[Rpc(SendTo.Everyone)]` → `ShutOffGame` appelle `ClientDisconnectHandler.NotifyExpectedShutdown()` — `Assets/Scripts/GameLogic/GameManager.cs:574-585`
- **Couche:** PlayMode-2NM
- **Given:** host + client, `GameManager` répliqué des deux côtés ; on instrumente `ClientDisconnectHandler` (flag `expectedShutdown`) résolu côté client.
- **When:** on appelle `HostGm.ShutOffGameRpc()` (fan-out Everyone) puis on laisse un tick.
- **Then:** la réplique cliente exécute `ShutOffGame` → `expectedShutdown` passe true côté client AVANT le shutdown, de sorte qu'une perte de socket subséquente ne déclenche pas la notif « connexion à l'hôte perdue ».
- **Stabilité:** empêche qu'une fin de partie gracieuse soit prise pour un crash-hôte côté client (fausse alerte de déconnexion) — le drapeau ne peut se vérifier qu'avec un vrai client récepteur.
- **Statut:** à implémenter

### 188 — OnBlessed déclenche l'effet-carte owner-local seulement selon la faction du Character LOCAL
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `Character.OnBlessed` (réaction locale à `isBlessed.OnValueChanged`) n'ajoute l'effet carte que si `GetLocalCharacter().role.factionType == anomaly || roleID == Dryade` — `Assets/Scripts/Characters/Character.cs:77-98`
- **Couche:** PlayMode-2NM
- **Given:** un Character sujet possédé par `ClientNm.LocalClientId` ; côté client, on donne au Character LOCAL de la réplique cliente un `role.factionType = anomaly` ; on capture les appels `CardEffectManager.AddCardEffect` (singleton partagé, non discriminant en soi mais l'entrée d'appel l'est) via un sink/spy.
- **When:** le serveur pose `isBlessed.Value = true` sur le sujet (write NetworkVariable) → la réplique cliente reçoit `OnValueChanged`.
- **Then:** l'effet carte Blessing est ajouté côté client (faction anomaly) ; dans le miroir avec un local `chosen`, aucun effet n'est ajouté.
- **Stabilité:** empêche que le feedback carte « bénédiction » s'affiche pour la mauvaise faction locale (réaction locale gated par la faction du joueur, testable seulement avec un vrai client ayant SON propre Character local).
- **Statut:** à implémenter

### 189 — HealPlayerServerRpc : le second heal est un no-op observé sur la réplique cliente
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** garde d'idempotence `if (isHealed.Value) return;` dans `HealPlayerServerRpc` — `Assets/Scripts/Characters/Character.cs:187-196`
- **Couche:** PlayMode-2NM
- **Given:** un Character possédé par `ClientNm.LocalClientId`, corrompu (`CorruptPlayerServerRpc` sur hôte → attendre `isCorrupted.Value==true` sur la réplique) ; premier `HealPlayerServerRpc` sur hôte → attendre `isHealed.Value==true` et `isCorrupted.Value==false` sur la réplique.
- **When:** on re-corrompt via `CorruptPlayerServerRpc` (attendre `isCorrupted==true` réplique) puis on invoque `HealPlayerServerRpc` une SECONDE fois sur l'hôte.
- **Then:** la réplique cliente montre que la seconde heal N'a PAS ré-effacé la corruption (`isCorrupted.Value` reste true) — la garde `isHealed` bloque le second soin, et cet état passe le fil.
- **Stabilité:** empêche une régression de la garde « un seul soin » qui laisserait un joueur se re-soigner indéfiniment, désync visible seulement côté client.
- **Statut:** à implémenter

### 190 — LivenessService serveur : un vrai client silencieux est chaîné via PeerLost→HandlePlayerLeft (bout-en-bout)
- **Catégorie:** F — Réplication / RPC 2-NM
- **Mécanique:** `LivenessService.HandlePeerLost` (rôle serveur) invoque `_onServerPeerLost` = `GameManager.HandlePlayerLeft` — `Assets/Scripts/Network/Liveness/LivenessService.cs:218-224` ; branche mid-game chaîne le leaver — `Assets/Scripts/GameLogic/GameManager.cs:647,690,748`
- **Couche:** PlayMode-2NM
- **Given:** host + vrai client, un `LivenessNetworkBridge` répliqué (`SpawnBridge`), un `LivenessService.StartServer` avec un `ManualLivenessPump` injecté et un `GameManager` en état mid-game (index hors LobbyState) ; le vrai client possède un Character (`SpawnRealCharacterForClient`) mais NE bat PAS (aucun `SendClientHeartbeat`).
- **When:** on avance le pump serveur manuellement au-delà du `Threshold` de misses sans qu'aucun heartbeat client n'arrive (le tracker émet `PeerLost` pour le clientId réel).
- **Then:** `HandlePlayerLeft(clientId)` est déclenché exactement une fois → le Character du client est `isChained.Value == true` (branche mid-game), sans erreur serveur, et la réplique cliente (si encore présente) observe `isChained==true`.
- **Stabilité:** empêche que la détection de client à moitié-mort (socket vivante mais gelé) n'aboutisse pas au pipeline de départ — seul un vrai second NM qui cesse de battre exerce la source d'ignition liveness (le seam RPC brut est couvert, l'aboutissement bout-en-bout ne l'est pas). Note : le tracker/décision sont couverts en EditMode ; ici on teste l'aboutissement 2-NM via pump manuel (pas de timing réel).
- **Statut:** à implémenter

### 191 — AutoCorruption corrompt son propre owner au démarrage
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PAutoCorruption.OnGameStartedServer` — `Assets/Scripts/Characters/Powers/PAutoCorruption.cs:17` → `AutoCorruptionDecision` émet `CorruptPlayer(owner)` — `Assets/Scripts/Domain/Powers/Decisions/AutoCorruptionDecision.cs:12`
- **Couche:** PlayMode-StartHost
- **Given:** harness CorruptionTests câblé ; un owner `AddNewCharacter(LocalClientId)` non corrompu ; un `PAutoCorruption` spawné avec `ownerClientId.Value = LocalClientId`.
- **When:** appeler `power.OnGameStartedServer()`.
- **Then:** `owner.isCorrupted.Value == true` (était false avant).
- **Stabilité:** garantit que le pouvoir passif d'auto-corruption s'applique vraiment via le pipeline décision→executor au démarrage de partie.
- **Statut:** déjà couvert (CorruptionTests.PAutoCorruption_CorruptsOwnerAtStart)

### 192 — CorruptingMark corrompt la cible cliquée
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptingMark.OnCardClickedRpc` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:71` → `CorruptingMarkDecision` émet `CorruptPlayer(target)` — `Assets/Scripts/Domain/Powers/Decisions/CorruptingMarkDecision.cs:12`
- **Couche:** PlayMode-StartHost
- **Given:** owner `AddNewCharacter(LocalClientId)` ; target `AddNewCharacter(999)` non corrompue ; `PCorruptingMark` spawné, owner = LocalClientId.
- **When:** `InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value)`.
- **Then:** `target.isCorrupted.Value == true`.
- **Stabilité:** garde le chemin RPC serveur → mutation de corruption de la cible.
- **Statut:** déjà couvert (CorruptionTests.PCorruptingMark_CorruptsTarget)

### 193 — CorruptingMark stocke la dernière cible corrompue (état power-local)
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `StoreLastCorrupted` via `ILastCorruptedState.StoreLastCorrupted` → NV `lastCorruptedCharacterId` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:36` (décision `CorruptingMarkDecision.cs:12` émet `StoreLastCorrupted(target)`)
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(999)` ; `PCorruptingMark` spawné ; NV privée `lastCorruptedCharacterId` initialisée à 9999999.
- **When:** `InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value)`.
- **Then:** `GetPrivateField(power, "lastCorruptedCharacterId")` (NetworkVariable<ulong>).Value == `target.ownerClientId.Value` (n'est plus 9999999).
- **Stabilité:** verrouille que l'effet `StoreLastCorrupted` écrit bien le carrier NV power-local via `SelfState`, préalable à l'effet concentré.
- **Statut:** à implémenter

### 194 — CorruptingMark effet concentré révèle le rôle du dernier corrompu à l'owner
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptingMark.OnConcentratedEffectServer` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:124` révèle `isRoleRevealed` Personal du `lastCorruptedCharacterId` à l'owner
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(999)` avec `role = new Role{ roleName="Mark" }` ; `PCorruptingMark` spawné ; `OnCardClickedRpc(target)` déjà invoqué (last-corrupted == target).
- **When:** `InvokePrivateMethod(power, "OnConcentratedEffectServer")` puis `yield return null`.
- **Then:** `_revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value).isRoleRevealed == RevealLevel.Personal`.
- **Stabilité:** empêche la régression de l'effet concentré (révélation du rôle du dernier marqué) qui dépend du NV stocké.
- **Statut:** à implémenter

### 195 — CorruptingMark déclenche l'évènement de corruption réussie
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `CorruptionSucceeded` → `ICorruptionEvents.RaiseSucceeded` → `InvokeOnCharacterCorruptionSuccessfulRpc` (SendTo.Everyone) → `onCharacterCorruptionSuccessful` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:37,77`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(999)` ; `PCorruptingMark` spawné ; abonnement `power.onCharacterCorruptionSuccessful += c => { firedFor = c; }`.
- **When:** `InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value)` puis `yield return null`.
- **Then:** `firedFor` non null et `firedFor.ownerClientId.Value == target.ownerClientId.Value`.
- **Stabilité:** garde le fan-out d'évènement (feedback audio/visuel de réussite) branché sur l'effet `CorruptionSucceeded`.
- **Statut:** à implémenter

### 196 — CorruptingMark rejette une cible invalide (bénie) sans corrompre
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptingMark.OnCharacterPicked` garde `CheckIsTargetValid` → `InvokeOnCharacterCorruptionFailedRpc`, pas de corruption — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:56-63` ; règle validator `TargetUtils.IsTargetValid` — `PCorruptingMark.cs:43`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(999)` avec `target.isBlessed.Value = true` ; `PCorruptingMark` spawné avec `targetIncludeFlags = Anomaly|Chosen|Marginal` (exclut les bénis) ; abonnement `power.onCharacterCorruptionFailed += c => failedFor = c`.
- **When:** `InvokePrivateMethod(power, "OnCharacterPicked", target)` puis `yield return null`.
- **Then:** `target.isCorrupted.Value == false` ET `failedFor.ownerClientId.Value == target.ownerClientId.Value`.
- **Stabilité:** verrouille que le garde de validité côté picker bloque la corruption d'une cible intargetable et notifie l'échec.
- **Statut:** à implémenter

### 197 — CorruptingMark enregistre le ciblage dans RoleTargetSystem
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `NewTargeting(owner, target)` — `CorruptingMarkDecision.cs:14` → `RoleTargetSystem.NewTargeting(owner, target)` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:71` (dispatch)
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(999)` ; RoleTargetSystem câblé (SetUp) ; `PCorruptingMark` spawné.
- **When:** `InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value)` puis `yield return null`.
- **Then:** `RoleTargetSystem.instance` référence un ciblage owner→target (asserter via l'API publique de RTS lisant le dernier targeting de l'owner, ex `GetTarget`/liste de targetings).
- **Stabilité:** garde le lien décision→RTS (le ciblage sert aux résolutions de nuit et aux comptes de victoire).
- **Statut:** à implémenter

### 198 — CorruptionInsight révèle la corruption à l'owner au démarrage
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptionInsight.OnGameStartedServer` — `Assets/Scripts/Characters/Powers/PCorruptionInsight.cs:19` → `CorruptionInsightDecision` émet `RevealInfo(CorruptRevealed)` par slot — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:18`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(12345)` ; `PCorruptionInsight` spawné, owner = LocalClientId.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `_revealer.GetCharacterInfo(target, owner).isCorruptRevealed == RevealLevel.Personal` (était False).
- **Stabilité:** garde la révélation passive de corruption de tous les joueurs à l'owner.
- **Statut:** déjà couvert (VisionPowerTests.PCorruptionInsight_RevealsCorruptionAtStart)

### 199 — CorruptionInsight révèle la corruption de CHAQUE slot du roster
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** boucle `foreach slot in ctx.Roster.Slots` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionInsightDecision.cs:17-19` (roster live `Power.Roster` — `Assets/Scripts/Characters/Powers/Power.cs:211`)
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; DEUX cibles `AddNewCharacter(12345)` et `AddNewCharacter(23456)` ; `PCorruptionInsight` spawné.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `isCorruptRevealed == RevealLevel.Personal` pour LES DEUX cibles observées par l'owner (`GetCharacterInfo(t1,owner)` ET `GetCharacterInfo(t2,owner)`).
- **Stabilité:** empêche une régression où l'itération du roster ne révèlerait qu'un seul slot au lieu de tous.
- **Statut:** à implémenter

### 200 — CorruptionKnowledge révèle le flag forceCorruptOnRoleRevealed au démarrage
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptionKnowledge.OnGameStartedServer` — `Assets/Scripts/Characters/Powers/PCorruptionKnowledge.cs:47` → `RevealInfo(ForceCorruptOnRoleRevealed)` par slot — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:18`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(23456)` ; `PCorruptionKnowledge` spawné.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `_revealer.GetCharacterInfo(target, owner).forceCorruptOnRoleRevealed == RevealLevel.Personal` (était False).
- **Stabilité:** garde la révélation passive du flag « corrompt si rôle révélé » à l'owner.
- **Statut:** déjà couvert (VisionPowerTests.PCorruptionKnowledge_RevealsForceCorruptFlagAtStart)

### 201 — CorruptionKnowledge révèle le flag pour CHAQUE slot du roster
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** boucle `foreach slot in ctx.Roster.Slots` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionKnowledgeDecision.cs:17-19`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; DEUX cibles `AddNewCharacter(23456)` et `AddNewCharacter(34567)` ; `PCorruptionKnowledge` spawné.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `forceCorruptOnRoleRevealed == RevealLevel.Personal` pour les DEUX cibles vues par l'owner.
- **Stabilité:** empêche une itération de roster tronquée à un seul slot.
- **Statut:** à implémenter

### 202 — CorruptionParanoia révèle sa propre corruption à soi au démarrage
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCorruptionParanoia.OnGameStartedServer` — `Assets/Scripts/Characters/Powers/PCorruptionParanoia.cs:13` → `RevealInfo(owner, CorruptRevealed, Personal, owner)` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs:15`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; `PCorruptionParanoia` spawné, owner = LocalClientId.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `_revealer.GetCharacterInfo(owner).isCorruptRevealed == RevealLevel.Personal` (était False).
- **Stabilité:** garde la révélation à soi de sa propre corruption (paranoïa).
- **Statut:** déjà couvert (CorruptionTests.PCorruptionParanoia_RevealsOwnCorruptionAtStart)

### 203 — CorruptionParanoia ne révèle QUE l'owner, pas les autres
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** décision à unique `RevealInfo(ctx.OwnerSlot, …)` — `Assets/Scripts/Domain/Powers/Decisions/CorruptionParanoiaDecision.cs:15` (pas de boucle roster, contrairement à Insight)
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; un bystander `AddNewCharacter(55555)` ; `PCorruptionParanoia` spawné.
- **When:** `power.OnGameStartedServer()` puis `yield return null`.
- **Then:** `GetCharacterInfo(owner).isCorruptRevealed == Personal` ET `GetCharacterInfo(bystander, owner).isCorruptRevealed == RevealLevel.False`.
- **Stabilité:** empêche une régression qui transformerait Paranoia en révélation de masse (fuite d'info) au lieu de la seule auto-révélation.
- **Statut:** à implémenter

### 204 — EmbraceOfShadows (rôle correspondant) corrompt la cible et révèle son rôle à l'owner
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PEmbraceOfShadows.OnCharacterAndRolePicked` → `RunClientDecisionEffects` — `Assets/Scripts/Characters/Powers/PEmbraceOfShadows.cs:49,60` → branche match : `CorruptPlayer` + `RevealInfo(CorruptRevealed)` + `RevealInfo(RoleRevealed)` + `CorruptionSucceeded` — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:15-22`
- **Couche:** PlayMode-StartHost
- **Given:** host==owner (LocalClientId) ; target `AddNewCharacter(4242)` avec `role = new Role{ roleName="Emb" }` ; `PEmbraceOfShadows` spawné, `targetValidator` remplacé par un `Validator<(ulong,TargetUtils.TargetType)>()` vide (Evaluate vrai) ; `compareRole = new Role{ roleName="Emb", ownerClientId = target.ownerClientId.Value }` (slot secondaire == cible → SameRole true) ; abonnement `power.onPowerSuccessful += () => ok = true`.
- **When:** `InvokePrivateMethod(power, "OnCharacterAndRolePicked", target, compareRole)` puis `yield return null` (x2).
- **Then:** `target.isCorrupted.Value == true` ; `GetCharacterInfo(target, owner).isRoleRevealed == Personal` ; `ok == true`.
- **Stabilité:** garde le chemin StartHost complet d'Étreinte des ombres réussie (corruption + révélation + évènement), non couvert par la fixture 2-NM (qui teste la latéralité owner-local).
- **Statut:** à implémenter

### 205 — EmbraceOfShadows (rôle non correspondant) ne corrompt pas mais cible et déclenche l'échec
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** branche no-match : `NewTargeting` + `CorruptionFailed` uniquement — `Assets/Scripts/Domain/Powers/Decisions/EmbraceOfShadowsDecision.cs:24-26` ; `CorruptionFailed`→`onPowerFailed` — `PEmbraceOfShadows.cs:41,84`
- **Couche:** PlayMode-StartHost
- **Given:** host==owner ; target `AddNewCharacter(4242)` avec `role = new Role{ roleName="Emb" }` ; un tiers `AddNewCharacter(4343)` avec `role = new Role{ roleName="Autre" }` ; `PEmbraceOfShadows` spawné, `targetValidator` vide ; `compareRole = new Role{ roleName="Autre", ownerClientId = tiers.ownerClientId.Value }` (SameRole(target,tiers) false) ; abonnement `power.onPowerFailed += () => failed = true`.
- **When:** `InvokePrivateMethod(power, "OnCharacterAndRolePicked", target, compareRole)` puis `yield return null` (x2).
- **Then:** `target.isCorrupted.Value == false` ET `failed == true`.
- **Stabilité:** verrouille que la comparaison de rôles empêche la corruption sur mauvais rôle et signale l'échec (pas de corruption « gratuite »).
- **Statut:** à implémenter

### 206 — CursedVision corrompt la cible et lui révèle sa corruption à l'owner
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PCursedVision.OnCharacterPicked` → `RunClientDecisionEffects` — `Assets/Scripts/Characters/Powers/PCursedVision.cs:34,43` → `CorruptPlayer(target)` + `RevealInfo(target, CorruptRevealed, Personal, owner)` — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:27-29`
- **Couche:** PlayMode-StartHost
- **Given:** host==owner ; owner `AddNewCharacter(LocalClientId)` ; target `AddNewCharacter(4242)` avec `role = new Role{ factionType=FactionType.chosen, roleName="CV" }` ; `PCursedVision` spawné, `targetValidator` vide ; ChatManager présent (SetUp) ; PAS de CardEffectManager → `LogAssert.Expect(LogType.Error, new Regex("No card effect found for ID"))`.
- **When:** `InvokePrivateMethod(power, "OnCharacterPicked", target)` puis `yield return null` (x2).
- **Then:** `target.isCorrupted.Value == true` ET `GetCharacterInfo(target, owner).isCorruptRevealed == Personal`.
- **Stabilité:** garde le chemin StartHost de Vision maudite (corruption + révélation de la cible), la fixture 2-NM ne testant que la latéralité owner-local.
- **Statut:** à implémenter

### 207 — CursedVision corrompt aussi l'owner et lui révèle sa propre corruption
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** effets finaux `CorruptPlayer(owner)` + `RevealInfo(owner, CorruptRevealed, Personal, owner)` — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:32-33`
- **Couche:** PlayMode-StartHost
- **Given:** identique à 206 (owner + target chosen, validator vide, `LogAssert.Expect` "No card effect found for ID").
- **When:** `InvokePrivateMethod(power, "OnCharacterPicked", target)` puis `yield return null` (x2).
- **Then:** `owner.isCorrupted.Value == true` ET `GetCharacterInfo(owner).isCorruptRevealed == Personal`.
- **Stabilité:** empêche une régression où la contrepartie « l'owner se corrompt lui-même » (coût du pouvoir) serait perdue.
- **Statut:** à implémenter

### 208 — CursedVision corrompt la cible même si elle n'est PAS un élu (corruption faction-indépendante)
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `CorruptPlayer`/`RevealInfo` inconditionnels ; seuls `AddCardEffect.cardHidden` et le verdict `ChatLocal` dépendent de `FactionOf` — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:20-33`
- **Couche:** PlayMode-StartHost
- **Given:** host==owner ; owner `AddNewCharacter(LocalClientId)` ; target `AddNewCharacter(4242)` avec `role = new Role{ factionType=FactionType.anomaly, roleName="CV" }` (NON élu) ; `PCursedVision` spawné, validator vide ; `LogAssert.Expect(LogType.Error, new Regex("No card effect found for ID"))`.
- **When:** `InvokePrivateMethod(power, "OnCharacterPicked", target)` puis `yield return null` (x2).
- **Then:** `target.isCorrupted.Value == true` ET `GetCharacterInfo(target, owner).isCorruptRevealed == Personal` (la faction ne conditionne pas la corruption).
- **Stabilité:** verrouille que la corruption s'applique quelle que soit la faction (seuls la carte/le verdict divergent), évitant un couplage erroné faction→corruption.
- **Statut:** à implémenter

### 209 — Blessing (rôle correspondant) soigne, bénit et révèle le rôle à l'owner
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PBlessing.TryBlessCharacterServerRpc` — `Assets/Scripts/Characters/Powers/PBlessing.cs:42` → branche match : `HealPlayer` + `RevealInfo(RoleRevealed)` + `SetBlessed` + `ChatBroadcast` — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:18-28`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(1357)` avec `role = new Role{ roleName="Bless-Test" }` ; `PBlessing` spawné ; `compareRole = new Role{ roleName="Bless-Test", ownerClientId = target.ownerClientId.Value }`.
- **When:** `InvokePrivateMethod(power, "TryBlessCharacterServerRpc", target.ownerClientId.Value, compareRole)` puis `yield return null`.
- **Then:** `target.isBlessed.Value == true` ; `target.isHealed.Value == true` ; `GetCharacterInfo(target, owner).isRoleRevealed == Personal`.
- **Stabilité:** garde le chemin RPC serveur complet de bénédiction (heal + reveal + bless).
- **Statut:** déjà couvert (CorruptionTests.PBlessing_HealsBlessesAndRevealsMatchingTarget)

### 210 — Blessing (rôle non correspondant) ne bénit/soigne/révèle rien, cible seulement
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** hors branche match, seul `NewTargeting` est émis — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:17-18` (garde `SameRole` faux)
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(1357)` avec `role = new Role{ roleName="Vrai" }` ; un tiers `AddNewCharacter(1358)` avec `role = new Role{ roleName="Faux" }` ; `PBlessing` spawné ; `compareRole = new Role{ roleName="Faux", ownerClientId = tiers.ownerClientId.Value }` (SameRole(target,tiers) faux).
- **When:** `InvokePrivateMethod(power, "TryBlessCharacterServerRpc", target.ownerClientId.Value, compareRole)` puis `yield return null`.
- **Then:** `target.isBlessed.Value == false` ; `target.isHealed.Value == false` ; `GetCharacterInfo(target, owner).isRoleRevealed == RevealLevel.False`.
- **Stabilité:** empêche une bénédiction/soin « offert » sur mauvaise devinette de rôle (le rôle deviné doit correspondre).
- **Statut:** à implémenter

### 211 — Blessing sur cible déjà soignée : bénit et révèle sans re-soigner
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `HealPlayer` seulement si `!IsHealed(target)` ; `SetBlessed`+`RevealInfo` inconditionnels dans la branche match — `Assets/Scripts/Domain/Powers/Decisions/BlessingDecision.cs:20-25`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; target `AddNewCharacter(1357)` avec `role = new Role{ roleName="Bless-Test" }` ET `target.isHealed.Value = true` (déjà soigné) ; `PBlessing` spawné ; `compareRole = new Role{ roleName="Bless-Test", ownerClientId = target.ownerClientId.Value }`.
- **When:** `InvokePrivateMethod(power, "TryBlessCharacterServerRpc", target.ownerClientId.Value, compareRole)` puis `yield return null`.
- **Then:** `target.isBlessed.Value == true` ET `target.isHealed.Value == true` (reste soigné, pas d'erreur) ET `GetCharacterInfo(target, owner).isRoleRevealed == Personal`.
- **Stabilité:** verrouille le garde `!IsHealed` (pas de double heal) tout en préservant bénédiction + révélation.
- **Statut:** à implémenter

### 212 — Blessing rend la cible intargetable pour la corruption
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** flag `isBlessed` exclu par `TargetUtils.IsTargetValid` via `targetIncludeFlags` — `Assets/Scripts/Characters/Powers/PCorruptingMark.cs:43` / `Assets/Scripts/Characters/Powers/Target/TargetUtils.cs:21`
- **Couche:** PlayMode-StartHost
- **Given:** target `AddNewCharacter(888)` ; `PCorruptingMark` spawné avec `targetIncludeFlags = Anomaly|Chosen|Marginal`.
- **When:** poser `target.isBlessed.Value = true` puis appeler `corruptPower.CheckIsTargetValid(target.ownerClientId.Value, TargetType.Character)`.
- **Then:** retourne `false` (valide avant bénédiction, invalide après).
- **Stabilité:** garde l'immunité de ciblage des personnages bénis.
- **Statut:** déjà couvert (CorruptionTests.PBlessing_MakesTargetUntargetableForCorruption)

### 213 — DroolyHealing (rôle correspondant, cible corrompue) soigne, enregistre et révèle
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PDroolyHealing.TryHealServerRpc` — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:48` : match rôle + (corrompu ou soigné) → `HealPlayerServerRpc` + `healedCharactersThisNight.Add` + `OnHealSuccessfulRpc` (reveal rôle owner) — `PDroolyHealing.cs:55-64,70-75`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId avec `owner.role = new Role{ roleName="Drooly", powers = new List<Power>{ power } }` (le RPC lit `owner.role.powers.First(...)` — `PDroolyHealing.cs:52`) ; target `AddNewCharacter(1357)` avec `role = new Role{ roleName="Cible" }` ET `target.isCorrupted.Value = true` ; `PDroolyHealing` spawné, owner = LocalClientId ; RTS + ChatManager câblés (SetUp) ; `compareRole = new Role{ roleName="Cible", ownerClientId = target.ownerClientId.Value }`.
- **When:** `InvokePrivateMethod(power, "TryHealServerRpc", target.ownerClientId.Value, compareRole)` puis `yield return null`.
- **Then:** `target.isHealed.Value == true` ; `power.healedCharactersThisNight.Contains(target.ownerClientId.Value)` ; `GetCharacterInfo(target, owner).isRoleRevealed == Personal`.
- **Stabilité:** garde le chemin nominal du soin baveux (heal effectif + enregistrement nuit + révélation au soigneur).
- **Statut:** à implémenter

### 214 — DroolyHealing (rôle correspondant, cible NON corrompue) enregistre et révèle mais ne soigne pas
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `HealPlayerServerRpc` gardé par `isCorrupted || isHealed` ; mais `healedCharactersThisNight.Add` + `OnHealSuccessfulRpc` s'exécutent pour TOUT rôle correspondant — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:57-64`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId avec `owner.role = new Role{ roleName="Drooly", powers = new List<Power>{ power } }` ; target `AddNewCharacter(1357)` avec `role = new Role{ roleName="Cible" }`, `isCorrupted == false` et `isHealed == false` ; `PDroolyHealing` spawné ; `compareRole = new Role{ roleName="Cible", ownerClientId = target.ownerClientId.Value }`.
- **When:** `InvokePrivateMethod(power, "TryHealServerRpc", target.ownerClientId.Value, compareRole)` puis `yield return null`.
- **Then:** `target.isHealed.Value == false` (pas de soin réel) MAIS `power.healedCharactersThisNight.Contains(target)` ET `GetCharacterInfo(target, owner).isRoleRevealed == Personal`.
- **Stabilité:** verrouille la branche subtile où enregistrement + révélation se produisent même sans soin effectif — une régression facile à casser en fusionnant les deux gardes.
- **Statut:** à implémenter

### 215 — DroolyHealing purge la liste des soignés en fin de nuit
- **Catégorie:** G1 — CHAQUE POUVOIR en PlayMode StartHost
- **Mécanique:** `PDroolyHealing.OnNightEndedServer` — `Assets/Scripts/Characters/Powers/PDroolyHealing.cs:87-106` envoie un message serveur par soigné puis `healedCharactersThisNight.Clear()`
- **Couche:** PlayMode-StartHost
- **Given:** owner = LocalClientId ; un soigné `AddNewCharacter(1357)` existant ; `PDroolyHealing` spawné avec `power.healedCharactersThisNight` pré-rempli de `1357` ; ChatManager câblé (SetUp).
- **When:** `InvokePrivateMethod(power, "OnNightEndedServer")` puis `yield return null`.
- **Then:** `power.healedCharactersThisNight.Count == 0` (liste purgée après le récap).
- **Stabilité:** empêche une fuite de la liste de soin entre nuits (double annonce / soins fantômes au tour suivant).
- **Statut:** à implémenter

### 216 — PersonalBeacons révèle forceCorrupt de chaque robot au start
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PPersonalBeacons.OnGameStartedServer` → `PersonalBeaconsDecision` révèle `forceCorruptOnRoleRevealed` de tout slot Robot — `Assets/Scripts/Characters/Powers/PPersonalBeacons.cs:48`, `Assets/Scripts/Domain/Powers/Decisions/PersonalBeaconsDecision.cs:20`
- **Couche:** PlayMode-StartHost
- **Given:** host = owner (localClientId) ; un `Character` cible id 700 avec `role = new Role { roleID = RoleID.Robot }` ; power `PPersonalBeacons` spawné, `ownerClientId = localClientId`
- **When:** appeler `power.OnGameStartedServer()`
- **Then:** `_revealer.GetCharacterInfo(700, owner).forceCorruptOnRoleRevealed == RevealLevel.Personal`
- **Stabilité:** empêche la régression (NRE latent du vieux chemin Awake noté en commentaire l.24-27) où le beacon robot n'expose plus la corruption forcée au porteur.
- **Statut:** à implémenter

### 217 — PersonalBeacons ne révèle rien sans robot dans le roster
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PersonalBeaconsDecision` retourne `AcceptEmpty` quand aucun slot n'est robot — `Assets/Scripts/Domain/Powers/Decisions/PersonalBeaconsDecision.cs:25`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) + une cible id 700 `role = new Role { roleID = RoleID.Omniscient }` (non-robot) ; power `PPersonalBeacons` spawné, `ownerClientId = localClientId`
- **When:** appeler `power.OnGameStartedServer()`
- **Then:** `_revealer.GetCharacterInfo(700, owner).forceCorruptOnRoleRevealed == RevealLevel.False` (aucune révélation émise)
- **Stabilité:** garde la branche vide — évite qu'un futur refactor révèle par erreur des non-robots au porteur du phare.
- **Statut:** à implémenter

### 218 — PersonalBeacons enregistre le ciblage owner→cible au clic
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PPersonalBeacons.OnCharacterClickedRpc` → `roleTargetSystem.NewTargeting(owner, cible)` + `CreateBeaconRpc` — `Assets/Scripts/Characters/Powers/PPersonalBeacons.cs:82`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) + cible id 700 `role = new Role { factionType = FactionType.chosen }` ; power `PPersonalBeacons` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnCharacterClickedRpc", 700UL)`
- **Then:** `RoleTargetSystem.instance.GetAllTargetingDataForTarget(700).Count > 0` (le beacon a bien posé un ciblage owner→cible)
- **Stabilité:** empêche que le clic de pose de phare cesse silencieusement d'enregistrer le ciblage (donnée lue par les autres pouvoirs de déduction).
- **Statut:** à implémenter

### 219 — LackOfAffection révèle le rôle du contacteur à une cible élue (bot simulé)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PLackOfAffection.OnPlayerContactedRpc` → `LackOfAffectionDecision` révèle le rôle du sender à la cible si elle est `chosen` — `Assets/Scripts/Characters/Powers/PLackOfAffection.cs:54`, `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:19`
- **Couche:** PlayMode-StartHost
- **Given:** sender = host (id 0) `role = new Role { roleName = "Orpheline" }` ; cible = bot SIMULÉ id 150 (>=100, donc `IsLocalOrSimulated` vrai, host intercepte) `role = new Role { factionType = FactionType.chosen }` ; power `PLackOfAffection` spawné, `ownerClientId = 0`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnPlayerContactedRpc", 150UL, 0UL, default(RpcParams))`
- **Then:** `_revealer.GetCharacterInfo(0, 150).isRoleRevealed == RevealLevel.Personal` (le rôle du sender révélé à l'observateur = cible 150)
- **Stabilité:** verrouille le sens de la révélation (rôle du CONTACTEUR exposé à la cible élue) côté host-intercept ; complète le pendant 2-NM `OwnerLocalEffectBoundaryTests`.
- **Statut:** à implémenter

### 220 — LackOfAffection ne révèle rien à une cible non-élue
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `LackOfAffectionDecision` — pas de reveal si `FactionOf(target) != chosen` — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:19`
- **Couche:** PlayMode-StartHost
- **Given:** sender = host (id 0) ; cible = bot simulé id 150 `role = new Role { factionType = FactionType.anomaly }` ; power `PLackOfAffection` spawné, `ownerClientId = 0`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnPlayerContactedRpc", 150UL, 0UL, default(RpcParams))`
- **Then:** `_revealer.GetCharacterInfo(0, 150).isRoleRevealed == RevealLevel.False`
- **Stabilité:** garde la condition élu-seul — une anomaly contactée ne doit jamais apprendre le rôle de l'Orpheline.
- **Statut:** à implémenter

### 221 — LackOfAffection poste la ligne locale pour la cible vraie-locale
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `LackOfAffectionDecision` émet `ChatLocal` quand `IsTrueLocalTarget` — `Assets/Scripts/Domain/Powers/Decisions/LackOfAffectionDecision.cs:23`, `PLackOfAffection.cs:59`
- **Couche:** PlayMode-StartHost
- **Given:** sender = bot simulé id 150 `role = new Role { roleName = "Orpheline" }` ; cible = host (id 0) `role = new Role { factionType = FactionType.chosen }` (donc `GetLocalClientId()==0==target` → true-local) ; power `PLackOfAffection` spawné, `ownerClientId = 150`
- **When:** s'abonner à `ChatManager.instance.onChatMessageReceived` puis `InvokePrivateMethod(power, "OnPlayerContactedRpc", 0UL, 150UL, default(RpcParams))`
- **Then:** un message local est posté (branche `IsTrueLocalTarget`), `senderClientId == ChatManager.SERVER_CLIENT_ID`
- **Stabilité:** empêche la perte de l'indice « quelqu'un est venu vous voir » chez le joueur réellement contacté (distinct de la révélation de rôle).
- **Statut:** à implémenter

### 222 — MarqueHurluberluges vole 3 pouvoirs actifs élus au start
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PMarqueHurluberluges.OnGameStartedServer` → `IStolenPowerGrant.GrantStolen` → `StolenPowerSelector.SelectStealable` + `GivePowerToCharacter(ConfigureAsOneShotStolenCopy)` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:45`, `:63`
- **Couche:** PlayMode-StartHost
- **Given:** owner Ugues = host (id 0) `role = new Role { factionType = FactionType.marginal }` sans pouvoir actif ; un `Character` élu id 800 `role = new Role { factionType = FactionType.chosen }` portant 3 `Power` spawnés distincts, actifs (`isPassive=false`), non-copiés ; power `PMarqueHurluberluges` spawné, `ownerClientId = 0`
- **When:** `power.OnGameStartedServer()` puis `WaitUntil(() => owner.role.powers.Count >= 3 || timeout)`
- **Then:** `owner.role.powers.Count == 3` (3 copies volées reçues via le spawn/reparent réel)
- **Stabilité:** cœur du rôle Ugues — empêche que l'attribution des vols cesse de peupler la barre de pouvoirs au démarrage.
- **Statut:** à implémenter

### 223 — MarqueHurluberluges ne vole rien sans pouvoir actif élu éligible
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `GrantStolen` — `_picks.Count == 0` → aucun `GivePowerToCharacter` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:102`
- **Couche:** PlayMode-StartHost
- **Given:** owner Ugues host id 0 ; un `Character` id 800 `role = new Role { factionType = FactionType.anomaly }` portant 2 `Power` actifs (faction non-chosen → inéligible) ; power spawné, `ownerClientId = 0`
- **When:** `power.OnGameStartedServer()` ; `yield return null` ×2
- **Then:** `owner.role.powers.Count == 0` (aucun vol)
- **Stabilité:** garde la règle d'éligibilité faction — Ugues ne vole que dans le camp élu, jamais chez les anomalies.
- **Statut:** à implémenter

### 224 — MarqueHurluberluges exclut ses propres pouvoirs (jamais auto-vol)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PowerCandidate` marque `_ownerIsUgues` par comparaison `ownerClientId == _ownerSlot` → filtré par `StolenPowerSelector` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:87`, `:95`
- **Couche:** PlayMode-StartHost
- **Given:** owner Ugues host id 0 `role = new Role { factionType = FactionType.chosen }` portant 2 `Power` actifs spawnés (Ugues lui-même en faction élue) ; aucun AUTRE personnage élu à pouvoir actif ; power spawné, `ownerClientId = 0`
- **When:** `power.OnGameStartedServer()` ; `yield return null` ×2
- **Then:** `owner.role.powers.Count == 2` (inchangé : les 2 pouvoirs d'Ugues, aucune copie ajoutée)
- **Stabilité:** empêche l'auto-vol (Ugues se copierait ses propres pouvoirs), bug de duplication silencieux.
- **Statut:** à implémenter

### 225 — MarqueHurluberluges ne vole qu'une fois (idempotence _hasStolen)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** garde `_hasStolen` — `OnGameStartedServer` peut être atteint 2× (OnPowerSpawned + OnGameStarted) — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:43`, `:52`
- **Couche:** PlayMode-StartHost
- **Given:** owner Ugues host id 0 ; un élu id 800 chosen portant 3 `Power` actifs spawnés ; power spawné, `ownerClientId = 0`
- **When:** `power.OnGameStartedServer()` ; `WaitUntil(count>=3)` ; puis `power.OnGameStartedServer()` une 2e fois ; `yield return null` ×2
- **Then:** `owner.role.powers.Count == 3` (pas 6 — le 2e appel est un no-op)
- **Stabilité:** empêche le double-vol quand la voie spawn tardif rappelle OnGameStartedServer.
- **Statut:** à implémenter

### 226 — MarqueHurluberluges ignore les pouvoirs passifs élus
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PowerCandidate(_p.BaseIsPassive)` → filtré (seuls les actifs volables) — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:95`
- **Couche:** PlayMode-StartHost
- **Given:** owner Ugues host id 0 ; un élu id 800 chosen portant 1 `Power` actif + 1 `Power` passif (`isPassive=true`) spawnés ; power spawné, `ownerClientId = 0`
- **When:** `power.OnGameStartedServer()` ; `WaitUntil(count>=1)` ; `yield return null` ×2
- **Then:** `owner.role.powers.Count == 1` (seul l'actif volé, le passif ignoré)
- **Stabilité:** garde la règle « pouvoirs actifs uniquement » — un passif volé serait inutilisable et polluerait la barre.
- **Statut:** à implémenter

### 227 — HighPriorityBounty non-robot enchaîne le CHASSEUR, pas la cible
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `HighPriorityBountyDecision` branche non-robot → `AddToChain(OwnerSlot)` + avertissement privé, cible non éliminée — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:27`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { roleName = "Hunter" }` ; cible id 8888 `role = new Role { roleID = RoleID.Omniscient }` (PAS Robot) ; power `PHighPriorityBounty` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", 8888UL)`
- **Then:** `target.isEliminated.Value == false` ET `ChainingManager.instance.chainingPlayers.Contains(owner.ownerClientId.Value)` (c'est l'owner qui est enchaîné)
- **Stabilité:** verrouille la sanction du mauvais pari (le chasseur se punit lui-même) ; sans ça une mauvaise cible n'aurait aucune conséquence.
- **Statut:** à implémenter

### 228 — ChainedByShadows rôle non-concordant : ni reveal ni chaîne
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `ChainedByShadowsDecision` — pas de reveal/chain si `!SameRole(target, secondary)` — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:20`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; cible id 4242 `role = new Role { factionType = FactionType.chosen, roleName = "A" }` ; `compareRole = new Role { roleName = "B", ownerClientId = 4242 }` (nom différent → `SameRole` faux) ; power `PChainedByTheShadows` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "TryCorruptCharacterServerRpc", 4242UL, compareRole)`
- **Then:** `ChainingManager.instance.chainingPlayers.Contains(4242) == false` ET `_revealer.GetCharacterInfo(4242, owner).isRoleRevealed == RevealLevel.False`
- **Stabilité:** garde la garde d'égalité de rôle — un mauvais pari de rôle ne doit ni révéler ni enchaîner (miroir négatif du golden `CorruptionTests`).
- **Statut:** à implémenter

### 229 — ChainedByShadows concordant mais non-élu : reveal seul, pas de chaîne
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `ChainedByShadowsDecision` — reveal si `SameRole`, mais `AddToChain` uniquement si `FactionOf(target)==chosen` — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:23`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; cible id 4242 `role = new Role { factionType = FactionType.anomaly, roleName = "A" }` ; `compareRole = new Role { roleName = "A", ownerClientId = 4242 }` (SameRole vrai) ; power spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "TryCorruptCharacterServerRpc", 4242UL, compareRole)`
- **Then:** `_revealer.GetCharacterInfo(4242, owner).isRoleRevealed == RevealLevel.Personal` ET `ChainingManager.instance.chainingPlayers.Contains(4242) == false`
- **Stabilité:** verrouille que seuls les élus sont enchaînés — une anomaly au rôle deviné est révélée mais échappe à la chaîne.
- **Statut:** à implémenter

### 230 — CardsShuffling devinette incorrecte : rien découvert, pas de reveal
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `CardsShufflingDecision` — branche `!IsCorrect` : pas de `DiscoveredAdd`/`RevealInfo`, seul le chat — `Assets/Scripts/Domain/Powers/Decisions/CardsShufflingDecision.cs:29`, `PCardsShuffling.cs:107`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; `clicked` id 1122 `role = new Role { roleID = RoleID.Omniscient }` ; `guess` id 3344 `role = new Role { roleID = RoleID.Dryade }` (rôle DIFFÉRENT → devinette fausse) ; power `PCardsShuffling` spawné, `ownerClientId = localClientId` ; `SetPrivateField(power, "currentRoleGuessClientId", 3344UL)`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "GuessRoleRpc", 1122UL)`
- **Then:** `power.discoveredClientIds.Contains(1122) == false` ET `_revealer.GetCharacterInfo(1122, owner).isRoleRevealed == RevealLevel.False`
- **Stabilité:** miroir négatif du golden correct — une mauvaise devinette ne doit rien révéler ni marquer découvert.
- **Statut:** à implémenter

### 231 — CardsShuffling fausse carte élue : copie one-shot + carte découverte
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PCardsShuffling.OnCharacterBarObjectClickedRpc` branche `_character.isFake` → `GrantCopyFromFakeRole` + `discoveredClientIds.Add` — `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:87`, `:146`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; `clicked` id 1122 avec `isFake == true`, `role = new Role { factionType = FactionType.chosen }` portant 1 `Power` actif spawné ; power `PCardsShuffling` spawné, `ownerClientId = localClientId` ; `LobbyPlayerInfoHolder` câblé (SetUp CorruptionTests)
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnCharacterBarObjectClickedRpc", 1122UL)` ; `WaitUntil(owner.role.powers.Count>=1)`
- **Then:** `power.discoveredClientIds.Contains(1122)` ET `owner.role.powers.Count == 1` (copie temporaire reçue)
- **Stabilité:** couvre le nouveau design 2026-07-15 (rôle élu absent → copie one-shot) ; zone fragile (`isFake`, `GivePowerToCharacter` async, `StolenPowerSelector` aléatoire).
- **Statut:** à implémenter

### 232 — VisionOfTheImpossible aucun match : ligne « non trouvé » + tous ciblés
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `VisionOfTheImpossibleDecision` — aucun `Matches` → message « Aucun personnage n'a été trouvé » + un `NewTargeting` par guess — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:30`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; `c1` id 881 `role = new Role { roleID = RoleID.Dryade }`, `c2` id 882 `role = new Role { roleID = RoleID.Marginal }` ; guessedRoles = `[ new Role { roleID = RoleID.Omniscient } ]` (aucun ne le porte) ; power `PVisionOfTheImpossible` spawné, `ownerClientId = localClientId`
- **When:** s'abonner à `onChatMessageReceived` ; `InvokePrivateMethod(power, "OnVisionGuessServerRpc", new[]{881UL,882UL}, guessedRoles)`
- **Then:** `body` contient « Aucun personnage n'a été trouvé » ET `RoleTargetSystem.instance.GetAllTargetingDataForTarget(881).Count > 0` ET `...(882).Count > 0` (les deux ciblés)
- **Stabilité:** miroir négatif du golden « found » — un échec de vision cible quand même chaque candidat et annonce l'échec.
- **Statut:** à implémenter

### 233 — VisionOfTheImpossible s'arrête au PREMIER match
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `VisionOfTheImpossibleDecision` — `break` au premier `Matches` : le suivant n'est pas ciblé — `Assets/Scripts/Domain/Powers/Decisions/VisionOfTheImpossibleDecision.cs:26`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; `first` id 881 `role = new Role { roleID = RoleID.Omniscient }` (match), `second` id 882 `role = new Role { roleID = RoleID.Omniscient }` (match aussi) ; guessedRoles = `[ RoleID.Omniscient ]` ; ordre des ids `[881,882]` ; power spawné, `ownerClientId = localClientId`
- **When:** `InvokePrivateMethod(power, "OnVisionGuessServerRpc", new[]{881UL,882UL}, guessedRoles)`
- **Then:** `RoleTargetSystem.instance.GetAllTargetingDataForTarget(881).Count > 0` ET `...(882).Count == 0` (le 2e n'est jamais atteint après le break)
- **Stabilité:** verrouille l'arrêt court — la vision ne révèle/cible pas au-delà de la première concordance (limite de portée du pouvoir).
- **Statut:** à implémenter

### 234 — ClandestineObservation branche count>0 (un rôle présent ciblé)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `ClandestineObservationDecision` branche `HasCharacters` → annonce « ...: {count} » sans point — `Assets/Scripts/Domain/Powers/Decisions/ClandestineObservationDecision.cs:18`, `PClandestineObservation.cs`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { roleID = RoleID.Dryade }` ; un `Character` id 900 portant `role = new Role { roleID = RoleID.Omniscient }` ; un ciblage existant vers 900 posé via `RoleTargetSystem.instance.NewTargeting(...)` ; power `PClandestineObservation` avec `targetRoleID = RoleID.Omniscient`, `isPassive=false`, `hasToBeAwakened=false`, `powerUseLeft.Value=1`, spawné, `ownerClientId = localClientId`
- **When:** s'abonner à `onChatMessageReceived` ; `power.DeclareAllTargetFocusServer()`
- **Then:** `received == true`, `sender == ChatManager.SERVER_CLIENT_ID`, `body` contient le nom du rôle observé et un compte >= 1 (pas la ligne « : 0. »)
- **Stabilité:** couvre la branche non-vide (jamais testée — seule la branche 0 l'est) ; garde le comptage réel des cibles d'un rôle présent.
- **Statut:** à implémenter

### 235 — EyeOfTheVoid owner non-anomaly ne découvre PAS le chat anomaly
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `EyeOfTheVoidDecision` ne découvre le chat que pour les slots anomaly — `Assets/Scripts/Characters/Powers/PEyeOfTheVoid.cs:16`, `Assets/Scripts/Domain/Powers/Decisions/EyeOfTheVoidDecision.cs`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { factionType = FactionType.chosen }` (PAS anomaly) ; power `PEyeOfTheVoid` spawné, `ownerClientId = localClientId` ; `ChatManager` câblé
- **When:** `power.OnGameStartedServer()`
- **Then:** `ChatManager.instance.discoveredChatIds.Contains((int)ChatWindowIDs.AnomalyOnly) == false`
- **Stabilité:** miroir négatif du golden anomaly — un élu ne doit jamais gagner l'accès au chat privé des anomalies.
- **Statut:** à implémenter

### 236 — BoundByInk attribue et découvre le chat d'encre au start
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PBoundByInk.OnGameStartedServer` → `AttributeBoundByInkChat` → `powerChatId` posé + `DiscoverChatRpc(owner)` — `Assets/Scripts/Characters/Powers/PBoundByInk.cs:91`, `:110`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; power `PBoundByInk` spawné, `ownerClientId = localClientId` ; `ChatManager`/`GameManager` (avec un `AwakeningState` dans `gameStates`) câblés
- **When:** `power.OnGameStartedServer()`
- **Then:** `(int)ReflectionHelper.GetPrivateField(power, "powerChatId") via NetworkVariable != -1` ET `ChatManager.instance.discoveredChatIds.Contains(cet id)`
- **Stabilité:** garde l'attribution du canal d'encre au porteur (sans lui, la cible liée ne partage aucun chat) ; zone fragile : `usedBoundByInkIds` est STATIC → l'id grandit entre tests, ne pas asserter la valeur littérale `515100`.
- **Statut:** à implémenter

### 237 — Legacy n'hérite pas tant que la cible n'est pas enchaînée
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PLegacy.OnCharacterChainChanged` n'agit que sur `_newValue==true` ; `ILegacyGrant.GrantLegacy` seulement alors — `Assets/Scripts/Characters/Powers/PLegacy.cs:24`, `:40`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { roleID = RoleID.Dryade }` ; cible id 555 `role = new Role { roleID = RoleID.Omniscient }`, `isChained.Value == false` ; `legacyPower` (Power spawné, powerName "Inherited"), `power.roleForLegacy = RoleID.Omniscient`, `power.legacyPower = legacyPower` ; power spawné, `ownerClientId = localClientId`
- **When:** `power.OnGameStartedServer()` (s'abonne) ; `yield return null` ×2 SANS jamais mettre `target.isChained.Value = true`
- **Then:** `power.isLegacyInherited == false` ET `owner.role.powers.Any(p => p.powerName=="Inherited") == false`
- **Stabilité:** miroir négatif du golden — l'héritage ne se déclenche jamais sans enchaînement effectif de la cible.
- **Statut:** à implémenter

### 238 — Legacy ne s'abonne pas à une cible au rôle non concordant
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PLegacy.OnGameStartedServer` ne filtre que `role.roleID == roleForLegacy` — une cible d'autre rôle n'est jamais surveillée — `Assets/Scripts/Characters/Powers/PLegacy.cs:27`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { roleID = RoleID.Dryade }` ; cible id 555 `role = new Role { roleID = RoleID.Marginal }` (≠ `roleForLegacy`) ; `power.roleForLegacy = RoleID.Omniscient`, `legacyPower` spawné ; power spawné, `ownerClientId = localClientId`
- **When:** `power.OnGameStartedServer()` puis `target.isChained.Value = true` ; `yield return null` ×2
- **Then:** `power.isLegacyInherited == false` (aucun abonnement → l'enchaînement d'un rôle non ciblé n'hérite rien)
- **Stabilité:** verrouille le filtre de rôle d'héritage — seul l'enchaînement du rôle désigné doit déclencher le legs.
- **Statut:** à implémenter

### 239 — InfiniteMessage laisse messageLeft des AUTRES inchangé
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `InfiniteMessageDecision` écrit `int.MaxValue` uniquement pour `ownerSlot` — `Assets/Scripts/Characters/Powers/PInfiniteMessage.cs:19`, `InfiniteMessageDecision.cs`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) + un `Character` autre id 600 avec `messageLeft.Value = 3` ; power `PInfiniteMessage` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnPowerReparented")`
- **Then:** `owner.messageLeft.Value == int.MaxValue` ET `otherCharacter(600).messageLeft.Value == 3` (inchangé)
- **Stabilité:** garde le ciblage owner-seul de la messagerie infinie — le buff ne doit pas fuiter vers les autres joueurs.
- **Statut:** à implémenter

### 240 — EyeOfTheVoid owner anomaly découvre le chat anomaly (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PEyeOfTheVoid.OnGameStartedServer` → `EyeOfTheVoidDecision` découvre `AnomalyOnly` pour un porteur anomaly — `Assets/Scripts/Characters/Powers/PEyeOfTheVoid.cs:16`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `role = new Role { factionType = FactionType.anomaly }` ; power `PEyeOfTheVoid` spawné, `ownerClientId = localClientId`
- **When:** `power.OnGameStartedServer()`
- **Then:** `ChatManager.instance.discoveredChatIds.Contains((int)ChatWindowIDs.AnomalyOnly)`
- **Stabilité:** cas nominal du chat anomaly (déjà verrouillé) — conservé pour la traçabilité de la catégorie.
- **Statut:** déjà couvert (EntrapmentPowerTests.PEyeOfTheVoid_DiscoversAnomalyChatForAnomalies)

### 241 — Omniscience cible élue révèle rôle + pose le hack (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `POmniscience.OnCardClickedRpc` → `OmniscienceDecision` reveal + `IHackTargetState.StoreHackTarget` — `Assets/Scripts/Characters/Powers/POmniscience.cs:108`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; cible id 54321 `role = new Role { factionType = FactionType.chosen }` ; power `POmniscience` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", 54321UL)`
- **Then:** `info.isRoleRevealed == RevealLevel.Personal`, `info.isHacked == RevealLevel.Personal`, `power.hackedCharacterClientId == 54321`
- **Stabilité:** cas nominal du hack élu (déjà verrouillé + branches d'expiration) — conservé pour traçabilité.
- **Statut:** déjà couvert (VisionPowerTests.POmniscience_ChosenTarget_RevealsRoleAndStoresHack)

### 242 — CardsShuffling devinette correcte révèle + enregistre (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PCardsShuffling.GuessRoleRpc` → `CardsShufflingDecision` `IsCorrect` → `DiscoveredAdd` + reveal — `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:107`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; `clicked` 1122 et `guess` 3344 même `roleID` ; `currentRoleGuessClientId = 3344`
- **When:** `InvokePrivateMethod(power, "GuessRoleRpc", 1122UL)`
- **Then:** `power.discoveredClientIds.Contains(1122)` ET `info.isRoleRevealed == RevealLevel.Personal`
- **Stabilité:** cas nominal (verrouillé) ; miroir positif du 230, conservé pour traçabilité.
- **Statut:** déjà couvert (CorruptionTests.PCardsShuffling_CorrectGuessRevealsAndRecordsTarget)

### 243 — ChainedByShadows concordant élu révèle + enchaîne (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PChainedByTheShadows.TryCorruptCharacterServerRpc` → `ChainedByShadowsDecision` reveal + `AddToChain` — `Assets/Scripts/Characters/Powers/PChainedByTheShadows.cs:42`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) ; cible 4242 chosen roleName "Chosen-Test" ; `compareRole` même nom, `ownerClientId = 4242`
- **When:** `InvokePrivateMethod(power, "TryCorruptCharacterServerRpc", 4242UL, compareRole)`
- **Then:** `ChainingManager.instance.chainingPlayers.Contains(4242)` ET `info.isRoleRevealed == RevealLevel.Personal`
- **Stabilité:** cas nominal (verrouillé) ; miroir positif des 228/229, conservé pour traçabilité.
- **Statut:** déjà couvert (CorruptionTests.PChainedByShadows_RevealsAndChainsMatchingChosenTarget)

### 244 — HighPriorityBounty robot éliminé + révélé public (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `HighPriorityBountyDecision` branche robot → `SetEliminated` + `RevealPublic` — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:18`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) roleName "Hunter" ; cible 8888 `role.roleID = RoleID.Robot`
- **When:** `InvokePrivateMethod(power, "OnCardClickedRpc", 8888UL)`
- **Then:** `target.isEliminated.Value == true` ET `info.isRoleRevealed == RevealLevel.Public`
- **Stabilité:** cas nominal (verrouillé) ; miroir positif du 227, conservé pour traçabilité.
- **Statut:** déjà couvert (CorruptionTests.PHighPriorityBounty_EliminatesAndRevealsRobotTarget)

### 245 — InfiniteMessage reparent met owner.messageLeft à MaxValue (traçabilité)
- **Catégorie:** G2 — chaque pouvoir en PlayMode StartHost
- **Mécanique:** `PInfiniteMessage.OnPowerReparented` → `InfiniteMessageDecision` écrit `int.MaxValue` sur l'owner — `Assets/Scripts/Characters/Powers/PInfiniteMessage.cs:19`
- **Couche:** PlayMode-StartHost
- **Given:** owner (host) `messageLeft.Value != int.MaxValue` ; power `PInfiniteMessage` spawné, `ownerClientId = localClientId`
- **When:** `ReflectionHelper.InvokePrivateMethod(power, "OnPowerReparented")`
- **Then:** `owner.messageLeft.Value == int.MaxValue`
- **Stabilité:** cas nominal (verrouillé) ; base positive du 239, conservé pour traçabilité.
- **Statut:** déjà couvert (VisionPowerTests.PInfiniteMessage_SetsOwnerMessageLeftToMaxOnReparent)

### 246 — Sélection distincte reproductible sous seed fixe
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** kernel pur `StolenPowerSelector.SelectDistinct` tirage Fisher-Yates partiel via `IRandomProvider` — `Assets/Scripts/Domain/StolenPowerSelector.cs:22`
- **Couche:** EditMode-pure
- **Given:** `candidateCount=6`, `pickCount=3`, deux `SeededRandomProvider(1234)` distincts (`Assets/Scripts/Domain/SeededRandomProvider.cs:13`)
- **When:** appeler `SelectDistinct(6, 3, seedA)` puis `SelectDistinct(6, 3, seedB)` avec le même seed 1234
- **Then:** les deux listes retournées sont identiques élément-par-élément (`CollectionAssert.AreEqual`)
- **Stabilité:** garantit que la source d'aléa du vol de pouvoir EST seedable au niveau du kernel — une régression qui remplacerait le port par un `Random` global casserait ce test.
- **Statut:** déjà couvert (StolenPowerSelectorTests.IsDeterministic_ForAGivenSeed)

### 247 — Filtre d'éligibilité Ugues → indices originaux, tirage scripté
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `StolenPowerSelector.SelectStealable` filtre (`PowerCandidate.IsEligible`) puis tire, renvoie les indices ORIGINAUX — `Assets/Scripts/Domain/StolenPowerSelector.cs:52` + règle d'éligibilité `:96`
- **Couche:** EditMode-pure
- **Given:** liste `[Eligible, NotChosen, Passive, Eligible, OwnedByUgues, AlreadyCopied, Eligible]` (indices 0..6), `pickCount=3`, RNG scripté `Stub(0,0,0)`
- **When:** appeler `SelectStealable(candidates, 3, stub)`
- **Then:** retourne exactement `[0, 3, 6]` (les 3 seuls éligibles, dans l'ordre de tirage)
- **Stabilité:** verrouille la règle « chosen && !Ugues && !passive && !copie » et le mapping index-original — empêche qu'un refactor du filtre vole un pouvoir d'anomalie / passif / déjà copié.
- **Statut:** déjà couvert (StolenPowerSelectorTests.SelectStealable_ReturnsOriginalIndices_OfEligibleOnly)

### 248 — SEAM BLOQUANT : l'aléa vivant du vol Ugues n'est PAS injectable
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `PMarqueHurluberluges.GrantStolen` construit un `new UnityRandomProvider()` en dur au point d'appel du kernel — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:101` (le provider `UnityRandomProvider` = `Random.Range`, `Assets/Scripts/GameLogic/UnityRandomProvider.cs:17`)
- **Couche:** PlayMode-2NM (intégration du vrai pouvoir spawné)
- **Given:** un vrai `PMarqueHurluberluges` spawné (hook `BuildExtraNetworkPrefabs`) + ≥4 Characters chosen porteurs de pouvoirs actifs distincts, seed voulu = valeur T
- **When:** déclencher `OnGameStartedServer` et vouloir asserter QUELS 3 pouvoirs sont volés pour un seed donné
- **Then:** IMPOSSIBLE de façon déterministe — le pouvoir n'expose aucun seam (champ/ctor/factory) pour injecter un `IRandomProvider` seedé ; `new UnityRandomProvider()` est câblé en dur → tirage `UnityEngine.Random` global non maîtrisable par le test. Story d'archi requise : ajouter un port `IRandomProvider` injectable sur le carrier (settable field ou factory, comme `RoleAttributionState`), défaut = `UnityRandomProvider`, override seedé en test.
- **Stabilité:** documente la seule source de non-déterminisme du vol Ugues au niveau intégration ; sans le seam, tout test « seed → sélection attendue » sur le vrai pouvoir serait flaky et interdit.
- **Statut:** bloqué — story d'archi requise (injecter IRandomProvider dans PMarqueHurluberluges, PMarqueHurluberluges.cs:101)

### 249 — SEAM BLOQUANT : « seed X → 3 pouvoirs volés attendus » sur le roster vivant
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** boucle candidate parallèle `_powers`/`_candidates` (ordre = `characterManager.GetCharacters(false)`) puis `SelectStealable(..., new UnityRandomProvider())` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:78-101`
- **Couche:** PlayMode-StartHost
- **Given:** 5 rôles chosen, chacun 1 pouvoir actif nommé P0..P4, Ugues présent ; on voudrait un seed produisant P1,P2,P4
- **When:** `OnGameStartedServer` de l'Ugues spawné
- **Then:** IMPOSSIBLE d'asserter l'ensemble {P1,P2,P4} tant que le RNG n'est pas injectable (même blocage que 248). NOTE : l'ORDRE d'itération est déterministe (`networkedCharacters` NetworkList = ordre d'ajout de seat, `CharacterManager.RebuildCharactersCache` `Assets/Scripts/Characters/CharacterManager.cs:150`), donc le mapping index→pouvoir est stable ; seul le tirage est non maîtrisable.
- **Stabilité:** prouve que la seule variable non-déterministe restante est le RNG (l'ordre du pool est déjà stable) — cible précise de la story d'archi 248.
- **Statut:** bloqué — story d'archi requise (injecter IRandomProvider dans PMarqueHurluberluges, PMarqueHurluberluges.cs:101)

### 250 — Ugues vole TOUS les éligibles quand il y en a exactement ≤ 3 (indépendant du RNG)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `take = Math.Min(pickCount, candidateCount)` — quand éligibles ≤ pickCount, l'ENSEMBLE volé est le pool entier quel que soit le tirage — `Assets/Scripts/Domain/StolenPowerSelector.cs:28`, appliqué par `GivePowerToCharacter` boucle `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:107-111`
- **Couche:** PlayMode-StartHost
- **Given:** exactement 2 Characters chosen porteurs chacun d'UN pouvoir actif non-copie (P0, P1), plus Ugues, plus 1 anomaly (à exclure)
- **When:** `OnGameStartedServer` de l'Ugues spawné (`POWERS_TO_STEAL=3` > 2 éligibles)
- **Then:** Ugues reçoit EXACTEMENT 2 copies one-shot, correspondant à P0 ET P1 (l'ensemble est déterministe même sous `UnityRandomProvider`) ; aucune copie du pouvoir d'anomalie
- **Stabilité:** verrouille le cap « au plus ce qui existe » et l'exhaustivité quand le pool est petit — régression si le filtre laissait passer une anomalie ou si le cap coupait un éligible.
- **Statut:** à implémenter

### 251 — Ugues ne vole RIEN quand aucun pouvoir n'est éligible (branche vide déterministe)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `_picks.Count == 0` → early-return sans appel à `GivePowerToCharacter` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:102-106`
- **Couche:** PlayMode-StartHost
- **Given:** roster où tous les pouvoirs chosen actifs sont ABSENTS : seulement des rôles anomaly (non-chosen), des passifs, et des copies déjà volées ; Ugues présent
- **When:** `OnGameStartedServer` de l'Ugues spawné
- **Then:** la barre de pouvoirs d'Ugues ne gagne AUCUNE copie (aucun spawn one-shot), log `no eligible chosen active power to steal` ; résultat déterministe (ensemble éligible vide, RNG jamais consulté)
- **Stabilité:** empêche un vol fantôme (copie d'un passif / d'une anomalie / d'une copie) quand rien n'est légalement volable — la branche vide ne doit jamais spawner.
- **Statut:** à implémenter

### 252 — SEAM BLOQUANT : Luma « Mélange des cartes » partage le même RNG non injectable
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `PCardsShuffling` copie 1 pouvoir actif via `SelectStealable(_candidates, 1, new UnityRandomProvider())` — `Assets/Scripts/Characters/Powers/PCardsShuffling.cs:157`
- **Couche:** PlayMode-StartHost
- **Given:** un rôle élu absent porteur de 2 pouvoirs actifs distincts P0/P1, Luma présent, seed voulu produisant P1
- **When:** déclenchement de la copie Luma
- **Then:** IMPOSSIBLE d'asserter « seed → P1 » de façon déterministe — deuxième site avec `new UnityRandomProvider()` câblé en dur, aucun seam d'injection. Même remède d'archi que 248 (port `IRandomProvider` injectable), à appliquer aux DEUX pouvoirs pour homogénéité.
- **Stabilité:** évite d'oublier un second consommateur d'aléa non maîtrisé lors de l'extraction du seam ; documente que le blocage de déterminisme est systémique aux pouvoirs voleurs, pas isolé à Ugues.
- **Statut:** bloqué — story d'archi requise (injecter IRandomProvider dans PCardsShuffling, PCardsShuffling.cs:157)

### 253 — CursedVision : cible ET propriétaire corrompus après quiescence (2 NV, même tick)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `CursedVisionDecision.Decide` émet `CorruptPlayer(target)` ET `CorruptPlayer(owner)` dans le même outcome — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:28,32` ; exécuté via `CorruptPlayerServerRpc` (`Assets/Scripts/Characters/Powers/Runtime/Executors/CorruptPlayerExecutor.cs:19` → `Assets/Scripts/Characters/Character.cs:184`)
- **Couche:** PlayMode-2NM
- **Given:** owner (chosen) seatA et target (anomaly) seatB, deux répliques Character sur le client ; baseline `isCorrupted.Value==false` sur les deux
- **When:** déclencher CursedVision côté host sur seatB, puis DRAINER plusieurs ticks jusqu'à quiescence (attendre au-delà de la 1re valeur, cf. pattern `WaitUntilOrTimeout` de MultiClientGameFixture)
- **Then:** APRÈS quiescence, la réplique client de seatA `isCorrupted.Value==true` ET seatB `isCorrupted.Value==true` — jamais un état où seul l'un des deux est corrompu de façon stable
- **Stabilité:** attrape une réplication partielle où le client observerait durablement la cible corrompue mais pas le lanceur (ou l'inverse) — les deux NV doivent converger.
- **Statut:** à implémenter

### 254 — CursedVision : corruption (NV) et reveal-corruption Personal (RPC) cohérents chez l'owner
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** même outcome émet `CorruptPlayer(target)` (NV) ET `RevealInfo(target, CorruptRevealed, Personal, owner, false)` (store reveal via RPC) — `Assets/Scripts/Domain/Powers/Decisions/CursedVisionDecision.cs:28-29` ; canaux DIFFÉRENTS (NetworkVariable delta vs `GameInfoRevealer.SendRevealLevelRpc` `Assets/Scripts/GameLogic/GameInfoRevealer.cs:188`)
- **Couche:** PlayMode-2NM
- **Given:** owner = client réel (id<100), target seatB anomaly, revealer spawné et rooté host, baseline reveal-store `isCorruptRevealed==False`
- **When:** déclencher CursedVision côté host, drainer jusqu'à quiescence
- **Then:** APRÈS quiescence, sur le client owner : `target.isCorrupted.Value==true` (NV) ET `revealer.GetCharacterInfo(seatB, clientLocalId).isCorruptRevealed==Personal` (store) — les deux porteurs (NV + RPC) sont cohérents, pas d'état « corrompu mais reveal absent » stable
- **Stabilité:** couvre l'incohérence inter-canaux (le reveal voyage par RPC ciblé, la corruption par delta NV) — régression si l'un arrive sans l'autre.
- **Statut:** à implémenter

### 255 — HighPriorityBounty (robot) : élimination (NV) + reveal Public (store) cohérents à distance
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `HighPriorityBountyDecision` branche robot émet `SetEliminated(target)` (NV `isEliminated`) + `RevealPublic(target, RoleRevealed)` (store) même tick — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:20,24`
- **Couche:** PlayMode-2NM
- **Given:** owner « Hunter », target rôle `RoleID.Robot`, revealer + chat spawnés, baseline `isEliminated==false`
- **When:** invoquer la bounty côté host sur la cible robot, attendre réplication des deux carriers
- **Then:** sur le client, `target.isEliminated.Value==true` (NV) ET `revealer.GetCharacterInfo(target, clientLocalId).isRoleRevealed==Public` (store)
- **Stabilité:** vérifie qu'élimination et reveal public du robot atterrissent tous deux sur la réplique — régression si l'un des deux carriers décroche.
- **Statut:** déjà couvert (PowerPipelineClientReplicationTests.HighPriorityBounty_EliminatesRobot_AndPublicReveal_ObservedOnRemoteClient)

### 256 — HighPriorityBounty (non-robot) : SEUL l'owner est enchaîné, la cible intacte
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** branche non-robot émet `AddToChain(owner)` (pas la cible) et n'émet NI `SetEliminated` NI reveal — `Assets/Scripts/Domain/Powers/Decisions/HighPriorityBountyDecision.cs:28`
- **Couche:** PlayMode-2NM
- **Given:** owner « Hunter » seatA, target NON-robot seatB, ChainingManager spawné, baseline liste chaînage vide + `seatA.isEliminated==false`
- **When:** invoquer la bounty côté host sur seatB (non-robot), drainer jusqu'à quiescence
- **Then:** APRÈS quiescence, réplique client : la NetworkList de chaînage contient seatA (owner) et PAS seatB ; `seatB.isEliminated.Value==false` et `seatA.isEliminated.Value==false` (auto-punition sans élimination)
- **Stabilité:** attrape une inversion de branche (enchaîner/éliminer la cible au lieu de l'owner) et toute fuite d'effet sur la mauvaise seat.
- **Statut:** à implémenter

### 257 — ChainedByShadows (match+chosen) : reveal (RPC) ET chaînage (NetworkList) cohérents
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** si `SameRole` alors `RevealInfo(target, RoleRevealed, Personal, owner, broadcast=true)` puis si chosen `AddToChain(target)` — deux carriers différents même tick — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:22-25`
- **Couche:** PlayMode-2NM
- **Given:** owner « Shadow », target chosen dont le rôle matche le secondary target, revealer + chaining spawnés, baselines vides
- **When:** déclencher ChainedByShadows côté host (rôles concordants), drainer jusqu'à quiescence
- **Then:** APRÈS quiescence, réplique client : la NetworkList de chaînage contient la cible ET `revealer.GetCharacterInfo(target, ...).isRoleRevealed` atteint le niveau révélé — les deux carriers présents ensemble
- **Stabilité:** le test existant n'assure que la NetworkList ; ajoute la cohérence croisée reveal+chaînage (RPC de reveal vs delta NetworkList) — régression si le reveal manque alors que le chaînage a réussi.
- **Statut:** à implémenter

### 258 — ChainedByShadows (rôles NON concordants) : ni reveal ni chaînage (cohérence négative)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `NewTargeting` est inconditionnel mais reveal + `AddToChain` sont GARDÉS par `Roster.SameRole` — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:19-27`
- **Couche:** PlayMode-2NM
- **Given:** owner « Shadow », target chosen dont le rôle NE matche PAS le secondary target, revealer + chaining spawnés, baselines vides
- **When:** déclencher ChainedByShadows côté host (rôles discordants), drainer jusqu'à quiescence
- **Then:** APRÈS quiescence, réplique client : NetworkList de chaînage vide (cible absente) ET reveal-store de la cible inchangé (`isRoleRevealed==False`) ; seule la cible de `NewTargeting` a bougé
- **Stabilité:** empêche un reveal/chaînage émis à tort quand la condition `SameRole` est fausse — garde-fou de la branche conditionnelle.
- **Statut:** à implémenter

### 259 — ChainCharacterServer : isChained ET isCorrupted répliqués ensemble (jamais l'un sans l'autre)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `ChainCharacterServer` écrit `isChained.Value=true` PUIS `isCorrupted.Value=true` (deux NV, même appel serveur) — `Assets/Scripts/Characters/Character.cs:205-206`
- **Couche:** PlayMode-2NM
- **Given:** un Character seatB spawné, baseline `isChained==false` et `isCorrupted==false` sur la réplique client
- **When:** appeler `ChainCharacterServer` côté host, drainer jusqu'à quiescence
- **Then:** APRÈS quiescence, réplique client : `isChained.Value==true` ET `isCorrupted.Value==true` — les deux booléens convergent
- **Stabilité:** un enchaînement doit toujours s'accompagner de la corruption sur le client ; régression si un des deux deltas NV se perd.
- **Statut:** déjà couvert (CharacterStateReplicationTests.ChainCharacterServer_ChainsAndCorrupts_OnRemoteClientReplica)

### 260 — Un client ne peut PAS écrire directement une NetworkVariable server-write
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `isCorrupted` (et les autres NV du Character) sont en permission d'écriture serveur par défaut (aucun `NetworkVariableWritePermission.Owner`) — `Assets/Scripts/Characters/Character.cs:22`
- **Couche:** PlayMode-2NM
- **Given:** un Character seatB spawné, réplique résolue côté `ClientNm` (via `ClientNm.SpawnManager.SpawnedObjects[netId].GetComponent<Character>()`), baseline `isCorrupted==false`
- **When:** depuis la réplique CLIENT, tenter `replica.isCorrupted.Value = true`
- **Then:** NGO lève une exception d'écriture non autorisée (client non autorisé à écrire) ; la valeur autoritaire côté host reste `false` — le client ne peut pas muter l'état d'autorité en contournant le serveur
- **Stabilité:** invariant « server authority strict » (CLAUDE.md) — attrape toute régression où une NV deviendrait owner/everyone-writable et laisserait un client trafiquer la corruption.
- **Statut:** à implémenter

### 261 — La corruption client ne passe QUE par le ServerRpc sanctionné (RequireOwnership=false)
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `CorruptPlayerServerRpc` `[Rpc(SendTo.Server, RequireOwnership = false)]` — le serveur seul écrit `isCorrupted.Value`, le client ne fait que proposer — `Assets/Scripts/Characters/Character.cs:181-185`
- **Couche:** PlayMode-2NM
- **Given:** un Character seatB spawné, baseline `isCorrupted==false`, réplique CLIENT résolue
- **When:** invoquer `replica.CorruptPlayerServerRpc()` DEPUIS le client (non-owner autorisé)
- **Then:** le serveur exécute et `isEliminated`/`isCorrupted` du host passe à `true`, puis re-réplique vers le client — la mutation d'autorité n'a lieu que via ce canal serveur, jamais par écriture directe (contraste avec 260)
- **Stabilité:** prouve la frontière : le seul chemin légitime client→autorité est le ServerRpc ; régression si `RequireOwnership` sautait ou si l'écriture était déplacée côté client.
- **Statut:** déjà couvert (CharacterStateReplicationTests.CorruptPlayerServerRpc_InvokedOnClientReplica_MutatesServerState)

### 262 — VoteState.CanVote : le serveur ignore le vote d'un joueur ÉLIMINÉ
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `OnPlayerVotedRpc` (serveur) abandonne si `!CanVote(sender)` ; `CanVote` renvoie false si `isEliminated.Value` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:59` + `:97`
- **Couche:** PlayMode-StartHost
- **Given:** VoteState en cours (`OnStartStateServer` exécuté, `votesForPlayer` initialisé), un votant seatA avec `isEliminated.Value==true`, une cible seatB valide
- **When:** appeler `OnPlayerVotedRpc(seatA, seatB)` côté serveur
- **Then:** `votesForPlayer[seatB]` NE contient PAS seatA (vote rejeté), warning « cannot vote » loggé
- **Stabilité:** garde serveur d'éligibilité de vote — empêche un joueur éliminé d'influencer le tally malgré un RPC forgé.
- **Statut:** à implémenter

### 263 — VoteState.CanVote : le serveur rejette le double vote
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `CanVote` (sans `_ignoreAlreadyVoted`) renvoie false si le joueur figure déjà dans une liste de votes — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:91`
- **Couche:** PlayMode-StartHost
- **Given:** VoteState en cours, seatA valide (non éliminé, non fake, présent), a déjà voté une fois pour seatB
- **When:** appeler `OnPlayerVotedRpc(seatA, seatC)` une seconde fois côté serveur
- **Then:** aucun second vote enregistré — seatA n'est ajouté ni à `votesForPlayer[seatC]` ni ailleurs ; le premier vote reste seul
- **Stabilité:** un client ne peut pas voter plusieurs fois en spammant le ServerRpc — verrou anti-double-comptage serveur.
- **Statut:** à implémenter

### 264 — VoteState.CanVote : un client réel PARTI est exclu de l'éligibilité
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `CanVote` renvoie false si `_playerId < 100 && gameManager.HasClientLeft(_playerId)` (les bots ≥100 restent toujours éligibles) — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:107`
- **Couche:** PlayMode-StartHost
- **Given:** VoteState en cours, seatA = client réel (id<100) enregistré comme parti (`HasClientLeft(seatA)==true`), seatB = client réel présent
- **When:** évaluer `CanVote(seatA)` et `CanVote(seatB)` côté serveur
- **Then:** `CanVote(seatA)==false` (parti exclu) ET `CanVote(seatB)==true` (présent éligible)
- **Stabilité:** un leaver ne doit ni voter ni gonfler le dénominateur d'auto-clôture ; garde-fou distinct de la collapse-de-timer déjà testée côté seam de départ.
- **Statut:** à implémenter (couverture liée : LeaveUnblockSeamTests.VoteState_DepartedNonVoterLeaves_ReducesDenominator_CollapsesTimer, qui teste le timer, pas CanVote directement)

### 265 — AwakenCharacterServerRpc : garde RequireOwnership (défaut true) rejette un non-owner
- **Catégorie:** H — Seams critiques (déterminisme / atomicité / frontière serveur-client)
- **Mécanique:** `AwakenCharacterServerRpc` est `[Rpc(SendTo.Server)]` SANS `RequireOwnership = false` → défaut `true` ; le Character est server-owned (spawn serveur), donc un client réel n'en est pas owner — `Assets/Scripts/Characters/Character.cs:139-146` (contraste : `CorruptPlayerServerRpc` met explicitement `RequireOwnership = false`, `:181`)
- **Couche:** PlayMode-2NM
- **Given:** un Character seatB spawné côté serveur (owner NGO = host), réplique CLIENT résolue, baseline `isAwakened.Value==false`
- **When:** invoquer `replica.AwakenCharacterServerRpc()` depuis le client (qui n'est PAS l'owner NGO)
- **Then:** NGO abandonne le RPC (ownership requis, warning côté serveur) ; après drainage, `isAwakened.Value` reste `false` côté host ET client — l'éveil n'a pas eu lieu
- **Stabilité:** vérifie que la garde de propriété par défaut protège bien l'éveil ; régression silencieuse si quelqu'un ajoutait `RequireOwnership=false` par copier-coller depuis CorruptPlayerServerRpc.
- **Statut:** à implémenter

### 266 — Awakening layers resolve in authored index order (same-night sequencing)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** night resolution ordering — `AwakeningState.AwakeLayer` iterates `awakeningOrder[_layerToAwake]` and advances `currentAwakeningIndex` monotonically — `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:77-106` + `:311-327`
- **Couche:** PlayMode-StartHost
- **Given:** an AwakeningState whose `awakeningOrder` has two layers — layer 0 = a role R0, layer 1 = a role R1 — each held by exactly one real seat; StartHost, both awake.
- **When:** the layer 0 seat sleeps (drains), driving `GoToNextAwakeLayer`.
- **Then:** `currentAwakeningIndex` becomes 1 and only R1's seat is awakened next (`currentlyAwakenedCharacters` contains R1's char, not R0's) — layer order strictly follows list index.
- **Stabilité:** guards against a reordering/off-by-one in the layer loop that would wake roles out of the authored night order.
- **Statut:** à implémenter

### 267 — Cross-role night-action ORDER between different layers is data-owned, not code-defined
> **CORRECTION OWNER (Poyo, 2026-07-17) :** PAS un trou de fairness. Les rôles se réveillent SÉQUENTIELLEMENT
> (`awakeningOrder`) EXPRÈS pour éviter toute simultanéité heal↔corrupt ; un corrompu reste soignable, point
> (règle réelle déjà couverte par HealAfterCorruption). Scénario rétrogradé en simple pin de l'ordre authored
> (priorité BASSE) — retirer le framing « design-owned / décision requise ». AUCUNE décision design à prendre.
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** relative order of heal-role vs corrupt-role acting the same night — determined solely by their `awakeningOrder` layer index authored in the AwakeningState SO, NOT by any code rule — `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:44-75` (`GetAwakeningLayerIndex`) + `:81-100`
- **Couche:** PlayMode-StartHost
- **Given:** the live AwakeningState SO loaded from GameScene; extract the layer index of the healing role (PDroolyHealing/PBlessing carrier) and of the corrupting role (PCorruptingMark carrier) via `GetAwakeningLayerIndex`.
- **When:** the test reads both indices.
- **Then:** assert the CURRENT authored relative order (pin whichever is lower today) and DOCUMENT it as `design-owned / non défini par le code` — no code path forces heal-before-corrupt or vice-versa; the winner on a same-target same-night heal↔corrupt race is decided entirely by this SO authoring.
- **Stabilité:** freezes today's night pecking order so a silent SO re-author (or a heal that suddenly loses to a corrupt) is caught; explicitly marks the ordering as design-owned so nobody "fixes" it in code.
- **Statut:** à implémenter

### 268 — Chaining a character sets BOTH isChained and isCorrupted
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `Character.ChainCharacterServer` — `isChained.Value = true; isCorrupted.Value = true` — `Assets/Scripts/Characters/Character.cs:198-207`
- **Couche:** PlayMode-StartHost
- **Given:** a non-fake anomaly seat, not chained, not corrupted (isChained=false, isCorrupted=false), StartHost.
- **When:** `ChainCharacterServer()` runs on the server for that seat.
- **Then:** both `isChained.Value` and `isCorrupted.Value` are true.
- **Stabilité:** pins the load-bearing side effect that a chain also corrupts — several win conditions read isCorrupted, so decoupling the two would silently break anomaly/chosen tallies.
- **Statut:** à implémenter

### 269 — Heal reverses corruption and stamps isHealed
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `Character.HealPlayerServerRpc` — `isCorrupted.Value = false; isHealed.Value = true` — `Assets/Scripts/Characters/Character.cs:187-196`
- **Couche:** PlayMode-StartHost
- **Given:** a seat with isCorrupted=true, isHealed=false, StartHost.
- **When:** `HealPlayerServerRpc()` runs on the server.
- **Then:** isCorrupted becomes false and isHealed becomes true.
- **Stabilité:** pins that a heal both un-corrupts and marks the one-shot flag — needed so the corrupt/heal interaction chain is deterministic.
- **Statut:** à implémenter

### 270 — Heal is one-shot: a second heal on an already-healed seat is a no-op
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `Character.HealPlayerServerRpc` early-returns when `isHealed.Value` — `Assets/Scripts/Characters/Character.cs:188-196`
- **Couche:** PlayMode-StartHost
- **Given:** a seat already healed once then re-corrupted (isHealed=true, isCorrupted=true), StartHost.
- **When:** `HealPlayerServerRpc()` runs again.
- **Then:** the method returns immediately — isCorrupted stays true (the second heal does NOT clear it), isHealed stays true.
- **Stabilité:** pins the "protection cannot be reapplied" rule; a regression that dropped the guard would make heals infinitely re-usable.
- **Statut:** à implémenter

### 271 — Corrupt after heal re-corrupts but leaves isHealed sticky (state does NOT return clean)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `Character.CorruptPlayerServerRpc` sets isCorrupted=true with NO isHealed guard — `Assets/Scripts/Characters/Character.cs:181-185` vs the heal guard `:188-196`
- **Couche:** PlayMode-StartHost
- **Given:** a seat that was corrupted, then healed (isCorrupted=false, isHealed=true), StartHost.
- **When:** `CorruptPlayerServerRpc()` runs.
- **Then:** isCorrupted becomes true again AND isHealed stays true — the seat is now corrupted-and-already-healed, so a future heal (scenario 270) cannot save it.
- **Stabilité:** pins the asymmetry (corruption is re-appliable, protection is not) that makes the corrupt→heal→re-corrupt sequence terminal for the target.
- **Statut:** à implémenter

### 272 — Reveal level is monotonic: a lower requested level never downgrades a stored higher one
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `GameInfoRevealer.SetRevealLevel` returns early when `_currentRevealLevel >= _revealLevel` — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:155-168`
- **Couche:** PlayMode-StartHost
- **Given:** a target whose `isRoleRevealed` was already set to `RevealLevel.Public` for observer O, StartHost.
- **When:** `SetRevealLevel(target, isRoleRevealed, RevealLevel.Personal, O)` is called (a lower level).
- **Then:** the stored level stays `Public` — no downgrade, `onCharacterInfoRevealedChanged` not driven by a downgrade.
- **Stabilité:** pins that public knowledge, once revealed, cannot be un-revealed by a later weaker reveal — protects the deduction board's consistency.
- **Statut:** à implémenter

### 273 — ChainedByShadows chains a matched-role target ONLY when its faction is chosen
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `ChainedByShadowsDecision.Decide` — adds `AddToChain` only inside `SameRole && FactionOf==chosen` — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:17-29`
- **Couche:** EditMode-pure
- **Given:** a `PowerContext` where the target's role matches the picked role (`Roster.SameRole==true`) and `Roster.FactionOf(target)==FactionType.chosen`; a FakeRoster supplies both.
- **When:** `Decide(ctx)` is called.
- **Then:** the emitted effects contain `NewTargeting`, `RevealInfo(RoleRevealed, Personal, owner)`, AND `AddToChain(target)`.
- **Stabilité:** pins the chosen-only chaining branch so a faction-check regression cannot chain anomalies by night.
- **Statut:** à implémenter

### 274 — ChainedByShadows on a matched anomaly reveals but does NOT chain
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `ChainedByShadowsDecision.Decide` faction guard — `Assets/Scripts/Domain/Powers/Decisions/ChainedByShadowsDecision.cs:20-27`
- **Couche:** EditMode-pure
- **Given:** a `PowerContext` where `Roster.SameRole==true` but `Roster.FactionOf(target)==FactionType.anomaly`.
- **When:** `Decide(ctx)` is called.
- **Then:** effects contain `NewTargeting` and `RevealInfo` but NO `AddToChain`.
- **Stabilité:** pins that the night-chain power can never chain an anomaly even on a correct role guess — the inverse of 273.
- **Statut:** à implémenter

### 275 — Ugues steals exactly 3 active chosen powers at game start
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `PMarqueHurluberluges.OnGameStartedServer` → `IStolenPowerGrant.GrantStolen` with `POWERS_TO_STEAL = 3` + `StolenPowerSelector.SelectStealable` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:37`,`:45-58`,`:63-112`
- **Couche:** PlayMode-StartHost
- **Given:** StartHost roster with Ugues plus ≥3 chosen seats each carrying at least one active (non-passive) power; other roles anomaly/marginal.
- **When:** the game-started server hook fires (`OnGameStartedServer`).
- **Then:** Ugues' `role.powers` gains exactly 3 spawned copies, each `isStolenCopy.Value==true`, each drawn from a chosen-faction active power, none from Ugues himself.
- **Stabilité:** pins the steal count + eligibility so a copier regression (stealing passives, anomaly powers, or the wrong count) is caught end-to-end.
- **Statut:** à implémenter

### 276 — A stolen copy clones the base prefab, so it starts at fresh state
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `CharacterManager.GivePowerToCharacter` clones `basePrefab` not the live instance — `Assets/Scripts/Characters/CharacterManager.cs:491-499`
- **Couche:** PlayMode-StartHost
- **Given:** a chosen seat whose corruption power has already been used (powerUseLeft decremented on the original), StartHost; Ugues steals it.
- **When:** the copy is granted to Ugues.
- **Then:** the copy's `powerUseLeft` reflects the one-shot config (1), independent of the original's spent state — the copy did not inherit the used instance's mutated counters.
- **Stabilité:** pins the "copy always starts fresh" contract so a thief never inherits a half-spent power.
- **Statut:** à implémenter

### 277 — A stolen corruption power, used by Ugues, corrupts under Ugues' authority context
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** granted copy gets `idHolderServer = Ugues slot` → `ownerClientId` = Ugues; the copied decision runs with `PowerContext(ownerSlot = Ugues)` — `Assets/Scripts/Characters/CharacterManager.cs:498` + `Power.OnNetworkSpawn` `:168-170` + `PCorruptingMark.OnCardClickedRpc` `:70-75`
- **Couche:** PlayMode-StartHost
- **Given:** Ugues holding a stolen PCorruptingMark copy; an eligible anomaly target T; StartHost.
- **When:** Ugues uses the stolen copy on T.
- **Then:** T.isCorrupted becomes true AND the corruption-reveal (`isCorruptRevealed`, Personal) is stamped for observer = Ugues' clientId (not the original owner's) — the copy carries the ORIGINAL power's effect but the THIEF's authority/reveal context.
- **Stabilité:** pins that a stolen power's reveal asymmetry re-targets to the thief; a regression keeping the original owner as reveal observer would leak info to the wrong seat.
- **Statut:** à implémenter

### 278 — A spent one-shot stolen copy despawns and can never re-fire
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `Power.OnUsed` schedules `DespawnSpentCopyNextFrameServer` when `isStolenCopy && powerUseLeft<=0`; config sets maxUse=1 — `Assets/Scripts/Characters/Powers/Power.cs:304-307`,`:312-329`,`:341-352`
- **Couche:** PlayMode-StartHost
- **Given:** Ugues holding a stolen active copy (isStolenCopy=true, powerUseLeft=1), StartHost.
- **When:** Ugues uses it once, then a frame elapses.
- **Then:** `powerUseLeft` hits 0, `RemovePowerFromCharacter` runs next frame, and the copy NetworkObject is despawned / removed from Ugues' `role.powers` — it is gone, not a greyed husk.
- **Stabilité:** pins "temporaire = perdu"; a regression leaving spent copies alive would let Ugues re-use a stolen power.
- **Statut:** à implémenter

### 279 — A stolen copy never regenerates its use on a later awakening
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `ConfigureAsOneShotStolenCopy` sets `powerUseRegenPerAwakening = 0` — `Assets/Scripts/Characters/Powers/Power.cs:341-352` (regen field `:70`)
- **Couche:** EditMode-pure
- **Given:** a `Power` instance configured via `ConfigureAsOneShotStolenCopy` on a server stub (powerUseLeft=1, regen=0).
- **When:** the awakening-regen rule is applied (regen adds `powerUseRegenPerAwakening`, which is 0).
- **Then:** `powerUseLeft` stays 0 after a spend — no refill on a new night, distinct from a normal power (regen=-1 → refills to maxUse).
- **Stabilité:** pins that a stolen one-shot is guaranteed single-fire even across nights, independent of the despawn path.
- **Statut:** à implémenter

### 280 — A copied power can never itself be re-copied (no copy chains)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `StolenPowerSelector.SelectStealable` excludes candidates flagged `isCopiedPower`; `PowerCandidate` fed with `_p.isCopiedPower.Value` — `Assets/Scripts/Characters/Powers/PMarqueHurluberluges.cs:95` + selector filter
- **Couche:** EditMode-pure
- **Given:** a candidate list where one chosen active power has `isCopiedPower==true` (already a stolen/granted copy) and one is a genuine original.
- **When:** `StolenPowerSelector.SelectStealable(candidates, 3, rng)` runs.
- **Then:** the returned indices never include the already-copied candidate.
- **Stabilité:** pins the no-recopy invariant so two copiers (Ugues + Réincarnation) can't launder a power indefinitely.
- **Statut:** déjà couvert (StolenPowerSelectorTests.SelectStealable_ExcludesNonChosen_Passive_Self_AndAlreadyCopied)

### 281 — Reveal record is keyed by clientId and independent of role/corruption state (no staleness)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `GameInfoRevealer.charactersInfoRevealed` maps `ulong → CharacterInfoReveal` storing reveal LEVELS, not a role snapshot — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:20`,`:84`,`:308-317`
- **Couche:** PlayMode-StartHost
- **Given:** a target revealed to `Public` (`isRoleRevealed`), StartHost; the target is then corrupted (isCorrupted flipped) after the reveal.
- **When:** `GetCharacterInfo(target, observer)` is read again post-corruption.
- **Then:** `isRoleRevealed` is still `Public` and unchanged — the stored reveal does not go stale or mutate on a corruption/flag change, because it stores a level, not the role's live faction.
- **Stabilité:** pins that later state changes never retroactively alter what was already revealed — the board reflects reveal history, not live faction.
- **Statut:** à implémenter

### 282 — Corruption does not change a character's factionType (revealed role stays constant)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** corruption writes only `isCorrupted` (`Character.cs:181-185`); no path assigns `role.factionType`; win conditions read `factionType` separately from `isCorrupted` — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:26-35` vs `WAnomalyCorruption.cs:36-47`
- **Couche:** PlayMode-StartHost
- **Given:** an anomaly seat, StartHost; capture `role.factionType`.
- **When:** the seat is corrupted then chained.
- **Then:** `role.factionType` is unchanged (still anomaly) across both mutations — corruption/chaining are orthogonal flags, not faction reassignment.
- **Stabilité:** pins that "corrupted" is a state flag, not a faction flip, so win-condition faction reads stay stable — protects against a future mechanic conflating the two.
- **Statut:** à implémenter

### 283 — Two revealers on the same target keep per-observer, siloed knowledge
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** Personal reveals write into the observer-scoped store only; simulated observers get their own brain — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:106-140`,`:227-241`
- **Couche:** PlayMode-2NM
- **Given:** two real clients A and B; a shared target T; 2-NM fixture; A reveals T's role Personal to A, B reveals T's corruption Personal to B (different turns).
- **When:** each client reads `GetCharacterInfo(T)` on its own replica store.
- **Then:** A's store has `isRoleRevealed>=Personal` but `isCorruptRevealed==False`; B's store has `isCorruptRevealed>=Personal` but `isRoleRevealed==False` — no cross-contamination.
- **Stabilité:** pins info cloisonnement across two independent revealers; the core anti-leak invariant of the deduction game.
- **Statut:** à implémenter

### 284 — Personal reveal to one observer does not leak to a second observer's store
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `SetRevealLevelRpc` Personal branch writes only the local viewer's store — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:204-224`
- **Couche:** PlayMode-2NM
- **Given:** two real clients (host + remote), a target T, 2-NM fixture.
- **When:** T's role is revealed Personal to the remote client only.
- **Then:** the remote client's replica shows the reveal; the host's store for T stays `False`.
- **Stabilité:** guards the Personal-scope reveal boundary across the wire.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.PersonalReveal_ToRemoteObserver_LandsOnClientRevealerOnly / PersonalReveal_ToHostObserver_DoesNotLeakToRemoteClientRevealer)

### 285 — Public reveal fans out to every observer store
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `SetRevealLevelRpc` Public branch writes local viewer AND all simulation brains — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:212-219`
- **Couche:** PlayMode-2NM
- **Given:** two real clients, a target T, 2-NM fixture.
- **When:** T's role is revealed Public.
- **Then:** both clients' replica stores show `isRoleRevealed>=Public` for T.
- **Stabilité:** guards public-knowledge fan-out (e.g. a chain reveal) reaching all seats.
- **Statut:** déjà couvert (RevealAsymmetryReplicationTests.PublicReveal_FansOutToBothRevealerStores)

### 286 — Chaining a leaver publicly reveals its role even with no ChainingState animation
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `ChainingManager.ChainCharacterRpc` calls `SetRevealLevelRpc(..., RevealLevel.Public, _showCardReveal)` right after `ChainCharacterServer` — `Assets/Scripts/GameLogic/ChainingManager.cs:89-109`
- **Couche:** PlayMode-StartHost
- **Given:** a mid-game seat about to be chained via the instant-leave path (`_showCardReveal=true`), StartHost with a wired GameInfoRevealer.
- **When:** `ChainCharacterRpc(seat, true)` runs on the server.
- **Then:** the seat is chained AND its `isRoleRevealed` reaches `Public` in the local store — a chained (or departed) seat still publishes its role.
- **Stabilité:** pins that chaining always reveals, so a dead/chained player's role becomes public knowledge regardless of the animation path.
- **Statut:** à implémenter

### 287 — VictoryEvaluator returns ALL satisfied teams with no priority (simultaneity)
> **RETIRÉ — CORRECTION OWNER (Poyo, 2026-07-17) :** exemple INVALIDE. Les anomalies sont corrompues DE BASE →
> le double-win chosen+anomaly construit ici repose sur une prémisse fausse. Poyo : « oublie ça, c'est pas un bon
> test. » **Statut effectif : retiré, ne pas implémenter.** Aucun GATE de tie-break `VictoryEvaluator` n'est requis
> (le panel a sur-théorisé une simultanéité qui n'existe pas dans les règles réelles).
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `VictoryEvaluator.Evaluate` accumulates every satisfied condition's team into a dictionary — no tie-break, no ordering — `Assets/Scripts/Domain/VictoryEvaluator.cs:30-53`
- **Couche:** EditMode-pure
- **Given:** a `GameSnapshot` + owner conditions crafted so BOTH a chosen condition (WChosenChainedAllAnomaly) and an anomaly condition (WAnomalyCorruption) evaluate true on the same snapshot.
- **When:** `Evaluate(snapshot, owners)` runs.
- **Then:** the returned dictionary contains BOTH `WinningTeam.chosen` and `WinningTeam.anomaly` keys — the code declares a simultaneous double win with no arbitration.
- **Stabilité:** pins the CURRENT no-priority behavior. **Flag: `design-owned / non défini`** — which team "really" wins on a same-tick double-win is NOT resolved in code; both are handed to GameEndingState.
- **Statut:** à implémenter

### 288 — Chaining the last un-chained anomaly satisfies the chosen win condition
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `WChosenChainedAllAnomaly.CheckCondition(snapshot)` — true once every non-fake anomaly is chained — `Assets/Scripts/Characters/WinningConditions/WChosenChainedAllAnomaly.cs:41-61`
- **Couche:** EditMode-pure
- **Given:** a snapshot with 2 anomalies where one is already chained and one is not, plus chosen/marginal seats.
- **When:** the still-unchained anomaly is flipped to chained in the snapshot and `CheckCondition(snapshot)` is re-evaluated.
- **Then:** it returns true (chosen win).
- **Stabilité:** pins the "all anomalies chained ⇒ chosen win" threshold at the exact last-anomaly transition.
- **Statut:** à implémenter

### 289 — Chaining the last anomaly (which also corrupts it) can co-satisfy the anomaly win same tick
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** chain sets isCorrupted (`Character.cs:198-207`); if that makes the whole non-fake population corrupted, `WAnomalyCorruption` also holds — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:34-49` evaluated against `WChosenChainedAllAnomaly.cs:41-61`
- **Couche:** EditMode-pure
- **Given:** a snapshot where every non-fake seat is corrupted EXCEPT the last anomaly, and every anomaly except that one is chained.
- **When:** that last anomaly is set chained+corrupted (the exact effect of ChainCharacterServer) and both conditions are evaluated on the resulting snapshot.
- **Then:** WChosenChainedAllAnomaly==true AND WAnomalyCorruption==true — a single chain triggers a double-team win (see 287 for the no-priority consequence).
- **Stabilité:** pins the dangerous coupling where chaining doubles as corruption; catches a change to chain/corrupt semantics that would silently flip who wins.
- **Statut:** à implémenter

### 290 — A heal that un-corrupts one seat denies the anomaly win
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `WAnomalyCorruption.CheckCondition(snapshot)` returns false at the first non-fake non-corrupted seat — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:34-49`
- **Couche:** EditMode-pure
- **Given:** a snapshot where all non-fake seats are corrupted (anomaly win would hold).
- **When:** one seat is flipped to non-corrupted (the effect of a heal, `Character.HealPlayerServerRpc`) and `CheckCondition(snapshot)` re-runs.
- **Then:** it returns false — the anomaly win is correctly denied by the single un-corrupted seat.
- **Stabilité:** pins that healing the last-needed corruption cancels the anomaly victory; core of the heal-vs-corruption race outcome.
- **Statut:** à implémenter

### 291 — Victory is evaluated once, off a snapshot taken at VictoryConditionCheckState entry (after chain state completes)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `VictoryConditionCheckState.TryResolveVictoryNow` builds `GameSnapshotBuilder.FromLiveState` synchronously then evaluates; ChainingState only advances to it after all animations finish — `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:44-83` + `Assets/Scripts/GameLogic/GameStates/ChainingState.cs:53-77`
- **Couche:** PlayMode-StartHost
- **Given:** a StartHost loop seeded with a ChainingState holding several chaining players followed by a VictoryConditionCheckState.
- **When:** the loop runs ChainingState → VictoryConditionCheckState.
- **Then:** the win evaluation observes the fully-resolved chained set (all chaining players chained) — victory is not evaluated mid-chain; `chainingPlayers` is cleared and NextGameState fires before the check.
- **Stabilité:** pins "evaluate after the whole chain resolves, not during it" — a mid-chain evaluation could declare a premature/wrong winner.
- **Statut:** à implémenter

### 292 — TryResolveVictoryNow performs no transition when no team wins
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `VictoryConditionCheckState.TryResolveVictoryNow` returns false without touching game state when `_winningTeams.Count == 0` — `Assets/Scripts/GameLogic/GameStates/VictoryConditionCheckState.cs:71-83`
- **Couche:** PlayMode-StartHost
- **Given:** a StartHost roster where no winning condition holds (mixed corrupted/chained but not all), current state is some mid-loop state.
- **When:** `TryResolveVictoryNow()` is invoked out-of-band (the leave-path re-check).
- **Then:** it returns false and `currentGameStateIndex` is unchanged — no jump to GameEndingState.
- **Stabilité:** pins the out-of-band re-check as side-effect-free on a no-winner, so a mid-game leave can't corrupt loop flow.
- **Statut:** à implémenter

### 293 — Fake seats are excluded from every win condition
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** snapshot conditions skip `IsFake`; adapter also filters fakes at mapping source — `Assets/Scripts/Characters/WinningConditions/WAnomalyCorruption.cs:37-40` + `WChosenChainedAllAnomaly.cs:44-47` + `VictoryConditionCheckState.cs:59-69`
- **Couche:** EditMode-pure
- **Given:** a snapshot where the only non-corrupted / only unchained-anomaly seat is a fake (IsFake=true), all real seats satisfy the condition.
- **When:** WAnomalyCorruption and WChosenChainedAllAnomaly are evaluated.
- **Then:** both return true — the fake seat does not block either victory.
- **Stabilité:** pins fake-seat neutrality in victory math; a fakify/decoy regression must not gate a real win.
- **Statut:** déjà couvert (WinningConditionGoldenMasterTests A7 / C8)

### 294 — corrupt→heal→re-corrupt leaves a seat corrupted AND already-healed (terminal state)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** sequence over `CorruptPlayerServerRpc` (no guard) + `HealPlayerServerRpc` (isHealed one-shot guard) — `Assets/Scripts/Characters/Character.cs:181-196`
- **Couche:** PlayMode-StartHost
- **Given:** a fresh seat (isCorrupted=false, isHealed=false), StartHost.
- **When:** run in order: `CorruptPlayerServerRpc()`, `HealPlayerServerRpc()`, `CorruptPlayerServerRpc()`.
- **Then:** final state isCorrupted=true, isHealed=true — the state does NOT return clean; a subsequent `HealPlayerServerRpc()` is a no-op (271/270), so the seat is unsavable.
- **Stabilité:** pins the full multi-step corrupt/heal cycle so a change to either guard is caught at the sequence level, not just per-op.
- **Statut:** à implémenter

### 295 — A character with a still-usable non-passive power is NOT auto-slept after using another power (Mage auto-skip by design)
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `AwakeningState.OnPowerUsedServer` sleeps only when NO non-passive power `CanUse()`; otherwise returns without sleeping — `Assets/Scripts/GameLogic/GameStates/AwakeningState.cs:367-402`
- **Couche:** PlayMode-StartHost
- **Given:** an awakened seat holding two active powers, one already spent (powerUseLeft=0) and one still usable (the Mage-style chain, e.g. the Étreintes power still has uses), StartHost.
- **When:** `OnPowerUsedServer` is invoked for the just-spent power.
- **Then:** the character is NOT slept (`isAwakened` stays true) because a usable non-passive power remains — pinning the by-design behavior that the Mage keeps acting after one power.
- **Stabilité:** FREEZES the "Mage does not sleep after 2 powers = by design" ruling (per memory) so it is never mistaken for a bug and re-patched.
- **Statut:** à implémenter

### 296 — A copied (one-shot) Réincarnation grants only ACTIVE role powers; a real Incomplet grants all
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `PReincarnation.IGrantRolePowers.GrantRolePowers` skips passives when `isStolenCopy`, and never copies an already-copied power — `Assets/Scripts/Characters/Powers/PReincarnation.cs:22-48`
- **Couche:** PlayMode-StartHost
- **Given:** a target role carrying one active + one passive power; two Réincarnation carriers — one flagged `isStolenCopy=true` (copied), one not (real Incomplet), StartHost.
- **When:** each reincarnates into that role.
- **Then:** the copied carrier receives ONLY the active power (as a one-shot copy); the real Incomplet receives BOTH (permanent copies); neither re-copies an already-copied source.
- **Stabilité:** pins the "temporary copy must not mint permanent powers" rule at the interaction of two copiers (Ugues-copied Réincarnation vs real Incomplet).
- **Statut:** à implémenter

### 297 — Golden game A: chosen win by chaining the last anomaly over two turns
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** full loop AwakeningState → VoteState → ChainingState → VictoryConditionCheckState with `WChosenChainedAllAnomaly` — `Assets/Scripts/GameLogic/GameManager.cs:317-342` (NextGameState) + `VictoryConditionCheckState.cs:22-33` + `WChosenChainedAllAnomaly.cs:41-61`
- **Couche:** PlayMode-StartHost
- **Given:** 6 fixed seats — 3 chosen, 2 anomaly (A1,A2), 1 marginal; deterministic scripted loop, StartHost (server authority suffices; no per-client info asserted).
- **When:** turn 1 the vote chains A1; turn 2 the vote chains A2 (both via `chainingManager.AddCharacterToChainingList` → ChainingState); the loop reaches VictoryConditionCheckState.
- **Then:** final state is GameEndingState with winners = `WinningTeam.chosen`; both A1 and A2 have isChained=true.
- **Stabilité:** end-to-end regression net for the canonical chosen victory path across multiple turns — catches loop-ordering, vote-tally, chain, and win-eval regressions together.
- **Statut:** à implémenter

### 298 — Golden game B: anomaly win by corrupting the whole table
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** night corruption via `PCorruptingMark`/`ChainCharacterServer` isCorrupted + `WAnomalyCorruption` — `Assets/Scripts/Characters/Character.cs:181-206` + `WAnomalyCorruption.cs:34-49`
- **Couche:** PlayMode-StartHost
- **Given:** 6 fixed seats — 2 anomaly, 3 chosen, 1 marginal; all seats non-corrupted at start; StartHost.
- **When:** a deterministic scripted sequence corrupts every non-fake seat (server-side `CorruptPlayerServerRpc` per seat), then the loop reaches VictoryConditionCheckState.
- **Then:** final state is GameEndingState with winners = `WinningTeam.anomaly`; every non-fake seat isCorrupted=true.
- **Stabilité:** end-to-end net for the anomaly victory threshold — catches a corruption-count or vacuous-truth regression at full-table scale.
- **Statut:** à implémenter

### 299 — Golden game C: marginal wins by being chained via vote
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** `VoteState.OnEndStateServer` tally → `AddCharacterToChainingList` → chain → `WMarginalIsChainedWin` — `Assets/Scripts/GameLogic/GameStates/VoteState.cs:208-243` + `WMarginalIsChainedWin.cs:28-39`
- **Couche:** PlayMode-StartHost
- **Given:** 7 fixed seats — 1 marginal (M, whose win condition is "M is chained"), 3 chosen, 3 anomaly; StartHost.
- **When:** a deterministic vote makes M the most-voted seat, ChainingState chains M, loop reaches VictoryConditionCheckState.
- **Then:** final state is GameEndingState and `WinningTeam.marginal` is among the winners; M.isChained=true.
- **Stabilité:** end-to-end net for the marginal "get yourself chained" win — an unusual win whose interaction with vote-tally + chaining is easy to break.
- **Statut:** à implémenter

### 300 — Golden game D (2-NM): scripted night yields correct per-seat info asymmetry across two real clients
- **Catégorie:** I — Interactions & Golden Games
- **Mécanique:** per-observer reveal stores replicated to each real client — `Assets/Scripts/GameLogic/GameInfoRevealer.cs:106-140`,`:204-241` observed on the second NetworkManager's replica
- **Couche:** PlayMode-2NM
- **Given:** MultiClientGameFixture with host seat H and remote seat R (real clients); a scripted night where H's role is revealed Personal to R only, and R's corruption is revealed Personal to R only.
- **When:** each client reads its own replica `GetCharacterInfo` after the scripted reveals settle over the wire.
- **Then:** R's replica shows H.isRoleRevealed>=Personal; H's replica shows H's own role only (EnsureOwnRoleRevealed) and does NOT see the R-scoped reveal — the two seats end the turn with different, correct knowledge sets.
- **Stabilité:** the flagship cross-client info-asymmetry net — the one thing StartHost cannot prove — guarding that a full scripted turn never leaks a Personal reveal to the wrong client.
- **Statut:** à implémenter
