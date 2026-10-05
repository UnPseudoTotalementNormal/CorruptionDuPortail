#region

using Network.Services;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#endregion

namespace Network
{
    /// <summary>
    /// [LEAVE][PHASE 3] Pure decision seam for client-side host-drop detection. Extracted from the
    /// MonoBehaviour so the graceful-vs-abrupt discrimination is unit-testable in EditMode without a
    /// live NetworkManager / loopback. See <see cref="ClientDisconnectHandler"/> for the runtime wiring.
    /// </summary>
    public static class HostDropPolicy
    {
        /// <summary>
        /// True iff an <c>OnClientStopped</c> / transport-failure should surface the "host connection
        /// lost" notification and force a return to the menu.
        /// <paramref name="wasPureClient"/>: the local NGO instance was a pure client (client &amp; NOT
        /// server) — a host is excluded (host teardown is handled by ShutOffGame).
        /// <paramref name="expectedShutdown"/>: a graceful <c>ShutOffGame</c> or a self-initiated leave
        /// already flagged the shutdown as expected. Only a pure client that lost the session
        /// unexpectedly is treated as a host drop.
        /// <paramref name="joinHandshakeInProgress"/>: the menu is still waiting on a join verdict
        /// (<see cref="JoinHandshake"/>). A rejected join stops NGO exactly like a host drop, but the menu owns
        /// that failure and shows the server's own reason ("La partie a déjà commencé."), so this layer must stay
        /// quiet — otherwise it overwrites it with the generic "Connexion à l'hôte perdue"
        /// (investigation join-started-game-gate).
        /// </summary>
        public static bool ShouldNotifyHostLoss(
            bool wasPureClient,
            bool expectedShutdown,
            bool joinHandshakeInProgress = false)
        {
            return wasPureClient && !expectedShutdown && !joinHandshakeInProgress;
        }
    }

    /// <summary>
    /// [LEAVE][PHASE 3] Client resilience: detects an abrupt host loss and returns the local client to
    /// the menu with a notification, and surfaces the previously produced-but-never-shown
    /// <see cref="LobbyManager.OnLobbyError"/> strings. (The in-game "leave to menu" action lives on the
    /// PausePanel's <c>LeaveGameButton</c>, not here; this only exposes <see cref="NotifyExpectedShutdown"/>
    /// so that button — and the graceful host ShutOffGame — can suppress the abrupt-loss popup.)
    ///
    /// Lives on a DDoL bootstrap GameObject spawned via <see cref="RuntimeInitializeOnLoadMethod"/> — it
    /// owns NO scene/prefab state. The notification UI is a RAW uGUI placeholder built in code (owner
    /// constraint: functional, design-owned look comes later).
    ///
    /// Graceful vs abrupt: <see cref="ShutOffGame"/> (host-triggered) and the PausePanel leave button set a
    /// one-shot "expected shutdown" flag (<see cref="NotifyExpectedShutdown"/>) BEFORE the NGO shutdown, so
    /// the host-loss notification only fires on an UNEXPECTED disconnect.
    /// </summary>
    public class ClientDisconnectHandler : MonoBehaviour
    {
        private const string LogTag = "[LEAVE][PHASE3]";
        private const int MainMenuSceneIndex = 1; // BootScene=0, MainMenu=1, GameScene=2 (build settings)
        private const string HostLostMessage = "Connexion à l'hôte perdue";
        private const float NotificationSeconds = 6f;

        // NOT named "instance"/"Instance": that would trip StaticSingletonCensusGuardTests. This is only a
        // bootstrap dedup guard, not a service locator — nothing resolves the handler through it.
        private static ClientDisconnectHandler s_current;

        // One-shot flag: set by the graceful ShutOffGame path (GameManager) and by the PausePanel leave button
        // so OnClientStopped can tell an expected teardown from an abrupt host loss. Static because the callers
        // live in other types; consumed (reset) on the next stop. Domain reload is disabled -> reset on start.
        private static bool s_expectedShutdown;

        // True while a menu join is still waiting on its verdict (JoinHandshake). A server-rejected join tears NGO
        // down exactly like a host drop, and OnClientStopped fires BEFORE the waiting menu code can react — so
        // without this the generic host-loss popup won the race and the joiner saw "Connexion à l'hôte perdue"
        // instead of "La partie a déjà commencé.". NOT reset by OnClientStarted: the window opens right after
        // StartClient, i.e. after that callback has already run.
        private static bool s_joinHandshakeInProgress;

        private NetworkManager _subscribedNm;
        private bool _lobbySubscribed;
        private bool _localWasPureClient; // captured while connected (client && !server)
        private bool _handlingLoss;        // idempotency vs OnClientStopped + OnTransportFailure double fire

        // RAW placeholder notification UI (built in code). Colors below are neutral debug values, NOT a design palette.
        private Canvas _canvas;
        private GameObject _notificationPanel;
        private Text _notificationText;
        private Font _font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_current != null)
            {
                return;
            }

            var _go = new GameObject(nameof(ClientDisconnectHandler));
            _go.AddComponent<ClientDisconnectHandler>();
        }

        /// <summary>
        /// Mark the next NGO shutdown as EXPECTED (graceful ShutOffGame or the PausePanel self-leave) so the
        /// abrupt host-loss notification is suppressed. Static because the callers (GameManager.ShutOffGame,
        /// LeaveGameButton) live in other types and the flag must survive even if the handler was not yet resolved.
        /// </summary>
        public static void NotifyExpectedShutdown()
        {
            s_expectedShutdown = true;
        }

        /// <summary>
        /// Open (<c>true</c>) or close (<c>false</c>) the menu-owned join window. While it is open, an NGO stop is
        /// a join verdict — the menu reports it with the server's reason — not a host drop, so this layer stays
        /// silent and does NOT force a return to the menu. Always closed in a <c>finally</c> by
        /// <see cref="JoinHandshake.WaitForConnectedOrTimeout"/>, so a successful join cannot leave it latched and
        /// mute a later, genuine host loss.
        /// </summary>
        public static void SetJoinHandshakeInProgress(bool _inProgress)
        {
            s_joinHandshakeInProgress = _inProgress;
        }

        /// <summary>
        /// [LIVENESS B2] The client-side liveness layer detected the host has gone silent (no keepalives) and
        /// declares the host lost — route it through the SAME host-loss path as an abrupt transport drop
        /// (arch-liveness-heartbeat §8.4). It still consults the graceful <c>expectedShutdown</c> flag and the
        /// <c>_handlingLoss</c> one-shot, so a pause-leave / ShutOffGame never trips a false liveness host-loss,
        /// and a later real <c>OnClientStopped</c> cannot double-fire. No-op if the handler is not yet bootstrapped.
        /// </summary>
        public static void NotifyLivenessHostLost()
        {
            if (s_current != null)
            {
                s_current.HandlePotentialHostLoss("LivenessHostLost");
            }
        }

        private void Awake()
        {
            if (s_current != null && s_current != this)
            {
                Destroy(gameObject);
                return;
            }

            s_current = this;
            DontDestroyOnLoad(gameObject);

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }

            BuildUI();
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkManager();
            UnsubscribeFromLobbyManager();
            if (s_current == this)
            {
                s_current = null;
            }
        }

        private void Update()
        {
            // NetworkManager lives in BootScene and persists (NGO DDoL). Subscribe once it exists;
            // re-subscribe if the Singleton is ever replaced. NetworkManager.Singleton is null during
            // shutdown windows — the Unity == overload keeps this comparison safe.
            NetworkManager _nm = NetworkManager.Singleton;
            if (_nm != _subscribedNm)
            {
                UnsubscribeFromNetworkManager();
                if (_nm != null)
                {
                    SubscribeToNetworkManager(_nm);
                }
            }

            // LobbyManager is a DDoL singleton created lazily in the menu. Wire OnLobbyError once it exists.
            if (!_lobbySubscribed && LobbyManager.instance != null)
            {
                LobbyManager.instance.OnLobbyError += OnLobbyError;
                _lobbySubscribed = true;
            }
        }

        // --- NetworkManager wiring -------------------------------------------------------------------

        private void SubscribeToNetworkManager(NetworkManager _nm)
        {
            _subscribedNm = _nm;
            _nm.OnClientStarted += OnClientStarted;
            _nm.OnClientStopped += OnClientStopped;
            _nm.OnTransportFailure += OnTransportFailure;

            // If we subscribed after the client already came up, capture the role now.
            if (_nm.IsClient && !_nm.IsServer)
            {
                _localWasPureClient = true;
            }
        }

        private void UnsubscribeFromNetworkManager()
        {
            if (_subscribedNm == null)
            {
                return;
            }

            _subscribedNm.OnClientStarted -= OnClientStarted;
            _subscribedNm.OnClientStopped -= OnClientStopped;
            _subscribedNm.OnTransportFailure -= OnTransportFailure;
            _subscribedNm = null;
        }

        private void UnsubscribeFromLobbyManager()
        {
            if (_lobbySubscribed && LobbyManager.instance != null)
            {
                LobbyManager.instance.OnLobbyError -= OnLobbyError;
            }
            _lobbySubscribed = false;
        }

        private void OnClientStarted()
        {
            NetworkManager _nm = NetworkManager.Singleton;
            _localWasPureClient = _nm != null && _nm.IsClient && !_nm.IsServer;
            s_expectedShutdown = false; // fresh session (domain reload is disabled — never trust a stale flag)
            _handlingLoss = false;
            Debug.Log($"{LogTag} Client started (pureClient={_localWasPureClient}).");
        }

        private void OnClientStopped(bool _wasHost)
        {
            HandlePotentialHostLoss($"OnClientStopped(wasHost={_wasHost})");
        }

        private void OnTransportFailure()
        {
            HandlePotentialHostLoss("OnTransportFailure");
        }

        private void HandlePotentialHostLoss(string _source)
        {
            if (_handlingLoss)
            {
                return;
            }

            bool _expected = s_expectedShutdown;
            bool _joining = s_joinHandshakeInProgress;
            bool _shouldNotify = HostDropPolicy.ShouldNotifyHostLoss(_localWasPureClient, _expected, _joining);
            Debug.Log($"{LogTag} {_source}: pureClient={_localWasPureClient} expected={_expected} joining={_joining} -> notify={_shouldNotify}");

            // One-shot: consume the expected flag no matter which path we take.
            s_expectedShutdown = false;

            // Latch on EVERY path, not just the notify path. OnClientStopped and OnTransportFailure can BOTH
            // fire (and liveness adds a third source): if the first consumes an EXPECTED-shutdown flag here and
            // does NOT latch, the second re-evaluates with the now-cleared flag and pops a spurious
            // "Connexion à l'hôte perdue" on a GRACEFUL end — the exact failure this layer exists to prevent
            // (arch code-review, MED). _handlingLoss resets per session in OnClientStarted, so latching on the
            // suppressed path is safe.
            _handlingLoss = true;

            if (!_shouldNotify)
            {
                // Host teardown, graceful ShutOffGame, or the PausePanel self-leave — those paths own their
                // own return-to-menu; nothing to do here.
                return;
            }

            _localWasPureClient = false;

            // NET-05: the server may have disconnected us ON PURPOSE with a reason (finished loading after the game
            // started, loading too long). Show that reason instead of a misleading "host lost". Read BEFORE
            // ReturnToMenu shuts NGO down.
            string _reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : null;
            string _message = CorruptionDuPortail.Domain.RelayFallbackPolicy.HasServerReason(_reason) ? _reason : HostLostMessage;

            // Best-effort: drop the cloud lobby so we don't linger as a ghost member.
            TryLeaveLobby();

            ShowNotification(_message);
            ReturnToMenu();
        }

        // [LEAVE][PHASE 4] Explicit per-session static reset on the client return-to-menu path (abrupt host
        // loss). Domain reload is disabled, so an abrupt teardown that skips the scene-placed managers'
        // OnDestroy could strand a stale GameManager/CompositionRoot static into the next join. Both targets
        // are idempotent + null-safe, so calling this here (and again if teardown already ran) cannot double-free or NRE.
        private static void ResetSessionStatics()
        {
            GameLogic.GameManager.ResetSessionStatics();
            GameLogic.CompositionRoot.ResetSessionStatics();
        }

        private void TryLeaveLobby()
        {
            // LobbyManager is a DDoL singleton, so the fire-and-forget task survives the scene load.
            if (LobbyManager.instance != null && LobbyManager.instance.IsInLobby)
            {
                _ = LobbyManager.instance.LeaveLobby();
            }
        }

        private void ReturnToMenu()
        {
            // NGO is already stopped (that is why OnClientStopped fired). Belt-and-suspenders: clear any
            // stale listening state before loading the menu.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            ResetSessionStatics();

            if (SceneManager.GetActiveScene().buildIndex != MainMenuSceneIndex)
            {
                SceneManager.LoadScene(MainMenuSceneIndex);
            }
        }

        // --- OnLobbyError ----------------------------------------------------------------------------

        private void OnLobbyError(string _message)
        {
            Debug.Log($"{LogTag} LobbyError surfaced: {_message}");
            ShowNotification(_message);
        }

        // --- UI (RAW placeholder — design-owned look comes later) ------------------------------------

        private void BuildUI()
        {
            var _canvasGo = new GameObject("ClientDisconnectCanvas");
            _canvasGo.transform.SetParent(transform, false);
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above every other UI, the menu's LoginCanvas (1000) included: a join refusal or kick is reported while the
            // login screen can be up, and the message must not be hidden behind it (found by autoplay, 2026-10-05).
            _canvas.sortingOrder = 32000;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            BuildNotificationPanel();
        }

        private void BuildNotificationPanel()
        {
            _notificationPanel = new GameObject("Notification");
            _notificationPanel.transform.SetParent(_canvas.transform, false);
            var _bg = _notificationPanel.AddComponent<Image>();
            _bg.color = new Color(0f, 0f, 0f, 0.8f); // neutral debug backdrop for legibility, NOT a design choice
            var _rt = _notificationPanel.GetComponent<RectTransform>();
            _rt.anchorMin = new Vector2(0.5f, 1f);
            _rt.anchorMax = new Vector2(0.5f, 1f);
            _rt.pivot = new Vector2(0.5f, 1f);
            _rt.anchoredPosition = new Vector2(0f, -20f);
            _rt.sizeDelta = new Vector2(600f, 90f);

            var _textGo = new GameObject("Text");
            _textGo.transform.SetParent(_notificationPanel.transform, false);
            _notificationText = _textGo.AddComponent<Text>();
            _notificationText.font = _font;
            _notificationText.alignment = TextAnchor.MiddleCenter;
            _notificationText.color = Color.white;
            _notificationText.fontSize = 20;
            var _trt = _textGo.GetComponent<RectTransform>();
            _trt.anchorMin = Vector2.zero;
            _trt.anchorMax = Vector2.one;
            _trt.offsetMin = new Vector2(10f, 32f);
            _trt.offsetMax = new Vector2(-10f, 0f);

            var _btnGo = new GameObject("DismissButton");
            _btnGo.transform.SetParent(_notificationPanel.transform, false);
            var _btnImg = _btnGo.AddComponent<Image>();
            _btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var _btn = _btnGo.AddComponent<Button>();
            _btn.onClick.AddListener(HideNotification);
            var _brt = _btnGo.GetComponent<RectTransform>();
            _brt.anchorMin = new Vector2(0.5f, 0f);
            _brt.anchorMax = new Vector2(0.5f, 0f);
            _brt.pivot = new Vector2(0.5f, 0f);
            _brt.anchoredPosition = new Vector2(0f, 6f);
            _brt.sizeDelta = new Vector2(120f, 26f);

            var _btnTextGo = new GameObject("Text");
            _btnTextGo.transform.SetParent(_btnGo.transform, false);
            var _btnText = _btnTextGo.AddComponent<Text>();
            _btnText.font = _font;
            _btnText.alignment = TextAnchor.MiddleCenter;
            _btnText.color = Color.white;
            _btnText.text = "OK";
            var _btrt = _btnTextGo.GetComponent<RectTransform>();
            _btrt.anchorMin = Vector2.zero;
            _btrt.anchorMax = Vector2.one;
            _btrt.offsetMin = Vector2.zero;
            _btrt.offsetMax = Vector2.zero;

            _notificationPanel.SetActive(false);
        }

        private void ShowNotification(string _message)
        {
            if (_notificationText == null || _notificationPanel == null)
            {
                return;
            }

            _notificationText.text = _message;
            _notificationPanel.SetActive(true);

            CancelInvoke(nameof(HideNotification));
            Invoke(nameof(HideNotification), NotificationSeconds); // auto-hide fallback (no input needed)
        }

        private void HideNotification()
        {
            if (_notificationPanel != null)
            {
                _notificationPanel.SetActive(false);
            }
        }
    }
}
