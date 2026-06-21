#region

using UnityEngine;

#endregion

namespace Network.Player
{
    public static class LocalPlayerInfoHolder
    {
        public static PlayerInfo playerInfo { get; set; } = BuildDefault();

        // The local profile default: "Player" + a random suffix. Centralised so the domain-reload-disabled
        // reset (below) and the initializer build it identically.
        private static PlayerInfo BuildDefault() => new()
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
                playerFullName = _playerName,
                playerName = _shortName,
            };
        }

#if UNITY_EDITOR
        // Story 13.0: domain reload is disabled in this project, so this static survives across Play Mode
        // sessions — without this reset the previous session's "Player####" (or a name set via the login
        // menu) would leak into the next Play. Restore the default once at Play entry (model:
        // CharacterManager.ResetStaticsForDomainReloadDisabled).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            playerInfo = BuildDefault();
        }
#endif
    }
}
