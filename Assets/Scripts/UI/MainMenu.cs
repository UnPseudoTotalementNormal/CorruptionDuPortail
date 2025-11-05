#region

using System.Threading.Tasks;
using Extensions;
using Network;
using Network.Services;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

#endregion

namespace UI
{
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] private Button openHostMenu;
        [SerializeField] private Button closeHostMenu;
    
        [SerializeField] private Button openJoinMenu;
        [SerializeField] private Button closeJoinMenu;
    
        [SerializeField] private CanvasGroup hostMenuCanvasGroup;
        [SerializeField] private CanvasGroup joinMenuCanvasGroup;
    
        [SerializeField] private Button _hostButton;
        [FormerlySerializedAs("_joinButton")] [SerializeField] private Button _joinWithCodeButton;
    
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TextMeshProUGUI hostCodeText;
    
        [SerializeField] private TMP_InputField lobbyNameInputField;

        private string lobbyCreatingName = "";
    
        private void Start()
        {
            closeHostMenu.onClick.AddListener(OnCloseHostButtonClicked);
            openHostMenu.onClick.AddListener(OnOpenHostButtonClicked);
        
            openJoinMenu.onClick.AddListener(OnJoinMenuButtonClicked);
            closeJoinMenu.onClick.AddListener(OnCloseJoinMenuButtonClicked);
        
            _hostButton.onClick.AddListener(OnHostButtonClicked);
            _joinWithCodeButton.onClick.AddListener(OnJoinButtonClicked);
            lobbyNameInputField.onValueChanged.AddListener(OnLobbyNameValueChange);
        }

        private void OnCloseJoinMenuButtonClicked()
        {
            joinMenuCanvasGroup.DoHideGroup();
        }

        private void OnJoinMenuButtonClicked()
        {
            joinMenuCanvasGroup.DoShowGroup();
        }

        private void OnOpenHostButtonClicked()
        {
            hostMenuCanvasGroup.DoShowGroup();
        }

        private void OnCloseHostButtonClicked()
        {
            hostMenuCanvasGroup.DoHideGroup();
        }

        private void OnLobbyNameValueChange(string _text)
        {
            lobbyCreatingName = _text;
        }

        public async void OnJoinButtonClicked()
        {
            _hostButton.interactable = false;
            _joinWithCodeButton.interactable = false;

            string _lobbyCode = joinCodeInputField.text;

            if (string.IsNullOrEmpty(_lobbyCode))
            {
                Debug.Log("Lobby code is empty");
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            var _lobby = await LobbyManager.instance.JoinLobbyByCode(_lobbyCode);
            
            if (_lobby == null)
            {
                Debug.Log("Failed to join lobby");
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            if (!_lobby.Data.TryGetValue("joinCode", out var _value))
            {
                Debug.Log("Lobby missing joinCode");
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            string _relayJoinCode = _value.Value;
            bool _connected = await StartClientWithRelay(_relayJoinCode, "dtls");

            if (!_connected)
            {
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            GameCode.gameCode = _lobby.LobbyCode;
            SwitchToGameScene();
        }


        public async void OnHostButtonClicked()
        {
            if (string.IsNullOrEmpty(lobbyCreatingName) || lobbyCreatingName.Length < 3)
            {
                return;
            }
        
            _hostButton.interactable = false;
            _joinWithCodeButton.interactable = false;

            Lobby _lobby = await LobbyManager.instance.CreateLobby(lobbyCreatingName, 15);
            if (_lobby == null)
            {
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            string _joinCode = await StartHostWithRelay(15, "dtls");
            if (string.IsNullOrEmpty(_joinCode))
            {
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
                return;
            }

            await LobbyManager.instance.UpdateLobbyJoinCode(_lobby.Id, _joinCode);

            hostCodeText.gameObject.SetActive(true);

            GameCode.gameCode = _lobby.LobbyCode;
            SwitchToGameScene();
        }


        private async Task<string> StartHostWithRelay(int _maxConnections, string _connectionType)
        {
            await UnityServices.InitializeAsync();
            var _allocation = await RelayService.Instance.CreateAllocationAsync(_maxConnections);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(_allocation.ToRelayServerData("dtls"));
            var _joinCode = await RelayService.Instance.GetJoinCodeAsync(_allocation.AllocationId);
            return NetworkManager.Singleton.StartHost() ? _joinCode : null;
        }

        private async Task<bool> StartClientWithRelay(string _joinCode, string _connectionType)
        {
            if (string.IsNullOrEmpty(_joinCode))
            {
                Debug.LogError("Join code is null or empty");
                return false;
            }
        
            await UnityServices.InitializeAsync();

            try
            {
                var _allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: _joinCode);
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(_allocation.ToRelayServerData(_connectionType));
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay join failed: {e.Message}");
                return false;
            }
            return !string.IsNullOrEmpty(_joinCode) && NetworkManager.Singleton.StartClient();
        }

        private void SwitchToGameScene()
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                "GameScene",
                LoadSceneMode.Single);
        }
    }
}
