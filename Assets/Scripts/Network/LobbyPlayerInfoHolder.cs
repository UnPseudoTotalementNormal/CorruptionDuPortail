#region

using Network.Player;
using Unity.Netcode;
using Characters;

#endregion

namespace Network
{
    public class LobbyPlayerInfoHolder : NetworkBehaviour
    {
        public static LobbyPlayerInfoHolder instance { get; private set; }
        
        public NetworkList<PlayerInfo> playerInfos { get; private set; } = new();

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
            
            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                OnClientConnected(NetworkManager.LocalClient.ClientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (instance == this)
            {
                instance = null;
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
            AskForPlayerInfo(clientId);
        }

        public void AskForPlayerInfo(ulong clientId)
        {
            AskForPlayerInfoRpc(CharacterManager.instance.GetSafeRpcTarget(clientId));
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

        public void AddDebugPlayer(ulong _clientId, string _name)
        {
            if (!IsServer) return;
            
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