#region

using System.Collections.Generic;
using System.Threading.Tasks;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Extensions;
using Network;
using Network.Services;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Netcode.Transports.Facepunch;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        
        [SerializeField] private Button quitGameButton;
    
        [SerializeField] private CanvasGroup hostMenuCanvasGroup;
        [SerializeField] private CanvasGroup joinMenuCanvasGroup;
    
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinWithCodeButton;
    
        [SerializeField] private TMP_InputField joinCodeInputField;
    
        [SerializeField] private TMP_InputField lobbyNameInputField;
        [SerializeField] private TMP_InputField lobbyPasswordInputField;
        
        [SerializeField] private CanvasGroup loadingCanvasGroup; //shown when loading stuff

        private string lobbyCreatingName = "";

        // Garde de rentrance pour éviter le double déclenchement Host/Join.
        private bool _isBusy;

        // The two-phase connect wait and its deadlines now live in the shared Network.JoinHandshake, so the
        // lobby-list join path (LobbySelectionPanel) uses the exact same handshake instead of loading GameScene
        // blind after StartClient (investigation join-started-game-gate).

        private void Start()
        {
            closeHostMenu.onClick.AddListener(OnCloseHostButtonClicked);
            openHostMenu.onClick.AddListener(OnOpenHostButtonClicked);
        
            openJoinMenu.onClick.AddListener(OnJoinMenuButtonClicked);
            closeJoinMenu.onClick.AddListener(OnCloseJoinMenuButtonClicked);
        
            _hostButton.onClick.AddListener(OnHostButtonClicked);
            _joinWithCodeButton.onClick.AddListener(OnJoinButtonClicked);
            lobbyNameInputField.onValueChanged.AddListener(OnLobbyNameValueChange);
            
            quitGameButton.onClick.AddListener(OnQuitGameButtonClicked);
        }

        private void OnQuitGameButtonClicked()
        {
            Application.Quit();
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

        public void OnJoinButtonClicked()
        {
            if (string.IsNullOrEmpty(joinCodeInputField.text))
            {
                return;
            }

            if (_isBusy)
            {
                return;
            }
            _isBusy = true;

            OnJoinButtonClickedAsync().Forget();
        }

        private async UniTaskVoid OnJoinButtonClickedAsync()
        {
            _hostButton.interactable = false;
            _joinWithCodeButton.interactable = false;
            loadingCanvasGroup.DoShowGroup();

            try
            {
                string _lobbyCode = joinCodeInputField.text;

                var _lobby = await LobbyManager.instance.JoinLobbyByCode(_lobbyCode);
                if (_lobby == null)
                {
                    throw new System.Exception("Failed to join lobby");
                }

                bool joinSuccess = false;
                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    joinSuccess = JoinWithFacepunch(_lobby);
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    joinSuccess = await JoinWithUnityRelay(_lobby);
                }
                else
                {
                    throw new System.Exception("Unknown transport type!");
                }

                if (!joinSuccess)
                {
                    throw new System.Exception("Failed to join game");
                }

                // [LEAVE][PHASE 4] StartClient() only reports whether the connect attempt STARTED; it never
                // waits for the connection to actually establish. Bound that wait so a connect that never
                // completes (dead host, bad connection data) cannot hang forever in a stale "connecting"
                // state. On timeout, throw into the existing catch teardown (Shutdown + LeaveLobby + UI reset).
                ConnectFailReason _fail = await JoinHandshake.WaitForConnectedOrTimeout();
                if (_fail != ConnectFailReason.None)
                {
                    // Build BEFORE the teardown below shuts NGO down — Shutdown may clear DisconnectReason.
                    string _failureMessage = JoinHandshake.BuildFailureMessage(_fail);
                    if (LobbyManager.instance != null)
                    {
                        LobbyManager.instance.ReportError(_failureMessage);
                    }
                    throw new System.Exception(_failureMessage);
                }

                GameCode.gameCode = _lobby.LobbyCode;
                SwitchToGameScene();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error joining game: {e}");

                // Nettoyer l'état du réseau
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                // Quitter le lobby si rejoint
                if (LobbyManager.instance != null)
                {
                    await LobbyManager.instance.LeaveLobby();
                }

                // Réactiver l'interface
                loadingCanvasGroup.DoHideGroup();
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
            }
            finally
            {
                _isBusy = false;
            }
        }


        private bool JoinWithFacepunch(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            // Récupérer le Steam ID de l'hôte depuis les données du lobby
            if (!lobby.Data.TryGetValue("hostSteamId", out var _value))
            {
                Debug.Log("Lobby missing hostSteamId");
                return false;
            }

            string _hostSteamIdString = _value.Value;
            
            if (!ulong.TryParse(_hostSteamIdString, out ulong _hostSteamId))
            {
                Debug.LogError($"Invalid Steam ID format: {_hostSteamIdString}");
                return false;
            }

            // Configurer le transport FacePunch avec le Steam ID de l'hôte
            var _transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
            if (_transport == null)
            {
                Debug.LogError("FacepunchTransport not found on NetworkManager!");
                return false;
            }

            _transport.targetSteamId = _hostSteamId;
            
            bool _connected = NetworkManager.Singleton.StartClient();
            if (!_connected)
            {
                Debug.LogError("Failed to start client");
                return false;
            }

            return true;
        }

        private async Task<bool> JoinWithUnityRelay(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            // Récupérer le join code Relay depuis les données du lobby
            if (!lobby.Data.TryGetValue("joinCode", out var _value))
            {
                Debug.Log("Lobby missing joinCode");
                return false;
            }

            string _relayJoinCode = _value.Value;

            try
            {
                var _allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: _relayJoinCode);
                NetworkManager.Singleton.GetComponent<UnityTransport>()
                    .SetRelayServerData(_allocation.ToRelayServerData("dtls"));
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay join failed: {e.Message}");
                return false;
            }

            return NetworkManager.Singleton.StartClient();
        }


        public void OnHostButtonClicked()
        {
            if (string.IsNullOrEmpty(lobbyCreatingName) || lobbyCreatingName.Length < 3)
            {
                return;
            }

            if (_isBusy)
            {
                return;
            }
            _isBusy = true;

            OnHostButtonClickedAsync().Forget();
        }

        private async UniTaskVoid OnHostButtonClickedAsync()
        {
            _hostButton.interactable = false;
            _joinWithCodeButton.interactable = false;
            loadingCanvasGroup.DoShowGroup();

            try
            {
                var _lobbySettings = new LobbyCreationSettings();
                var password = lobbyPasswordInputField.text;
                if (string.IsNullOrEmpty(password))
                {
                    password = null;
                }
                _lobbySettings.password = password;
                _lobbySettings.isLocked = false;
                _lobbySettings.isPrivate = false;
                _lobbySettings.lobbyName = lobbyCreatingName;
                _lobbySettings.maxPlayers = 15;
                _lobbySettings.data = new Dictionary<string, DataObject>()
                {
                    {
                        nameof(LobbyCreationSettings.LobbyCustomDataKeys.Language),
                        new DataObject(DataObject.VisibilityOptions.Public,
                            "Français")
                    }
                };

                string connectionData = null;
                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    connectionData = HostWithFacepunch();
                    if (!string.IsNullOrEmpty(connectionData))
                    {
                        _lobbySettings.data["hostSteamId"] = new DataObject(DataObject.VisibilityOptions.Public, connectionData);
                    }
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    connectionData = await HostWithUnityRelay(15);
                    if (!string.IsNullOrEmpty(connectionData))
                    {
                        _lobbySettings.data["joinCode"] = new DataObject(DataObject.VisibilityOptions.Public, connectionData);
                    }
                }
                else
                {
                    throw new System.Exception("Unknown transport type!");
                }

                if (string.IsNullOrEmpty(connectionData))
                {
                    throw new System.Exception("Failed to get connection data");
                }

                Unity.Services.Lobbies.Models.Lobby _lobby = await LobbyManager.instance.CreateLobby(_lobbySettings);
                if (_lobby == null)
                {
                    throw new System.Exception("Failed to create lobby");
                }

                GameCode.gameCode = _lobby.LobbyCode;
                SwitchToGameScene();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error hosting game: {e}");

                // Nettoyer l'état du réseau
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }

                // Quitter le lobby si créé
                if (LobbyManager.instance != null)
                {
                    await LobbyManager.instance.LeaveLobby();
                }

                // Réactiver l'interface
                loadingCanvasGroup.DoHideGroup();
                _hostButton.interactable = true;
                _joinWithCodeButton.interactable = true;
            }
            finally
            {
                _isBusy = false;
            }
        }


        private string HostWithFacepunch()
        {
            // Vérifier que Steam est initialisé
            if (!SteamClient.IsValid)
            {
                Debug.LogError("Steam client not initialized!");
                return null;
            }

            ulong _hostSteamId = SteamClient.SteamId;

            // Reject mid-game joins at the NGO handshake ("Rejoindre une partie déjà en cours"). Must be
            // enabled before StartHost so the server answers approval for every connecting client.
            ConnectionApprovalGate.Enable(NetworkManager.Singleton);

            // Démarrer l'hôte avec FacepunchTransport
            bool _started = NetworkManager.Singleton.StartHost();
            if (!_started)
            {
                Debug.LogError("Failed to start host");
                return null;
            }

            return _hostSteamId.ToString();
        }

        private async Task<string> HostWithUnityRelay(int maxConnections)
        {
            try
            {
                var _allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                NetworkManager.Singleton.GetComponent<UnityTransport>()
                    .SetRelayServerData(_allocation.ToRelayServerData("dtls"));
                var _joinCode = await RelayService.Instance.GetJoinCodeAsync(_allocation.AllocationId);

                // Reject mid-game joins at the NGO handshake ("Rejoindre une partie déjà en cours"). Must be
                // enabled before StartHost so the server answers approval for every connecting client.
                ConnectionApprovalGate.Enable(NetworkManager.Singleton);

                bool _started = NetworkManager.Singleton.StartHost();
                if (!_started)
                {
                    Debug.LogError("Failed to start host");
                    return null;
                }

                return _joinCode;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay host failed: {e.Message}");
                return null;
            }
        }



        private void SwitchToGameScene()
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                "GameScene",
                LoadSceneMode.Single);
        }
    }
}