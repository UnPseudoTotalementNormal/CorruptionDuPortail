#region

using Cysharp.Threading.Tasks;
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

        private void OnLeavePressed() => Leave("pause");

        /// <summary>Leaves the game: the host ends it for everyone, a client goes back to the menu alone. Also the
        /// end screen's « Quitter la partie » for the players who are not the host (T9 maquettes menus v1 C2).</summary>
        public static void Leave(string _from)
        {
            NetworkManager _nm = NetworkManager.Singleton;
            Diagnostics.Logs.GameLog.Info("net", "leave.pressed", "host", _nm != null && _nm.IsServer, "from", _from);

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
            // Leaving on purpose: nothing to rejoin (rejoin is for drops and crashes).
            RejoinSessionStore.Clear();

            if (_nm != null && _nm.IsListening)
            {
                _nm.Shutdown();
            }

            if (LobbyManager.instance != null && LobbyManager.instance.IsInLobby)
            {
                LobbyManager.instance.LeaveOrDeleteLobby().Forget();
            }

            SceneManager.LoadScene(MainMenuSceneIndex);
        }
    }
}
