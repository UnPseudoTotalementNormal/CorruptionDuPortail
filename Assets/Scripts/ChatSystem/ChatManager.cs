#region

using System;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace ChatSystem
{
    public class ChatManager : NetworkBehaviour
    {
        public static ChatManager instance;
        
        [SerializeField] private ChatWindow chatWindow;

        public const ulong SERVER_CLIENT_ID = GameValues.FAKE_CLIENT_ID;

        private void Awake()
        {
            instance = this;
            chatWindow.SetChatManager(this);
        }
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(FixedString512Bytes _message, ulong _senderClientId)
        {
            ReceiveChatMessageClientRpc(_message, _senderClientId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReceiveChatMessageClientRpc(FixedString512Bytes _message, ulong _senderClientId)
        {
            string _senderName = _senderClientId == SERVER_CLIENT_ID ? "Server" : LobbyPlayerInfoHolder.instance.GetPlayerInfo(_senderClientId).playerName.ToString();
            chatWindow.AddMessage(_message, _senderName);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void SendChatMessageSingleRpc(FixedString512Bytes _message, ulong _senderClientId, RpcParams _rpcParams)
        {
            string _senderName = _senderClientId == SERVER_CLIENT_ID ? "Server" : LobbyPlayerInfoHolder.instance.GetPlayerInfo(_senderClientId).playerName.ToString();
            chatWindow.AddMessage(_message, _senderName);
        }
        
        public void AddMessageLocal(string _message, string _senderName)
        {
            chatWindow.AddMessage(_message, _senderName);
        }
    }
    
    [Serializable]
    public struct ChatWindowInfo : INetworkSerializable
    {
        public int windowId;
        public FixedString64Bytes windowName;

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref windowId);
            _serializer.SerializeValue(ref windowName);
        }
    }
}