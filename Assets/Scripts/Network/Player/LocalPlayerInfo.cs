#region

using Steamworks;
using UnityEngine;

#endregion

namespace Network.Player
{
    public static class LocalPlayerInfoHolder
    {
        public static PlayerInfo playerInfo { get; set; } = new()
        {
            playerName = "Player" + UnityEngine.Random.Range(1, 9999),
        };
        
        public static void CreateNewClientData(string _playerName)
        {
            string _shortName = _playerName;
            int _hashIndex = _playerName.IndexOf('#');
            if (_hashIndex >= 0)
            {
                _shortName = _playerName.Substring(0, _hashIndex);
            }
            
            playerInfo = new PlayerInfo()
            {
                playerFullName =  _playerName,
                playerName = _shortName,
                //playerSteamId = SteamClient.SteamId.Value
            };
        }
        
        public static PlayerInfo GetClientData()
        {
            return playerInfo;
        }
    }
}