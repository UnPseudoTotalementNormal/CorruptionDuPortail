using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.Assertions;

namespace MessageSystem
{
    public class MessageManager : NetworkBehaviour
    {
        public static MessageManager instance;
        
        public NetworkList<MessageInfo> revealedMessages = new();
        public NetworkList<MessageInfo> messagesToReveal = new();
        
        private void Awake()
        {
            instance = this;
        }

        [Rpc(SendTo.Server)]
        public void SendMessageRpc(ulong _sender, FixedString512Bytes _message)
        {
            messagesToReveal.Add(new MessageInfo(_sender, _message));
        }

        public void RevealAllMessage()
        {
            Assert.IsTrue(IsServer, $"{nameof(RevealAllMessage)} can only be called on the server.");
            foreach (var _messageInfo in messagesToReveal)
            {
                revealedMessages.Add(_messageInfo);
            }
            messagesToReveal.Clear();
        }
    }
    
    [Serializable]
    public struct MessageInfo : INetworkSerializable, IEquatable<MessageInfo>
    {
        public ulong senderClientId;
        public FixedString512Bytes message;
        
        public MessageInfo(ulong _senderClientId, FixedString512Bytes _message)
        {
            message = _message;
            senderClientId = _senderClientId;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref message);
            _serializer.SerializeValue(ref senderClientId);
        }

        public bool Equals(MessageInfo _other)
        {
            return senderClientId == _other.senderClientId && message.Equals(_other.message);
        }
    }
}