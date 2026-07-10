#region

using System;
using System.Collections.Generic;
using AudioSystem;
using AYellowpaper.SerializedCollections;
using Extensions;
using FMODUnity;
using Network;
using TMPro;
using Characters;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace ChatSystem
{
    public class ChatManager : NetworkBehaviour
    {
        // Story 10.1 (Epic 10 / D4): the gameplay consumers (powers + PowerComponents) were rerouted
        // off this global onto an injected chatManager base field, resolved through CompositionRoot.
        // The static now backs ONLY recorded-callers exceptions: the CompositionRoot's chat accessor
        // (the one sanctioned locator, since ChatManager is not de-singletonised) and the UI leaves
        // ChatPanel / ChatNotificationComponent / ChatWindow (→ Epic 12). // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests
        public static ChatManager instance;

        public const ulong SERVER_CLIENT_ID = GameValues.FAKE_CLIENT_ID;

        // Story 11.3 (Epic 11 / D5): the chat routing / visibility rules (who may see/send what, the
        // active-channel fallback, the window-name policy) extracted to a pure EditMode-tested Domain
        // POCO. This adapter still owns the discovered-id set, the active-channel state, every RPC, and
        // the clientId>=100 bot interception — it only delegates the *decisions*.
        private readonly CorruptionDuPortail.Domain.ChatChannelPolicy _policy = new();

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
        
        public SerializedDictionary<int, EventReference> switchChatSoundOverride = new();
        public SerializedDictionary<int, string> chatWindowNameOverride = new();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DiscoverChat((int)ChatWindowIDs.Server);
            DiscoverChat((int)ChatWindowIDs.General);
            
            onChatMessageSent += (_) => { GameAudioManager.instance?.PlayOneShot(sendMessageSound.GetPath()); };
            onChatMessageReceived += (_) => { GameAudioManager.instance?.PlayOneShot(receiveMessageSound.GetPath()); };
            onActiveChatChanged += (_newChatId) =>
            {
                if (GameAudioManager.instance == null) return;

                if (switchChatSoundOverride.TryGetValue(_newChatId, out EventReference _overrideSound) && !string.IsNullOrEmpty(_overrideSound.GetPath()))
                {
                    GameAudioManager.instance.PlayOneShot(_overrideSound.GetPath());
                    return;
                }
                GameAudioManager.instance.PlayOneShot(switchChatSound.GetPath());
            };
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        public void ChangeActiveChat(int _chatId)
        {
            if (_policy.CanActivateChannel(_chatId, discoveredChatIds))
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
            if (!_policy.CanSendMessage(_text, activeChatId, (int)ChatWindowIDs.Server))
            {
                return;
            }
            
            FixedString512Bytes _message = new FixedString512Bytes(_text);
            SendChatMessageServerRpc(new ChatMessage(Characters.CharacterManager.instance.GetLocalClientId(), _message, activeChatId));
        }

        public void DiscoverChat(int _chatId, string _overrideName = null)
        {
            discoveredChatIds.Add(_chatId);
            if (!string.IsNullOrEmpty(_overrideName))
            {
                chatWindowNameOverride.TryAdd(_chatId, _overrideName);
            }
            onChatDiscovered?.Invoke(_chatId);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void DiscoverChatRpc(int _chatId, FixedString64Bytes _overrideName = default, RpcParams _rpcParams = default)
        {
            DiscoverChat(_chatId, _overrideName.ToString());
        }
        
        public void UndiscoverChat(int _chatId)
        {
            if (discoveredChatIds.Remove(_chatId))
            {
                Debug.Log("Undiscovering chat with ID: " + _chatId);
                if (_policy.ShouldFallBackToGeneralAfterUndiscover(_chatId, activeChatId))
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
            // The adapter resolves the engine-side lookups (the override dictionary, the Game-enum
            // reflection); the POCO owns the three-way precedence policy.
            bool _hasOverride = chatWindowNameOverride.TryGetValue(_chatId, out string _overrideName);
            string _enumName = Enum.IsDefined(typeof(ChatWindowIDs), _chatId) ? ((ChatWindowIDs)_chatId).ToString() : null;
            return _policy.ResolveWindowName(_chatId, _hasOverride, _overrideName, _enumName);
        }
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(ChatMessage _chatMessage)
        {
            ReceiveChatMessageRpc(_chatMessage);
            OnMessageSentRpc(_chatMessage, CharacterManager.instance.GetSafeRpcTarget(_chatMessage.senderClientId));
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void OnMessageSentRpc(ChatMessage _chatMessage, RpcParams _rpcParams = default)
        {
            onChatMessageSent?.Invoke(_chatMessage);
        }

        [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
        public void ReceiveChatMessageRpc(ChatMessage _chatMessage, RpcParams _rpcParams = default)
        {
            if (!_policy.IsMessageVisible(_chatMessage.chatId, discoveredChatIds))
            {
                return;
            }
            
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