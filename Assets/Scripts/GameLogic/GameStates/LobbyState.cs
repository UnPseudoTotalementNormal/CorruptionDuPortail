#region

using System.Linq;
using Characters;
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

        private void OnClientDisconnected(ulong clientId)
        {
            Command.RemoveCharacter(clientId);
        }
        
        public void OnStartGameButtonPressed()
        {
            int _playerCount = CharacterQuery.GetCharacters().Count;

            // Quick-dev gamesettings-refonte (2026-06-20): the total comes from the replicated, server-
            // authoritative gameSettingsManager (pushed lane-B), honouring the host's lobby edits. Fall back
            // to the authored RoleAttributionState dictionary only when no manager is wired (standalone test).
            int _totalRolesToAttribute = gameSettingsManager != null
                ? gameSettingsManager.GetTotalRolesToAttribute()
                : ((RoleAttributionState)gameManager.GetGameStates(typeof(RoleAttributionState)).First())
                    .roleAttributionDictionary.Values.Sum(_setting => _setting.roleToAttribute);

            if (_playerCount > _totalRolesToAttribute)
            {
                Debug.LogWarning("Not enough roles to attribute to all players!");
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
            gameManager.NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
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