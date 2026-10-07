#region

using CorruptionDuPortail.Domain;
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

        /// <summary>UGS sign-in: the name comes as "Name#1234"; the "#1234" discriminator is stripped for display.</summary>
        public static void CreateNewClientData(string _playerName) => CreateNewClientData(_playerName, true);

        /// <summary>
        /// NET-02: <paramref name="_isUgsName"/> = false for Steam names, which may legitimately contain '#'
        /// ("#Nohan" used to become an empty pseudo). Names are sanitized (whitespace, empty → default pseudo,
        /// UTF-8-safe truncation to the FixedString64Bytes capacity).
        /// </summary>
        public static void CreateNewClientData(string _playerName, bool _isUgsName)
        {
            string _fallback = BuildDefault().playerName.ToString();
            playerInfo = new PlayerInfo()
            {
                playerFullName = PlayerNameSanitizer.TruncateUtf8(_playerName ?? string.Empty, PlayerNameSanitizer.MaxUtf8Bytes),
                playerName = PlayerNameSanitizer.Sanitize(_playerName, _isUgsName, _fallback),
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
