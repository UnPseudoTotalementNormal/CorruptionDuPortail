using System;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Rejoin 02 (feat/player-rejoin), client side: the secret session token the host handed to this player when he
    /// was seated, and how to reach that host again, saved on the PC (PlayerPrefs) so it survives a crash and a
    /// relaunch. Sent with every connection request (<see cref="ClientConnectionPayload"/>): the host only honours it
    /// mid-game, for the seat it reserved for this player. Cleared when the game ends or the player leaves on purpose.
    /// </summary>
    public static class RejoinSessionStore
    {
        private const string Key = "cdp.rejoin.session";
        // A session older than this is ignored (a game never lasts that long; a stale token just costs nothing).
        private const double MaxAgeHours = 6.0;

        [Serializable]
        public sealed class Session
        {
            public string token = string.Empty;
            /// <summary>How the host was reached: "relay" (join code), "steam" (host SteamId), "direct" (ip:port).</summary>
            public string hostKind = string.Empty;
            public string hostAddress = string.Empty;
            /// <summary>The UGS lobby code shown to players (display / lobby path).</summary>
            public string lobbyCode = string.Empty;
            public long savedAtUnixSeconds;

            public bool IsFresh => !string.IsNullOrEmpty(token) &&
                                   DateTimeOffset.UtcNow.ToUnixTimeSeconds() - savedAtUnixSeconds < MaxAgeHours * 3600.0;
        }

        /// <summary>Dev / autoplay only: several player processes on one machine share PlayerPrefs, so each one keeps
        /// its session under its own key.</summary>
        public static string KeySuffix { get; set; } = string.Empty;

        // Where the current connection attempt goes (set by the join path before StartClient); saved with the token.
        private static string s_pendingHostKind = string.Empty;
        private static string s_pendingHostAddress = string.Empty;
        private static string s_pendingLobbyCode = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            KeySuffix = string.Empty;
            s_pendingHostKind = s_pendingHostAddress = s_pendingLobbyCode = string.Empty;
        }

        /// <summary>Join path: remembers which host this connection goes to (saved once the host hands a token).</summary>
        public static void SetConnectionTarget(string _hostKind, string _hostAddress, string _lobbyCode)
        {
            s_pendingHostKind = _hostKind ?? string.Empty;
            s_pendingHostAddress = _hostAddress ?? string.Empty;
            s_pendingLobbyCode = _lobbyCode ?? string.Empty;
        }

        /// <summary>The host seated this player: keep the token with the host it came from.</summary>
        public static void Remember(string _token)
        {
            TryLoad(out Session _previous);
            var _session = new Session
            {
                token = _token ?? string.Empty,
                hostKind = !string.IsNullOrEmpty(s_pendingHostKind) ? s_pendingHostKind : _previous?.hostKind ?? string.Empty,
                hostAddress = !string.IsNullOrEmpty(s_pendingHostAddress) ? s_pendingHostAddress : _previous?.hostAddress ?? string.Empty,
                lobbyCode = !string.IsNullOrEmpty(s_pendingLobbyCode) ? s_pendingLobbyCode : _previous?.lobbyCode ?? string.Empty,
                savedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            PlayerPrefs.SetString(Key + KeySuffix, JsonUtility.ToJson(_session));
            PlayerPrefs.Save();
        }

        public static bool TryLoad(out Session _session)
        {
            _session = null;
            string _json = PlayerPrefs.GetString(Key + KeySuffix, string.Empty);
            if (string.IsNullOrEmpty(_json))
            {
                return false;
            }
            try
            {
                _session = JsonUtility.FromJson<Session>(_json);
            }
            catch (Exception)
            {
                _session = null;
            }
            return _session != null && _session.IsFresh;
        }

        /// <summary>The token to put in a connection request (empty when none is saved or it is stale).</summary>
        public static string TokenForConnection() => TryLoad(out Session _session) ? _session.token : string.Empty;

        /// <summary>The game is over for this player (ended, or he left on purpose): nothing to rejoin.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key + KeySuffix);
            PlayerPrefs.Save();
        }
    }
}
