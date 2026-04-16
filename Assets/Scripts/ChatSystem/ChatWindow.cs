#region

using System;
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
        
        public event Action<ChatMessage> onMessageReceived;

        private void TrySendChatMessage(string _text)
        {
            if (chatId == (int)ChatWindowIDs.Server)
            {
                return;
            }
            
            SendChatMessage(_text);
        }

        private void SendChatMessage(string _text)
        {
            if (string.IsNullOrEmpty(_text))
                return;

            FixedString512Bytes _message = new FixedString512Bytes(_text);
            chatManager.SendChatMessageServerRpc(new ChatMessage(Characters.CharacterManager.instance.GetLocalClientId(), _message, chatId));
        }

        public void AddChatMessage(ChatMessage _message)
        {
            chatMessages.Add(_message);
            onMessageReceived?.Invoke(_message);
        }
    }
}
