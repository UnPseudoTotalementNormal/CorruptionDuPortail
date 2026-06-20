#region

using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace Smartphone.Apps.Lobby
{
    /// <summary>
    /// Quick-dev lobby-menu-on-tablet (2026-06-20). Lane-A driver that auto-raises the tablet onto the Lobby
    /// SmartphoneApp on Lobby entry and stands it down otherwise. A PURE REACTION to the replicated
    /// <c>currentGameStateIndex</c> (mirrors <see cref="Avatars.AvatarCameraArbiter"/>): subscribe + prime in
    /// <see cref="Start"/>, unsubscribe in <see cref="OnDestroy"/>; it NEVER writes the index. Closable — it
    /// only RAISES the tablet on Lobby entry; the player lowers/swipes via the existing TabletToggleInput and
    /// carousel, untouched. Presentation-only MonoBehaviour: lane A, reads the narrow IGameStateQuery slice
    /// off the serialized concrete GameManager (Unity can't serialize an interface), like the arbiter.
    /// </summary>
    public class LobbyAppPresenter : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private SmartphoneController smartphone;
        [SerializeField] private SmartphoneApp lobbyApp;
        // Where to re-home the tablet when leaving the Lobby, so it never gets stuck showing the now-inactive
        // lobby app (the carousel only SKIPS inactive apps, it does not navigate away from a shown one). Wire
        // the default app (InfoTable).
        [SerializeField] private SmartphoneApp fallbackApp;

        private IGameStateQuery Query => gameManager;
        private bool _subscribed;

        private void Awake()
        {
            Assert.IsNotNull(gameManager, "LobbyAppPresenter.gameManager is not wired — wire it in GameScene (like AvatarCameraArbiter).");
            Assert.IsNotNull(smartphone, "LobbyAppPresenter.smartphone is not wired — wire the Tablet's SmartphoneController.");
            Assert.IsNotNull(lobbyApp, "LobbyAppPresenter.lobbyApp is not wired — wire the Lobby SmartphoneApp.");
            Assert.IsNotNull(fallbackApp, "LobbyAppPresenter.fallbackApp is not wired — wire the default app (InfoTable) to re-home the tablet on Lobby exit.");
        }

        private void Start()
        {
            // Pure reaction (mirrors BoardCameraManager / AvatarCameraArbiter): subscribe + prime with the
            // current value. Never writes the index.
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
            _subscribed = true;
            OnGameStateChanged(Query.currentGameStateIndex.Value, Query.currentGameStateIndex.Value);
        }

        private void OnDestroy()
        {
            if (_subscribed && gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            bool _isLobby = Query.GetGameState(_newValue) is LobbyState;

            // The lobby app is only reachable during the Lobby — the carousel skips an inactive app.
            lobbyApp.isActive = _isLobby;

            if (_isLobby)
            {
                // Raise the tablet onto the lobby app. Not forced to stay: the player can lower/swipe it.
                smartphone.TryOpenPanel();
                smartphone.GoToApp(lobbyApp);
            }
            else if (smartphone.currentApp == lobbyApp)
            {
                // Leaving the Lobby while the tablet is showing the (now-inactive) lobby app: re-home it to the
                // default app so it is not stuck on a dead view the carousel would only skip past.
                smartphone.GoToApp(fallbackApp);
            }
        }
    }
}
