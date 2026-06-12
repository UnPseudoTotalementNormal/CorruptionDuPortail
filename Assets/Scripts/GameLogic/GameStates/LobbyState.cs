#region

using System.Linq;
using AYellowpaper.SerializedCollections;
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
            characterManager.AddNewCharacter(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            characterManager.RemoveCharacter(clientId);
        }
        
        public void OnStartGameButtonPressed()
        {
            SerializedDictionary<RoleDataObject, RoleAttributionSetting> _roleAttributionDictionary = 
                ((RoleAttributionState)gameManager.GetGameStates(typeof(RoleAttributionState))
                .First()).roleAttributionDictionary;
            int _playerCount = characterManager.GetCharacters().Count;

            int _totalRolesToAttribute = _roleAttributionDictionary.Values.Sum(_setting => _setting.roleToAttribute);
            
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