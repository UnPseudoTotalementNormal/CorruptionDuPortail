#region

using System.Collections;
using Extensions;
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

            // Render on top of the 3D: the lobby app is extracted from the Screen-Space overlay so its subtree
            // inherited the UI layer; put it (the tablet host's concern) on the Phone layer, which PhoneCamera
            // draws depth-cleared over the world (the main camera culls Phone). The runtime-built role-slider
            // widgets are layered to their container by RoleAttributionSettingTab (it can't be done here — they
            // don't exist yet). Static-content layer; the GameObjects need it per-element for camera culling.
            gameObject.SetLayerRecursively("Phone");
        }

        private void Start()
        {
            // Pure reaction (mirrors BoardCameraManager / AvatarCameraArbiter): subscribe + prime with the
            // current value. Never writes the index.
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
            _subscribed = true;
            // Defer the initial prime: SmartphoneController.Start force-closes the tablet on its FIRST frame
            // (its WaitAFrameAndClosePhone), which would clobber a Lobby raise issued synchronously here and
            // leave the player on a CLOSED tablet on initial Lobby entry (the common fresh-boot case — the
            // 0->0 index write raises no OnValueChanged, so nothing re-opens it). Prime after that boot
            // self-close so the raise sticks; real later transitions are still handled live via OnValueChanged.
            StartCoroutine(PrimeAfterSmartphoneBoot());
        }

        private IEnumerator PrimeAfterSmartphoneBoot()
        {
            // The smartphone self-closes on frame 1; wait two frames so the prime lands after it regardless of
            // the (unordered) coroutine resume order within a frame.
            yield return null;
            yield return null;
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
                return;
            }

            // Act ONLY on the Lobby → game transition (the game launches), not on every later phase change —
            // the board phases own their tablet open/close via openOnCamera. Re-home a tablet still showing the
            // now-inactive lobby app to the default app, then close it.
            bool _wasLobby = Query.GetGameState(_previousValue) is LobbyState;
            if (_wasLobby)
            {
                if (smartphone.currentApp == lobbyApp)
                {
                    smartphone.GoToApp(fallbackApp);
                }
                smartphone.TryClosePanel();
            }
        }
    }
}
