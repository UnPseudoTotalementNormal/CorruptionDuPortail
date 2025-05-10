using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ChatSystem
{
    public class ChatManager : NetworkBehaviour
    {
        public static ChatManager instance;
        
        [SerializeField] private ChatWindow chatWindow;

        private void Awake()
        {
            instance = this;
            chatWindow.SetChatManager(this);
        }
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageRpc(FixedString512Bytes _message, ulong _senderClientId)
        {
            ReceiveChatMessageRpc(_message, _senderClientId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReceiveChatMessageRpc(FixedString512Bytes _message, ulong _senderClientId)
        {
            var _playerNickname = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_senderClientId).playerName.ToString();
            chatWindow.AddMessage(_message, _playerNickname);
        }
        
        public void AddMessageLocal(string _message, string _senderName)
        {
            chatWindow.AddMessage(_message, _senderName);
        }
    }
}