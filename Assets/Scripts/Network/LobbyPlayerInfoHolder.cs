using System;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

namespace Network
{
    public class LobbyPlayerInfoHolder : NetworkBehaviour
    {
        public static NetworkList<PlayerInfo> playerInfos { get; private set; } = new();
        
        private void Start()
        {
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
    }
}