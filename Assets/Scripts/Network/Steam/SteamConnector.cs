#region

using Netcode.Transports.Facepunch;
using Network.Services;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Diagnostics.Logs;

#endregion

namespace Network.Steam
{
    /// <summary>
    /// Steam build: host and join over the Steam Datagram Relay (<see cref="FacepunchTransport"/>, installed by
    /// <see cref="SteamRuntime"/>). The joiner dials the host's SteamID64 published in the lobby; Steam routes the
    /// traffic through its relays, so nobody opens a port or sees another player's IP. Shared by "Créer",
    /// "Rejoindre avec un code" and the server browser (the Relay build's counterpart is RelayConnector).
    /// </summary>
    public static class SteamConnector
    {
        public const string PasswordNeedsListMessage =
            "Cette partie a un mot de passe : rejoins-la depuis la liste des parties.";

        /// <summary>Starts the host. Returns its SteamID64, to publish in the lobby, or null on failure.</summary>
        /// <param name="lobbyPassword">Checked by the connection approval (Steam lobbies have none); null = open.</param>
        public static string StartHost(NetworkManager networkManager, string lobbyPassword)
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError($"[STEAM] Cannot host: {SteamRuntime.NotRunningMessage}");
                GameLog.Warn("net", "steam.host.failed", "reason", "steam not running");
                return null;
            }

            // Reject mid-game joins and wrong passwords at the NGO handshake. Must be enabled before StartHost so
            // the server answers approval for every connecting client.
            ConnectionApprovalGate.Enable(networkManager, lobbyPassword);
            if (!networkManager.StartHost())
            {
                Debug.LogError("[STEAM] StartHost failed.");
                GameLog.Warn("net", "steam.host.failed", "reason", "StartHost refused");
                return null;
            }
            GameLog.Info("net", "steam.host", "steamId", SteamClient.SteamId.Value.ToString(),
                "hasPassword", !string.IsNullOrEmpty(lobbyPassword));
            return SteamClient.SteamId.Value.ToString();
        }

        /// <summary>
        /// Dials the lobby's host. Only STARTS the connection, like NetworkManager.StartClient: the caller then
        /// waits for the verdict with <see cref="JoinHandshake.WaitForConnectedOrTimeout"/>.
        /// </summary>
        public static bool StartClient(NetworkManager networkManager, GameLobby lobby, string lobbyPassword)
        {
            if (!TryGetHostSteamId(lobby, out ulong _hostSteamId, out bool _fromOwner))
            {
                Debug.LogError("[STEAM] Lobby without a host SteamID: not a Steam game.");
                GameLog.Guarded(() => GameLog.Warn("net", "steam.client.failed", "reason", "no host SteamID", "lobby", lobby.Id,
                    "dataKeys", string.Join(",", lobby.Data.Keys)));
                return false;
            }
            if (_fromOwner)
            {
                // Playtest 2026-10-24: every join read a lobby without this key although the host writes it.
                GameLog.Guarded(() => GameLog.Warn("net", "steam.client.hostFromOwner", "lobby", lobby.Id,
                    "host", _hostSteamId.ToString(), "dataKeys", string.Join(",", lobby.Data.Keys)));
            }

            FacepunchTransport _transport = networkManager.GetComponent<FacepunchTransport>();
            if (_transport == null)
            {
                Debug.LogError("[STEAM] FacepunchTransport not found on NetworkManager.");
                GameLog.Warn("net", "steam.client.failed", "reason", "no FacepunchTransport");
                return false;
            }

            _transport.targetSteamId = _hostSteamId;
            // Rejoin: remember this host, saved with the session token it hands back once we are seated.
            RejoinSessionStore.SetConnectionTarget("steam", _hostSteamId.ToString(), lobby.Code);
            // NET-02: the profile, build version and lobby password travel inside the connection request.
            ClientConnectionPayload.Apply(networkManager, lobbyPassword);
            if (!networkManager.StartClient())
            {
                Debug.LogError("[STEAM] StartClient failed.");
                GameLog.Warn("net", "steam.client.failed", "reason", "StartClient refused", "host", _hostSteamId.ToString());
                return false;
            }
            GameLog.Guarded(() => GameLog.Info("net", "steam.client", "host", _hostSteamId.ToString(), "lobby", lobby.Id,
                "code", lobby.Code, "hasPassword", !string.IsNullOrEmpty(lobbyPassword)));
            return true;
        }

        /// <summary>
        /// The SteamID64 to dial: the one the host published in the lobby data, else the lobby owner (Steam tells it to
        /// every member, and the host is the one who created the lobby).
        /// </summary>
        public static bool TryGetHostSteamId(GameLobby lobby, out ulong hostSteamId, out bool fromOwner)
        {
            fromOwner = false;
            if (lobby.TryGetData(GameLobby.HostSteamIdKey, out string _published) && ulong.TryParse(_published, out hostSteamId)
                && hostSteamId != 0)
            {
                return true;
            }
            fromOwner = true;
            return ulong.TryParse(lobby.HostId, out hostSteamId) && hostSteamId != 0;
        }
    }
}
