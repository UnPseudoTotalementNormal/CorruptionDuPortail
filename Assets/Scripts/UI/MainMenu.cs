#region

using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Extensions;
using Network;
using Network.Services;
using TMPro;
using Unity.Netcode;
using Netcode.Transports.Facepunch;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
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

                // Set BEFORE connecting: NGO synchronization loads GameScene by itself, and GameCodeText reads this
                // static in Start(). Assigning it after the handshake would leave the joiner's code label empty.
                GameCode.gameCode = _lobby.LobbyCode;

                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    if (!JoinWithFacepunch(_lobby))
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
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    // The relay path owns its own handshake wait — and the dtls -> wss fallback — inside
                    // RelayConnector; the shared wait that used to sit below would double-run it.
                    RelayConnectResult _result = await JoinWithUnityRelay(_lobby);
                    if (!_result.Success)
                    {
                        if (!string.IsNullOrEmpty(_result.FailureMessage))
                        {
                            // Message was built BEFORE any teardown (Shutdown may clear DisconnectReason).
                            if (LobbyManager.instance != null)
                            {
                                LobbyManager.instance.ReportError(_result.FailureMessage);
                            }
                            throw new System.Exception(_result.FailureMessage);
                        }
                        throw new System.Exception("Failed to join game");
                    }
                }
                else
                {
                    throw new System.Exception("Unknown transport type!");
                }

                // No scene load on the client: NGO already synchronized us into GameScene before the handshake
                // above returned. SwitchToGameScene() is the SERVER entry point (host path) — calling it here was
                // a silent ServerOnlyAction no-op (investigation client-join-lobby-desync).
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
            
            // NET-02: the profile + build version travel inside the connection request.
            Network.ClientConnectionPayload.Apply(NetworkManager.Singleton);
            bool _connected = NetworkManager.Singleton.StartClient();
            if (!_connected)
            {
                Debug.LogError("Failed to start client");
                return false;
            }

            return true;
        }

        private async UniTask<RelayConnectResult> JoinWithUnityRelay(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            // Récupérer le join code Relay depuis les données du lobby
            if (!lobby.Data.TryGetValue("joinCode", out var _value))
            {
                Debug.Log("Lobby missing joinCode");
                return RelayConnectResult.Failed(null);
            }

            // Allocation + transport + StartClient + handshake wait, with the dtls -> wss fallback, all live
            // in the single decision point (investigation vpn-instant-disconnect, backlog #7).
            return await RelayConnector.ConnectClientAsync(_value.Value);
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

        private async UniTask<string> HostWithUnityRelay(int maxConnections)
        {
            // Reject mid-game joins at the NGO handshake ("Rejoindre une partie déjà en cours"). Must be
            // enabled before StartHost (inside HostAsync) so the server answers approval for every connecting
            // client — and it survives the connector's inter-attempt Shutdown (NetworkConfig + callback, not
            // driver state).
            ConnectionApprovalGate.Enable(NetworkManager.Singleton);

            // Allocation + StartHost + relay BIND check with the dtls -> wss fallback; the join code returned
            // (and published to the lobby) always belongs to the allocation that actually bound.
            return await RelayConnector.HostAsync(maxConnections);
        }



        private void SwitchToGameScene()
        {
            NetworkManager.Singleton.SceneManager.LoadScene(
                "GameScene",
                LoadSceneMode.Single);
        }
    }
}