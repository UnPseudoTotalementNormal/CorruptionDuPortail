#region

using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// NGO connection-approval gate. Fixes the "Rejoindre une partie déjà en cours" bug: once the game has
    /// left the lobby phase, an incoming client connection is REJECTED at the NGO handshake instead of
    /// silently joining. Mid-game there is nothing to seat the joiner — LobbyState.OnClientConnected (the
    /// only path that builds a Character) has already unsubscribed — so a late joiner became a
    /// character-less ghost occupying a connection slot. This gate stops the connection before that.
    ///
    /// Must be enabled BEFORE StartHost so the server expects and answers approval for every connecting
    /// client. The host's own local connection is approved because GameManager does not exist yet at
    /// StartHost time (GameScene, where GameManager is scene-placed, loads afterwards) — see
    /// <see cref="ShouldApprove"/>. Simulated bots (clientId &gt;= 100) never reach the transport, so they
    /// are unaffected.
    /// </summary>
    public static class ConnectionApprovalGate
    {
        /// <summary>
        /// Surfaced to the rejected client via <see cref="NetworkManager.DisconnectReason"/> so the join
        /// UI can explain the failure instead of showing a generic timeout.
        /// </summary>
        public const string GameInProgressReason = "La partie a déjà commencé.";

        /// <summary>
        /// Turns on NGO connection approval and installs the gate callback. Idempotent and null-safe.
        /// Call once on the host path just before <c>StartHost()</c>.
        /// </summary>
        public static void Enable(NetworkManager _networkManager)
        {
            if (_networkManager == null)
            {
                return;
            }

            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.ConnectionApprovalCallback = HandleApproval;
            s_joinPhase.Clear();
            TrackConnections(_networkManager);
        }

        /// <summary>
        /// Pure decision, unit-testable without a live NetworkManager: a client may join iff the game loop
        /// has not spawned yet (the pre-scene host-start window) OR it is still in the lobby phase.
        /// </summary>
        public static bool ShouldApprove(bool _gameManagerPresent, bool _isInLobbyPhase)
        {
            return !_gameManagerPresent || _isInLobbyPhase;
        }

        private static void HandleApproval(
            NetworkManager.ConnectionApprovalRequest _request,
            NetworkManager.ConnectionApprovalResponse _response)
        {
            GameManager _gameManager = GameManager.instance;
            bool _approve = ShouldApprove(
                _gameManager != null,
                _gameManager != null && _gameManager.IsInLobbyPhase);

            _response.Approved = _approve;
            // Characters are spawned manually by LobbyState.OnClientConnected, never via an auto player
            // prefab (none is wired) — so approval must NOT create a player object.
            _response.CreatePlayerObject = false;
            _response.Pending = false;

            if (!_approve)
            {
                _response.Reason = GameInProgressReason;
                Debug.Log($"[JOIN-GATE] Rejected connection {_request.ClientNetworkId} — game already started.");
                return;
            }

            ulong _clientId = _request.ClientNetworkId;
            bool _isHostSelf = _clientId == NetworkManager.ServerClientId;
            bool _hasPayload = ConnectionPayload.TryParse(_request.Payload, out ConnectionPayload _payload);

            // NET-05: refuse a different player build (silent deserialization failures otherwise).
            if (!_isHostSelf)
            {
                BuildVersionGate.Verdict _verdict = BuildVersionGate.Evaluate(
                    Application.version, Application.isEditor, _hasPayload ? _payload : null);
                if (_verdict == BuildVersionGate.Verdict.Rejected)
                {
                    _response.Approved = false;
                    _response.Reason = BuildVersionGate.MismatchReason;
                    Debug.Log($"[JOIN-GATE] Rejected connection {_clientId} — build '{(_hasPayload ? _payload.BuildVersion : "none")}' " +
                              $"differs from host build '{Application.version}'.");
                    return;
                }
                if (_verdict == BuildVersionGate.Verdict.AllowedEditorMismatch)
                {
                    Debug.LogWarning($"[JOIN-GATE] Build mismatch allowed because an Editor is involved: joiner " +
                                     $"'{(_hasPayload ? _payload.BuildVersion : "none")}', host '{Application.version}'.");
                }

                // NET-05: approved but not synchronized yet — blocks the lobby start until it finishes or leaves.
                s_joinPhase.Approved(_clientId, Time.realtimeSinceStartupAsDouble);
            }

            // NET-02: keep the joiner's profile until it finishes synchronizing; LobbyPlayerInfoHolder takes it on
            // OnClientConnected and upserts the roster row itself (no follow-up RPC round-trip to lose).
            if (_hasPayload)
            {
                s_pendingProfiles[_clientId] = _payload;
            }
            else if (!_isHostSelf)
            {
                Debug.LogWarning($"[ROSTER] missing or malformed connection payload from {_clientId}; " +
                                 "falling back to the profile RPC.");
            }
        }

        // ---- NET-05: approved-but-still-loading joiners ----

        private static readonly JoinPhaseTracker s_joinPhase = new();
        private static NetworkManager s_trackedNetworkManager;

        /// <summary>True while an approved joiner is still loading/synchronizing — the game must not start.</summary>
        public static bool HasSynchronizingClients => s_joinPhase.HasSynchronizingClients;

        /// <summary>Server-side: disconnects joiners still synchronizing after the join sync cap (90 s), so a stuck
        /// loader can never block the lobby forever. Reuses the existing stuck-load wording.</summary>
        public static void KickExpiredLoaders(NetworkManager _networkManager)
        {
            if (_networkManager == null || !_networkManager.IsServer || !s_joinPhase.HasSynchronizingClients)
            {
                return;
            }
            foreach (ulong _clientId in s_joinPhase.Expired(Time.realtimeSinceStartupAsDouble, JoinHandshake.SyncTotalTimeoutSeconds))
            {
                s_joinPhase.Left(_clientId);
                s_pendingProfiles.Remove(_clientId);
                Debug.Log($"[JOIN-GATE] Disconnecting {_clientId}: still loading after {JoinHandshake.SyncTotalTimeoutSeconds} s.");
                _networkManager.DisconnectClient(_clientId, JoinFailureMessage.StuckLoadMessage);
            }
        }

        private static void TrackConnections(NetworkManager _networkManager)
        {
            if (s_trackedNetworkManager != null)
            {
                s_trackedNetworkManager.OnClientConnectedCallback -= OnClientSynchronized;
                s_trackedNetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            }
            s_trackedNetworkManager = _networkManager;
            _networkManager.OnClientConnectedCallback += OnClientSynchronized;
            _networkManager.OnClientDisconnectCallback += OnClientLeft;
        }

        // Server: OnClientConnectedCallback fires at the end of the joiner's synchronization.
        private static void OnClientSynchronized(ulong _clientId)
        {
            NetworkManager _networkManager = s_trackedNetworkManager;
            if (_networkManager == null || !_networkManager.IsServer)
            {
                return;
            }
            s_joinPhase.Synchronized(_clientId);

            // NET-05: approved in the lobby but finished loading after the game started — nothing can seat it (no
            // character, no role). Send it back to the menu with the server's reason instead of leaving a ghost.
            GameManager _gameManager = GameManager.instance;
            if (_clientId != NetworkManager.ServerClientId && _gameManager != null && _gameManager.IsSpawned
                && !_gameManager.IsInLobbyPhase)
            {
                Debug.Log($"[JOIN-GATE] Disconnecting {_clientId}: finished loading after the game started.");
                s_pendingProfiles.Remove(_clientId);
                _networkManager.DisconnectClient(_clientId, GameInProgressReason);
            }
        }

        private static void OnClientLeft(ulong _clientId)
        {
            s_joinPhase.Left(_clientId);
        }

        // ---- NET-02: profiles received with the connection request, waiting for sync completion ----

        private static readonly Dictionary<ulong, ConnectionPayload> s_pendingProfiles = new();

        /// <summary>Server-side: hands over (and forgets) the profile a client sent with its connection request.</summary>
        public static bool TryTakePendingProfile(ulong _clientId, out ConnectionPayload _payload)
        {
            if (s_pendingProfiles.TryGetValue(_clientId, out _payload))
            {
                s_pendingProfiles.Remove(_clientId);
                return true;
            }
            return false;
        }

        /// <summary>Server-side: drops the profile of a client that left before finishing its synchronization.</summary>
        public static void DiscardPendingProfile(ulong _clientId) => s_pendingProfiles.Remove(_clientId);

        /// <summary>Per-session reset (called from CompositionRoot.ResetSessionStatics).</summary>
        public static void ResetSessionStatics()
        {
            s_pendingProfiles.Clear();
            s_joinPhase.Clear();
        }

#if UNITY_EDITOR
        // Domain reload is disabled in this project: drop any state left by a previous Play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_pendingProfiles.Clear();
            s_joinPhase.Clear();
            s_trackedNetworkManager = null;
        }
#endif
    }
}
