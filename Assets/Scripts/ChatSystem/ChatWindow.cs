#region

using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

#endregion

namespace ChatSystem
{
    public class ChatWindow
    {
        private ChatManager chatManager => ChatManager.instance;
        
        public int chatId;
        
        public FixedString64Bytes chatName;
        public List<ChatMessage> chatMessages = new();

        private void TrySendChatMessage(string _text)
        {
            SendChatMessage(_text);
        }

        private void SendChatMessage(string _text)
        {
            if (string.IsNullOrEmpty(_text))
                return;

            FixedString512Bytes _message = new FixedString512Bytes(_text);
            chatManager.SendChatMessageServerRpc(new ChatMessage(NetworkManager.Singleton.LocalClientId, _message), chatId);
        }
    }
}
