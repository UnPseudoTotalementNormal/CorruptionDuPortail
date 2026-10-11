using System.Collections.Generic;

namespace Network.Services
{
    /// <summary>What the host asks for when creating a lobby; each backend translates it (UGS options, Steam lobby data).</summary>
    public class LobbyCreationSettings
    {
        public string lobbyName = "New Lobby";
        public int maxPlayers = 30;
        public bool isPrivate;
        public bool isLocked;
        /// <summary>Null or empty = no password.</summary>
        public string password;
        /// <summary>Public lobby data (language, connection data of the host…).</summary>
        public Dictionary<string, string> data = new();

        public enum LobbyCustomDataKeys
        {
            Language
        }
    }
}
