#region

using System;
using System.Collections.Generic;
using AudioSystem;
using Extensions;
using FMODUnity;
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
        public HashSet<int> discoveredChatIds = new();
        
        public int activeChatId { get; private set; } = (int)ChatWindowIDs.General;
        
        public event Action<ChatMessage> onChatMessageReceived;
        public event Action<ChatMessage> onChatMessageSent;
        public event Action<int> onActiveChatChanged;
        public event Action<int> onChatDiscovered;
        public event Action<int> onChatUndiscovered;
        
        public EventReference switchChatSound;
        public EventReference receiveMessageSound;
        public EventReference sendMessageSound;

        private void Awake()
        {
            instance = this;
            DiscoverChat((int)ChatWindowIDs.Server);
            DiscoverChat((int)ChatWindowIDs.General);
            
            onChatMessageSent += (_) => { GameAudioManager.instance.PlayOneShot(sendMessageSound.GetPath()); };
            onChatMessageReceived += (_) => { GameAudioManager.instance.PlayOneShot(receiveMessageSound.GetPath()); };
            onActiveChatChanged += (_) => { GameAudioManager.instance.PlayOneShot(switchChatSound.GetPath()); };
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
            if (string.IsNullOrEmpty(_text) || activeChatId == (int)ChatWindowIDs.Server)
            {
                return;
            }
            
            FixedString512Bytes _message = new FixedString512Bytes(_text);
            SendChatMessageServerRpc(new ChatMessage(NetworkManager.Singleton.LocalClientId, _message, activeChatId));
        }

        public void DiscoverChat(int _chatId)
        {
            Debug.Log("Discovering chat with ID: " + _chatId);
            discoveredChatIds.Add(_chatId);
            onChatDiscovered?.Invoke(_chatId);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void DiscoverChatRpc(int _chatId, RpcParams _rpcParams = default)
        {
            DiscoverChat(_chatId);
        }
        
        public void UndiscoverChat(int _chatId)
        {
            if (discoveredChatIds.Remove(_chatId))
            {
                Debug.Log("Undiscovering chat with ID: " + _chatId);
                if (activeChatId == _chatId)
                {
                    ChangeActiveChat((int)ChatWindowIDs.General);
                }
                onChatUndiscovered?.Invoke(_chatId);
            }
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void UndiscoverChatRpc(int _chatId, RpcParams _rpcParams = default)
        {
            UndiscoverChat(_chatId);
        }

        public ChatWindow GetChatWindow(int _chatId)
        {
            ChatWindow _window = chatWindows.Find(w => w.chatId == _chatId);
            if (_window == null)
            {
                _window = new ChatWindow
                {
                    chatId = _chatId,
                    chatName = GetChatWindowName(_chatId),
                    chatMessages = new List<ChatMessage>()
                };
                chatWindows.Add(_window);
            }
            
            return _window;
        }
        
        public string GetChatWindowName(int _chatId)
        {
            if (Enum.IsDefined(typeof(ChatWindowIDs), _chatId))
            {
                return ((ChatWindowIDs)_chatId).ToString();
            }
            return $"Chat {_chatId}";
        }
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(ChatMessage _chatMessage)
        {
            ReceiveChatMessageRpc(_chatMessage);
            OnMessageSentRpc(_chatMessage, RpcTarget.Single(_chatMessage.senderClientId, RpcTargetUse.Persistent));
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void OnMessageSentRpc(ChatMessage _chatMessage, RpcParams _rpcParams = default)
        {
            onChatMessageSent?.Invoke(_chatMessage);
        }

        [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
        public void ReceiveChatMessageRpc(ChatMessage _chatMessage, RpcParams _rpcParams = default)
        {
            ChatWindow _window = GetChatWindow(_chatMessage.chatId);
            _window?.AddChatMessage(_chatMessage);
            onChatMessageReceived?.Invoke(_chatMessage);
        }

        public void AddMessageLocal(string _message, ulong _senderId, int _chatId = (int)ChatWindowIDs.General)
        {
            ChatWindow _window = GetChatWindow(_chatId);
            var _chatMessage = new ChatMessage(_senderId, _message, _chatId);
            _window?.AddChatMessage(_chatMessage);
            onChatMessageReceived?.Invoke(_chatMessage);
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