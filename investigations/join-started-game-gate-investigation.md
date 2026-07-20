# Investigation: Join d'une partie déjà commencée — lobby toujours listé, kick après chargement de scène

## Hand-off Brief

1. **What happened.** Un client rejoint une partie déjà lancée : le lobby reste visible dans la liste (Confirmed — `LobbyManager.SetLobbyLocked` n'a **aucun appelant**), et le chemin de join par la liste charge `GameScene` sans attendre l'approbation NGO (Confirmed — `LobbySelectionPanel.cs:289`), donc le rejet serveur arrive après le chargement et se présente comme « Connexion à l'hôte perdue » (`ClientDisconnectHandler.cs:54`).
2. **Where the case stands.** Deux causes racines Confirmed, indépendantes ; le gate serveur (`ConnectionApprovalGate`) fonctionne correctement et n'est pas en cause.
3. **What's needed next.** Implémenter les deux correctifs : (A) verrouiller le lobby au démarrage de partie, (B) aligner `LobbySelectionPanel.JoinLobby` sur le chemin déjà correct de `MainMenu` (attente d'approbation + `DisconnectReason`). → `gds-quick-dev`.

## Case Info

| Field            | Value                                                                                 |
| ---------------- | ------------------------------------------------------------------------------------- |
| Ticket           | N/A (branche `claude/game-started-join-error-511284`)                                  |
| Date opened      | 2026-07-20                                                                             |
| Status           | Active                                                                                 |
| System           | Unity 6000.2.6f2 (build player 6000.5.0f1), NGO, Unity Relay/Lobby, Windows 11         |
| Evidence sources | Console Unity (session host + instance client), code source `Assets/Scripts/`          |

## Problem Statement

Un client ne peut pas rejoindre une game déjà commencée, mais le flow est cassé : la game commencée reste affichée
dans la liste des lobbies, le client lance le join, charge la scène de jeu, PUIS se fait kick avec « connexion à
l'host perdue ». Attendu : (1) une partie commencée n'apparaît plus dans la liste ; (2) si elle apparaît quand même
(liste non rafraîchie), le join est pré-shot côté client AVANT de charger la scène, avec le message « partie déjà
commencée ».

## Evidence Inventory

| Source                        | Status    | Notes                                                                     |
| ----------------------------- | --------- | ------------------------------------------------------------------------- |
| Console Unity (host)          | Available | `[JOIN-GATE] Rejected connection 1 — game already started.`                |
| Console Unity (instance)      | Available | `[LEAVE][PHASE3] OnClientStopped(wasHost=False): pureClient=True expected=False -> notify=True` |
| Code — approval gate          | Available | `Assets/Scripts/Network/ConnectionApprovalGate.cs`                         |
| Code — join par liste         | Available | `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs`                         |
| Code — join par code          | Available | `Assets/Scripts/UI/MainMenu.cs`                                            |
| Code — service lobby          | Available | `Assets/Scripts/Network/Services/LobbyManager.cs`                          |
| Horodatage précis des logs    | Missing   | Console MCP ne renvoie pas les timestamps ; ordre relatif seul exploitable |

## Investigation Backlog

| # | Path to Explore                                                                 | Priority | Status | Notes                                                        |
| - | ------------------------------------------------------------------------------- | -------- | ------ | ------------------------------------------------------------ |
| 1 | Localiser le gate serveur et vérifier son comportement                          | High     | Done   | Gate correct, rejette bien (Finding 1)                        |
| 2 | Vérifier pourquoi le lobby reste listé                                           | High     | Done   | `SetLobbyLocked` sans appelant (Finding 2)                    |
| 3 | Comparer les deux chemins de join (liste vs code)                                | High     | Done   | Divergence Confirmed (Finding 3)                              |
| 4 | Décider du point d'accrochage exact du verrouillage (start de partie serveur)    | Medium   | Open   | Candidat : transition hors `IsInLobbyPhase`, host uniquement  |
| 5 | Déverrouillage / suppression du lobby en fin de partie ou au retour au menu      | Medium   | Open   | Sinon lobby zombie verrouillé jusqu'à expiration du heartbeat |

## Timeline of Events

| Time | Event                                                                      | Source                        | Confidence |
| ---- | -------------------------------------------------------------------------- | ----------------------------- | ---------- |
| T0   | Host crée le lobby `abababa`, `isLocked = false`                            | Console + `MainMenu.cs:381`   | Confirmed  |
| T1   | Host démarre la partie (sortie de la phase lobby)                           | Déduit du gate qui rejette    | Deduced    |
| T2   | Client voit encore `abababa` dans la liste et clique Connecter              | Console `Lobby rejoint`       | Confirmed  |
| T3   | Client `StartClient()` puis charge `GameScene` immédiatement                | `LobbySelectionPanel.cs:289`  | Confirmed  |
| T4   | Serveur rejette l'approbation                                               | `[JOIN-GATE] Rejected…`       | Confirmed  |
| T5   | Client déjà dans `GameScene` → déconnexion → « Connexion à l'hôte perdue »  | `ClientDisconnectHandler.cs:54` | Confirmed |

## Confirmed Findings

### Finding 1: Le gate serveur fonctionne — il n'est pas la cause

**Evidence:** `Assets/Scripts/Network/ConnectionApprovalGate.cs:51-76`, log host `[JOIN-GATE] Rejected connection 1 — game already started.`

**Detail:** `ShouldApprove(_gameManagerPresent, _isInLobbyPhase)` renvoie `false` hors phase lobby, le `_response.Reason`
est bien rempli avec `GameInProgressReason = "La partie a déjà commencé."` (`ConnectionApprovalGate.cs:30`). Le rejet
est correct et porte déjà le bon message. Le problème est en aval, côté client.

### Finding 2: Aucun code n'appelle jamais `SetLobbyLocked`

**Evidence:** `Assets/Scripts/Network/Services/LobbyManager.cs:459` (définition) — grep sur tout `Assets/` : **une seule
occurrence**, la déclaration. Zéro appelant.

**Detail:** `LobbySelectionPanel.RefreshAsync` filtre déjà correctement (`LobbySelectionPanel.cs:172-175` :
`if (lobby.IsLocked) continue;`). Le filtre est donc opérationnel mais le flag n'est jamais posé : le lobby est créé
avec `_lobbySettings.isLocked = false` (`MainMenu.cs:381`) et reste `false` pour toute la durée de la partie. Objectif
(1) du ticket est un simple câblage manquant, pas un bug de filtrage.

### Finding 3: Le join par la liste charge la scène sans attendre l'approbation

**Evidence:** `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:281-290` — après `StartClient()`, enchaîne directement
`UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene")`.

**Detail:** `StartClient()` ne renvoie que « la tentative a démarré », jamais « la connexion est établie ». Ce chemin
n'a aucune attente ni lecture de `NetworkManager.DisconnectReason`.

### Finding 4: Le chemin de join par code fait déjà ce qu'il faut

**Evidence:** `Assets/Scripts/UI/MainMenu.cs:166-173` et `MainMenu.cs:272-283`.

**Detail:** `WaitForClientConnectedOrTimeout()` attend la connexion réelle (politique en deux phases,
approval 10 s / total 90 s) puis `BuildJoinFailureMessage` **priorise `NetworkManager.DisconnectReason`** — donc
affiche « La partie a déjà commencé. » — et ne charge `GameScene` qu'après succès. Ce chemin satisfait déjà
l'objectif (2). La régression est localisée au seul `LobbySelectionPanel`.

### Finding 5: Le message vu par le joueur vient du handler de déconnexion générique

**Evidence:** `Assets/Scripts/Network/ClientDisconnectHandler.cs:54` — `HostLostMessage = "Connexion à l'hôte perdue"`.

**Detail:** Le client étant déjà passé dans `GameScene`, la déconnexion consécutive au rejet est traitée comme une
perte d'hôte ordinaire. Le `DisconnectReason` renseigné par le serveur n'est jamais consulté sur ce chemin.

## Deduced Conclusions

### Deduction 1: Deux défauts indépendants, cumulés

**Based on:** Findings 2, 3, 4, 5.

**Reasoning:** Le flag `IsLocked` non posé fait apparaître le lobby (défaut A, couche service Lobby). Le join sans
attente charge la scène avant le verdict serveur (défaut B, couche UI/NGO). Corriger A seul laisserait la fenêtre de
course ouverte (liste rafraîchie toutes les 7 s — `LobbySelectionPanel.cs:32`). Corriger B seul laisserait des lobbies
injoignables affichés.

**Conclusion:** Les deux objectifs du ticket correspondent exactement aux deux défauts ; ils doivent être corrigés
ensemble.

### Deduction 2: Le correctif B est un portage, pas une conception

**Based on:** Findings 3 et 4.

**Reasoning:** `MainMenu` possède déjà la logique correcte (`WaitForClientConnectedOrTimeout`,
`BuildJoinFailureMessage`, `ConnectFailReason`, `ConnectHandshakePolicy`), privée et dupliquée en partie dans
`LobbySelectionPanel` (les deux ont leur propre `JoinWithFacepunch` / `JoinWithUnityRelay`).

**Conclusion:** Extraire cette logique d'attente dans un helper partagé et l'appeler depuis les deux panneaux, plutôt
que de la recopier une troisième fois.

## Hypothesized Paths

### Hypothesis 1: Un lobby verrouillé pourrait rester zombie après la partie

**Status:** Open

**Theory:** Si le verrouillage est posé au démarrage de la partie sans contrepartie en fin de partie / retour au menu,
le lobby reste verrouillé jusqu'à expiration du heartbeat.

**Supporting indicators:** Aucun appel `SetLobbyLocked(false)` n'existe ; `LeaveLobby` existe
(`LobbyManager.cs:298`) mais n'est pas systématiquement invoqué sur le chemin hôte fin-de-partie.

**Would confirm:** Après une partie terminée, un lobby verrouillé persiste dans le back-end Lobby.

**Would refute:** Le host appelle bien `LeaveLobby`/`DeleteLobby` sur le retour au menu — le lobby disparaît alors du
back-end, verrouillé ou non.

**Resolution:** —

## Missing Evidence

| Gap                                       | Impact                                                                | How to Obtain                                        |
| ----------------------------------------- | --------------------------------------------------------------------- | ---------------------------------------------------- |
| Timestamps des logs                       | Mesurer l'écart T3→T4 (durée de scène chargée pour rien)              | Activer les timestamps dans la console / log fichier  |
| Chemin exact de fin de partie côté hôte   | Trancher l'Hypothèse 1 (lobby zombie)                                 | Tracer les appels `LeaveLobby` sur le retour au menu  |

## Source Code Trace

| Element       | Detail                                                                                                     |
| ------------- | ---------------------------------------------------------------------------------------------------------- |
| Error origin  | `Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs:289` (`LoadScene` sans attente) ; `Assets/Scripts/Network/Services/LobbyManager.cs:459` (`SetLobbyLocked` sans appelant) |
| Trigger       | Clic « Connecter » sur une entrée de lobby dont la partie a déjà quitté la phase lobby                       |
| Condition     | `GameManager.IsInLobbyPhase == false` côté hôte + `lobby.IsLocked == false` côté back-end Lobby              |
| Related files | `Network/ConnectionApprovalGate.cs`, `Network/ClientDisconnectHandler.cs:54`, `UI/MainMenu.cs:166-283`, `GameLogic/GameManager.cs:396` |

## Conclusion

**Confidence:** High

Deux causes racines **Confirmed**, indépendantes et cumulatives :

- **A — Lobby jamais verrouillé.** `LobbyManager.SetLobbyLocked` existe mais n'est appelé nulle part. Le filtre
  `IsLocked` du panneau de liste (`LobbySelectionPanel.cs:172`) est donc inopérant en pratique.
- **B — Join par liste sans pré-shot.** `LobbySelectionPanel.JoinLobby` charge `GameScene` immédiatement après
  `StartClient()` (`:289`), sans attendre l'approbation NGO ni lire `NetworkManager.DisconnectReason`. Le rejet arrive
  après le chargement et se dégrade en « Connexion à l'hôte perdue » (`ClientDisconnectHandler.cs:54`).

Le gate serveur `ConnectionApprovalGate` est **hors de cause** : il rejette correctement et fournit déjà le bon
message (« La partie a déjà commencé. »). La prémisse utilisateur est validée sur les deux points.

## Recommended Next Steps

### Fix direction

**Mécanisme A — visibilité du lobby (objectif 1).** Appeler `LobbyManager.instance.SetLobbyLocked(true)` côté hôte au
moment où la partie quitte la phase lobby (aligné sur `GameManager.IsInLobbyPhase`, source de vérité déjà utilisée par
le gate — `GameManager.cs:396`). Hôte uniquement (le back-end Lobby refuse l'update d'un non-hôte). Prévoir la
contrepartie en fin de partie (Hypothèse 1).

**Mécanisme B — pré-shot du join (objectif 2).** Dans `LobbySelectionPanel.JoinLobby`, attendre la connexion réelle
avant `LoadScene`, et afficher `NetworkManager.DisconnectReason` en priorité. La logique existe déjà dans `MainMenu`
(`WaitForClientConnectedOrTimeout` + `BuildJoinFailureMessage`) : l'extraire dans un helper partagé
(`Network/` ou `UI/LobbyUI/`) et l'appeler des deux côtés — ne pas dupliquer. Sur échec : `Shutdown()` +
`LeaveLobby()` + réactivation UI, comme `MainMenu.cs:175-195`, sans jamais quitter le menu.

Non couvert par ces deux mécanismes mais utile : refuser le clic côté client quand l'entrée sélectionnée porte déjà
`IsLocked` (garde bon marché contre une liste rafraîchie entre-temps).

### Diagnostic

Aucun diagnostic supplémentaire requis pour confirmer les causes racines. Pour l'Hypothèse 1 : tracer un log
`[LOBBY-LOCK]` sur `SetLobbyLocked` et vérifier l'état du lobby après une partie terminée.

## Reproduction Plan

1. Build A (hôte) : créer un lobby, ajouter un bot simulé, démarrer la partie.
2. Build B (client) : ouvrir le panneau de liste des lobbies **sans le rafraîchir** après le démarrage.
3. Sélectionner le lobby, cliquer Connecter.
4. Observé (bug) : chargement, entrée dans `GameScene`, puis « Connexion à l'hôte perdue » ; côté hôte
   `[JOIN-GATE] Rejected connection N — game already started.`
5. Attendu après correctif : le lobby disparaît de la liste au rafraîchissement suivant (≤ 7 s) ; sur une liste
   périmée, le clic produit « La partie a déjà commencé. » **sans** chargement de `GameScene`.

Tests unitaires possibles sans build : `ConnectionApprovalGateTests` couvre déjà la décision pure ; ajouter une
couverture EditMode sur le helper d'attente extrait (mécanisme B), sur le modèle de
`Assets/Scripts/Tests/Editor/ConnectionApprovalGateTests.cs`.

## Side Findings

- **Duplication de code de join.** `JoinWithFacepunch` / `JoinWithUnityRelay` sont dupliqués entre `MainMenu.cs:286-347`
  et `LobbySelectionPanel.cs:300-368`, avec des comportements divergents (l'un attend la connexion, l'autre non). C'est
  la cause structurelle de cette régression. (Confirmed)
- **Filtrage de liste incomplet.** `RefreshAsync` filtre `IsLocked` mais pas `AvailableSlots == 0` — un lobby plein
  reste cliquable. (Confirmed, `LobbySelectionPanel.cs:170-187`)
- **Bruit console non lié (à traiter séparément).** `The referenced script on this Behaviour (Game Object 'LobbySelectionPanel') is missing!`
  et des erreurs DOTween `Target or field is missing/null` (tween uGUI sur objet détruit pendant le changement de
  scène) apparaissent dans la même session. Hors périmètre, mais le script manquant sur `LobbySelectionPanel` mérite
  une vérification. (Confirmed)

## Follow-up: 2026-07-20

### Additional Findings

#### Finding 6: Une troisième cause, révélée pendant l'implémentation — course sur le popup

**Evidence:** `Assets/Scripts/Network/ClientDisconnectHandler.cs:214-260`, log console
`[LEAVE][PHASE3] OnClientStopped(wasHost=False): pureClient=True expected=False -> notify=True`.

**Detail:** Corriger le seul chargement de scène (Finding 3) n'aurait **pas** suffi pour l'objectif (2). Un join
rejeté arrête NGO exactement comme une perte d'hôte : `OnClientStopped` tire **avant** que le code menu en attente
n'observe `!IsListening` à sa prochaine frame. `HostDropPolicy.ShouldNotifyHostLoss(pureClient=true,
expected=false)` renvoyait donc `true` et le popup générique « Connexion à l'hôte perdue » gagnait la course contre
le message réel. La ligne de log ci-dessus, présente dès la session d'origine, est la trace directe de cette course
— elle avait d'abord été lue comme une simple conséquence du chargement de scène.

**Correctif:** paramètre optionnel `joinHandshakeInProgress` sur la décision pure `HostDropPolicy`, piloté par un
one-shot `ClientDisconnectHandler.SetJoinHandshakeInProgress(bool)` que `JoinHandshake` ouvre avant l'attente et
referme en `finally` — donc jamais latché après un join réussi (une vraie perte d'hôte ultérieure reste notifiée).

### Updated Hypotheses

**Hypothesis 1 (lobby zombie verrouillé) — Status : Refuted.**
`GameManager.ShutOffGame` appelle déjà `LobbyManager.LeaveOrDeleteLobby()` (`GameManager.cs:592`), et
`LobbyManager.OnApplicationQuit` fait de même (`LobbyManager.cs:65`) : l'hôte **supprime** le lobby en fin de
partie, verrouillé ou non. Aucune contrepartie fin-de-partie n'est nécessaire. Un déverrouillage symétrique a
malgré tout été posé sur `LobbyState.OnStartStateServer` pour le cas d'un retour en phase lobby (que
`_started = false` anticipe déjà).

### Backlog Changes

- #4 (point d'accrochage du verrouillage) → **Done** : `LobbyState.OnEndStateServer` / `OnStartStateServer` —
  server-only par contrat (`SwitchGameState` asserte `IsServer`), exactement la frontière que lit
  `GameManager.IsInLobbyPhase`.
- #5 (déverrouillage fin de partie) → **Done / sans objet** : voir Hypothesis 1 réfutée.

### Updated Conclusion

Trois causes racines, pas deux. Confiance **High** inchangée sur A et B ; Finding 6 est confirmé par le code et par
la ligne de log d'origine. Correctifs implémentés — spec :
`_bmad-output/implementation-artifacts/spec-join-started-game-gate.md`. Compilation et tests NON exécutés
(worktree sans Unity MCP).
