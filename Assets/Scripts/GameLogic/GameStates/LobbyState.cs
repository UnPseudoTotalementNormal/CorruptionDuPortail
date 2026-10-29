#region

using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic;
using Network;
using Network.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "LobbyState", menuName = "GameStates/LobbyState")]
    public class LobbyState : GameState
    {
        // feat/lobby-ready-system: the bot-aware census this state watches to auto-start once everyone is ready.
        private LobbyPlayerInfoHolder _lobbyInfo;

        private void OnClientConnected(ulong clientId)
        {
            AddNewCharacter(clientId);
        }

        private void AddNewCharacter(ulong clientId)
        {
            Command.AddNewCharacter(clientId);
        }

        // [LEAVE] Phase 1 (epic-player-leave-stability): the lobby-disconnect reaction (RemoveCharacter)
        // and its leaking OnClientDisconnectCallback subscription were REMOVED from here. The single
        // authoritative server pipeline GameManager.HandlePlayerLeft now owns the lobby-remove branch, so
        // exactly one code path reacts to a disconnect and the mid-game double-handling / subscription leak
        // (this state's subscription was never unsubscribed) is gone.

        // feat/lobby-ready-system: fires at most once per lobby entry (auto-start OR force-start latches it).
        private bool _started;

        // Playtest logs only: the last auto-start blocking reason logged (the poll runs every frame, logs on change).
        private string _lastBlockedReason;

        // Opening the cloud lobby once GameScene is loaded (see OpenCloudLobbyWhenSceneLoaded). The deadline is
        // realtime, -1 when not waiting; the attempt id cancels pending retries once the game starts.
        private const float OpenFallbackSeconds = 10f;
        private const int OpenRetryCount = 3;
        private const float OpenRetryDelaySeconds = 2f;
        private float _openDeadline = -1f;
        private int _openAttemptId;

        // The composition gate: coverage Σmax ≥ players, guaranteed-fit (subsumes the old Σforced ≤ players floor),
        // plus ≥1 anomaly / ≥1 élu — evaluated by the pure CompositionValidator through RoleAttributionState (the
        // same rule surface the tablet footer mirrors and RoleDistributor guarantees). Reads the replicated
        // max/forced via gameSettingsManager, faction from the authored pool. Returns false (no advance) if invalid.
        private bool TryResolveValidComposition(out CompositionValidation validation)
        {
            int _playerCount = CharacterQuery.GetCharacters().Count;
            var _roleState = (RoleAttributionState)gameManager.GetGameStates(typeof(RoleAttributionState)).First();
            validation = _roleState.ValidateComposition(_playerCount);
            return validation.IsValid;
        }

        // Server-authoritative compo-only start (dev force-start + the guard tests use it). Logs the reason when
        // refused. Returns true iff the loop advanced.
        public bool OnStartGameButtonPressed()
        {
            if (!TryResolveValidComposition(out CompositionValidation _validation))
            {
                Debug.LogWarning($"Cannot start the game — invalid composition: {_validation.FirstReason}");
                Diagnostics.Logs.GameLog.Info("lobby", "lobby.start.refused", "forced", true, "reason", "invalid-composition",
                    "detail", _validation.FirstReason);
                return false;
            }

            Diagnostics.Logs.GameLog.Info("lobby", "lobby.start", "players", CharacterQuery.GetCharacters().Count, "forced", true);
            Loop.NextGameState();
            return true;
        }

        // feat/lobby-ready-system: server-side auto-start poll (run from StateUpdateServer, NOT an event callback —
        // robust to holder spawn order, and free of the re-entrancy a NetworkList callback re-entering NextGameState
        // would cause). Starts once every game participant is ready AND the composition is valid. Composition is
        // checked SILENTLY here (this runs every frame) so an invalid pool never spams warnings.
        private void TryAutoStart()
        {
            if (_started || !gameManager.NetworkManager.IsServer)
            {
                return;
            }

            // Resolve the census lazily so a holder that spawns after this state entered still wires up.
            if (_lobbyInfo == null)
            {
                _lobbyInfo = CompositionRoot.For(gameManager.NetworkManager).LobbyPlayerInfoHolder;
                if (_lobbyInfo == null)
                {
                    return;
                }
            }

            // NET-05: an approved joiner still loading has no Character yet, so AllParticipantsReady cannot see it —
            // starting now would turn it into a character-less ghost. Wait until it finishes, leaves, or is kicked.
            // Same checks, same order and short-circuit as before; the reason only feeds the playtest log.
            CompositionValidation _validation = default;
            string _blockedReason = ConnectionApprovalGate.HasSynchronizingClients ? "player-loading"
                : !AllParticipantsReady() ? "not-all-ready"
                : !TryResolveValidComposition(out _validation) ? "invalid-composition"
                : null;
            if (_blockedReason != null)
            {
                if (_blockedReason != _lastBlockedReason)
                {
                    _lastBlockedReason = _blockedReason;
                    Diagnostics.Logs.GameLog.Info("lobby", "lobby.autostart.blocked", "reason", _blockedReason,
                        "players", CharacterQuery.GetCharacters().Count,
                        "detail", _blockedReason == "invalid-composition" ? _validation.FirstReason : null);
                }
                return;
            }

            _started = true;
            Diagnostics.Logs.GameLog.Info("lobby", "lobby.start", "players", CharacterQuery.GetCharacters().Count, "forced", false);
            Loop.NextGameState();
        }

        // Every game participant (a spawned Character) must have a ready census entry. Ties readiness to the ACTUAL
        // players who will receive roles (not the raw census): a just-connected player whose census entry is still
        // in flight blocks the start; a duplicated census row can't wedge it (GetPlayerInfo takes the first match —
        // the same one SetReadyServer flips); bots (no character) neither block nor need to be checked.
        private bool AllParticipantsReady()
        {
            var _characters = CharacterQuery.GetCharacters();
            if (_characters.Count == 0)
            {
                return false;
            }

            foreach (var _character in _characters)
            {
                if (_character == null)
                {
                    continue;
                }

                ulong _owner = _character.ownerClientId.Value;
                PlayerInfo _info = _lobbyInfo.GetPlayerInfo(_owner);
                if (_info.playerClientId != _owner || !_info.isReady)
                {
                    return false;
                }
            }
            return true;
        }

        // feat/lobby-ready-system: dev "Démarrage forcé" (Autres options tab) — skips the all-ready condition ONLY.
        // The composition gate still applies (OnStartGameButtonPressed re-validates it, and logs if refused). Host-only.
        public void ForceStart()
        {
            if (_started || !gameManager.NetworkManager.IsServer)
            {
                return;
            }

            // NET-05: even a forced start must not strand a joiner that is still loading.
            if (ConnectionApprovalGate.HasSynchronizingClients)
            {
                Debug.LogWarning("Cannot force-start: a player is still loading into the lobby.");
                Diagnostics.Logs.GameLog.Info("lobby", "lobby.start.refused", "forced", true, "reason", "player-loading");
                return;
            }

            if (OnStartGameButtonPressed())
            {
                _started = true;
            }
        }

        public override void OnStateCreated()
        { 
            base.OnStateCreated();
            
            if (!gameManager.NetworkManager.IsServer)
            {
                return;
            }

            foreach (var connectedClient in gameManager.NetworkManager.ConnectedClients)
            {
                AddNewCharacter(connectedClient.Key);
            }
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            gameManager.NetworkManager.OnClientConnectedCallback += OnClientConnected;

            // feat/lobby-ready-system: fresh lobby entry — the auto-start poll (StateUpdateServer) re-evaluates
            // every frame and lazily resolves the census, so no subscription to wire here.
            _started = false;
            _lobbyInfo = null;
            _lastBlockedReason = null;

            // The menu creates the cloud lobby locked: it only appears in the lobby list once the host has finished
            // loading GameScene. This state starts while NGO is still processing that load (in-scene objects spawn
            // mid-event), and a client joining before the event completes gets a broken scene sync ("Server Scene
            // Handle already exist", stuck). Open it when the load event completes.
            OpenCloudLobbyWhenSceneLoaded();
        }

        private void OpenCloudLobbyWhenSceneLoaded()
        {
            var _sceneManager = gameManager.NetworkManager.SceneManager;
            if (_sceneManager == null || !gameManager.NetworkManager.NetworkConfig.EnableSceneManagement)
            {
                OpenCloudLobby("no-scene-management");
                return;
            }

            _sceneManager.OnLoadEventCompleted -= OnSceneLoadEventCompleted;
            _sceneManager.OnLoadEventCompleted += OnSceneLoadEventCompleted;
            // NGO spawns the in-scene objects (this state starts) before it completes the load event, so the event
            // cannot be missed; the deadline only guards against a lobby that would otherwise stay hidden forever.
            _openDeadline = Time.realtimeSinceStartup + OpenFallbackSeconds;
            Diagnostics.Logs.GameLog.Info("lobby", "lobby.open.wait", "scene", gameManager.gameObject.scene.name);
        }

        private void OnSceneLoadEventCompleted(string _sceneName, LoadSceneMode _mode, List<ulong> _completed, List<ulong> _timedOut)
        {
            if (_sceneName != gameManager.gameObject.scene.name)
            {
                return;
            }

            OpenCloudLobby("scene-loaded");
        }

        // Polled from StateUpdateServer.
        private void OpenCloudLobbyIfLoadNeverCompleted()
        {
            if (_openDeadline < 0f || Time.realtimeSinceStartup < _openDeadline)
            {
                return;
            }

            Diagnostics.Logs.GameLog.Warn("lobby", "lobby.open.fallback", "afterSeconds", OpenFallbackSeconds);
            OpenCloudLobby("fallback");
        }

        private void StopWaitingForSceneLoad()
        {
            _openDeadline = -1f;
            var _sceneManager = gameManager.NetworkManager != null ? gameManager.NetworkManager.SceneManager : null;
            if (_sceneManager != null)
            {
                _sceneManager.OnLoadEventCompleted -= OnSceneLoadEventCompleted;
            }
        }

        private void OpenCloudLobby(string _trigger)
        {
            StopWaitingForSceneLoad();
            Diagnostics.Logs.GameLog.Info("lobby", "lobby.open", "trigger", _trigger);
            OpenCloudLobbyWithRetriesAsync(++_openAttemptId).Forget();
        }

        // Before this change a failed unlock left the lobby open; now the lobby starts locked, so a failed unlock
        // (UGS rate limit, network hiccup) would leave the game unjoinable: retry a few times.
        private async UniTaskVoid OpenCloudLobbyWithRetriesAsync(int _attemptId)
        {
            for (int _try = 1; _try <= OpenRetryCount; _try++)
            {
                if (_attemptId != _openAttemptId || gameManager == null || !gameManager.IsSpawned)
                {
                    // The game started (OnEndStateServer locked it again) or the session ended: never reopen.
                    Diagnostics.Logs.GameLog.Info("lobby", "lobby.open.cancelled", "try", _try);
                    return;
                }

                if (await SetCloudLobbyLockedAsync(false))
                {
                    return;
                }

                Diagnostics.Logs.GameLog.Warn("lobby", "lobby.open.retry", "try", _try, "of", OpenRetryCount);
                await UniTask.Delay(System.TimeSpan.FromSeconds(OpenRetryDelaySeconds), ignoreTimeScale: true);
            }

            Diagnostics.Logs.GameLog.Warn("lobby", "lobby.open.failed", "tries", OpenRetryCount);
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            gameManager.NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            StopWaitingForSceneLoad();
            _openAttemptId++;
            _lobbyInfo = null;

            // The game just started. Hide the session from the lobby list (investigation join-started-game-gate):
            // ConnectionApprovalGate already REJECTS a mid-game joiner, but a still-listed lobby invites a join
            // that can only fail. This is the exact same phase boundary the gate reads through
            // GameManager.IsInLobbyPhase — leaving LobbyState IS the start of the game.
            SetCloudLobbyLocked(true);
        }

        // Server-only, fire-and-forget: only the lobby host may update the cloud lobby, and both call sites run on
        // the server (SwitchGameState asserts IsServer). Null-safe — EditMode / bot flows have no LobbyManager.
        // No end-of-game counterpart is needed: GameManager.ShutOffGame already deletes the lobby outright.
        private void SetCloudLobbyLocked(bool _locked)
        {
            SetCloudLobbyLockedAsync(_locked).Forget();
        }

        // True when done or when there is no cloud lobby to update (EditMode, bots, a client).
        private UniTask<bool> SetCloudLobbyLockedAsync(bool _locked)
        {
            if (!gameManager.NetworkManager.IsServer)
            {
                return UniTask.FromResult(true);
            }

            var _lobbyManager = Network.Services.LobbyManager.instance;
            if (_lobbyManager == null || !_lobbyManager.IsInLobby || !_lobbyManager.IsLobbyHost)
            {
                return UniTask.FromResult(true);
            }

            return _lobbyManager.SetLobbyLocked(_locked);
        }
        
        public override void OnStartStateClient()
        {
            charactersBar.DestroyCharactersBar();
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();

            // feat/lobby-ready-system: poll the auto-start each server tick (latched to fire once).
            if (gameManager.NetworkManager.IsServer)
            {
                // NET-05: a loader stuck past the sync cap is disconnected so it cannot block the start forever.
                ConnectionApprovalGate.KickExpiredLoaders(gameManager.NetworkManager);
                OpenCloudLobbyIfLoadNeverCompleted();
                TryAutoStart();
            }
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}