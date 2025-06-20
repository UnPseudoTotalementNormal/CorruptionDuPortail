using Unity.Collections;
using Unity.Netcode;

namespace ChatSystem
{
    public class ChatMessage : INetworkSerializable
    {
        public ulong senderClientId;
        public FixedString512Bytes message;
        
        public ChatMessage(ulong _senderClientId, FixedString512Bytes _message)
        {
            senderClientId = _senderClientId;
            message = _message;
        }
        
        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref senderClientId);
            _serializer.SerializeValue(ref message);
        }
    }
}