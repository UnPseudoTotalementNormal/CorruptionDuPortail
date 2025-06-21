#region

using System;
using System.Collections.Generic;
using Network;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace ChatSystem
{
    public class ChatManager : NetworkBehaviour
    {
        public static ChatManager instance;

        public const ulong SERVER_CLIENT_ID = GameValues.FAKE_CLIENT_ID;

        private List<ChatWindow> chatWindows = new();
        private HashSet<int> discoveredChatIds = new();
        
        public int activeChatId { get; private set; } = (int)ChatWindowIDs.General;
        
        public event Action<int> onActiveChatChanged;
        public event Action<int> onChatDiscovered;

        private void Awake()
        {
            instance = this;
            DiscoverChat((int)ChatWindowIDs.General);
        }
        
        public void ChangeActiveChat(int _chatId)
        {
            if (discoveredChatIds.Contains(_chatId))
            {
                activeChatId = _chatId;
                onActiveChatChanged?.Invoke(activeChatId);
            }
            else
            {
                Debug.LogWarning($"Chat with ID {_chatId} is not discovered yet.");
            }
        }

        public void TrySendChatMessage(string _text)
        {
            if (string.IsNullOrEmpty(_text))
            {
                return;
            }
            
            FixedString512Bytes _message = new FixedString512Bytes(_text);
            SendChatMessageServerRpc(new ChatMessage(NetworkManager.Singleton.LocalClientId, _message), activeChatId);
        }

        public void DiscoverChat(int _chatId)
        {
            discoveredChatIds.Add(_chatId);
            onChatDiscovered?.Invoke(_chatId);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void DiscoverChatRpc(int _chatId, RpcParams _rpcParams = default)
        {
            DiscoverChat(_chatId);
        }

        public ChatWindow GetChatWindow(int _chatId)
        {
            ChatWindow _window = chatWindows.Find(w => w.chatId == _chatId);
            if (_window == null)
            {
                _window = new ChatWindow
                {
                    chatId = _chatId,
                    chatName = $"Chat {_chatId}",
                    chatMessages = new List<ChatMessage>()
                };
                chatWindows.Add(_window);
            }
            
            return _window;
        }
        
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(ChatMessage _chatMessage, int _chatId)
        {
            ReceiveChatMessageRpc(_chatMessage, _chatId);
        }

        [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
        public void ReceiveChatMessageRpc(ChatMessage _chatMessage, int _chatId = (int)ChatWindowIDs.General, RpcParams _rpcParams = default)
        {
            ChatWindow _window = GetChatWindow(_chatId);
            _window?.AddChatMessage(_chatMessage);
        }

        public void AddMessageLocal(string _message, ulong _senderId, int _chatId = (int)ChatWindowIDs.General)
        {
            ChatWindow _window = GetChatWindow(_chatId);
            _window?.AddChatMessage(new ChatMessage(_senderId, _message));
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