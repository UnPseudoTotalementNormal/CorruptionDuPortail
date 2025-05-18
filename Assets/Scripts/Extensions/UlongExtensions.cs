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
    }
}