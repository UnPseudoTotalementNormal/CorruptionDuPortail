---
status: ready-for-dev
---

# Spec: Pre-shot du join sur une partie déjà commencée

## Contexte

Case file : [join-started-game-gate-investigation.md](../../investigations/join-started-game-gate-investigation.md)

Deux causes racines confirmées, cumulatives :

- **A** — `LobbyManager.SetLobbyLocked` (`Assets/Scripts/Network/Services/LobbyManager.cs:459`) n'a aucun appelant, donc
  le filtre `IsLocked` de la liste (`LobbySelectionPanel.cs:172`) est inopérant : une partie commencée reste listée.
- **B** — `LobbySelectionPanel.JoinLobby` (`:289`) charge `GameScene` juste après `StartClient()`, sans attendre le
  verdict d'approbation NGO. Le rejet arrive après le chargement et se dégrade en « Connexion à l'hôte perdue »
  (`ClientDisconnectHandler.cs:54`) au lieu du `DisconnectReason` serveur « La partie a déjà commencé. »
  (`ConnectionApprovalGate.cs:30`). `MainMenu` (join par code) possède déjà la bonne logique — elle doit être
  partagée, pas dupliquée une troisième fois.

Le gate serveur `ConnectionApprovalGate` est correct et n'est pas modifié.

## Acceptance Criteria

**AC1 — Le lobby disparaît de la liste au démarrage**
Given un hôte dans le lobby avec une partie non démarrée
When la boucle de jeu quitte `LobbyState` (auto-start ou démarrage forcé)
Then `LobbyManager.SetLobbyLocked(true)` est appelé côté serveur uniquement
And au rafraîchissement suivant (≤ 7 s) le lobby n'apparaît plus dans `LobbySelectionPanel`.

**AC2 — Retour au lobby = redevient visible**
Given une partie dont la boucle revient dans `LobbyState`
When `LobbyState.OnStartStateServer` s'exécute
Then `SetLobbyLocked(false)` est appelé, le lobby redevient listable.

**AC3 — Join pré-shot, pas de chargement de scène**
Given une partie commencée encore affichée dans la liste (liste périmée)
When le joueur clique Connecter
Then le client attend le verdict d'approbation NGO **avant** tout `LoadScene`
And `GameScene` n'est jamais chargée
And le joueur reste sur le menu avec l'UI réactivée.

**AC4 — Message correct**
Given le join est rejeté par `ConnectionApprovalGate`
When l'échec est présenté au joueur
Then le message affiché est « La partie a déjà commencé. » (le `NetworkManager.DisconnectReason`)
And jamais « Connexion à l'hôte perdue ».

**AC5 — Même comportement sur les deux chemins du menu**
Given l'un des deux chemins de join (par code dans `MainMenu`, par la liste dans `LobbySelectionPanel`)
When le join échoue pour quelque raison que ce soit
Then les deux chemins passent par le même helper d'attente et la même construction de message
And les deux exécutent `Shutdown()` + `LeaveLobby()` + réactivation UI.

**AC6 — Garde bon marché sur l'entrée verrouillée**
Given une entrée de liste dont le `Lobby.IsLocked` est vrai (sélectionnée avant un refresh)
When le joueur clique Connecter
Then le join est refusé immédiatement avec le message « La partie a déjà commencé. », sans appel réseau.

**AC7 — Non-régression**
Given un join normal sur un lobby en phase lobby
When le joueur rejoint
Then le comportement est inchangé (chargement de `GameScene` après connexion établie).

## Tasks

1. **`Assets/Scripts/Network/Services/LobbyManager.cs`** — ajouter `public void ReportError(string _message)` qui
   invoque `OnLobbyError` (l'événement n'est pas invocable de l'extérieur). C'est le canal d'affichage : il est déjà
   branché sur la notification DDoL (`ClientDisconnectHandler.OnLobbyError` → `ShowNotification`).

2. **`Assets/Scripts/Domain/JoinFailureMessage.cs`** (nouveau) — seam pur, sans moteur, sur le modèle de
   `ConnectHandshakePolicy` :
   `public static string Build(string _disconnectReason, ConnectFailReason _reason)`.
   Priorité au `_disconnectReason` non vide ; sinon `TotalTimeout` → « Connexion échouée — la partie a mis trop de
   temps à charger. », défaut → « Connexion expirée — l'hôte n'a pas répondu. » (wording actuel de
   `MainMenu.BuildJoinFailureMessage`, conservé).

3. **`Assets/Scripts/Network/JoinHandshake.cs`** (nouveau) — helper partagé. Déplacer ici, verbatim, le corps de
   `MainMenu.WaitForClientConnectedOrTimeout` :
   `public static async UniTask<ConnectFailReason> WaitForConnectedOrTimeout(float _approvalSeconds, float _totalSeconds)`
   + `public static string BuildFailureMessage(ConnectFailReason _reason)` qui lit
   `NetworkManager.Singleton.DisconnectReason` et délègue à `JoinFailureMessage.Build`.
   Exposer les deux constantes de délai (`ApprovalTimeoutSeconds = 10f`, `SyncTotalTimeoutSeconds = 90f`).
   UniTask uniquement.

4. **`Assets/Scripts/UI/MainMenu.cs`** — supprimer `WaitForClientConnectedOrTimeout` et `BuildJoinFailureMessage`
   (et les deux constantes) ; appeler `JoinHandshake`. Sur échec, appeler `LobbyManager.instance.ReportError(...)`
   avant le teardown pour que le joueur voie enfin le message (aujourd'hui il n'est que loggé).

5. **`Assets/Scripts/UI/LobbyUI/LobbySelectionPanel.cs`** — le correctif principal :
   - `JoinLobby` : après `StartClient()`, `await JoinHandshake.WaitForConnectedOrTimeout(...)` **avant**
     `SceneManager.LoadScene("GameScene")`. Sur échec : `ReportError(JoinHandshake.BuildFailureMessage(...))`,
     `NetworkManager.Singleton.Shutdown()` (si `IsListening`), `await LobbyManager.instance.LeaveLobby()`,
     masquer le loading, `return false`. Ne jamais quitter le menu.
   - `OnConnectButtonClicked` / `JoinLobby` : refuser d'entrée si `lobby.IsLocked` (AC6), avec
     `ConnectionApprovalGate.GameInProgressReason` comme message — pas de littéral dupliqué.
   - Convertir `JoinLobby` et `JoinWithUnityRelay` de `Task` vers `UniTask` (règle projet), et ajuster les deux
     appelants (`OnSubmitPasswordButtonClickedAsync`, `OnConnectButtonClicked`).

6. **`Assets/Scripts/GameLogic/GameStates/LobbyState.cs`** — verrouillage :
   - `OnEndStateServer()` → `SetLobbyLocked(true)` (fire-and-forget `_ =`, null-safe sur `LobbyManager.instance`).
   - `OnStartStateServer()` → `SetLobbyLocked(false)` (AC2).
   Ces deux hooks sont server-only par contrat (`SwitchGameState` asserte `IsServer`) et constituent exactement la
   même frontière que `GameManager.IsInLobbyPhase`, consommée par `ConnectionApprovalGate`. Pas de contrepartie
   fin-de-partie nécessaire : `GameManager.ShutOffGame` supprime déjà le lobby via `LeaveOrDeleteLobby()`
   (`GameManager.cs:592`).

7. **`Assets/Scripts/Network/ClientDisconnectHandler.cs`** — *découvert pendant l'implémentation, indispensable à AC4* :
   un join rejeté arrête NGO exactement comme une perte d'hôte, et `OnClientStopped` tire **avant** que le code menu
   en attente ne réagisse — le popup générique gagnait la course. Ajouter un paramètre optionnel
   `joinHandshakeInProgress` à `HostDropPolicy.ShouldNotifyHostLoss` (les 4 tests existants compilent inchangés) et
   un one-shot `SetJoinHandshakeInProgress(bool)` que `JoinHandshake` ouvre avant l'attente et referme en `finally`
   (donc jamais latché après un join réussi). Étendre `HostDropPolicyTests` des deux cas.

8. **`Assets/Scripts/Tests/Editor/JoinFailureMessageTests.cs`** (nouveau) — EditMode pur, sur le modèle de
   `ConnectionApprovalGateTests` : `DisconnectReason` non vide gagne sur toutes les raisons ; `null`/vide →
   fallback par raison ; `TotalTimeout` et défaut distincts.

## Design Notes

- **Pas de nouveau canal d'UI.** L'affichage passe par `OnLobbyError` → `ClientDisconnectHandler.ShowNotification`,
  déjà DDoL et déjà branché. Aucun prefab, aucune scène touchée — cohérent avec la contrainte worktree (Edit/Write
  seulement, pas de Unity MCP).
- **`ConnectionApprovalGate` inchangé.** Il produit déjà le bon `DisconnectReason`.
- **Ordre critique** : construire le message **avant** `Shutdown()` — NGO peut effacer `DisconnectReason` au
  shutdown.
- **Duplication restante hors périmètre** : `JoinWithFacepunch`/`JoinWithUnityRelay` restent dupliqués entre les deux
  fichiers (transport, pas handshake). Noté comme dette dans le case file, pas traité ici.

## Verification

Compilation et tests différés : Unity MCP indisponible dans le worktree. À l'ouverture dans Unity —
`read_console` pour les erreurs de compilation, puis `run_tests` (EditMode, catégories `ConnectionApprovalGate` /
nouveau `JoinFailureMessage`). Playtest 2 builds (Poyo) selon le Reproduction Plan du case file.
