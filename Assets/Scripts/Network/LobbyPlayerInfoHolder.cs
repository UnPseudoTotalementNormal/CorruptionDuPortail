#region

using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Network.Player;
using Unity.Netcode;
using Characters;
using GameLogic;
using UnityEngine;

#endregion

namespace Network
{
    public class LobbyPlayerInfoHolder : NetworkBehaviour
    {
        // Story 10.4 (Epic 10 / D4): recorded-callers-only façade. The migrated consumers (4 player-name
        // powers via Power's base field, Character via lane C) now resolve this holder through the
        // composition root; the remaining direct readers are the unregistered UI leaves
        // (PlayerButtonObject, ConnectedPlayerPanel, ChatPanel → Epic 12.2), the UlongExtensions static
        // (→ 10.5), and CharacterManager.AddDebugPlayer (debug). Guard #1 forbids the qualified instance
        // accessor in the migrated set; this holder itself uses the bare `instance` self-ref below.
        public static LobbyPlayerInfoHolder instance { get; private set; } // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests

        // NET-01 (epic-network-sync-hardening): the roster is ONE full-value snapshot in a NetworkVariable, never a
        // NetworkList (index deltas + NGO #3280 late-join duplicate made replicas lose other players' rows). Every
        // server write assigns a NEW snapshot; see PlayerRosterSnapshot for the rules.
        private readonly NetworkVariable<PlayerRosterSnapshot> _roster = new(new PlayerRosterSnapshot());

        /// <summary>The replicated roster rows, unique by clientId, in first-join order. Read-only on every peer.</summary>
        public IReadOnlyList<PlayerInfo> playerInfos => _roster.Value != null ? _roster.Value.Entries : Array.Empty<PlayerInfo>();

        /// <summary>Raised on every peer whenever the replicated roster changes (and once on spawn).</summary>
        public event System.Action onRosterChanged;

        // Story 10.4 (Epic 10 / D4): CharacterManager resolved once here (lane C) so this holder stops
        // reaching the locator for GetSafeRpcTarget (clears the §4a CharacterManager-census row). Resolved
        // null-tolerant (no Assert): GetSafeRpcTarget is reached ONLY on the server path (OnClientConnected
        // is registered server-only, and is also fired in-line below during this OnNetworkSpawn), where the
        // CharacterManager registry is populated exactly as the old .instance read required. On clients the
        // field may stay null and is never dereferenced — identical to the prior behaviour. NFR5: the
        // GetSafeRpcTarget call stays verbatim on the concrete CharacterManager.
        private CharacterManager characterManager;

        // NET-03 lane A (scene-wired in GameScene): read only to know whether a disconnect happens in the lobby
        // (row removed) or mid-game (row kept, flagged hasLeft). Never resolved in OnNetworkSpawn (spawn-order race).
        [SerializeField] private GameManager gameManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            characterManager = CompositionRoot.For(NetworkManager).CharacterManager;
            _roster.OnValueChanged += OnRosterValueChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                OnClientConnected(NetworkManager.LocalClient.ClientId);
            }

            // A late joiner receives the roster in the spawn payload without OnValueChanged: notify once so
            // subscribers that wired before spawn render the initial state.
            onRosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _roster.OnValueChanged -= OnRosterValueChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void OnRosterValueChanged(PlayerRosterSnapshot _previous, PlayerRosterSnapshot _current)
        {
            onRosterChanged?.Invoke();
        }

        // Server-only single write point: assigning a NEW snapshot is what marks the NetworkVariable dirty.
        private void SetRosterServer(PlayerRosterSnapshot _next)
        {
            if (!IsServer || !IsSpawned || _next == null || ReferenceEquals(_next, _roster.Value))
            {
                return;
            }
            _roster.Value = _next;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            ConnectionApprovalGate.DiscardPendingProfile(clientId);
            if (!IsSpawned) return;

            // NET-03: mid-game, the leaver's character stays on the board (chained), so its row is KEPT and flagged —
            // removing it made every client lose that player's name. Lobby leaves still remove the row. An unwired /
            // unspawned GameManager reads as lobby (= the previous always-remove behaviour).
            bool _inLobby = gameManager == null || !gameManager.IsSpawned || gameManager.IsInLobbyPhase;
            SetRosterServer(_inLobby ? _roster.Value.WithRemoved(clientId) : _roster.Value.WithLeft(clientId));
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer || !IsSpawned) return;

            // NET-02: the host's own profile is local — no RPC to itself.
            if (clientId == NetworkManager.LocalClientId)
            {
                PlayerInfo _host = LocalPlayerInfoHolder.playerInfo;
                _host.playerClientId = clientId;
                SetRosterServer(_roster.Value.WithUpsert(_host));
                return;
            }

            // NET-05: a joiner that finished loading after the game started is disconnected by ConnectionApprovalGate;
            // it must not get a roster row (it would show up as a phantom "(parti)" player).
            if (gameManager != null && gameManager.IsSpawned && !gameManager.IsInLobbyPhase)
            {
                ConnectionApprovalGate.DiscardPendingProfile(clientId);
                return;
            }

            // NET-02: a joiner's profile arrived WITH its connection request (atomic with approval) — upsert it now,
            // keyed on the transport's clientId. The ask/answer RPC below is only the fallback for a client that sent
            // no (or a malformed) payload.
            if (ConnectionApprovalGate.TryTakePendingProfile(clientId, out ConnectionPayload _payload))
            {
                SetRosterServer(_roster.Value.WithUpsert(new PlayerInfo
                {
                    playerClientId = clientId,
                    playerName = PlayerNameSanitizer.Sanitize(_payload.PlayerName, false, $"Player{clientId}"),
                    playerFullName = PlayerNameSanitizer.TruncateUtf8(_payload.PlayerFullName, PlayerNameSanitizer.MaxUtf8Bytes),
                    playerSteamId = _payload.SteamId,
                }));
                return;
            }

            AskForPlayerInfo(clientId);
        }

        public void AskForPlayerInfo(ulong clientId)
        {
            AskForPlayerInfoRpc(characterManager.GetSafeRpcTarget(clientId));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AskForPlayerInfoRpc(RpcParams rpcParams = default)
        {
            var _info = LocalPlayerInfoHolder.playerInfo;
            _info.playerClientId = NetworkManager.LocalClient.ClientId;
            LocalPlayerInfoHolder.playerInfo = _info;

            SavePlayerInfoRpc(_info);
        }

        [Rpc(SendTo.Server)]
        private void SavePlayerInfoRpc(PlayerInfo playerInfo, RpcParams rpcParams = default)
        {
            if (!IsSpawned) return;
            // NET-01: key the row on the RPC SENDER (never a client-supplied id) and upsert — a repeated answer
            // replaces the row instead of adding a second one.
            playerInfo.playerClientId = rpcParams.Receive.SenderClientId;
            SetRosterServer(_roster.Value.WithUpsert(playerInfo));
        }

        public PlayerInfo GetPlayerInfo(ulong _clientId)
        {
            if (!IsSpawned || _roster.Value == null) return default;
            return _roster.Value.TryGet(_clientId, out PlayerInfo _info) ? _info : default;
        }

        /// <summary>True when the replicated roster holds a row for that clientId.</summary>
        public bool HasPlayerInfo(ulong _clientId) => TryGetPlayerInfo(_clientId, out _);

        public bool TryGetPlayerInfo(ulong _clientId, out PlayerInfo _info)
        {
            _info = default;
            return IsSpawned && _roster.Value != null && _roster.Value.TryGet(_clientId, out _info);
        }

        /// <summary>NET-03: the pseudo as every name surface must show it (fallback + "left" marker).</summary>
        public string GetDisplayPseudo(ulong _clientId)
        {
            bool _hasEntry = TryGetPlayerInfo(_clientId, out PlayerInfo _info);
            return PseudoDisplay.Format(_hasEntry, _info.playerName.ToString(), _info.hasLeft);
        }

        // Story 13.0 (Epic 13): runtime update seam (additive — no current caller). Today playerInfos only
        // grows on connect and shrinks on disconnect; a mid-session change (the personalization story's
        // appearance vars) had nowhere to go. A later 13.x story calls UpdateLocalPlayerInfo() after the
        // local player edits an appearance var, and the server replaces that client's entry so it replicates.
        // Server-authoritative: the roster is mutated server-side only, exactly like SavePlayerInfoRpc.

        /// <summary>
        /// Client → server: stamp this client's id onto its local profile and ask the server to replace the
        /// replicated entry. Mirrors the AskForPlayerInfoRpc client write; SendTo.Server addresses no specific
        /// client, so there is no per-target RpcParams to wrap (NFR5) — same shape as SavePlayerInfoRpc.
        /// </summary>
        public void UpdateLocalPlayerInfo()
        {
            // Code-review F1: this seam is public with no production caller yet, so a future 13.x UI caller
            // could invoke it before connect. NetworkManager.LocalClient is only populated after connection —
            // guard the NRE. (AskForPlayerInfoRpc reads LocalClient too, but only ever runs inside an
            // already-delivered RPC, so it is post-connect by construction; this entry point is not.)
            if (!IsSpawned || NetworkManager == null || NetworkManager.LocalClient == null) return;

            var _info = LocalPlayerInfoHolder.playerInfo;
            _info.playerClientId = NetworkManager.LocalClient.ClientId;
            LocalPlayerInfoHolder.playerInfo = _info;

            UpdatePlayerInfoServerRpc(_info);
        }

        [Rpc(SendTo.Server)]
        private void UpdatePlayerInfoServerRpc(PlayerInfo playerInfo, RpcParams rpcParams = default)
        {
            // Code-review F2 (server authority): trust the RPC SENDER's identity, not the client-supplied
            // playerClientId, so a client can only ever update its OWN replicated entry (no impersonation /
            // overwriting another player's row). The server/bot direct path uses the public UpdatePlayerInfo
            // below, which legitimately addresses an arbitrary clientId (e.g. the host updating a bot).
            playerInfo.playerClientId = rpcParams.Receive.SenderClientId;
            UpdatePlayerInfo(playerInfo);
        }

        /// <summary>
        /// Server-authoritative replace-by-clientId. Idempotent — an unknown clientId is a no-op (no phantom entry
        /// is added), an unchanged value is skipped (no redundant replication), and the row's ready flag is kept
        /// (isReady is owned exclusively by SetReadyServer).
        /// </summary>
        public void UpdatePlayerInfo(PlayerInfo _info)
        {
            if (!IsServer || !IsSpawned) return;
            SetRosterServer(_roster.Value.WithUpdate(_info));
        }

        public void AddDebugPlayer(ulong _clientId, string _name)
        {
            if (!IsServer || !IsSpawned) return;

            // Simulated bots (clientId >= 100) are auto-ready: they never open a UI to toggle, so without this the
            // all-ready gate could never be satisfied in solo bot-debug (feat/lobby-ready-system). Upsert first
            // (keeps any existing ready flag), then force the bot ready.
            PlayerRosterSnapshot _next = _roster.Value.WithUpsert(new PlayerInfo
            {
                playerClientId = _clientId,
                playerName = _name,
                playerFullName = _name,
                playerSteamId = 0,
            });
            SetRosterServer(_next.WithReady(_clientId, true));
        }

        // ─────────────────── Lobby ready-to-start (feat/lobby-ready-system) ───────────────────

        /// <summary>
        /// Client → server: set THIS client's ready flag. Server-authoritative and sender-trusted (same shape as
        /// UpdatePlayerInfoServerRpc) — a client can only ready itself, never another player. SendTo.Server has no
        /// per-target params, so no GetSafeRpcTarget wrap (NFR5), exactly like SavePlayerInfoRpc.
        /// </summary>
        public void RequestSetReady(bool ready) => SetReadyServerRpc(ready);

        [Rpc(SendTo.Server)]
        private void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            SetReadyServer(rpcParams.Receive.SenderClientId, ready);
        }

        /// <summary>
        /// Server-side: flip ONLY the isReady field of one row (by clientId). Unknown clientId = no-op; unchanged
        /// value skipped (no redundant replication). Also the host's direct entry point for a simulated identity.
        /// </summary>
        public void SetReadyServer(ulong clientId, bool ready)
        {
            if (!IsServer || !IsSpawned) return;
            SetRosterServer(_roster.Value.WithReady(clientId, ready));
        }

        /// <summary>Every roster row is ready AND there is at least one (an empty roster is NOT all-ready).</summary>
        public bool AllReady()
        {
            if (!IsSpawned || playerInfos.Count == 0)
            {
                return false;
            }

            foreach (PlayerInfo _info in playerInfos)
            {
                if (!_info.isReady)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Number of ready roster rows (for the "X / N prêts" tally).</summary>
        public int ReadyCount()
        {
            if (!IsSpawned)
            {
                return 0;
            }

            int _count = 0;
            foreach (PlayerInfo _info in playerInfos)
            {
                if (_info.isReady)
                {
                    _count++;
                }
            }
            return _count;
        }
    }
}
