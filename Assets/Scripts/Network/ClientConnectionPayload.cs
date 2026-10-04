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
            };
            _networkManager.NetworkConfig.ConnectionData = _payload.ToBytes();
        }
    }
}
