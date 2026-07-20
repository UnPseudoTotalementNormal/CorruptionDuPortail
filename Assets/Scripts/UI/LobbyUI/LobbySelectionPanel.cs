using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Extensions;
using Network;
using Network.Services;
using TMPro;
using UI.LobbyUI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Netcode.Transports.Facepunch;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Lobby
{
    public class LobbySelectionPanel : MonoBehaviour
    {
        [SerializeField] private LayoutElement[] headers;
    
        [SerializeField] RectTransform contentRoot;
        [SerializeField] LobbyEntryUI lobbyEntryPrefab;
        [SerializeField] Button refreshButton;
        
        [SerializeField] private Button connectButton;
    
        private const float AUTO_REFRESH_INTERVAL = 7f;
        private float refreshTimer = 0f;

        // Gardes de rentrance pour éviter le double déclenchement.
        private bool _isRefreshing;
        private bool _isJoining;

        private Unity.Services.Lobbies.Models.Lobby currentLobbySelected;
        private LobbyEntryUI currentLobbyEntryUISelected;
        
        private Dictionary<string, LobbyEntryUI> existingEntries = new Dictionary<string, LobbyEntryUI>();
        
        [SerializeField] private CanvasGroup loadingCanvasGroup;
        
        [SerializeField] private CanvasGroup enterPasswordCanvasGroup;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private Button submitPasswordButton;
        [SerializeField] private Button cancelPasswordButton;
    
        void Start()
        {
            Clear();
            refreshButton.onClick.AddListener(Refresh);
            connectButton.onClick.AddListener(OnConnectButtonClicked);
            cancelPasswordButton.onClick.AddListener(() => 
            {
                enterPasswordCanvasGroup.DoHideGroup();
                passwordInputField.text = "";
            });
            submitPasswordButton.onClick.AddListener(OnSubmitPasswordButtonClicked);
            Refresh();
        }

        private void OnSubmitPasswordButtonClicked()
        {
            if (currentLobbySelected == null)
                return;

            string password = passwordInputField.text;
            if (string.IsNullOrEmpty(password))
                return;

            if (_isJoining)
                return;
            _isJoining = true;

            OnSubmitPasswordButtonClickedAsync(password).Forget();
        }

        private async UniTaskVoid OnSubmitPasswordButtonClickedAsync(string password)
        {
            try
            {
                loadingCanvasGroup.DoShowGroup();

                bool result = await JoinLobby(currentLobbySelected, password);

                loadingCanvasGroup.DoHideGroup();

                if (!result)
                {
                    Debug.LogError("Failed to join lobby with password.");
                    passwordInputField.text = "";
                    return;
                }

                enterPasswordCanvasGroup.DoHideGroup();
                passwordInputField.text = "";
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (destruction de l'objet) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec de la connexion au lobby avec mot de passe: {e}");
                loadingCanvasGroup.DoHideGroup();
                passwordInputField.text = "";
            }
            finally
            {
                _isJoining = false;
            }
        }

        private void OnConnectButtonClicked()
        {
            if (currentLobbySelected == null)
                return;

            if (_isJoining)
                return;
            _isJoining = true;

            OnConnectButtonClickedAsync().Forget();
        }

        private async UniTaskVoid OnConnectButtonClickedAsync()
        {
            try
            {
                await JoinLobby(currentLobbySelected);
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (destruction de l'objet) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec de la connexion au lobby: {e}");
                loadingCanvasGroup.DoHideGroup();
            }
            finally
            {
                _isJoining = false;
            }
        }

        private void Update()
        {
            if (!AuthenticationService.Instance.IsSignedIn)
                return;

            refreshTimer += Time.deltaTime;
            if (refreshTimer >= AUTO_REFRESH_INTERVAL)
            {
                refreshTimer = 0f;
                Refresh();
            }
        }

        public void Refresh()
        {
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                return;
            }

            if (_isRefreshing)
            {
                return;
            }
            _isRefreshing = true;

            RefreshAsync().Forget();
        }

        private async UniTaskVoid RefreshAsync()
        {
            try
            {
                List<Unity.Services.Lobbies.Models.Lobby> lobbies;
                try
                {
                    lobbies = await LobbyManager.instance.GetLobbies();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to get lobbies: {e.Message}");
                    return;
                }

                HashSet<string> currentLobbyIds = new HashSet<string>();
                foreach (Unity.Services.Lobbies.Models.Lobby lobby in lobbies)
                {
                    if (lobby.IsLocked)
                    {
                        continue;
                    }

                    currentLobbyIds.Add(lobby.Id);

                    if (!existingEntries.ContainsKey(lobby.Id))
                    {
                        CreateEntry(lobby);
                    }
                    else
                    {
                        existingEntries[lobby.Id].Setup(lobby);
                    }
                }

                List<string> entriesToRemove = new List<string>();
                foreach (var kvp in existingEntries)
                {
                    if (!currentLobbyIds.Contains(kvp.Key))
                    {
                        entriesToRemove.Add(kvp.Key);
                        if (kvp.Value)
                        {
                            Destroy(kvp.Value.gameObject);
                        }
                    }
                }

                foreach (string id in entriesToRemove)
                {
                    existingEntries.Remove(id);
                }
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (destruction de l'objet) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec du rafraîchissement de la liste des lobbies: {e}");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void CreateEntry(Unity.Services.Lobbies.Models.Lobby _lobby)
        {
            LobbyEntryUI _entry = Instantiate(lobbyEntryPrefab, contentRoot);
            for (var i = 0; i < _entry.entries.Length; i++)
            {
                LayoutElement entryLayoutElement = _entry.entries[i];
                LayoutElement headerLayoutElement = headers[i];
                entryLayoutElement.preferredWidth = headerLayoutElement.preferredWidth;

                entryLayoutElement.GetComponent<TMP_Text>().fontSize = 
                    headerLayoutElement.GetComponent<TMP_Text>().fontSize;
            }
            _entry.Setup(_lobby);
            _entry.onEntryClicked += (lobbyClicked) =>
            {
                OnLobbyEntryClicked(lobbyClicked, _entry);
            };
            
            existingEntries[_lobby.Id] = _entry;
        }
        
        private async UniTask<bool> JoinLobby(Unity.Services.Lobbies.Models.Lobby lobby, string password = null)
        {
            // Cheap pre-shot: the list refreshes every AUTO_REFRESH_INTERVAL, so a lobby locked between the last
            // refresh and this click is still selectable. Refuse it here rather than paying a full connect just to
            // be rejected by ConnectionApprovalGate (investigation join-started-game-gate).
            if (lobby.IsLocked)
            {
                ReportJoinFailure(ConnectionApprovalGate.GameInProgressReason);
                loadingCanvasGroup.DoHideGroup();
                return false;
            }

            // Si le lobby a un mot de passe et qu'on ne l'a pas fourni, demander le mot de passe
            if (lobby.HasPassword && string.IsNullOrEmpty(password))
            {
                loadingCanvasGroup.DoHideGroup();
                enterPasswordCanvasGroup.DoShowGroup();
                return false;
            }

            loadingCanvasGroup.DoShowGroup();
            
            try
            {
                lobby = await LobbyManager.instance.JoinLobby(lobby.Id, password);
                if (lobby == null)
                {
                    loadingCanvasGroup.DoHideGroup();
                    return false;
                }
                
                // Détecter le type de transport et se connecter en conséquence
                bool connected = false;
                
                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    connected = JoinWithFacepunch(lobby);
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    connected = await JoinWithUnityRelay(lobby);
                }
                else
                {
                    Debug.LogError("Unknown transport type!");
                    loadingCanvasGroup.DoHideGroup();
                    return false;
                }

                if (!connected)
                {
                    Debug.LogError("Failed to connect to game");
                    await AbortJoin(null);
                    return false;
                }

                // StartClient() only reports that the connect attempt STARTED. Wait for the ACTUAL verdict before
                // loading GameScene — otherwise a server rejection (mid-game join) only lands once we are already
                // in the game scene, where it degrades into "Connexion à l'hôte perdue" instead of the server's own
                // "La partie a déjà commencé." (investigation join-started-game-gate).
                ConnectFailReason _fail = await JoinHandshake.WaitForConnectedOrTimeout();
                if (_fail != ConnectFailReason.None)
                {
                    // Build BEFORE AbortJoin shuts NGO down — Shutdown may clear DisconnectReason.
                    await AbortJoin(JoinHandshake.BuildFailureMessage(_fail));
                    return false;
                }

                GameCode.gameCode = lobby.LobbyCode;
                UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to join lobby: {e.Message}");
                await AbortJoin(null);
                return false;
            }
        }

        // Failed join teardown, mirroring MainMenu's: drop the half-open NGO session and the cloud lobby, surface the
        // reason, restore the menu. We NEVER leave the menu on a failed join — that is the whole point of the fix.
        private async UniTask AbortJoin(string _failureMessage)
        {
            ReportJoinFailure(_failureMessage);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            if (LobbyManager.instance != null && LobbyManager.instance.IsInLobby)
            {
                await LobbyManager.instance.LeaveLobby();
            }

            loadingCanvasGroup.DoHideGroup();
        }

        // Routes the message to the shared lobby-error channel, which ClientDisconnectHandler already renders as an
        // on-screen notification. Logging alone left the player staring at a menu with no explanation.
        private void ReportJoinFailure(string _failureMessage)
        {
            if (string.IsNullOrEmpty(_failureMessage))
            {
                return;
            }

            Debug.LogError($"Join refused: {_failureMessage}");
            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.ReportError(_failureMessage);
            }
        }

        private bool JoinWithFacepunch(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            // Récupérer le Steam ID de l'hôte depuis les données du lobby
            if (!lobby.Data.ContainsKey("hostSteamId"))
            {
                Debug.LogError("Lobby missing hostSteamId");
                return false;
            }

            string _hostSteamIdString = lobby.Data["hostSteamId"].Value;
            
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
            
            bool ok = NetworkManager.Singleton.StartClient();
            if (!ok)
            {
                Debug.LogError("Failed to start client");
                return false;
            }

            return true;
        }

        private async UniTask<bool> JoinWithUnityRelay(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            // Récupérer le join code Relay depuis les données du lobby
            if (!lobby.Data.ContainsKey("joinCode"))
            {
                Debug.LogError("Lobby missing joinCode");
                return false;
            }

            string _joinCode = lobby.Data["joinCode"].Value;

            try
            {
                var _allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: _joinCode);
                NetworkManager.Singleton.GetComponent<UnityTransport>()
                    .SetRelayServerData(_allocation.ToRelayServerData("dtls"));
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"Relay join failed: {e.Message}");
                return false;
            }

            bool ok = NetworkManager.Singleton.StartClient();
            if (!ok)
            {
                Debug.LogError("Failed to start client");
                return false;
            }

            return true;
        }

        private void OnLobbyEntryClicked(Unity.Services.Lobbies.Models.Lobby lobby, LobbyEntryUI lobbyEntryUI)
        {
            if (currentLobbyEntryUISelected)
            {
                currentLobbyEntryUISelected.OnDeselectedFeedback();
            }
            
            currentLobbySelected = lobby;
            currentLobbyEntryUISelected = lobbyEntryUI;
            currentLobbyEntryUISelected.OnSelectedFeedback();
        }

        void Clear()
        {
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Destroy(contentRoot.GetChild(i).gameObject);
            
            existingEntries.Clear();
        }
    }
}
