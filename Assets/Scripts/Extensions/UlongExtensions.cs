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

            // NET-03: same display rule as every name surface ("Joueur ?" when missing, "(parti)" marker after a leave).
            LobbyPlayerInfoHolder _holder = LobbyPlayerInfoHolder.instance;
            return _holder != null ? _holder.GetDisplayPseudo(_clientId) : CorruptionDuPortail.Domain.PseudoDisplay.MissingLabel;
        }
    }
}