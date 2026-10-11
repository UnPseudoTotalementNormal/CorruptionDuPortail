using System.Collections.Generic;

namespace Network.Services
{
    /// <summary>
    /// A lobby as the menus see it, whatever the backend (UGS lobbies in the Relay build, Steam lobbies in the Steam
    /// build). A snapshot: refreshed by <see cref="LobbyManager"/>, never mutated by the UI.
    /// </summary>
    public sealed class GameLobby
    {
        /// <summary>Lobby data key of the Unity Relay join code (Relay build).</summary>
        public const string RelayJoinCodeKey = "joinCode";
        /// <summary>Lobby data key of the host's SteamID64 (Steam build).</summary>
        public const string HostSteamIdKey = "hostSteamId";

        public string Id = string.Empty;
        public string Name = string.Empty;
        /// <summary>Code typed in "Rejoindre avec un code".</summary>
        public string Code = string.Empty;
        /// <summary>Backend id of the lobby owner (UGS player id / SteamID64).</summary>
        public string HostId = string.Empty;
        public bool HasPassword;
        /// <summary>The game started: nobody new may join.</summary>
        public bool IsLocked;
        public int PlayerCount;
        public int MaxPlayers;
        public IReadOnlyDictionary<string, string> Data = new Dictionary<string, string>();

        public bool TryGetData(string key, out string value)
        {
            if (Data != null && Data.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            {
                return true;
            }
            value = null;
            return false;
        }
    }
}
