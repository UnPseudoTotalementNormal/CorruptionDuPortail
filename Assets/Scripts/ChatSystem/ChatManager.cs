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

        // NET-11: server-side channel membership (general + server channels are implicit for everyone).
        private readonly CorruptionDuPortail.Domain.ChatMembership _membership =
            new((int)ChatWindowIDs.General, (int)ChatWindowIDs.Server);

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
        
        // NET-11: the server decides who reads a private channel. The sender id is the transport's, never the one
        // written in the message (only the host may speak for a simulated bot or the server sentinel), the sender
        // must be a member, and the message goes ONLY to the members known right now. A member's discovery was sent
        // on this same object before, so reliable ordered delivery guarantees its client knows the channel first.
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(ChatMessage _chatMessage, RpcParams _params = default)
        {
            if (_chatMessage == null)
            {
                return;
            }

            ulong _transportSender = _params.Receive.SenderClientId;
            bool _fromServer = _transportSender == NetworkManager.ServerClientId;
            if (!_fromServer)
            {
                _chatMessage.senderClientId = _transportSender;
                if (_chatMessage.chatId == (int)ChatWindowIDs.Server)
                {
                    Debug.LogWarning($"[CHAT] Client {_transportSender} tried to write in the read-only server channel.");
                    return;
                }
            }

            bool _isSentinel = _fromServer && _chatMessage.senderClientId == SERVER_CLIENT_ID;
            if (!_isSentinel && !_membership.IsMember(_chatMessage.chatId, _chatMessage.senderClientId))
            {
                Debug.LogWarning($"[CHAT] {_chatMessage.senderClientId} is not a member of channel {_chatMessage.chatId}; message dropped.");
                return;
            }

            if (_membership.IsPublic(_chatMessage.chatId))
            {
                ReceiveChatMessageRpc(_chatMessage);
            }
            else
            {
                foreach (ulong _recipient in RoutedRecipients(_chatMessage.chatId))
                {
                    ReceiveRoutedChatMessageRpc(_chatMessage, CharacterManager.instance.GetSafeRpcTarget(_recipient));
                }
            }

            if (_chatMessage.senderClientId != SERVER_CLIENT_ID)
            {
                OnMessageSentRpc(_chatMessage, CharacterManager.instance.GetSafeRpcTarget(_chatMessage.senderClientId));
            }
        }

        // One delivery per real connection: simulated bots (>= 100) are all served by the host, which must receive a
        // message once even when itself and several of its bots are members. Departed clients are skipped.
        private List<ulong> RoutedRecipients(int _chatId)
        {
            var _recipients = new List<ulong>();
            foreach (ulong _member in _membership.MembersOf(_chatId))
            {
                ulong _connection = _member >= 100 ? NetworkManager.ServerClientId : _member;
                if (_recipients.Contains(_connection))
                {
                    continue;
                }
                if (_connection != NetworkManager.ServerClientId && !NetworkManager.ConnectedClients.ContainsKey(_connection))
                {
                    continue;
                }
                _recipients.Add(_connection);
            }
            return _recipients;
        }

        /// <summary>NET-11: server-only. Makes <paramref name="_member"/> a member of a private channel and tells its client.</summary>
        public void GrantChannelServer(int _chatId, string _overrideName, ulong _member)
        {
            if (!IsServer)
            {
                Debug.LogError($"[CHAT] GrantChannelServer({_chatId}, {_member}) called on a client; ignored.");
                return;
            }
            _membership.Grant(_chatId, _member);
            DiscoverChatRpc(_chatId, new FixedString64Bytes(_overrideName ?? string.Empty),
                CharacterManager.instance.GetSafeRpcTarget(_member));
        }

        /// <summary>NET-11: server-only. Removes <paramref name="_member"/> from a private channel and tells its client.</summary>
        public void RevokeChannelServer(int _chatId, ulong _member)
        {
            if (!IsServer)
            {
                Debug.LogError($"[CHAT] RevokeChannelServer({_chatId}, {_member}) called on a client; ignored.");
                return;
            }
            _membership.Revoke(_chatId, _member);
            UndiscoverChatRpc(_chatId, CharacterManager.instance.GetSafeRpcTarget(_member));
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

        // NET-11: a private message the server routed to this client because it is a member. Never filtered on the
        // local channel list: the server's membership is the truth (the discovery always arrives first anyway).
        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveRoutedChatMessageRpc(ChatMessage _chatMessage, RpcParams _rpcParams = default)
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