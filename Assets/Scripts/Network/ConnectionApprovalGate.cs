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

            // NET-02: keep the joiner's profile until it finishes synchronizing; LobbyPlayerInfoHolder takes it on
            // OnClientConnected and upserts the roster row itself (no follow-up RPC round-trip to lose).
            if (ConnectionPayload.TryParse(_request.Payload, out ConnectionPayload _payload))
            {
                s_pendingProfiles[_request.ClientNetworkId] = _payload;
            }
            else if (_request.ClientNetworkId != NetworkManager.ServerClientId)
            {
                Debug.LogWarning($"[ROSTER] missing or malformed connection payload from {_request.ClientNetworkId}; " +
                                 "falling back to the profile RPC.");
            }
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
        public static void ResetSessionStatics() => s_pendingProfiles.Clear();

#if UNITY_EDITOR
        // Domain reload is disabled in this project: drop any profile left by a previous Play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled() => s_pendingProfiles.Clear();
#endif
    }
}
