#region

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

        public NetworkList<PlayerInfo> playerInfos { get; private set; } = new();

        // Story 10.4 (Epic 10 / D4): CharacterManager resolved once here (lane C) so this holder stops
        // reaching the locator for GetSafeRpcTarget (clears the §4a CharacterManager-census row). Resolved
        // null-tolerant (no Assert): GetSafeRpcTarget is reached ONLY on the server path (OnClientConnected
        // is registered server-only, and is also fired in-line below during this OnNetworkSpawn), where the
        // CharacterManager registry is populated exactly as the old .instance read required. On clients the
        // field may stay null and is never dereferenced — identical to the prior behaviour. NFR5: the
        // GetSafeRpcTarget call stays verbatim on the concrete CharacterManager.
        private CharacterManager characterManager;

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

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                OnClientConnected(NetworkManager.LocalClient.ClientId);
            }
        }

        public override void OnNetworkDespawn()
        {
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

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsSpawned) return;

            for (int i = 0; i < playerInfos.Count; i++)
            {
                if (playerInfos[i].playerClientId != clientId)
                {
                    continue;
                }
                
                playerInfos.RemoveAt(i);
                break;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
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
        private void SavePlayerInfoRpc(PlayerInfo playerInfo)
        {
            if (!IsSpawned || playerInfos == null) return;
            playerInfos.Add(playerInfo);
        }

        public PlayerInfo GetPlayerInfo(ulong _clientId)
        {
            if (!IsSpawned || playerInfos == null) return default;

            foreach (var info in playerInfos)
            {
                if (info.playerClientId == _clientId)
                {
                    return info;
                }
            }
            return default;
        }

        // Story 13.0 (Epic 13): runtime update seam (additive — no current caller). Today playerInfos only
        // grows on connect and shrinks on disconnect; a mid-session change (the personalization story's
        // appearance vars) had nowhere to go. A later 13.x story calls UpdateLocalPlayerInfo() after the
        // local player edits an appearance var, and the server replaces that client's entry so it replicates.
        // Server-authoritative: the list is mutated server-side only, exactly like SavePlayerInfoRpc.

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
        /// Server-authoritative replace-by-clientId: find the entry whose playerClientId matches and overwrite
        /// it in place (NetworkList index-set replicates the change). Idempotent — an unknown clientId is a
        /// no-op (no phantom entry is added), and an unchanged value is skipped (no redundant replication).
        /// </summary>
        public void UpdatePlayerInfo(PlayerInfo _info)
        {
            if (!IsServer || !IsSpawned || playerInfos == null) return;

            for (int i = 0; i < playerInfos.Count; i++)
            {
                if (playerInfos[i].playerClientId != _info.playerClientId)
                {
                    continue;
                }

                // Code-review F3: skip a no-op write so a redundant UpdateLocalPlayerInfo() (e.g. the
                // personalization UI firing with no real change) does not spam OnListChanged / replication.
                // Uses the value equality this story tidied up.
                if (!playerInfos[i].Equals(_info))
                {
                    playerInfos[i] = _info;
                }
                return;
            }
        }

        public void AddDebugPlayer(ulong _clientId, string _name)
        {
            if (!IsServer || !IsSpawned || playerInfos == null) return;
            
            playerInfos.Add(new Network.Player.PlayerInfo
            {
                playerClientId = _clientId,
                playerName = _name,
                playerFullName = _name,
                playerSteamId = 0
            });
        }
    }
}
