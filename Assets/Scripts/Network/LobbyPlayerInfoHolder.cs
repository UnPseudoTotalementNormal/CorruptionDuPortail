#region

using Network.Player;
using Unity.Netcode;

#endregion

namespace Network
{
    public class LobbyPlayerInfoHolder : NetworkBehaviour
    {
        public static LobbyPlayerInfoHolder instance { get; private set; }
        
        public NetworkList<PlayerInfo> playerInfos { get; private set; } = new();

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(instance.gameObject);
                return;
            }
            instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            
            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                OnClientConnected(NetworkManager.LocalClient.ClientId);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
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
            AskForPlayerInfoRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        private void AskForPlayerInfoRpc(RpcParams rpcParams = default)
        {
            var _info = LocalPlayerInfoHolder.Instance.playerInfo;
            _info.playerClientId = NetworkManager.LocalClient.ClientId;
            LocalPlayerInfoHolder.Instance.playerInfo = _info;
            
            SavePlayerInfoRpc(_info);
        }

        [Rpc(SendTo.Server)]
        private void SavePlayerInfoRpc(PlayerInfo playerInfo)
        {
            playerInfos.Add(playerInfo);
        }

        public PlayerInfo GetPlayerInfo(ulong _clientId)
        { 
            foreach (var info in playerInfos)
            {
                if (info.playerClientId == _clientId)
                {
                    return info;
                }
            }
            return default;
        }
    }
}