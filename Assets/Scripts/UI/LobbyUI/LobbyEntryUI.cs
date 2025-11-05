using Network;
using TMPro;
using Unity.Netcode;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

namespace UI.LobbyUI
{
    public class LobbyEntryUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI lobbyNameText;
        [SerializeField] Button joinButton;

        Unity.Services.Lobbies.Models.Lobby lobby;

        public void Setup(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            this.lobby = lobby;
            lobbyNameText.text = lobby.Name;
            joinButton.onClick.AddListener(OnJoinClicked);
        }

        async void OnJoinClicked()
        {
            lobby = await Network.Services.LobbyManager.instance.JoinLobby(lobby.Id);
            if (!lobby.Data.ContainsKey("joinCode"))
                return;

            string _joinCode = lobby.Data["joinCode"].Value;
            var _allocation = await RelayService.Instance.JoinAllocationAsync(_joinCode);
            NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
                .SetRelayServerData(_allocation.ToRelayServerData("dtls"));

            bool ok = NetworkManager.Singleton.StartClient();
            if (!ok) return;

            GameCode.gameCode = lobby.LobbyCode;
            NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}