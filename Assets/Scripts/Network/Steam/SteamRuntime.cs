#region

using System;
using Netcode.Transports.Facepunch;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network.Steam
{
    /// <summary>
    /// Steam build only: owns the Steam client for the whole session. Starts it once the first scene is loaded (the
    /// NetworkManager lives in BootScene), pumps its callbacks every frame (lobby results, connections, profile
    /// updates) and shuts it down on quit. Also swaps the NetworkManager's transport to <see cref="FacepunchTransport"/>,
    /// which carries the game over the Steam Datagram Relay (no port to open, no IP exposed). The Relay build never
    /// creates it, so it never touches Steam.
    /// </summary>
    public sealed class SteamRuntime : MonoBehaviour
    {
        public const string NotRunningMessage = "Steam n'est pas lancé. Lance Steam, puis réessaie.";

        /// <summary>The Steam client answered: profile, lobbies and the Steam relay are usable.</summary>
        public static bool IsReady => SteamClient.IsValid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!GameFlavor.IsSteam)
            {
                return;
            }

            if (FindFirstObjectByType<SteamRuntime>() == null)
            {
                var _go = new GameObject(nameof(SteamRuntime));
                DontDestroyOnLoad(_go);
                _go.AddComponent<SteamRuntime>(); // Awake starts the client before the transport below needs it
            }
            UseSteamTransport();
        }

        private static void UseSteamTransport()
        {
            NetworkManager _networkManager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : FindFirstObjectByType<NetworkManager>();
            if (_networkManager == null)
            {
                Debug.LogWarning("[STEAM] No NetworkManager in the first scene: transport left unchanged.");
                return;
            }

            FacepunchTransport _transport = _networkManager.GetComponent<FacepunchTransport>();
            if (_transport == null)
            {
                _transport = _networkManager.gameObject.AddComponent<FacepunchTransport>();
            }
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            Debug.Log("[STEAM] Network transport = Steam Datagram Relay (FacepunchTransport).");
        }

        private void Awake()
        {
            TryStartClient();
        }

        /// <summary>Starts the Steam client if it is not running yet (again after Steam was launched late).</summary>
        public static bool TryStartClient()
        {
            if (SteamClient.IsValid)
            {
                return true;
            }

            try
            {
                // Spacewar (480) until the game has its own app id. Callbacks are pumped in Update (main thread),
                // never on Facepunch's background thread: they touch Unity objects.
                SteamClient.Init(GameValues.STEAM_APP_ID, false);
                SteamNetworkingUtils.InitRelayNetworkAccess();
                Debug.Log($"[STEAM] Client ready: {SteamClient.Name} ({SteamClient.SteamId.Value}), app {GameValues.STEAM_APP_ID}.");
                SteamInvites.Listen();
                return true;
            }
            catch (Exception e)
            {
                // Steam closed, not logged in, or steam_api64.dll missing: the menu shows NotRunningMessage.
                Debug.LogError($"[STEAM] Steam client unavailable: {e.Message}");
                return false;
            }
        }

        private void Update()
        {
            if (SteamClient.IsValid)
            {
                SteamClient.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            SteamInvites.StopListening();
            if (SteamClient.IsValid)
            {
                SteamClient.Shutdown();
            }
        }
    }
}
