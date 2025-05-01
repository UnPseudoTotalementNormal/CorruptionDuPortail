using System.Linq;
using Characters;
using Unity.Netcode;
using UnityEngine;

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
            Character _newCharacter = new()
            {
                ownerClientId = clientId
            };
            gameManager.GetCharacters().Add(_newCharacter);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Character clientCharacter = gameManager.GetCharacters().FirstOrDefault(character => character.ownerClientId == clientId);
            if (clientCharacter == null)
            {
                return;
            }
            
            gameManager.GetCharacters().Remove(clientCharacter);
        }
        
        public void OnStartGameButtonPressed()
        {
            gameManager.NextGameState();
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
            
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }
            
            foreach (var connectedClient in NetworkManager.Singleton.ConnectedClients)
            {
                AddNewCharacter(connectedClient.Key);
            }
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
            gameManager.charactersBar.DestroyCharactersBar();
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