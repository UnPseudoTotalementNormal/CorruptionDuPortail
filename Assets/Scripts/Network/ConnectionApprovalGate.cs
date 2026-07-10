#region

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
            }
        }
    }
}
