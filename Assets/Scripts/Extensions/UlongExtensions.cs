using Network;
using Network.Player;

namespace Extensions
{
    public static class UlongExtensions
    {
        public static bool IsFakeClientId(this ulong _clientId)
        {
            if (_clientId >= GameValues.FAKE_CLIENT_ID - GameValues.MAX_PLAYERS)
            {
                return true;
            }
            return false;
        }

        public static string GetPlayerName(this ulong _clientId)
        {
            if (_clientId.IsFakeClientId())
            {
                int _fakeIdIndex = (int)(GameValues.FAKE_CLIENT_ID - _clientId);
                return $"AI {_fakeIdIndex}";
            }

            PlayerInfo _playerInfo = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_clientId);
            
            return _playerInfo.playerName.ToString();
        }
    }
}