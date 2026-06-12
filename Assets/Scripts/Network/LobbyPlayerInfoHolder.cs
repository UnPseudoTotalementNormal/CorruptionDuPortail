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
        public static LobbyPlayerInfoHolder instance { get; private set; } // recorded: dies in 12.3

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
