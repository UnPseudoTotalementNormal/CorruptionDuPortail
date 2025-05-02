using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ChatSystem
{
    public class ChatManager : NetworkBehaviour
    {
        [SerializeField] private ChatWindow chatWindow;

        private void Awake()
        {
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
            chatWindow.AddMessage(_message, _senderClientId);
        }
    }
}