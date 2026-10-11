#region

using System;
using CorruptionDuPortail.Domain;
using Network.Services;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network.Steam
{
    /// <summary>
    /// Steam build: requests to join a friend's lobby that come from Steam itself: "Rejoindre la partie" on a friend's
    /// profile (rich presence connect, see <see cref="SteamLobbyBackend"/>), an accepted invite, or the launch
    /// arguments Steam passes when it starts the game for such a join. The menu takes the request
    /// (<see cref="TryTakePending"/>) and runs the server browser's join, password prompt included.
    /// </summary>
    public static class SteamInvites
    {
        public const string AlreadyInGameMessage = "Tu es déjà dans une partie : quitte-la pour rejoindre ton ami.";

        /// <summary>A join request is waiting. Raised on the main thread (callbacks pumped by SteamRuntime).</summary>
        public static event System.Action JoinRequested;

        private static string s_pendingLobbyId;

        /// <summary>Called by <see cref="SteamRuntime"/> once the Steam client runs.</summary>
        internal static void Listen()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoinRequested;
            SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested -= OnRichPresenceJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested += OnRichPresenceJoinRequested;

            if (SteamJoinRequest.TryParseArgs(Environment.GetCommandLineArgs(), out ulong _launchLobby))
            {
                Request(_launchLobby, "launch arguments");
            }
        }

        internal static void StopListening()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested -= OnRichPresenceJoinRequested;
        }

        /// <summary>Hands over (and forgets) the lobby a join was requested for.</summary>
        public static bool TryTakePending(out string lobbyId)
        {
            lobbyId = s_pendingLobbyId;
            s_pendingLobbyId = null;
            return !string.IsNullOrEmpty(lobbyId);
        }

        private static void OnLobbyJoinRequested(Lobby lobby, SteamId friend)
        {
            Request(lobby.Id.Value, $"invite / friend {friend.Value}");
        }

        private static void OnRichPresenceJoinRequested(Friend friend, string connect)
        {
            if (SteamJoinRequest.TryParseConnect(connect, out ulong _lobbyId))
            {
                Request(_lobbyId, $"rich presence of {friend.Name}");
            }
        }

        private static void Request(ulong lobbyId, string source)
        {
            // Mid-game the menu is gone: never keep the request for later, it would join by surprise on the way back.
            NetworkManager _networkManager = NetworkManager.Singleton;
            if (_networkManager != null && _networkManager.IsListening)
            {
                Debug.Log($"[STEAM] Join request for lobby {lobbyId} ({source}) ignored: already in a game.");
                if (LobbyManager.instance != null)
                {
                    LobbyManager.instance.ReportError(AlreadyInGameMessage);
                }
                return;
            }

            Debug.Log($"[STEAM] Join request for lobby {lobbyId} ({source}).");
            s_pendingLobbyId = lobbyId.ToString();
            JoinRequested?.Invoke();
        }

#if UNITY_EDITOR
        // Domain reload is disabled in this project: drop any request or subscriber left by a previous Play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_pendingLobbyId = null;
            JoinRequested = null;
        }
#endif
    }
}
