using System;
using GameLogic;
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
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server)]
        public void SendMessageRpc(ulong _sender, FixedString512Bytes _message)
        {
            messagesToReveal.Add(new MessageInfo(_sender, _message, GameManager.instance.currentDay));
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
        public int day;
        
        public MessageInfo(ulong _senderClientId, FixedString512Bytes _message, int _day)
        {
            message = _message;
            senderClientId = _senderClientId;
            day = _day;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref message);
            _serializer.SerializeValue(ref senderClientId);
            _serializer.SerializeValue(ref day);
        }

        public bool Equals(MessageInfo _other)
        {
            return senderClientId == _other.senderClientId && message.Equals(_other.message) && day == _other.day;
        }
    }
}