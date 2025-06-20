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
        
        [SerializeField] private TMP_Text chatTextPrefab;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform layoutTransform;

        private List<ChatWindow> chatWindows = new();
        private HashSet<int> discoveredChatIds = new();

        private void Awake()
        {
            instance = this;
        }

        public void DiscoverChat(int _chatId)
        {
            discoveredChatIds.Add(_chatId);
        }

        public ChatWindow OpenChat(int _chatId)
        {
            if (discoveredChatIds.Contains(_chatId))
            {
                return chatWindows.Find(w => w.chatId == _chatId);
            }
            return null;
        }
        
        [Rpc(SendTo.Server)]
        public void SendChatMessageServerRpc(ChatMessage _chatMessage, int _chatId)
        {
            ReceiveChatMessageRpc(_chatMessage, _chatId);
        }

        [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
        public void ReceiveChatMessageRpc(ChatMessage _chatMessage, int _chatId = (int)ChatWindowIDs.General, RpcParams _rpcParams = default)
        {
            string _senderName = _chatMessage.senderClientId == SERVER_CLIENT_ID 
                ? "Server" 
                : LobbyPlayerInfoHolder.instance.GetPlayerInfo(_chatMessage.senderClientId).playerName.ToString();
            
            var _window = chatWindows.Find(w => w.chatId == _chatId);
            _window?.chatMessages.Add(_chatMessage);
        }

        public void AddMessageLocal(string _message, ulong _senderId, int _chatId = (int)ChatWindowIDs.General)
        {
            var _window = chatWindows.Find(w => w.chatId == _chatId);
            _window?.chatMessages.Add(new ChatMessage(_senderId, _message));
        }
        
        public void AddMessage(FixedString512Bytes _message, string _senderName)
        {
            TMP_Text _chatText = Instantiate(chatTextPrefab, layoutTransform);

            _chatText.text = $"{_senderName}: {_message.ToString()}";

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
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