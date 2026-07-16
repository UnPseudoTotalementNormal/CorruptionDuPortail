#region

using System.Linq;
using Characters;
using CorruptionDuPortail.Domain;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "LobbyState", menuName = "GameStates/LobbyState")]
    public class LobbyState : GameState
    {
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

        public void OnStartGameButtonPressed()
        {
            int _playerCount = CharacterQuery.GetCharacters().Count;

            // The start gate is now the composition rule set (coverage Σmax ≥ players, guaranteed-fit — which
            // subsumes the old Σforced ≤ players floor — plus ≥1 anomaly / ≥1 élu). It is evaluated by the pure
            // CompositionValidator through RoleAttributionState (same rule surface the tablet footer mirrors and
            // RoleDistributor guarantees at distribution). Server-authoritative: host-only callers, and it reads
            // the replicated max/forced through gameSettingsManager, faction from the authored pool.
            var _roleState = (RoleAttributionState)gameManager.GetGameStates(typeof(RoleAttributionState)).First();
            CompositionValidation _validation = _roleState.ValidateComposition(_playerCount);
            if (!_validation.IsValid)
            {
                Debug.LogWarning($"Cannot start the game — invalid composition: {_validation.FirstReason}");
                return;
            }

            Loop.NextGameState();
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
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            gameManager.NetworkManager.OnClientConnectedCallback -= OnClientConnected;
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
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}