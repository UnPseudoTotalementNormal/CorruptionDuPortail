#region

using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using GameLogic;
using Network;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

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
                return false;
            }

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
            if (ConnectionApprovalGate.HasSynchronizingClients || !AllParticipantsReady() || !TryResolveValidComposition(out _))
            {
                return;
            }

            _started = true;
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

            // Symmetric counterpart of the lock below: re-entering the lobby makes the session joinable again, so
            // it must reappear in the lobby list.
            SetCloudLobbyLocked(false);
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            gameManager.NetworkManager.OnClientConnectedCallback -= OnClientConnected;
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
            if (!gameManager.NetworkManager.IsServer)
            {
                return;
            }

            var _lobbyManager = Network.Services.LobbyManager.instance;
            if (_lobbyManager == null || !_lobbyManager.IsInLobby || !_lobbyManager.IsLobbyHost)
            {
                return;
            }

            _ = _lobbyManager.SetLobbyLocked(_locked);
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
                TryAutoStart();
            }
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}