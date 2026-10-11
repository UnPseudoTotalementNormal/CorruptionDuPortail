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
using Network.Steam;
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

        private GameLobby currentLobbySelected;
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

        /// <summary>
        /// Steam build: join the lobby a friend invited us to (SteamInvites), through the same path as a click in the
        /// list: started game refused, password asked when the lobby has one.
        /// </summary>
        public void JoinFromSteamInvite(string _lobbyId)
        {
            if (_isJoining)
            {
                ReportJoinFailure(BusyForInviteMessage);
                return;
            }
            _isJoining = true;

            JoinFromSteamInviteAsync(_lobbyId).Forget();
        }

        private async UniTaskVoid JoinFromSteamInviteAsync(string _lobbyId)
        {
            try
            {
                loadingCanvasGroup.DoShowGroup();
                GameLobby _lobby = await LobbyManager.instance.GetLobbyInfo(_lobbyId);
                if (_lobby == null)
                {
                    HideLoadingSafe(); // GetLobbyInfo already reported why
                    return;
                }
                // The invite replaces whatever was picked in the list: no highlighted row, no half-typed password
                // (it would be sent to the invited lobby).
                if (currentLobbyEntryUISelected != null)
                {
                    currentLobbyEntryUISelected.OnDeselectedFeedback();
                    currentLobbyEntryUISelected = null;
                }
                enterPasswordCanvasGroup.DoHideGroup();
                passwordInputField.text = "";
                currentLobbySelected = _lobby;
                await JoinLobby(_lobby);
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (destruction de l'objet) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Échec de la connexion à la partie d'un ami: {e}");
                HideLoadingSafe();
            }
            finally
            {
                _isJoining = false;
            }
        }

        public const string BusyForInviteMessage = "Une connexion est déjà en cours : réessaie de rejoindre ton ami dans un instant.";

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
                HideLoadingSafe();
            }
            finally
            {
                _isJoining = false;
            }
        }

        private void Update()
        {
            if (LobbyManager.instance == null || !LobbyManager.instance.IsReady)
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
            if (LobbyManager.instance == null || !LobbyManager.instance.IsReady)
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
                List<GameLobby> lobbies;
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
                foreach (GameLobby lobby in lobbies)
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

        private void CreateEntry(GameLobby _lobby)
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
        
        private async UniTask<bool> JoinLobby(GameLobby lobby, string password = null)
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

                // Set BEFORE connecting: NGO synchronization loads GameScene by itself, and GameCodeText reads this
                // static in Start(). Assigning it after the handshake would leave the joiner's code label empty.
                GameCode.gameCode = lobby.Code;

                // Détecter le type de transport et se connecter en conséquence
                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    if (!SteamConnector.StartClient(NetworkManager.Singleton, lobby, password))
                    {
                        Debug.LogError("Failed to connect to game");
                        await AbortJoin(null);
                        return false;
                    }

                    // StartClient() only reports that the connect attempt STARTED. Wait for the ACTUAL verdict
                    // before loading GameScene — otherwise a server rejection (mid-game join) only lands once we
                    // are already in the game scene, where it degrades into "Connexion à l'hôte perdue" instead of
                    // the server's own "La partie a déjà commencé." (investigation join-started-game-gate).
                    ConnectFailReason _fail = await JoinHandshake.WaitForConnectedOrTimeout();
                    if (_fail != ConnectFailReason.None)
                    {
                        // Build BEFORE AbortJoin shuts NGO down — Shutdown may clear DisconnectReason.
                        await AbortJoin(JoinHandshake.BuildFailureMessage(_fail));
                        return false;
                    }
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    // The relay path owns its own handshake wait — and the dtls -> wss fallback — inside
                    // RelayConnector (investigation vpn-instant-disconnect, backlog #7). Its FailureMessage was
                    // built BEFORE any teardown, so AbortJoin can safely shut NGO down afterwards.
                    RelayConnectResult _result = await JoinWithUnityRelay(lobby);
                    if (!_result.Success)
                    {
                        await AbortJoin(_result.FailureMessage);
                        return false;
                    }
                }
                else
                {
                    Debug.LogError("Unknown transport type!");
                    loadingCanvasGroup.DoHideGroup();
                    return false;
                }

                // Do NOT load GameScene here. The handshake above only returns once NGO reported
                // SynchronizeComplete, which happens AFTER NGO itself loaded GameScene on this client
                // (EnableSceneManagement = 1). A raw SceneManager.LoadScene at this point re-loads the scene
                // OUTSIDE NGO: every replicated NetworkObject in it is destroyed while the DontDestroyOnLoad
                // NetworkManager stays connected, leaving the joiner desynced — a movable body with no
                // embodiment, and a census stuck at 1 because LobbyPlayerInfoHolder's client replica dies before
                // it can answer AskForPlayerInfoRpc (investigation client-join-lobby-desync).
                //
                // Touch NO UI past this point either: the menu scene is ALREADY unloaded, so every serialized
                // reference on this panel is a destroyed object. Hiding the loading group here threw an NRE that
                // fell into the catch below and ran AbortJoin — shutting down a perfectly good connection.
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to join lobby: {e.Message}");

                // Never tear down an ESTABLISHED session on a stray exception. AbortJoin shuts NGO down and leaves
                // the cloud lobby — correct while the join is still pending, catastrophic once we are connected and
                // synchronized (that is exactly how an NRE on a destroyed menu widget killed a healthy join).
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
                {
                    return false;
                }

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

            HideLoadingSafe();
        }

        // The join path awaits across frames, and NGO can unload the menu scene underneath us (a successful
        // synchronization switches the client to GameScene). Every serialized reference on this panel is then a
        // destroyed object, and touching one throws — which previously cascaded into an AbortJoin that killed a
        // healthy connection. Always hide through this guard from an async continuation.
        private void HideLoadingSafe()
        {
            if (loadingCanvasGroup == null)
            {
                return;
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

        private async UniTask<RelayConnectResult> JoinWithUnityRelay(GameLobby lobby)
        {
            // Récupérer le join code Relay depuis les données du lobby
            if (!lobby.TryGetData(GameLobby.RelayJoinCodeKey, out string _joinCode))
            {
                Debug.LogError("Lobby missing joinCode");
                return RelayConnectResult.Failed(null);
            }

            // Allocation + transport + StartClient + handshake wait, with the dtls -> wss fallback, all live
            // in the single decision point (investigation vpn-instant-disconnect, backlog #7).
            // Rejoin: remember this host, saved with the session token it hands back once we are seated.
            Network.RejoinSessionStore.SetConnectionTarget("relay", _joinCode, lobby.Code);
            return await RelayConnector.ConnectClientAsync(_joinCode);
        }

        private void OnLobbyEntryClicked(GameLobby lobby, LobbyEntryUI lobbyEntryUI)
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
