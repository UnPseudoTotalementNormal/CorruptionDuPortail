using System;
using Unity.Collections;
using Unity.Netcode;

namespace ChatSystem
{
    [Serializable]
    public class ChatMessage : INetworkSerializable
    {
        public ulong senderClientId;
        public FixedString512Bytes message;
        public int chatId;

        public ChatMessage()
        {
            
        }
        
        public ChatMessage(ulong _senderClientId, FixedString512Bytes _message, int _chatId)
        {
            senderClientId = _senderClientId;
            message = _message;
            chatId = _chatId;
        }
        
        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref senderClientId);
            _serializer.SerializeValue(ref message);
            _serializer.SerializeValue(ref chatId);
        }
    }
}