#region

using GameLogic;
using Network;
using Network.Services;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#endregion

namespace UI
{
    /// <summary>
    /// [LEAVE] In-pause "quitter la partie" button (lives on the PausePanel). Host = authority: leaving ends
    /// the match for EVERYONE via ShutOffGameRpc (which also deletes the cloud lobby). A non-host client leaves
    /// INDIVIDUALLY (flag the shutdown expected so the host-loss popup is suppressed, shut down NGO, leave the
    /// cloud lobby, return to the menu) while the host keeps the game going. Mirrors the CustomButton
    /// subscription pattern of <see cref="ShutOffGameButton"/>.
    /// </summary>
    public class LeaveGameButton : MonoBehaviour
    {
        private const int MainMenuSceneIndex = 1; // BootScene=0, MainMenu=1, GameScene=2

        private void Awake()
        {
            GetComponent<CustomButton>().onButtonClicked += OnLeavePressed;
        }

        private void OnLeavePressed()
        {
            NetworkManager _nm = NetworkManager.Singleton;

            // Host is the authority — the match cannot continue without it, so leaving ends it for everyone.
            // ShutOffGameRpc (SendTo.Everyone) shuts every peer down + deletes the cloud lobby on the host.
            if (_nm != null && _nm.IsServer)
            {
                CompositionRoot.For(_nm).GameManager.ShutOffGameRpc();
                return;
            }

            // Non-host client: leave on our own; the host keeps the game. Flag the shutdown as EXPECTED so the
            // client-side ClientDisconnectHandler does not mistake it for an abrupt host loss.
            ClientDisconnectHandler.NotifyExpectedShutdown();

            if (_nm != null && _nm.IsListening)
            {
                _nm.Shutdown();
            }

            if (LobbyManager.instance != null && LobbyManager.instance.IsInLobby)
            {
                _ = LobbyManager.instance.LeaveOrDeleteLobby();
            }

            SceneManager.LoadScene(MainMenuSceneIndex);
        }
    }
}
