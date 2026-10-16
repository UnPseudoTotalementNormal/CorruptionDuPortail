#region

using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Extensions;
using Network;
using Network.Services;
using TMPro;
using UI.Lobby;
using Unity.Netcode;
using Netcode.Transports.Facepunch;
using Network.Steam;
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

        [Tooltip("The role book (UI Toolkit overlay of this scene) opened by the \"Personnages\" button.")]
        [SerializeField] private RoleBook.RoleBookController roleBook;

        [Tooltip("The cat customization screen (UI Toolkit overlay of this scene), opened by clicking the menu's cat.")]
        [SerializeField] private CatCustomization.CatCustomizationController catCustomization;

        private string lobbyCreatingName = "";

        // Garde de rentrance pour éviter le double déclenchement Host/Join.
        private bool _isBusy;

        // The two-phase connect wait and its deadlines now live in the shared Network.JoinHandshake, so the
        // lobby-list join path (LobbySelectionPanel) uses the exact same handshake instead of loading GameScene
        // blind after StartClient (investigation join-started-game-gate).

        private void Start()
        {
            CreateRejoinButton();
            CreateRoleBookButton();
            CreateMenuCat();
            closeHostMenu.onClick.AddListener(OnCloseHostButtonClicked);
            openHostMenu.onClick.AddListener(OnOpenHostButtonClicked);
        
            openJoinMenu.onClick.AddListener(OnJoinMenuButtonClicked);
            closeJoinMenu.onClick.AddListener(OnCloseJoinMenuButtonClicked);
        
            _hostButton.onClick.AddListener(OnHostButtonClicked);
            _joinWithCodeButton.onClick.AddListener(OnJoinButtonClicked);
            lobbyNameInputField.onValueChanged.AddListener(OnLobbyNameValueChange);
            
            quitGameButton.onClick.AddListener(OnQuitGameButtonClicked);

            SteamInvites.JoinRequested += OnSteamJoinRequested;
            OnSteamJoinRequested(); // a request made before the menu loaded (Steam started the game to join a friend)
        }

        // Steam build: a friend's invite or "Rejoindre la partie" on their profile. Runs the server browser's join,
        // so a password, a full or started game are handled exactly as a click in the list.
        private void OnSteamJoinRequested()
        {
            if (!SteamInvites.TryTakePending(out string _lobbyId))
            {
                return;
            }
            if (_isBusy)
            {
                Debug.Log($"[STEAM] Join request for lobby {_lobbyId} ignored: the menu is already hosting or joining.");
                LobbyManager.instance?.ReportError(LobbySelectionPanel.BusyForInviteMessage);
                return;
            }

            LobbySelectionPanel _browser = FindFirstObjectByType<LobbySelectionPanel>(FindObjectsInactive.Include);
            if (_browser == null)
            {
                Debug.LogError("[STEAM] No server browser in the menu: cannot join the friend's game.");
                return;
            }
            hostMenuCanvasGroup.DoHideGroup();
            joinMenuCanvasGroup.DoShowGroup();
            _browser.JoinFromSteamInvite(_lobbyId);
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

        // ---- Role book: every role of the game, browsable from the menu ----

        // Built from the join button at runtime like the rejoin button: same look, no scene edit (visuals are
        // placeholders, design-owned). The Host / Join / Quit column is full: it sits in the bottom-left corner.
        private void CreateRoleBookButton()
        {
            if (roleBook == null || openJoinMenu == null)
            {
                return;
            }

            GameObject _copy = Instantiate(openJoinMenu.gameObject, openJoinMenu.transform.parent);
            _copy.name = "RoleBookButton";
            var _rect = (RectTransform)_copy.transform;
            var _source = (RectTransform)openJoinMenu.transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.zero;
            _rect.pivot = Vector2.zero;
            _rect.anchoredPosition = new Vector2(40f, 40f);
            _rect.sizeDelta = _source.sizeDelta;
            foreach (TMP_Text _label in _copy.GetComponentsInChildren<TMP_Text>(true))
            {
                _label.text = "Personnages";
                _label.enableWordWrapping = false;
                _label.fontSizeMax = _label.fontSize;
                _label.fontSizeMin = _label.fontSize * 0.5f;
                _label.enableAutoSizing = true;
            }
            var _button = _copy.GetComponent<Button>();
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(roleBook.Open);
            roleBook.Opened += HideMenuForOverlay;
            roleBook.Closed += ShowMenuAfterOverlay;
        }

        // ---- The player's cat (customization): standing by the buttons, a click opens the customization screen ----

        // Built at runtime like the other added buttons: a RawImage showing the cat stage's texture, a Button on it and
        // a line under it (approved mockup personnalisation v1, B2). Bottom right, clear of the Host / Join / Quit
        // column and of the audio panel.
        private void CreateMenuCat()
        {
            if (catCustomization == null || catCustomization.Stage == null || openJoinMenu == null)
            {
                return;
            }

            // Sized on the canvas (approved mockup: the cat stands about 0.42 of the screen high), not on the buttons.
            var _canvasRect = (RectTransform)openJoinMenu.GetComponentInParent<Canvas>().rootCanvas.transform;
            float _screenHeight = _canvasRect.rect.height;
            RenderTexture _texture = catCustomization.Stage.Texture;

            var _cat = new GameObject("MenuCat", typeof(RectTransform), typeof(RawImage), typeof(Button));
            _cat.transform.SetParent(openJoinMenu.transform.parent, false);
            var _rect = (RectTransform)_cat.transform;
            _rect.anchorMin = new Vector2(0.84f, 0f);
            _rect.anchorMax = new Vector2(0.84f, 0f);
            _rect.pivot = new Vector2(0.5f, 0f);
            float _height = _screenHeight * 0.42f;
            _rect.sizeDelta = new Vector2(_height * _texture.width / _texture.height, _height);
            _rect.anchoredPosition = new Vector2(0f, _screenHeight * 0.075f);
            var _image = _cat.GetComponent<RawImage>();
            _image.texture = _texture;
            var _button = _cat.GetComponent<Button>();
            _button.targetGraphic = _image;
            _button.onClick.AddListener(catCustomization.Open);

            // The line under the cat: the join button's label style (font, colour), on one line.
            TMP_Text _model = openJoinMenu.GetComponentInChildren<TMP_Text>(true);
            var _line = new GameObject("MenuCatLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            _line.transform.SetParent(_cat.transform, false);
            var _lineRect = (RectTransform)_line.transform;
            _lineRect.anchorMin = new Vector2(0f, 0f);
            _lineRect.anchorMax = new Vector2(1f, 0f);
            _lineRect.pivot = new Vector2(0.5f, 1f);
            _lineRect.sizeDelta = new Vector2(_rect.sizeDelta.x * 0.4f, _screenHeight * 0.05f);
            _lineRect.anchoredPosition = Vector2.zero;
            var _text = _line.GetComponent<TextMeshProUGUI>();
            _text.text = "Personnaliser mon chat";
            if (_model != null)
            {
                _text.font = _model.font;
            }
            _text.fontSize = _screenHeight * 0.036f;
            _text.color = new Color(0.96f, 0.85f, 0.66f);
            _text.fontStyle = FontStyles.Italic;
            _text.alignment = TextAlignmentOptions.Center;
            _text.enableWordWrapping = false;
            _text.raycastTarget = false;

            catCustomization.Opened += HideMenuForOverlay;
            catCustomization.Closed += ShowMenuAfterOverlay;
        }

        // The menu's screen-space canvas draws over the UI Toolkit screens (role book, cat customization) and would
        // take their clicks: while one is open, everything but the menu's background is put away, then brought back.
        private readonly List<GameObject> _hiddenForOverlay = new();

        private void HideMenuForOverlay()
        {
            _hiddenForOverlay.Clear();
            foreach (Transform _child in transform)
            {
                if (_child.name == "Background" || !_child.gameObject.activeSelf)
                {
                    continue;
                }
                _child.gameObject.SetActive(false);
                _hiddenForOverlay.Add(_child.gameObject);
            }
        }

        private void ShowMenuAfterOverlay()
        {
            foreach (GameObject _child in _hiddenForOverlay)
            {
                if (_child != null)
                {
                    _child.SetActive(true);
                }
            }
            _hiddenForOverlay.Clear();
        }

        private void OnDestroy()
        {
            SteamInvites.JoinRequested -= OnSteamJoinRequested;
            if (roleBook != null)
            {
                roleBook.Opened -= HideMenuForOverlay;
                roleBook.Closed -= ShowMenuAfterOverlay;
            }
            if (catCustomization != null)
            {
                catCustomization.Opened -= HideMenuForOverlay;
                catCustomization.Closed -= ShowMenuAfterOverlay;
            }
        }

        // ---- Rejoin (feat/player-rejoin): back into the game this player dropped from ----

        private Button _rejoinButton;

        // Shown only when this PC holds a fresh session of a game it was seated in (token + how to reach the host),
        // reached through this build's online stack (the Relay and Steam builds share the PC's PlayerPrefs).
        // Built from the join button at runtime: same look, no scene edit (visuals are placeholders, design-owned).
        private void CreateRejoinButton()
        {
            if (!Network.RejoinSessionStore.TryLoad(out Network.RejoinSessionStore.Session _session) ||
                !OnlineFlavorRules.CanRejoin(GameFlavor.Current, _session.hostKind) || openJoinMenu == null)
            {
                return;
            }
            if (_rejoinButton != null)
            {
                _rejoinButton.gameObject.SetActive(true);
                return;
            }

            GameObject _copy = Instantiate(openJoinMenu.gameObject, openJoinMenu.transform.parent);
            _copy.name = "RejoinButton";
            var _rect = (RectTransform)_copy.transform;
            var _source = (RectTransform)openJoinMenu.transform;
            // The Host / Join / Quit column has no room left: the button sits alone at the top centre, above the
            // logo, wide enough for its label on one line.
            _rect.anchorMin = new Vector2(0.5f, 1f);
            _rect.anchorMax = new Vector2(0.5f, 1f);
            _rect.pivot = new Vector2(0.5f, 1f);
            _rect.sizeDelta = new Vector2(_source.rect.width * 1.6f, _source.rect.height); // clear of the audio panel
            _rect.anchoredPosition = new Vector2(0f, -_source.rect.height * 0.8f);
            foreach (TMP_Text _label in _copy.GetComponentsInChildren<TMP_Text>(true))
            {
                _label.text = "Rejoindre la partie en cours";
                _label.enableWordWrapping = false;
                _label.fontSizeMax = _label.fontSize;
                _label.fontSizeMin = _label.fontSize * 0.5f;
                _label.enableAutoSizing = true; // one line, as large as the button allows
            }
            _rejoinButton = _copy.GetComponent<Button>();
            _rejoinButton.onClick.RemoveAllListeners();
            _rejoinButton.onClick.AddListener(OnRejoinButtonClicked);
        }

        /// <summary>Looks at the saved session again (the dev / autoplay session key can be set after this menu started).</summary>
        public void RefreshRejoinButton()
        {
            if (Network.RejoinSessionStore.TryLoad(out _))
            {
                CreateRejoinButton();
            }
            else if (_rejoinButton != null)
            {
                _rejoinButton.gameObject.SetActive(false);
            }
        }

        private static async UniTask<bool> StartRejoinClientAsync()
        {
            Network.ClientConnectionPayload.Apply(NetworkManager.Singleton);
            if (!NetworkManager.Singleton.StartClient())
            {
                throw new System.Exception("Failed to start client");
            }
            ConnectFailReason _fail = await JoinHandshake.WaitForConnectedOrTimeout();
            if (_fail != ConnectFailReason.None)
            {
                LobbyManager.instance?.ReportError(JoinHandshake.BuildFailureMessage(_fail));
            }
            return _fail == ConnectFailReason.None;
        }

        private static bool TrySplitHostPort(string _hostAddress, out string _address, out ushort _port)
        {
            _address = string.Empty;
            _port = 0;
            int _colon = string.IsNullOrEmpty(_hostAddress) ? -1 : _hostAddress.LastIndexOf(':');
            if (_colon <= 0)
            {
                return false;
            }
            _address = _hostAddress.Substring(0, _colon);
            return ushort.TryParse(_hostAddress.Substring(_colon + 1), out _port);
        }

        public void OnRejoinButtonClicked()
        {
            if (_isBusy)
            {
                return;
            }
            _isBusy = true;
            OnRejoinButtonClickedAsync().Forget();
        }

        private async UniTaskVoid OnRejoinButtonClickedAsync()
        {
            loadingCanvasGroup.DoShowGroup();
            bool _ok = false;
            try
            {
                if (!Network.RejoinSessionStore.TryLoad(out Network.RejoinSessionStore.Session _session))
                {
                    throw new System.Exception("No game to rejoin");
                }
                GameCode.gameCode = _session.lobbyCode;
                // The cloud lobby is locked once the game started: go straight to the host.
                Network.RejoinSessionStore.SetConnectionTarget(_session.hostKind, _session.hostAddress, _session.lobbyCode);
                if (_session.hostKind == "relay")
                {
                    RelayConnectResult _result = await RelayConnector.ConnectClientAsync(_session.hostAddress);
                    _ok = _result.Success;
                    if (!_ok && !string.IsNullOrEmpty(_result.FailureMessage))
                    {
                        LobbyManager.instance?.ReportError(_result.FailureMessage);
                    }
                }
                else if (_session.hostKind == "steam" && ulong.TryParse(_session.hostAddress, out ulong _hostSteamId))
                {
                    var _transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
                    if (_transport == null)
                    {
                        throw new System.Exception("FacepunchTransport not found on NetworkManager!");
                    }
                    _transport.targetSteamId = _hostSteamId;
                    _ok = await StartRejoinClientAsync();
                }
                else if (_session.hostKind == "direct" && TrySplitHostPort(_session.hostAddress, out string _address, out ushort _port))
                {
                    // A host reached by address (LAN / dev builds, the autoplay): plain UDP transport, no Relay.
                    var _transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    if (_transport == null)
                    {
                        throw new System.Exception("UnityTransport not found on NetworkManager!");
                    }
                    NetworkManager.Singleton.NetworkConfig.NetworkTransport = _transport;
                    _transport.SetConnectionData(_address, _port);
                    _ok = await StartRejoinClientAsync();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[REJOIN] Error rejoining the game: {e}");
            }
            finally
            {
                if (!_ok)
                {
                    // Read BEFORE the shutdown (it may clear the reason).
                    bool _refused = NetworkManager.Singleton != null &&
                                    CorruptionDuPortail.Domain.RelayFallbackPolicy.HasServerReason(NetworkManager.Singleton.DisconnectReason);
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    {
                        NetworkManager.Singleton.Shutdown();
                    }
                    // Refused by the host (seat no longer reserved, game over): nothing left to rejoin. A connection
                    // that merely failed keeps the session, so the player can try again while his seat is reserved.
                    if (_refused)
                    {
                        Network.RejoinSessionStore.Clear();
                        if (_rejoinButton != null)
                        {
                            _rejoinButton.gameObject.SetActive(false);
                        }
                    }
                    loadingCanvasGroup.DoHideGroup();
                }
                _isBusy = false;
            }
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
                GameCode.gameCode = _lobby.Code;

                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    // A Steam lobby has no password of its own: the host checks it at connection, and this path
                    // has no field to type one.
                    if (_lobby.HasPassword)
                    {
                        LobbyManager.instance.ReportError(SteamConnector.PasswordNeedsListMessage);
                        throw new System.Exception(SteamConnector.PasswordNeedsListMessage);
                    }
                    if (!SteamConnector.StartClient(NetworkManager.Singleton, _lobby, null))
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


        private async UniTask<RelayConnectResult> JoinWithUnityRelay(GameLobby lobby)
        {
            // Récupérer le join code Relay depuis les données du lobby
            if (!lobby.TryGetData(GameLobby.RelayJoinCodeKey, out string _joinCode))
            {
                Debug.Log("Lobby missing joinCode");
                return RelayConnectResult.Failed(null);
            }

            // Rejoin: remember this host, saved with the session token it hands back once we are seated.
            Network.RejoinSessionStore.SetConnectionTarget("relay", _joinCode, lobby.Code);
            // Allocation + transport + StartClient + handshake wait, with the dtls -> wss fallback, all live
            // in the single decision point (investigation vpn-instant-disconnect, backlog #7).
            return await RelayConnector.ConnectClientAsync(_joinCode);
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
                _lobbySettings.data = new Dictionary<string, string>()
                {
                    { nameof(LobbyCreationSettings.LobbyCustomDataKeys.Language), "Français" }
                };

                string connectionData = null;
                if (NetworkTransportDetector.IsUsingFacepunch())
                {
                    // Steam lobbies have no password of their own: the host's connection approval checks it.
                    connectionData = SteamConnector.StartHost(NetworkManager.Singleton, password);
                    if (!string.IsNullOrEmpty(connectionData))
                    {
                        _lobbySettings.data[GameLobby.HostSteamIdKey] = connectionData;
                    }
                }
                else if (NetworkTransportDetector.IsUsingUnityRelay())
                {
                    connectionData = await HostWithUnityRelay(15);
                    if (!string.IsNullOrEmpty(connectionData))
                    {
                        _lobbySettings.data[GameLobby.RelayJoinCodeKey] = connectionData;
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

                GameLobby _lobby = await LobbyManager.instance.CreateLobby(_lobbySettings);
                if (_lobby == null)
                {
                    throw new System.Exception("Failed to create lobby");
                }

                GameCode.gameCode = _lobby.Code;
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