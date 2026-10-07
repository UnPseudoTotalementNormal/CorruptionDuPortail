#region

using CorruptionDuPortail.Domain;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// NET-02 (epic-network-sync-hardening): stamps this client's profile + build version into the NGO connection
    /// request. Call on EVERY client start path right before <c>StartClient()</c> (lobby list, join by code, Relay).
    /// Also keeps NGO's deferred-message window open for the whole join (<see cref="JoinHandshake.DeferredMessageWindowSeconds"/>).
    /// </summary>
    public static class ClientConnectionPayload
    {
        public static void Apply(NetworkManager _networkManager)
        {
            if (_networkManager == null)
            {
                return;
            }

            PlayerInfo _local = LocalPlayerInfoHolder.playerInfo;
            var _payload = new ConnectionPayload
            {
                BuildVersion = Application.version,
                PlayerName = _local.playerName.ToString(),
                PlayerFullName = _local.playerFullName.ToString(),
                SteamId = _local.playerSteamId,
                IsEditor = Application.isEditor,
                // Rejoin 02: proves this player owns a seat if he reconnects to a game already started.
                RejoinToken = RejoinSessionStore.TokenForConnection(),
            };
            _networkManager.NetworkConfig.ConnectionData = _payload.ToBytes();

            // Deltas received for objects spawned while this client still loads GameScene must survive until the
            // load ends (see JoinHandshake.DeferredMessageWindowSeconds); never shorten a longer configured window.
            if (_networkManager.NetworkConfig.SpawnTimeout < JoinHandshake.DeferredMessageWindowSeconds)
            {
                _networkManager.NetworkConfig.SpawnTimeout = JoinHandshake.DeferredMessageWindowSeconds;
            }
        }
    }
}
