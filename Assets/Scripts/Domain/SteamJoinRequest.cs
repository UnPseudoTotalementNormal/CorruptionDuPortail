using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Steam build: "Rejoindre la partie" from a friend's profile. The host's rich presence publishes
    /// <see cref="ConnectFor"/>; Steam hands that string back to the joiner, either in-game (rich presence join
    /// request) or as launch arguments when it starts the game. Both are parsed here.
    /// </summary>
    public static class SteamJoinRequest
    {
        public const string ConnectLobbyArg = "+connect_lobby";

        /// <summary>The rich presence "connect" value for a lobby.</summary>
        public static string ConnectFor(ulong lobbyId) => $"{ConnectLobbyArg} {lobbyId}";

        /// <summary>A connect string ("+connect_lobby 1099…") as Steam passes it to the running game.</summary>
        public static bool TryParseConnect(string connect, out ulong lobbyId)
        {
            lobbyId = 0;
            return !string.IsNullOrEmpty(connect)
                   && TryParseArgs(connect.Split((char[])null, StringSplitOptions.RemoveEmptyEntries), out lobbyId);
        }

        /// <summary>The game's launch arguments: "+connect_lobby" followed by a non-zero lobby id.</summary>
        public static bool TryParseArgs(IReadOnlyList<string> args, out ulong lobbyId)
        {
            lobbyId = 0;
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], ConnectLobbyArg, StringComparison.OrdinalIgnoreCase)
                    && ulong.TryParse(args[i + 1], out lobbyId) && lobbyId != 0)
                {
                    return true;
                }
            }
            lobbyId = 0;
            return false;
        }
    }
}
