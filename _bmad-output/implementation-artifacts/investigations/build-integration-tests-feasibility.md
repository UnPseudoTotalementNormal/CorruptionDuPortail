# Investigation — Faisabilité : tests d'intégration "build réel" (connexion / lancer une game / pouvoirs entre joueurs)

## Hand-off Brief

Poyo veut des tests automatisés qui lancent des builds pour tester la vraie connexion, démarrer une game
et simuler des pouvoirs interagissant. **Verdict : entièrement faisable et CI-viable — en partie déjà construit.**
Le harnais `MultiClientGameFixture` fait tourner un **vrai** host + un **vrai** client distant in-process sur
transport UnityTransport loopback (vraie sérialisation, vrais ticks, vraie réplication) ; on peut l'étendre à N
clients et à des flux de pouvoirs complets.

**MISE À JOUR (Poyo, 2026-07-17) :** Steam pas utilisé pour l'instant, et même plus tard les tests resteront sur
**Unity Transport** (Relay en prod, loopback en test) — jamais Facepunch. → l'obstacle "Steam/build-.exe-en-CI"
que citait la v1 de cette investigation **tombe** : le transport de test est représentatif du transport de prod,
donc plus de tier "impossible". Tout est Tier 1.

- **Statut :** Concluded
- **Confiance :** High (harnais Confirmé en place, transport prod Confirmé non couvert)
- **Type :** area-exploration / feasibility

## Case Info

- Slug : `build-integration-tests-feasibility`
- Date : 2026-07-17
- Entrée : question de faisabilité (free-text), pas un bug

## Problem Statement (verbatim)

> "j'aimerais bien pouvoir avoir des test unitaire qui font des build pour vraiment tester la connexion, lancer une
> game, simuler des pouvoirs entre eux, etc... tu pense que tu pourrais faire ça ? ou c'est impossible/trop compliqué"

Reformulé : tests d'intégration end-to-end couvrant (a) connexion réseau réelle, (b) démarrage de partie,
(c) interaction de pouvoirs entre plusieurs joueurs.

## Confirmed Findings

1. **Harnais multi-client réel DÉJÀ en place.** `Assets/Scripts/Tests/PlayMode/Desingleton/MultiClientGameFixture.cs`
   boote un host + un client distant réel via **deux `NetworkManager` en un process** sur `UnityTransport` loopback
   (`127.0.0.1:7788`). Vraie sérialisation, vrai tick, vraie réplication host→client :
   `MultiClientGameFixture.cs:16-48`, config transport `:440-453`, `StartHost`/`StartClient` `:201,222`.
   → C'est de la **vraie** intégration réseau, pas des mocks. La distinction "StartHost seul (RTT=0, in-process,
   aucune sérialisation)" vs "vrai 2e client qui regarde la donnée traverser le fil" est explicitée `:23-30`.
2. **Réplication d'état gameplay déjà testée sur le fil.** Trace ordonnée de `currentGameStateIndex.OnValueChanged`
   observée SUR LE CLIENT distant (`:108-112, 234-236`) ; spawn de vrais `Character` server-owned pour le client
   (`:365-387`) ; pont Liveness répliqué (`:459-473`). → "lancer une game" + transitions d'état = déjà couvrable.
3. **Pouvoirs = POCO purs, déjà testés unitairement.** `PowerTests.cs`, `EntrapmentPowerTests`, `VisionPowerTests`,
   `CorruptionTests`, `OwnerLocalEffectBoundaryTests`, la v2 `IPowerDecision` (mémoire
   `project_powers_poco_v2_complete`). → l'interaction de pouvoirs se teste au niveau logique SANS build ; le
   harnais 2-NM peut en plus valider l'effet côté client distant (cf. régression CursedVision/Embrace corrigée par
   `OwnerLocalEffectBoundaryTests`).
4. **Transport de test = Unity Transport, aligné sur la prod.** Le commentaire `MultiClientGameFixture.cs:42-43`
   ("Production transport is Facepunch (Steam)...") est désormais partiellement périmé côté stratégie de test :
   Poyo confirme (2026-07-17) que Steam n'est pas utilisé actuellement et que même à terme les tests resteront sur
   **Unity Transport Relay** (loopback en test). Le harnais loopback est donc **représentatif** du transport de
   prod — aucune couche transport n'échappe aux tests. (Nuance : Relay ajoute un relais cloud + auth UGS ; le
   loopback ne le simule pas, mais c'est de l'infra tierce Unity, pas de la logique projet.)
5. **CI test-runner DÉSACTIVÉ.** `.github/workflows/unity-tests.yml:20` = `if: false`. game-ci `unity-test-runner`
   configuré (`testMode: all`) mais jamais exécuté. Build (`Build.yml`) tourne sur `Main`, StandaloneWindows64,
   game-ci Docker Linux headless.
6. **Contraintes connues (mémoire projet).** `feedback_no_playtest_by_claude` : flake de bind port 7777 tue la
   suite PlayMode ; Claude ne lance pas de builds/playtests (job de Poyo). `project_vfx_graph_ci_crash` : le Docker
   game-ci a déjà crashé (crunch). `reference_isowner_unreliable_multi_nm_tests` : en 2-NM loopback, piloter les
   assertions owner depuis le HOST.

## Deduced Conclusions

- **"tester la connexion" + "lancer une game" + "pouvoirs entre joueurs" = faisable AUJOURD'HUI** en étendant
  `MultiClientGameFixture` (N clients, vrai `SwitchGameState`, vraie distribution de rôles, séquences de pouvoirs).
  C'est de l'intégration réseau réelle au niveau NGO — la couche où vivent les bugs d'approval/handshake déjà
  investigués (`ConnectionApprovalGate`, specs join-*). Coût : moyen, incrémental, pas de build.
- **"font des build" au sens .exe lancé + connexion Steam automatisée = le seul vrai obstacle.** Facepunch exige un
  client Steam vivant + app id + comptes ; les runners CI sont Linux headless Docker sans Steam. Non viable en CI ;
  faisable seulement en manuel local (2 process, 2 comptes Steam) — c-à-d le playtest, pas un test auto.
- **La couche Steam-transport n'est pas là où sont les bugs.** Les régressions passées (approval symétrique,
  join-load-timeout, replication skew) vivent au niveau NGO/logique, couvert par le loopback. Steam n'ajoute que
  relay/NAT — peu de logique projet, beaucoup d'infra tierce.

## Fix direction

Le transport de test étant Unity Transport (jamais Steam), il n'y a **qu'un seul tier** — celui qui est faisable et
CI-viable. Les tiers "manuel Steam" / "impossible" de la v1 sont retirés (sans objet).

| Quoi | Couvre | Coût | CI ? |
|---|---|---|---|
| Étendre `MultiClientGameFixture` : N clients, `SwitchGameState` réel, distribution rôles, séquences de pouvoirs bout-à-bout observées côté client | connexion NGO, lancer game, pouvoirs entre joueurs, replication skew | Moyen, incrémental | Oui (une fois `if:false` levé) |

**Le point-clé pour Poyo :** tout ce qu'il décrit ("connexion / game / pouvoirs") tient dans ce seul chantier, et
une partie roule déjà. Le mot "build" n'implique aucun .exe : le vrai host+client tourne in-process.

## Backlog (data-collection / next steps)

- [ ] Ré-activer le CI test-runner (`unity-tests.yml:20` `if:false`→condition réelle) — sinon les tests ne gardent
      rien en CI. Bloqueur connu : stabilité du Docker game-ci (crash crunch déjà vu) + flake bind de port.
- [ ] Décider le scope Tier 1 : quels flux de pouvoirs prioriser (les plus multi-joueurs : Corruption, Vision,
      Étreintes, Entrapment ont déjà des tests logiques à promouvoir en 2-NM).
- [x] ~~Confirmer si la couche Steam doit être couverte~~ → Poyo (2026-07-17) : non, tests toujours sur Unity
      Transport. Résolu.

## Status

Concluded — faisabilité tranchée. Implémentation = décision de scope (Tier 1) à valider avec Poyo.
