#region

using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// Shared client-side join handshake (investigation join-started-game-gate). Both menu join paths — by code
    /// (<c>MainMenu</c>) and by lobby list (<c>LobbySelectionPanel</c>) — MUST wait here before loading GameScene.
    ///
    /// <c>NetworkManager.StartClient()</c> only reports that the connect attempt STARTED; it never waits for the
    /// connection to establish. The lobby-list path used to load GameScene straight after it, so a connection the
    /// server rejected (<see cref="ConnectionApprovalGate"/>, mid-game join) only surfaced once the joiner was
    /// already in the game scene — as a generic "Connexion à l'hôte perdue". Waiting here pre-shoots that: the
    /// rejection is caught in the menu, with the server's own reason.
    /// </summary>
    public static class JoinHandshake
    {
        // Two-phase connect deadline (investigation join-load-timeout-kick). A joining client's IsConnectedClient
        // flips true only at NGO SynchronizeComplete, i.e. AFTER GameScene finished loading, so a single deadline
        // spanning the whole load kicked slow-PC joiners mid-load. Split it:
        // - ApprovalTimeoutSeconds: applies ONLY until synchronization starts (OnSynchronize) — fast-fails a
        //   dead / silent host that never approves us.
        // - SyncTotalTimeoutSeconds: generous absolute cap that lets an honest slow load finish yet still bounds a
        //   genuinely stuck synchronization. Decision lives in the pure ConnectHandshakePolicy.
        // Raised 10s -> 30s (investigation vpn-instant-disconnect): 10s was not enough for a client whose
        // Relay/DTLS handshake is slow (VPN, mobile, distant relay region) — it fast-failed an honest joiner as
        // a "dead host". Unreal's own InitialConnectTimeout is 60s; 30s stays well inside NGO's transport-level
        // connect budget (ConnectTimeoutMS 1000 x MaxConnectAttempts 60).
        public const float ApprovalTimeoutSeconds = 30f;
        public const float SyncTotalTimeoutSeconds = 90f;

        /// <summary>
        /// Bounded two-phase wait for the local client to ACTUALLY connect. Polls each frame (UniTask — never
        /// System.Threading.Tasks.Task) and defers the decision to the pure ConnectHandshakePolicy. The approval
        /// deadline only bites until NGO reports synchronization has STARTED (OnSynchronize) — after that the
        /// generous total deadline governs, so a slow-PC GameScene load is no longer kicked mid-load. Returns
        /// <see cref="ConnectFailReason.None"/> on success. Fast-fails on a torn-down / rejected NetworkManager
        /// (!IsListening — a reject fills DisconnectReason).
        /// </summary>
        public static async UniTask<ConnectFailReason> WaitForConnectedOrTimeout(
            float _approvalSeconds = ApprovalTimeoutSeconds,
            float _totalSeconds = SyncTotalTimeoutSeconds)
        {
            NetworkManager _nm = NetworkManager.Singleton;
            if (_nm == null)
            {
                return ConnectFailReason.SessionEnded;
            }

            if (_nm.IsConnectedClient)
            {
                return ConnectFailReason.None; // already connected — fast path
            }

            double _startTime = Time.realtimeSinceStartupAsDouble;
            bool _syncStarted = false;

            // OnSynchronize fires client-side once the server has approved us and begun synchronizing (a network
            // round-trip after StartClient, so it cannot arrive before we subscribe here). It marks the load as
            // STARTED, which cancels the short approval deadline. SceneManager is created inside StartClient
            // (before it returns), so it is non-null on this path; guard defensively anyway.
            NetworkSceneManager _sceneManager = _nm.SceneManager;
            NetworkSceneManager.OnSynchronizeDelegateHandler _onSynchronize = _clientId => _syncStarted = true;
            if (_sceneManager != null)
            {
                _sceneManager.OnSynchronize += _onSynchronize;
            }

            // Claim the failure for the menu. A server rejection stops NGO, and ClientDisconnectHandler's
            // OnClientStopped fires BEFORE our next poll — it would pop "Connexion à l'hôte perdue" and beat the
            // real reason to the screen. Closed in the finally, always.
            ClientDisconnectHandler.SetJoinHandshakeInProgress(true);

            try
            {
                while (true)
                {
                    NetworkManager _current = NetworkManager.Singleton;
                    bool _sessionAlive = _current != null && _current.IsListening;
                    bool _isConnected = _current != null && _current.IsConnectedClient;
                    double _elapsed = Time.realtimeSinceStartupAsDouble - _startTime;

                    ConnectWaitState _state = ConnectHandshakePolicy.Evaluate(
                        _elapsed, _syncStarted, _isConnected, _sessionAlive,
                        _approvalSeconds, _totalSeconds);

                    if (_state.Outcome == ConnectWaitOutcome.Connected)
                    {
                        return ConnectFailReason.None;
                    }
                    if (_state.Outcome == ConnectWaitOutcome.Failed)
                    {
                        return _state.Reason;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }
            finally
            {
                ClientDisconnectHandler.SetJoinHandshakeInProgress(false);

                if (_sceneManager != null)
                {
                    _sceneManager.OnSynchronize -= _onSynchronize;
                }
            }
        }

        /// <summary>
        /// Wording for a failed join. Reads the server-supplied <c>DisconnectReason</c> (a rejection fills it) and
        /// defers to the pure <see cref="JoinFailureMessage"/>. MUST be called BEFORE
        /// <c>NetworkManager.Shutdown()</c> — the shutdown may clear DisconnectReason.
        /// </summary>
        public static string BuildFailureMessage(ConnectFailReason _reason)
        {
            NetworkManager _nm = NetworkManager.Singleton;
            return JoinFailureMessage.Build(_nm != null ? _nm.DisconnectReason : null, _reason);
        }
    }
}
