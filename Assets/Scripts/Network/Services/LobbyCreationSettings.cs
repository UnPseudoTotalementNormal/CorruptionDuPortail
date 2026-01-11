using System.Collections.Generic;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Network.Services
{
    public class LobbyCreationSettings
    {
        private CreateLobbyOptions options = new();
        public string lobbyName = "New Lobby";
        public int maxPlayers = 30;
        
        public bool? isPrivate
        {
            get => options.IsPrivate;
            set => options.IsPrivate = value;
        }
        
        public bool? isLocked
        {
            get => options.IsLocked;
            set => options.IsLocked = value;
        }
        
        public string password
        {
            get => options.Password;
            set => options.Password = value;
        }
        
        public Unity.Services.Lobbies.Models.Player player
        {
            get => options.Player;
            set => options.Player = value;
        }
        
        public Dictionary<string, DataObject> data
        {
            get => options.Data;
            set => options.Data = value;
        }
        
        public CreateLobbyOptions Options => options;
        
        public enum LobbyCustomDataKeys
        {
            Language
        }
    }
}