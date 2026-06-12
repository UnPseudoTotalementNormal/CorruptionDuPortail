using System;
using GameLogic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace MessageSystem
{
    public class MessageManager : NetworkBehaviour
    {
        // Story 10.4 (Epic 10 / D4): recorded-callers-only façade. The send flow (SendMessagePanel) now
        // resolves this manager through the composition root (lane C); the only remaining direct readers
        // are the two unregistered UI leaves (AwakeningRecapMessages, AnonymousRevealedMessagesComponent),
        // which keep the global until the Epic 12.2 UI pass. Guard #1 forbids the qualified instance
        // accessor in the migrated set (this manager itself uses the bare `instance` self-ref below).
        public static MessageManager instance; // recorded: dies in 12.3
        
        public NetworkList<MessageInfo> revealedMessages = new();
        public NetworkList<MessageInfo> messagesToReveal = new();

        // Story 8.3 lane A: scene-wired GameManager, narrowed to the loop slice (IGameLoop) for currentDay.
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            Assert.IsNotNull(gameManager, "MessageManager.gameManager is not wired — wire it in GameScene (the composition root).");
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
            messagesToReveal.Add(new MessageInfo(_sender, _message, Loop.currentDay));
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