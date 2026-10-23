using System;
using Diagnostics.Logs;
using UnityEngine;

namespace Network
{
    /// <summary>
    /// Rejoin 02 (feat/player-rejoin), client side: the secret session token the host handed to this player when he
    /// was seated, and how to reach that host again, saved on the PC (PlayerPrefs) so it survives a crash and a
    /// relaunch. Sent with every connection request (<see cref="ClientConnectionPayload"/>): the host only honours it
    /// mid-game, for the seat it reserved for this player. Cleared when the game ends, the host shuts it down or the player
    /// leaves on purpose. Refreshed while this PC is connected (<see cref="KeepAlive"/>): once the connection has been gone
    /// longer than a seat can stay reserved, there is nothing left to rejoin (host gone, grace over) and it is ignored.
    /// </summary>
    public static class RejoinSessionStore
    {
        private const string Key = "cdp.rejoin.session";
        // Seconds after the last sign of life of the connection during which the session still opens something: the
        // host's grace delay plus slack for the keep-alive period and the host noticing the drop.
        public const double FreshSeconds = GameValues.REJOIN_GRACE_SECONDS + 60.0;
        // How often a connected client refreshes its session (well under the slack above).
        public const double KeepAlivePeriodSeconds = 10.0;

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
            /// <summary>Last time this PC was still connected to that host (0 = never refreshed: <see cref="savedAtUnixSeconds"/>).</summary>
            public long lastSeenUnixSeconds;

            public bool IsFresh => !string.IsNullOrEmpty(token) &&
                                   DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Math.Max(savedAtUnixSeconds, lastSeenUnixSeconds) < FreshSeconds;
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
            s_lastInvalidLogged = null;
        }

        // Playtest logs: TryLoad runs often (keep-alive, menu, each connection request) — an invalid session is logged
        // once, not on every read.
        private static string s_lastInvalidLogged;

        private static void LogInvalidOnce(string _reason, Session _session)
        {
            GameLog.Guarded(() =>
            {
                string _key = _reason + "|" + (_session != null ? _session.savedAtUnixSeconds : 0);
                if (_key == s_lastInvalidLogged)
                {
                    return;
                }
                s_lastInvalidLogged = _key;
                long _age = _session != null
                    ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Math.Max(_session.savedAtUnixSeconds, _session.lastSeenUnixSeconds)
                    : 0;
                // Never the token itself: only whether there was one.
                GameLog.Info("net", "rejoin.session.invalid", "reason", _reason, "ageSeconds", _age,
                    "hostKind", _session != null ? _session.hostKind : null,
                    "hadToken", _session != null && !string.IsNullOrEmpty(_session.token));
            });
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
            Save(_session);
        }

        /// <summary>Still connected to the host: the session stays good for a rejoin should the connection drop now.</summary>
        public static void KeepAlive()
        {
            if (!TryLoad(out Session _session))
            {
                return;
            }
            _session.lastSeenUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save(_session);
        }

        private static void Save(Session _session)
        {
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
            bool _valid = _session != null && _session.IsFresh;
            if (!_valid)
            {
                LogInvalidOnce(_session == null ? "corrupt" : string.IsNullOrEmpty(_session.token) ? "no token" : "stale", _session);
            }
            return _valid;
        }

        /// <summary>The token to put in a connection request (empty when none is saved or it is stale).</summary>
        public static string TokenForConnection() => TryLoad(out Session _session) ? _session.token : string.Empty;

        /// <summary>The game is over for this player (ended, shut down by the host, or he left on purpose): nothing to rejoin.</summary>
        public static void Clear()
        {
            GameLog.Info("net", "rejoin.session.cleared");
            PlayerPrefs.DeleteKey(Key + KeySuffix);
            PlayerPrefs.Save();
        }
    }
}
